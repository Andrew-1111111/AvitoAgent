using AvitoAgent.Avito.Interfaces;
using AvitoAgent.Avito.Logging;
using AvitoAgent.Avito.Parsing;
using AvitoAgent.Core.Interfaces;
using AvitoAgent.Core.Models;
using AvitoAgent.Playwright.Interfaces;
using AvitoAgent.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace AvitoAgent.Avito.Services;

public sealed class AvitoAuthService(
    IOptions<AvitoOptions> options,
    ApplicationPaths paths,
    BrowserWorkingPageHolder pageHolder,
    IBrowserManager browserManager,
    INotificationService notifications,
    IAgentRestartCoordinator restartCoordinator,
    ILogger<AvitoAuthService> logger
) : IAvitoAuthService, IAvitoAuthControl
{
    private const int LocatorProbeTimeoutMs = 1_500;
    private const int AuthProbeTimeoutMs = 500;

    private const string AuthRequiredTelegramMessage =
        "Требуется вход в Avito.\n"
        + "Войдите вручную в открытом браузере агента.\n"
        + "После входа агент продолжит работу сам.";

    private static readonly string[] AuthenticatedSelectors =
    [
        "[data-marker='header/username-button']",
        "[data-marker='header/user-menu']",
        "[data-marker='header/user-name']",
        "a[href*='/profile/settings']",
    ];

    private static readonly string[] LoginButtonSelectors =
    [
        "[data-marker='header/login-button']",
        "a[data-marker='header/login-button']",
        "button[data-marker='header/login-button']",
        "a:has-text('Вход и регистрация')",
        "button:has-text('Вход и регистрация')",
        "text=Вход и регистрация",
    ];

    private static readonly string[] LoginInputSelectors =
    [
        "input[data-marker='login-form/login/input']",
        "input[data-marker='login-form/phone/input']",
        "input[autocomplete='tel']",
        "input[type='tel']",
        "input[name='login']",
        "input[type='email']",
        "[data-marker='login-form'] input",
    ];

    private static readonly string[] SmsCodeInputSelectors =
    [
        "input[data-marker='login-form/code/input']",
        "input[inputmode='numeric']",
        "input[name='code']",
        "input[autocomplete='one-time-code']",
    ];

    private readonly AvitoOptions _options = options.Value;
    private readonly ApplicationPaths _paths = paths;
    private readonly BrowserWorkingPageHolder _pageHolder = pageHolder;
    private readonly IBrowserManager _browserManager = browserManager;
    private readonly INotificationService _notifications = notifications;
    private readonly IAgentRestartCoordinator _restartCoordinator = restartCoordinator;
    private readonly ILogger<AvitoAuthService> _logger = logger;
    private readonly SemaphoreSlim _loginLock = new(1, 1);
    private int _loginInProgress;
    private int _authRequiredNotified;

    public string StorageStatePath => Path.Combine(_paths.Data, _options.Auth.StorageStateFile);

    public bool IsLoginInProgress => Volatile.Read(ref _loginInProgress) == 1;

    public async Task<AvitoAuthStatusInfo> GetStatusAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (!_options.Auth.Enabled)
        {
            return new(AvitoAuthState.Disabled, "Авторизация Avito отключена.");
        }

        if (IsLoginInProgress)
        {
            return new(AvitoAuthState.LoginInProgress, "Проверяется вход в Avito.");
        }

        var session = await GetPersistentSessionAsync(cancellationToken);
        var page = await OpenStartPageAsync(session, cancellationToken);
        if (await AvitoPageDiagnostics.IsAccessRestrictedAsync(page))
        {
            return new(
                AvitoAuthState.NotAuthenticated,
                "Доступ к Avito ограничен. Пройдите капчу вручную в браузере агента."
            );
        }

        // Статус: глубокая проверка один раз (UI + /profile при сомнении).
        return await TryIsAuthenticatedAsync(
            session,
            page,
            allowProfileProbe: true,
            cancellationToken
        ) == true
            ? new(AvitoAuthState.Authenticated, "Вход в Avito выполнен.")
            : new(AvitoAuthState.NotAuthenticated, "Требуется вход в Avito.");
    }

    public async Task<bool> EnsureAuthenticatedAsync(
        IBrowserSession session,
        CancellationToken cancellationToken = default
    )
    {
        await _loginLock.WaitAsync(cancellationToken);
        Interlocked.Exchange(ref _loginInProgress, 1);
        if (!_options.Auth.Enabled)
        {
            _loginLock.Release();
            Interlocked.Exchange(ref _loginInProgress, 0);
            return true;
        }

        try
        {
            return await RunManualLoginFlowAsync(session, cancellationToken);
        }
        finally
        {
            Interlocked.Exchange(ref _loginInProgress, 0);
            _loginLock.Release();
        }
    }

    public Task SaveSessionAsync(
        IBrowserSession session,
        CancellationToken cancellationToken = default
    ) => SaveStorageStateAsync(session.Context, cancellationToken);

    private Task<IBrowserSession> GetPersistentSessionAsync(CancellationToken cancellationToken) =>
        _browserManager.GetPersistentSessionAsync(
            new BrowserSessionRequest { StorageStatePath = StorageStatePath },
            cancellationToken
        );

    private async Task<bool> RunManualLoginFlowAsync(
        IBrowserSession session,
        CancellationToken cancellationToken
    )
    {
        var page = await OpenStartPageAsync(session, cancellationToken);
        page = await EnsureAvitoAccessibleAsync(session, page, cancellationToken);

        if (
            await TryIsAuthenticatedAsync(session, page, allowProfileProbe: true, cancellationToken)
            == true
        )
        {
            await SaveStorageStateAsync(session.Context, cancellationToken);
            if (Interlocked.Exchange(ref _authRequiredNotified, 0) == 1)
            {
                await MarkLoginSucceededAsync(cancellationToken);
            }
            else
            {
                AvitoLog.AlreadyAuthenticated(_logger);
            }

            return true;
        }

        AvitoLog.LoginStarted(_logger);
        AvitoLog.ManualLoginInstructions(_logger);

        // Сначала окно браузера (рестарт из Headless), уведомление в TG - только после этого.
        await EnsureVisibleBrowserForLoginAsync(cancellationToken);
        await NotifyAuthRequiredAsync(cancellationToken);
        return false;
    }

    private async Task<IPage> OpenStartPageAsync(
        IBrowserSession session,
        CancellationToken cancellationToken
    )
    {
        var page = _pageHolder.Page is { IsClosed: false } p
            ? p
            : await session.Pages.CreatePageAsync(cancellationToken);

        _pageHolder.SetPage(page);

        if (!AvitoNavigationState.InitialUrlOpened)
        {
            await AvitoPageActions.OpenInitialAvitoUrlAsync(
                page,
                AvitoPageActions.BuildInitialAvitoUrl(_options),
                _options.NavigationTimeoutMs,
                cancellationToken
            );
        }

        await WaitForSettleAsync(page, cancellationToken);
        return page;
    }

    private async Task<IPage> ResolveActivePageAsync(
        IBrowserSession session,
        IPage currentPage,
        CancellationToken cancellationToken
    )
    {
        if (!currentPage.IsClosed)
        {
            return currentPage;
        }

        AvitoLog.PageClosedSwitching(_logger);

        var activePage = session.Context.Pages.LastOrDefault(p => !p.IsClosed);
        if (activePage is not null)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                var url = SafeGetPageUrl(activePage);
                AvitoLog.LoginCurrentUrl(_logger, url);
            }

            _pageHolder.SetPage(activePage);
            return activePage;
        }

        var newPage = await session.Pages.CreatePageAsync(cancellationToken);
        _pageHolder.SetPage(newPage);
        return newPage;
    }

    private async Task<bool?> TryIsAuthenticatedAsync(
        IBrowserSession session,
        IPage page,
        bool allowProfileProbe,
        CancellationToken cancellationToken
    )
    {
        if (page.IsClosed)
        {
            return null;
        }

        try
        {
            if (await AvitoPageDiagnostics.IsAccessRestrictedAsync(page))
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    var pageUrl = SafeGetPageUrl(page);
                    AvitoLog.AccessRestricted(_logger, pageUrl);
                }
                return false;
            }

            if (
                await IsAnyVisibleOnMainPageAsync(
                    page,
                    AuthenticatedSelectors,
                    1_000,
                    cancellationToken
                )
            )
            {
                // На странице IP-блока селекторы профиля не должны считаться входом.
                if (await AvitoPageDiagnostics.IsAccessRestrictedAsync(page))
                {
                    if (_logger.IsEnabled(LogLevel.Warning))
                    {
                        var pageUrl = SafeGetPageUrl(page);
                        AvitoLog.AccessRestricted(_logger, pageUrl);
                    }
                    return false;
                }

                return true;
            }

            if (
                await IsAnyVisibleOnMainPageAsync(
                    page,
                    LoginButtonSelectors,
                    1_000,
                    cancellationToken
                )
            )
            {
                // Иногда на главной кратковременно видна кнопка входа даже при валидной сессии.
                // В "глубоком" режиме дополнительно проверяем /profile, чтобы не требовать логин повторно.
                if (allowProfileProbe)
                {
                    var viaProfile = await VerifyViaProfilePageAsync(page, cancellationToken);
                    if (viaProfile)
                    {
                        return true;
                    }
                }

                return false;
            }

            // SMS/login форма - точно не авторизованы, /profile не трогаем.
            if (await IsAnyVisibleAsync(page, SmsCodeInputSelectors, 250, cancellationToken)
                || await IsAnyVisibleAsync(page, LoginInputSelectors, 250, cancellationToken))
            {
                return false;
            }

            var url = SafeGetPageUrl(page);
            if (
                url.Contains("#login", StringComparison.OrdinalIgnoreCase)
                || url.Contains("/login", StringComparison.OrdinalIgnoreCase)
            )
            {
                return false;
            }

            if (!allowProfileProbe)
            {
                return false;
            }

            // Cookies auth=1/u часто остаются после выхода - сами по себе не доказательство.
            if (await HasStaleAuthCookiesAsync(session.Context))
            {
                var viaProfile = await VerifyViaProfilePageAsync(page, cancellationToken);
                if (viaProfile)
                {
                    return true;
                }

                AvitoLog.LoginStep(
                    _logger,
                    "Cookies auth=1/u есть, но /profile не подтвердил вход - считаем неавторизованным"
                );
            }

            return false;
        }
        catch (PlaywrightException ex) when (IsTransientNavigationError(ex))
        {
            return null;
        }
    }

    private async Task<IPage> EnsureAvitoAccessibleAsync(
        IBrowserSession session,
        IPage page,
        CancellationToken cancellationToken
    )
    {
        if (!await AvitoPageDiagnostics.IsAccessRestrictedAsync(page))
        {
            return page;
        }

        await EnsureVisibleBrowserForLoginAsync(cancellationToken);
        page = await ResolveActivePageAsync(session, page, cancellationToken);

        var unblocked = await AvitoCaptchaWaiter.WaitUntilUnblockedAsync(
            page,
            session,
            _options,
            this,
            _notifications,
            _paths,
            _logger,
            cancellationToken
        );

        page = await ResolveActivePageAsync(session, page, cancellationToken);
        if (
            cancellationToken.IsCancellationRequested
            || page.IsClosed
            || !unblocked
            || await AvitoPageDiagnostics.IsAccessRestrictedAsync(page)
        )
        {
            if (cancellationToken.IsCancellationRequested || page.IsClosed)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            await SaveFailureScreenshotAsync(page, cancellationToken);
            throw new InvalidOperationException(
                "Доступ к Avito ограничен - пройдите капчу вручную в браузере агента."
            );
        }

        return page;
    }

    private static async Task<bool> HasStaleAuthCookiesAsync(IBrowserContext context)
    {
        var cookies = await context.CookiesAsync(["https://www.avito.ru/", "https://avito.ru/"]);

        var auth = cookies.FirstOrDefault(c =>
            c.Name.Equals("auth", StringComparison.OrdinalIgnoreCase)
        );
        if (
            auth is null
            || string.IsNullOrWhiteSpace(auth.Value)
            || auth.Value.Equals("0", StringComparison.OrdinalIgnoreCase)
        )
        {
            return false;
        }

        var user = cookies.FirstOrDefault(c =>
            c.Name.Equals("u", StringComparison.OrdinalIgnoreCase)
        );
        return user is not null
            && !string.IsNullOrWhiteSpace(user.Value)
            && !user.Value.Equals("0", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> VerifyViaProfilePageAsync(
        IPage page,
        CancellationToken cancellationToken
    )
    {
        if (page.IsClosed)
        {
            return false;
        }

        var previousUrl = SafeGetPageUrl(page);

        try
        {
            await page.GotoAsync(
                "https://www.avito.ru/profile",
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = Math.Min(_options.NavigationTimeoutMs, 15_000),
                }
            );
            await WaitForSettleAsync(page, cancellationToken);

            if (await AvitoPageDiagnostics.IsAccessRestrictedAsync(page))
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    var pageUrl = SafeGetPageUrl(page);
                    AvitoLog.AccessRestricted(_logger, pageUrl);
                }
                return false;
            }

            var url = SafeGetPageUrl(page);
            if (
                url.Contains("login", StringComparison.OrdinalIgnoreCase)
                || url.Contains("#login", StringComparison.OrdinalIgnoreCase)
            )
            {
                return false;
            }

            if (
                await IsAnyVisibleOnMainPageAsync(
                    page,
                    LoginButtonSelectors,
                    1_000,
                    cancellationToken
                )
            )
            {
                return false;
            }

            // Только явные маркеры кабинета. URL /profile на странице IP-блока - ложное «да».
            return await IsAnyVisibleOnMainPageAsync(
                page,
                AuthenticatedSelectors,
                1_000,
                cancellationToken
            );
        }
        catch (PlaywrightException ex) when (IsTransientNavigationError(ex))
        {
            return false;
        }
        finally
        {
            if (
                !page.IsClosed
                && !string.IsNullOrWhiteSpace(previousUrl)
                && previousUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                && !SafeGetPageUrl(page).Equals(previousUrl, StringComparison.OrdinalIgnoreCase)
            )
            {
                try
                {
                    await page.GotoAsync(
                        previousUrl,
                        new PageGotoOptions
                        {
                            WaitUntil = WaitUntilState.DOMContentLoaded,
                            Timeout = Math.Min(_options.NavigationTimeoutMs, 15_000),
                        }
                    );
                }
                catch (PlaywrightException)
                {
                    // проверка важнее возврата на предыдущий URL
                }
            }
        }
    }

    private static string SafeGetPageUrl(IPage page)
    {
        try
        {
            return page.IsClosed ? "(вкладка закрыта)" : page.Url;
        }
        catch (PlaywrightException)
        {
            return "(url недоступен)";
        }
    }

    private static async Task<bool> IsAnyVisibleOnMainPageAsync(
        IPage page,
        IEnumerable<string> selectors,
        int timeoutMs,
        CancellationToken cancellationToken
    )
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var selector in selectors)
            {
                if (await IsLocatorVisibleOnMainPageAsync(page, selector))
                {
                    return true;
                }
            }

            await Task.Delay(200, cancellationToken);
        }

        return false;
    }

    private static async Task<bool> IsLocatorVisibleOnMainPageAsync(IPage page, string selector)
    {
        if (page.IsClosed)
        {
            return false;
        }

        try
        {
            var locator = page.Locator(selector).First;
            await locator.WaitForAsync(
                new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = AuthProbeTimeoutMs,
                }
            );
            return true;
        }
        catch (PlaywrightException ex) when (IsTransientNavigationError(ex))
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static async Task WaitForSettleAsync(IPage page, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await page.WaitForLoadStateAsync(
                LoadState.DOMContentLoaded,
                new PageWaitForLoadStateOptions { Timeout = 15_000 }
            );
        }
        catch (PlaywrightException) { }

        await page.WaitForTimeoutAsync(300);
    }

    private async Task EnsureVisibleBrowserForLoginAsync(CancellationToken cancellationToken)
    {
        if (!_restartCoordinator.IsBrowserHeadless)
        {
            return;
        }

        var restarting = await _restartCoordinator.DisableHeadlessAndRestartAsync(
            "вход в Avito",
            cancellationToken
        );

        if (restarting)
        {
            AvitoLog.RestartingForVisibleLogin(_logger);
            throw new OperationCanceledException(
                "Агент перезапускается с Headless=false для входа в Avito.",
                cancellationToken
            );
        }
    }

    private async Task NotifyAuthRequiredAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _authRequiredNotified, 1) == 1)
        {
            return;
        }

        try
        {
            await _notifications.NotifyAvitoAuthRequiredAsync(
                AuthRequiredTelegramMessage,
                cancellationToken
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AvitoLog.ActionFailed(_logger, "Не удалось отправить в Telegram запрос на вход в Avito", ex);
        }
    }

    private async Task MarkLoginSucceededAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _authRequiredNotified, 0);
        AvitoLog.LoginSucceeded(_logger);

        try
        {
            await _notifications.NotifyAvitoAuthSucceededAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AvitoLog.ActionFailed(_logger, "Не удалось отправить в Telegram уведомление об успешном входе", ex);
        }
    }

    private async Task SaveStorageStateAsync(
        IBrowserContext context,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_paths.Data);

        await context.StorageStateAsync(
            new BrowserContextStorageStateOptions { Path = StorageStatePath }
        );

        AvitoLog.StorageStateSaved(_logger, StorageStatePath);
    }

    private async Task SaveFailureScreenshotAsync(IPage page, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (page.IsClosed)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(_paths.Logs);
            var path = Path.Combine(
                _paths.Logs,
                $"avito-login-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png"
            );
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true });
            AvitoLog.LoginFailedScreenshot(_logger, path);
        }
        catch (Exception ex)
        {
            AvitoLog.ActionFailed(_logger, "Не удалось сохранить скриншот входа Avito", ex);
        }
    }

    private static async Task<bool> IsAnyVisibleAsync(
        IPage page,
        IEnumerable<string> selectors,
        int timeoutMs,
        CancellationToken cancellationToken
    )
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                foreach (var selector in selectors)
                {
                    if (await IsLocatorVisibleAsync(page, selector))
                    {
                        return true;
                    }
                }
            }
            catch (PlaywrightException ex) when (IsTransientNavigationError(ex)) { }

            await Task.Delay(250, cancellationToken);
        }

        return false;
    }

    private static IEnumerable<ILocator> EnumerateLocators(IPage page, string selector)
    {
        yield return page.Locator(selector).First;

        IReadOnlyList<IFrame> frames;
        try
        {
            frames = page.Frames;
        }
        catch (PlaywrightException)
        {
            yield break;
        }

        foreach (var frame in frames)
        {
            if (frame == page.MainFrame)
            {
                continue;
            }

            yield return frame.Locator(selector).First;
        }
    }

    private static async Task<bool> IsLocatorVisibleAsync(IPage page, string selector)
    {
        if (page.IsClosed)
        {
            return false;
        }

        foreach (var locator in EnumerateLocators(page, selector))
        {
            try
            {
                await locator.WaitForAsync(
                    new LocatorWaitForOptions
                    {
                        State = WaitForSelectorState.Visible,
                        Timeout = LocatorProbeTimeoutMs,
                    }
                );
                return true;
            }
            catch (PlaywrightException ex) when (IsTransientNavigationError(ex)) { }
            catch (TimeoutException) { }
        }

        return false;
    }

    private static bool IsTransientNavigationError(PlaywrightException exception) =>
        exception.Message.Contains(
            "Execution context was destroyed",
            StringComparison.OrdinalIgnoreCase
        )
        || exception.Message.Contains("Frame was detached", StringComparison.OrdinalIgnoreCase)
        || exception.Message.Contains("has been closed", StringComparison.OrdinalIgnoreCase)
        || exception.Message.Contains(
            "Target page, context or browser",
            StringComparison.OrdinalIgnoreCase
        )
        || exception.Message.Contains("navigation", StringComparison.OrdinalIgnoreCase);
}

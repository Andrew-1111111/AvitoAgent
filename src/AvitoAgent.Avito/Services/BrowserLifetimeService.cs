using AvitoAgent.Avito.Interfaces;
using AvitoAgent.Avito.Logging;
using AvitoAgent.Core.Interfaces;
using AvitoAgent.Playwright.Interfaces;
using AvitoAgent.Shared;
using AvitoAgent.Shared.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace AvitoAgent.Avito.Services;

public sealed class BrowserLifetimeService(
    IBrowserManager browserManager,
    IAvitoAuthService authService,
    BrowserWorkingPageHolder pageHolder,
    INotificationService notifications,
    IAgentRestartCoordinator restartCoordinator,
    IHostApplicationLifetime lifetime,
    IOptions<AvitoOptions> options,
    ILogger<BrowserLifetimeService> logger
) : IHostedService
{
    private static readonly TimeSpan CloseNotifyTimeout = TimeSpan.FromSeconds(10);

    private readonly IBrowserManager _browserManager = browserManager;
    private readonly IAvitoAuthService _authService = authService;
    private readonly BrowserWorkingPageHolder _pageHolder = pageHolder;
    private readonly INotificationService _notifications = notifications;
    private readonly IAgentRestartCoordinator _restartCoordinator = restartCoordinator;
    private readonly IHostApplicationLifetime _lifetime = lifetime;
    private readonly AvitoOptions _options = options.Value;
    private readonly ILogger<BrowserLifetimeService> _logger = logger;

    private IBrowserSession? _session;
    private IPage? _keepAlivePage;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private int _stoppingHost;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            AvitoLog.BrowserStarting(_logger);

            var sessionRequest = new BrowserSessionRequest
            {
                StorageStatePath = _authService.StorageStatePath,
            };

            var session = await _browserManager.GetPersistentSessionAsync(
                sessionRequest,
                cancellationToken
            );
            _session = session;

            _keepAlivePage = await session.Pages.CreatePageAsync(cancellationToken);
            _pageHolder.SetPage(_keepAlivePage);

            SubscribeBrowserClosed(session);

            var startUrl = AvitoPageActions.BuildInitialAvitoUrl(_options);
            await AvitoPageActions.OpenInitialAvitoUrlAsync(
                _keepAlivePage,
                startUrl,
                _options.NavigationTimeoutMs,
                cancellationToken
            );

            AvitoLog.OpenedStartUrl(_logger, _keepAlivePage.Url);
            AvitoLog.BrowserReady(_logger);

            if (!_options.Auth.Enabled)
            {
                return;
            }

            try
            {
                await _authService.EnsureAuthenticatedAsync(session, cancellationToken);
            }
            catch (OperationCanceledException ex)
                when (ex.Message.Contains("перезапускается", StringComparison.OrdinalIgnoreCase))
            {
                AvitoLog.RestartingForVisibleLogin(_logger);
            }
            catch (Exception ex) when (IsQuietShutdown(ex, cancellationToken))
            {
                await RequestShutdownAsync(browserClosed: BrowserErrorText.IsBrowserClosed(ex));
            }
            catch (Exception ex)
            {
                AvitoLog.AuthStartupFailed(_logger, ex.Message);
            }
        }
        catch (Exception)
            when (Volatile.Read(ref _stoppingHost) == 1
                || cancellationToken.IsCancellationRequested
                || _lifetime.ApplicationStopping.IsCancellationRequested)
        {
            await RequestShutdownAsync(browserClosed: true);
        }
        catch (Exception ex)
        {
            AvitoLog.BrowserStartFailed(_logger, BrowserErrorText.Describe(ex));
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        UnsubscribeBrowserClosed();

        try
        {
            await _browserManager.ClosePersistentSessionAsync(
                _authService.StorageStatePath,
                cancellationToken
            );
        }
        catch (Exception ex)
        {
            AvitoLog.ShutdownNotifyFailed(_logger, BrowserErrorText.Describe(ex));
        }

        _session = null;
        _keepAlivePage = null;
        await NotifyClosedIfNeededAsync();
    }

    private void SubscribeBrowserClosed(IBrowserSession session)
    {
        _browser = session.Browser;
        _context = session.Context;

        if (_browser is not null)
        {
            _browser.Disconnected += OnBrowserDisconnected;
        }

        _context.Close += OnContextClosed;
    }

    private void UnsubscribeBrowserClosed()
    {
        if (_browser is not null)
        {
            _browser.Disconnected -= OnBrowserDisconnected;
        }

        if (_context is not null)
        {
            _context.Close -= OnContextClosed;
        }
    }

    private void OnBrowserDisconnected(object? sender, IBrowser e) =>
        _ = RequestShutdownAsync(browserClosed: true);

    private void OnContextClosed(object? sender, IBrowserContext e) =>
        _ = RequestShutdownAsync(browserClosed: true);

    private bool IsQuietShutdown(Exception exception, CancellationToken cancellationToken) =>
        _restartCoordinator.IsRestarting
        || Volatile.Read(ref _stoppingHost) == 1
        || cancellationToken.IsCancellationRequested
        || _lifetime.ApplicationStopping.IsCancellationRequested
        || exception is OperationCanceledException
        || BrowserErrorText.IsBrowserClosed(exception);

    private async Task RequestShutdownAsync(bool browserClosed)
    {
        if (_restartCoordinator.IsRestarting)
        {
            return;
        }

        if (Interlocked.Exchange(ref _stoppingHost, 1) != 0)
        {
            return;
        }

        if (browserClosed)
        {
            AvitoLog.BrowserClosedExternally(_logger);
        }
        else
        {
            AvitoLog.ApplicationStopping(_logger);
        }

        await NotifyClosedIfNeededAsync();
        _lifetime.StopApplication();
    }

    private async Task NotifyClosedIfNeededAsync()
    {
        if (_restartCoordinator.IsRestarting)
        {
            return;
        }

        using var cts = new CancellationTokenSource(CloseNotifyTimeout);
        try
        {
            await _notifications.NotifyApplicationClosedAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // SOCKS/Telegram не успели за таймаут — не блокируем выход.
        }
        catch (Exception ex)
        {
            AvitoLog.ShutdownNotifyFailed(_logger, ex.Message);
        }
    }
}

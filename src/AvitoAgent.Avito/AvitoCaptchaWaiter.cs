using AvitoAgent.Avito.Interfaces;
using AvitoAgent.Avito.Logging;
using AvitoAgent.Avito.Parsing;
using AvitoAgent.Core.Interfaces;
using AvitoAgent.Playwright.Interfaces;
using AvitoAgent.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AvitoAgent.Avito;

internal static class AvitoCaptchaWaiter
{
    public static async Task<bool> WaitUntilUnblockedAsync(
        IPage page,
        IBrowserSession session,
        AvitoOptions options,
        IAvitoAuthService authService,
        INotificationService notifications,
        ApplicationPaths paths,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        AvitoLog.ManualCaptchaRequired(logger, page.Url);
        AvitoBlockState.RegisterBlock();
        await NotifyWithScreenshotAsync(page, notifications, paths, logger, cancellationToken);

        var started = DateTime.UtcNow;
        var lastProgressLog = started;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (page.IsClosed)
            {
                AvitoLog.ManualCaptchaPageClosed(logger);
                return false;
            }

            if (!await AvitoPageDiagnostics.IsAccessRestrictedAsync(page))
            {
                // Экран Qrator часто мигает при переходе: подтверждаем разблокировку перезагрузкой.
                if (!await ConfirmUnblockedAsync(page, options, logger, cancellationToken))
                {
                    await Task.Delay(options.ManualCaptchaPollMs, cancellationToken);
                    continue;
                }

                AvitoLog.ManualCaptchaResolved(logger);

                try
                {
                    await authService.SaveSessionAsync(session, cancellationToken);
                }
                catch (Exception ex)
                {
                    AvitoLog.ActionFailed(logger, "Не удалось сохранить сессию после капчи", ex);
                }

                var cooldown = AvitoBlockState.ResolveCooldown(options.PostCaptchaDelayMs);
                if (cooldown > TimeSpan.Zero)
                {
                    AvitoLog.PostCaptchaPause(logger, (int)cooldown.TotalMilliseconds);
                    await Task.Delay(cooldown, cancellationToken);
                }

                return true;
            }

            if (
                options.ManualCaptchaWaitMs > 0
                && (DateTime.UtcNow - started).TotalMilliseconds >= options.ManualCaptchaWaitMs
            )
            {
                AvitoLog.ManualCaptchaTimeout(logger, options.ManualCaptchaWaitMs);
                return false;
            }

            var elapsedSeconds = (int)(DateTime.UtcNow - started).TotalSeconds;

            if ((DateTime.UtcNow - lastProgressLog).TotalSeconds >= 30)
            {
                AvitoLog.ManualCaptchaWaiting(logger, elapsedSeconds);
                lastProgressLog = DateTime.UtcNow;
            }

            await Task.Delay(options.ManualCaptchaPollMs, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return false;
    }

    /// <summary>
    /// Перезагружает страницу и проверяет, что Qrator снова не отдал экран блокировки.
    /// </summary>
    private static async Task<bool> ConfirmUnblockedAsync(
        IPage page,
        AvitoOptions options,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await Task.Delay(2_000, cancellationToken);
            await page.ReloadAsync(
                new PageReloadOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = options.NavigationTimeoutMs,
                }
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AvitoLog.ActionFailed(logger, "Не удалось перезагрузить страницу после капчи", ex);
            return false;
        }

        if (!await AvitoPageDiagnostics.IsAccessRestrictedAsync(page))
        {
            return true;
        }

        AvitoLog.BlockPersistsAfterCaptcha(logger);
        return false;
    }

    private static async Task NotifyWithScreenshotAsync(
        IPage page,
        INotificationService notifications,
        ApplicationPaths paths,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        string? screenshotPath = null;
        try
        {
            screenshotPath = await AvitoPageDiagnostics.SaveScreenshotAsync(
                page,
                paths,
                "avito-captcha",
                logger,
                cancellationToken
            );
        }
        catch (Exception ex)
        {
            AvitoLog.ActionFailed(logger, "Не удалось сохранить скриншот капчи", ex);
        }

        if (string.IsNullOrEmpty(screenshotPath))
        {
            return;
        }

        try
        {
            await notifications.NotifyCaptchaRequiredAsync(
                screenshotPath,
                page.IsClosed ? null : page.Url,
                cancellationToken
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AvitoLog.ActionFailed(
                logger,
                "Не удалось отправить в Telegram уведомление о капче Avito",
                ex
            );
        }
    }
}

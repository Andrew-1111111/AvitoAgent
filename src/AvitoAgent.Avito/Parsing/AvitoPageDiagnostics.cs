using AvitoAgent.Avito.Logging;
using AvitoAgent.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AvitoAgent.Avito.Parsing;

internal static class AvitoPageDiagnostics
{
    private static readonly string[] BlockMarkers =
    [
        "Доступ ограничен",
        "проблема с IP",
        "Подтвердите, что вы не робот",
        "не робот",
        "captcha",
        "hcaptcha",
        "geetest",
        // Экран Qrator перед Avito: HTTP 429 с этим текстом.
        "Слишком много запросов",
        "Too Many Requests",
    ];

    private static readonly string[] ResultWaitSelectors =
    [
        "[data-marker='catalog-serp'] [data-marker='item']",
        "[data-marker='catalog-serp'] [data-item-id]",
        "[data-marker='item'][data-item-id]",
        "[data-marker='item']",
        "div[data-item-id]",
        "[data-marker='catalog-serp']",
        "[data-marker='catalog-serp/noop']", // пустая выдача
        "h1:has-text('Ничего не найдено')",
        "h2:has-text('Ничего не найдено')",
        "[data-marker*='empty']",
    ];

    public static async Task<bool> WaitForSearchResultsAsync(
        IPage page,
        AvitoOptions options,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        var timeoutMs = Math.Max(1_000, options.SearchResultsWaitMs);
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await HasSearchResultsOrEmptyStateAsync(page))
            {
                return true;
            }

            if (await IsAccessRestrictedAsync(page))
            {
                return false;
            }

            var remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
            if (remaining <= 0)
            {
                break;
            }

            try
            {
                await Task.Delay(Math.Min(400, remaining), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
        }

        await LogPageStateAsync(page, logger);
        return await HasSearchResultsOrEmptyStateAsync(page);
    }

    private static async Task<bool> HasSearchResultsOrEmptyStateAsync(IPage page)
    {
        foreach (var selector in ResultWaitSelectors)
        {
            try
            {
                var handle = await page.QuerySelectorAsync(selector);
                if (handle is not null)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // ignore flaky DOM during navigation
            }
        }

        return false;
    }

    public static async Task<bool> IsAccessRestrictedAsync(IPage page)
    {
        try
        {
            var title = await page.TitleAsync();
            var content = await page.Locator("body")
                .InnerTextAsync(new LocatorInnerTextOptions { Timeout = 3_000 });

            var text = $"{title}\n{content}";

            return BlockMarkers.Any(marker =>
                text.Contains(marker, StringComparison.OrdinalIgnoreCase)
            );
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static async Task<string?> SaveScreenshotAsync(
        IPage page,
        ApplicationPaths paths,
        string filePrefix,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        _ = cancellationToken;
        if (page.IsClosed)
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(paths.Logs);
            var path = Path.Combine(
                paths.Logs,
                $"{filePrefix}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png"
            );
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true });
            AvitoLog.SearchScreenshotSaved(logger, path);
            return path;
        }
        catch (Exception ex)
        {
            AvitoLog.ActionFailed(logger, "Не удалось сохранить скриншот страницы", ex);
            return null;
        }
    }

    public static async Task LogPageStateAsync(IPage page, ILogger logger)
    {
        try
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                var title = await page.TitleAsync();
                AvitoLog.SearchPageState(logger, title, page.Url);
            }
        }
        catch (Exception ex)
        {
            AvitoLog.ActionFailed(logger, "Не удалось прочитать заголовок страницы", ex);
        }
    }
}

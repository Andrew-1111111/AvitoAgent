using AvitoAgent.Shared.Configuration;
using AvitoAgent.Playwright.Extensions;
using Microsoft.Playwright;

namespace AvitoAgent.Avito;

internal static class AvitoNavigationState
{
    public static bool InitialUrlOpened { get; set; }
}

internal static class AvitoPageActions
{
    /// <summary>
    /// Единственный программный переход по URL - первое открытие Avito.
    /// </summary>
    public static Task OpenInitialAvitoUrlAsync(
        IPage page,
        string url,
        int timeoutMs,
        CancellationToken cancellationToken = default
    ) => OpenInitialAvitoUrlCoreAsync(page, url, timeoutMs, cancellationToken);

    private static async Task OpenInitialAvitoUrlCoreAsync(
        IPage page,
        string url,
        int timeoutMs,
        CancellationToken cancellationToken
    )
    {
        if (AvitoNavigationState.InitialUrlOpened)
        {
            return;
        }

        await page.GotoAsync(
            url,
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = timeoutMs }
        );

        AvitoNavigationState.InitialUrlOpened = true;
        await page.HumanPauseRangeAsync(400, 900, cancellationToken);
    }

    public static string BuildInitialAvitoUrl(AvitoOptions options, string? locationSlug = null)
    {
        var baseUrl = options.BaseUrl.TrimEnd('/');
        if (options.NavigateViaHomepage)
        {
            return baseUrl + "/";
        }

        var location = string.IsNullOrWhiteSpace(locationSlug)
            ? AvitoUrlBuilder.ResolveLocation(options)
            : locationSlug.Trim().Trim('/');

        return $"{baseUrl}/{location}";
    }
}

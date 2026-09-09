using System.Text.RegularExpressions;
using AvitoAgent.Core.Models;
using AvitoAgent.Playwright.Extensions;
using AvitoAgent.Shared.Configuration;
using Microsoft.Playwright;

namespace AvitoAgent.Avito;

internal static partial class AvitoHumanNavigator
{
    private static readonly string[] SearchInputSelectors =
    [
        "input[data-marker='search-form/input']",
        "input[data-marker='search-form/suggest/input']",
        "input[data-marker='search-form/search-input']",
        "input[name='search']",
        "input[placeholder*='Поиск']",
        "input[placeholder*='поиск']",
        "#search",
    ];

    private static readonly string[] SearchSubmitSelectors =
    [
        "button[data-marker='search-form/submit-button']",
        "button[data-marker='search-form/submit']",
        "button[type='submit'][data-marker*='search']",
        "button[data-marker*='search-form'][type='submit']",
        "form[data-marker='search-form'] button[type='submit']",
        "button:has-text('Найти')",
    ];

    private static readonly string[] NextPageSelectors =
    [
        "[data-marker='pagination-button/nextPage']",
        "a[data-marker='pagination-button/nextPage']",
        "button[data-marker='pagination-button/nextPage']",
        "[data-marker='pagination-button/next']",
    ];

    private static readonly string[] SearchResultSelectors =
    [
        "[data-marker='catalog-serp'] [data-marker='item']",
        "[data-marker='catalog-serp'] [data-item-id]",
    ];

    private static readonly string[] ListingOpenedSelectors =
    [
        "[data-marker='item-view/item-description']",
        "[data-marker='item-view/item-params']",
        "[data-marker='item-view/title']",
        "[data-marker='image-frame/image-wrapper']",
        "[data-marker='item-view/gallery']",
    ];

    public static async Task NavigateToSearchAsync(
        IPage page,
        AvitoOptions options,
        SearchCriteria criteria,
        int pageIndex,
        CancellationToken cancellationToken
    )
    {
        if (!AvitoNavigationState.InitialUrlOpened)
        {
            await AvitoPageActions.OpenInitialAvitoUrlAsync(
                page,
                AvitoPageActions.BuildInitialAvitoUrl(options),
                options.NavigationTimeoutMs,
                cancellationToken
            );
        }

        if (pageIndex == 1)
        {
            await PerformSearchAsync(page, criteria, options, cancellationToken);
            return;
        }

        await GoToSearchPageAsync(page, pageIndex, options, cancellationToken);
    }

    public static async Task<bool> GoToNextSearchPageAsync(
        IPage page,
        AvitoOptions options,
        CancellationToken cancellationToken
    )
    {
        await page.HumanScrollToBottomAsync(cancellationToken);
        await page.HumanPauseRangeAsync(300, 600, cancellationToken);

        var next = await FindVisibleLocatorAsync(page, NextPageSelectors, cancellationToken);

        if (next is null)
        {
            return false;
        }

        try
        {
            if (await next.IsDisabledAsync())
            {
                return false;
            }
        }
        catch (PlaywrightException) { }

        await page.HumanClickAsync(next, options.NavigationTimeoutMs, cancellationToken);
        await page.WaitForLoadStateAsync(
            LoadState.DOMContentLoaded,
            new PageWaitForLoadStateOptions { Timeout = options.NavigationTimeoutMs }
        );
        await WaitForSearchResultsAsync(page, options.SearchResultsWaitMs, cancellationToken);
        await page.HumanPauseRangeAsync(400, 800, cancellationToken);
        return true;
    }

    /// <summary>
    /// Открывает объявление одним кликом. Возвращает вкладку с объявлением (та же или новая).
    /// </summary>
    public static Task<IPage?> OpenListingByClickAsync(
        IPage page,
        string listingId,
        int timeoutMs,
        CancellationToken cancellationToken
    ) => OpenListingByClickCoreAsync(page, listingId, timeoutMs, cancellationToken);


    private static async Task<IPage?> OpenListingByClickCoreAsync(
        IPage page,
        string listingId,
        int timeoutMs,
        CancellationToken cancellationToken
    )
    {
        listingId = NormalizeListingId(listingId);

        var already = await FindListingPageAsync(page, listingId, cancellationToken);
        if (already is not null)
        {
            return await PreferSameTabListingAsync(page, already, listingId, timeoutMs, cancellationToken);
        }

        await EnsureListingCardVisibleAsync(page, listingId, cancellationToken);
        await page.HumanPauseRangeAsync(400, 900, cancellationToken);

        var card = page.Locator($"[data-item-id='{listingId}']").First;

        if (await card.CountAsync() == 0)
        {
            return null;
        }

        var link = await FindListingLinkAsync(card, listingId);

        if (link is null)
        {
            return null;
        }

        // Сначала та же вкладка через location.assign — новая вкладка активирует Chrome.
        if (await TryOpenListingSameTabAsync(page, link, listingId, timeoutMs, cancellationToken))
        {
            return page;
        }

        var pagesBefore = page.Context.Pages.ToArray();
        await ForceSameTabLinkAsync(link);

        if (!await TryClickLinkAsync(link, timeoutMs, force: false))
        {
            return await FindListingPageAsync(page, listingId, cancellationToken);
        }

        var opened = await WaitForListingPageAsync(
            page,
            listingId,
            pagesBefore,
            8_000,
            cancellationToken
        );
        opened = await PreferSameTabListingAsync(page, opened, listingId, timeoutMs, cancellationToken);
        if (opened is not null)
        {
            return opened;
        }

        try
        {
            await ForceSameTabLinkAsync(link);
            await page.HumanPauseRangeAsync(200, 450, cancellationToken);
            if (await TryOpenListingSameTabAsync(page, link, listingId, timeoutMs, cancellationToken))
            {
                return page;
            }
        }
        catch (PlaywrightException) { }

        pagesBefore = [.. page.Context.Pages];
        await ForceSameTabLinkAsync(link);
        if (!await TryClickLinkAsync(link, timeoutMs, force: true))
        {
            return await FindListingPageAsync(page, listingId, cancellationToken);
        }

        opened = await WaitForListingPageAsync(
            page,
            listingId,
            pagesBefore,
            10_000,
            cancellationToken
        );
        return await PreferSameTabListingAsync(page, opened, listingId, timeoutMs, cancellationToken);
    }

    /// <summary>
    /// Открывает объявление в текущей вкладке (без NewPage / target=_blank).
    /// Важно: не использовать location.assign через Evaluate — Playwright ждёт возврат JS,
    /// контекст уничтожается навигацией → Timeout ~30 с («страница не ответила вовремя»).
    /// </summary>
    private static async Task<bool> TryOpenListingSameTabAsync(
        IPage page,
        ILocator link,
        string listingId,
        int timeoutMs,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var href = await link.EvaluateAsync<string>(
                """
                el => {
                  const a = el.closest && el.closest('a') ? el.closest('a') : el;
                  return (a && a.href) ? a.href : '';
                }
                """
            );

            if (string.IsNullOrWhiteSpace(href))
            {
                return false;
            }

            var absolute = ToAbsoluteAvitoUrl(page.Url, href);
            if (string.IsNullOrWhiteSpace(absolute))
            {
                return false;
            }

            try
            {
                await page.GotoAsync(
                    absolute,
                    new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = timeoutMs,
                    }
                );
            }
            catch (PlaywrightException)
            {
                // Уже могли прийти на объявление — проверяем ниже.
            }

            await page.HumanPauseRangeAsync(350, 800, cancellationToken);
            return await PageLooksLikeListingAsync(page, listingId);
        }
        catch (PlaywrightException)
        {
            try
            {
                return await PageLooksLikeListingAsync(page, listingId);
            }
            catch (PlaywrightException)
            {
                return false;
            }
        }
    }

    private static string? ToAbsoluteAvitoUrl(string pageUrl, string href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        href = href.Trim();
        if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return href;
        }

        try
        {
            var baseUri = string.IsNullOrWhiteSpace(pageUrl)
                ? new Uri("https://www.avito.ru/")
                : new Uri(pageUrl);
            return new Uri(baseUri, href).AbsoluteUri;
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Если объявление открылось во вкладке — переносим URL в исходную и закрываем лишнюю
    /// (новая вкладка забирает фокус ОС).
    /// </summary>
    private static async Task<IPage?> PreferSameTabListingAsync(
        IPage origin,
        IPage? opened,
        string listingId,
        int timeoutMs,
        CancellationToken cancellationToken
    )
    {
        if (opened is null)
        {
            return null;
        }

        if (ReferenceEquals(opened, origin) || opened.IsClosed)
        {
            return await PageLooksLikeListingAsync(origin, listingId) ? origin : opened;
        }

        string? url = null;
        try
        {
            url = opened.Url;
        }
        catch (PlaywrightException) { }

        try
        {
            await opened.CloseAsync();
        }
        catch (PlaywrightException) { }

        if (string.IsNullOrWhiteSpace(url))
        {
            return await PageLooksLikeListingAsync(origin, listingId) ? origin : null;
        }

        try
        {
            await origin.GotoAsync(
                url,
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = timeoutMs,
                }
            );
        }
        catch (PlaywrightException) { }

        try
        {
            await origin.HumanPauseRangeAsync(300, 700, cancellationToken);
            return await PageLooksLikeListingAsync(origin, listingId) ? origin : null;
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }

    private static async Task<bool> PageLooksLikeListingAsync(IPage page, string listingId)
    {
        try
        {
            if (page.IsClosed)
            {
                return false;
            }

            var url = page.Url ?? string.Empty;
            if (url.Contains(listingId, StringComparison.Ordinal))
            {
                return true;
            }

            return await page.Locator("[data-marker='item-view/item-id'], [data-marker='item-view/title']")
                    .CountAsync() > 0
                && url.Contains("/item/", StringComparison.OrdinalIgnoreCase);
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    private static async Task ForceSameTabLinkAsync(ILocator link)
    {
        try
        {
            await link.EvaluateAsync(
                """ el => { el.removeAttribute('target'); el.setAttribute('target', '_self'); if (el.rel) { el.rel = el.rel .replace(/\bnoopener\b/gi, '') .replace(/\bnoreferrer\b/gi, '') .trim(); } } """
            );
        }
        catch (PlaywrightException) { }
    }

    private static async Task<ILocator?> FindListingLinkAsync(ILocator card, string listingId)
    {
        var linkCandidates = new[]
        {
            card.Locator($"a[href*='{listingId}']").First,
            card.Locator("a[data-marker='item-title']").First,
            card.Locator("a[itemprop='url']").First,
            card.Locator("a[itemprop='name']").First,
            card.Locator("a[href*='/']").First,
        };

        foreach (var candidate in linkCandidates)
        {
            try
            {
                if (await candidate.CountAsync() == 0 || !await candidate.IsVisibleAsync())
                {
                    continue;
                }

                var candidateHref = await candidate.GetAttributeAsync("href");

                if (string.IsNullOrWhiteSpace(candidateHref))
                {
                    continue;
                }

                return candidate;
            }
            catch (PlaywrightException) { }
        }

        return null;
    }

    private static async Task<bool> TryClickLinkAsync(ILocator link, int timeoutMs, bool force)
    {
        _ = force;
        try
        {
            var page = link.Page;
            // Короткий wait: после неудачной навигации локатор часто уже «мёртвый».
            var waitMs = Math.Min(timeoutMs, 5_000);
            await page.HumanClickAsync(link, waitMs);
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    private static async Task<IPage?> WaitForListingPageAsync(
        IPage page,
        string listingId,
        IReadOnlyList<IPage> pagesBefore,
        int timeoutMs,
        CancellationToken cancellationToken
    )
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var beforeSet = pagesBefore.ToHashSet();

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var found = await FindListingPageAsync(page, listingId, cancellationToken);

            if (found is not null)
            {
                try
                {
                    await found.WaitForLoadStateAsync(
                        LoadState.DOMContentLoaded,
                        new PageWaitForLoadStateOptions { Timeout = 8_000 }
                    );
                }
                catch (PlaywrightException) { }

                return found;
            }

            // Новая вкладка могла появиться с небольшой задержкой.
            foreach (var candidate in page.Context.Pages)
            {
                if (beforeSet.Contains(candidate) || candidate.IsClosed)
                {
                    continue;
                }

                if (
                    IsListingPage(candidate.Url, listingId)
                    || await IsListingContentVisibleAsync(candidate)
                )
                {
                    return candidate;
                }
            }

            await page.HumanPauseRangeAsync(100, 180, cancellationToken);
        }

        return await FindListingPageAsync(page, listingId, cancellationToken);
    }

    private static async Task<IPage?> FindListingPageAsync(
        IPage page,
        string listingId,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var candidate in page.Context.Pages)
        {
            if (candidate.IsClosed)
            {
                continue;
            }

            if (IsListingPage(candidate.Url, listingId))
            {
                return candidate;
            }
        }

        // SPA на текущей вкладке без смены URL.
        if (await IsListingContentVisibleAsync(page) && !await IsSearchResultsVisibleAsync(page))
        {
            return page;
        }

        if (await IsListingOpenedAsync(page, listingId))
        {
            return page;
        }

        return null;
    }

    private static async Task<bool> IsListingOpenedAsync(IPage page, string listingId)
    {
        if (IsListingPage(page.Url, listingId))
        {
            return true;
        }

        if (
            page.Url.Contains(listingId, StringComparison.Ordinal)
            && await IsListingContentVisibleAsync(page)
        )
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Возврат к выдаче поиска. Возвращает вкладку с результатами.
    /// </summary>
    public static Task<IPage?> ReturnToSearchAsync(
        IPage page,
        int timeoutMs,
        CancellationToken cancellationToken
    ) => ReturnToSearchCoreAsync(page, timeoutMs, cancellationToken);

    private static async Task<IPage?> ReturnToSearchCoreAsync(
        IPage page,
        int timeoutMs,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await page.HumanKeyPressAsync("Escape", cancellationToken);
            await page.HumanPauseRangeAsync(80, 150, cancellationToken);
        }
        catch (PlaywrightException) { }

        // Уже на выдаче.
        if (await IsOnSearchResultsPageAsync(page))
        {
            return page;
        }

        // Другая вкладка с поиском (если вдруг осталась).
        var searchTab = await FindSearchResultsPageAsync(page.Context, cancellationToken);
        if (searchTab is not null)
        {
            await CloseNonSearchPagesAsync(searchTab, cancellationToken);
            return searchTab;
        }

        // История браузера: GoBack надёжнее Alt+Left на Avito SPA.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await IsOnSearchResultsPageAsync(page))
            {
                return page;
            }

            var wentBack = false;
            try
            {
                var response = await page.GoBackAsync(
                    new PageGoBackOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = Math.Min(timeoutMs, 10_000),
                    }
                );
                wentBack = response is not null || await IsOnSearchResultsPageAsync(page);
            }
            catch (PlaywrightException) { }

            if (!wentBack)
            {
                try
                {
                    await page.HumanKeyPressAsync("Alt+ArrowLeft", cancellationToken);
                    await page.WaitForLoadStateAsync(
                        LoadState.DOMContentLoaded,
                        new PageWaitForLoadStateOptions { Timeout = 5_000 }
                    );
                }
                catch (PlaywrightException) { }
            }

            await page.HumanPauseRangeAsync(200, 400, cancellationToken);

            if (await WaitForSearchResultsAsync(page, 5_000, cancellationToken))
            {
                await CloseNonSearchPagesAsync(page, cancellationToken);
                return page;
            }

            // Клик по логотипу Avito / «поиск» в шапке.
            if (await TryClickSearchChromeAsync(page, cancellationToken))
            {
                if (await WaitForSearchResultsAsync(page, 5_000, cancellationToken))
                {
                    return page;
                }
            }
        }

        return await IsOnSearchResultsPageAsync(page) ? page : null;
    }

    /// <summary>
    /// Гарантированно возвращает на выдачу с теми же фильтрами поиска.
    /// </summary>
    public static async Task<IPage> EnsureBackOnSearchAsync(
        IPage page,
        AvitoOptions options,
        SearchCriteria criteria,
        int timeoutMs,
        CancellationToken cancellationToken
    )
    {
        var searchPage = await ReturnToSearchAsync(page, timeoutMs, cancellationToken);
        var query =
            criteria.Keywords.FirstOrDefault(static k => !string.IsNullOrWhiteSpace(k))?.Trim()
            ?? string.Empty;

        if (searchPage is not null && await IsOnSearchResultsPageAsync(searchPage))
        {
            // Достаточно той же выдачи по запросу. Не пересобирать URL — Avito сам дописывает context=.
            if (
                string.IsNullOrWhiteSpace(query)
                || AvitoUrlBuilder.UrlMatchesSearchQuery(searchPage.Url, query)
            )
            {
                return searchPage;
            }
        }

        var working = searchPage ?? page;
        await NavigateToSearchAsync(working, options, criteria, pageIndex: 1, cancellationToken);
        await WaitForSearchResultsAsync(working, options.SearchResultsWaitMs, cancellationToken);
        return working;
    }

    private static async Task CloseNonSearchPagesAsync(
        IPage keep,
        CancellationToken cancellationToken
    )
    {
        foreach (var openPage in keep.Context.Pages.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ReferenceEquals(openPage, keep) || openPage.IsClosed)
            {
                continue;
            }

            try
            {
                await openPage.CloseAsync();
            }
            catch (PlaywrightException) { }
        }
    }

    private static async Task<bool> TryClickSearchChromeAsync(
        IPage page,
        CancellationToken cancellationToken
    )
    {
        var chromeSelectors = new[]
        {
            "a[data-marker='header/logo']",
            "a[data-marker='header/logo-link']",
            "[data-marker='search-form/input']",
            "a[href='/'][data-marker*='logo']",
            "header a[href='https://www.avito.ru/']",
            "header a[href='/']",
        };

        foreach (var selector in chromeSelectors)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var locator = page.Locator(selector).First;

                if (await locator.CountAsync() == 0 || !await locator.IsVisibleAsync())
                {
                    continue;
                }

                await page.HumanClickAsync(locator, 3_000, cancellationToken);
                await page.HumanPauseRangeAsync(200, 400, cancellationToken);
                return true;
            }
            catch (PlaywrightException) { }
        }

        return false;
    }

    private static async Task<IPage?> FindSearchResultsPageAsync(
        IBrowserContext context,
        CancellationToken cancellationToken
    )
    {
        foreach (var candidate in context.Pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (candidate.IsClosed)
            {
                continue;
            }

            if (await IsOnSearchResultsPageAsync(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static async Task EnsureListingCardVisibleAsync(
        IPage page,
        string listingId,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var card = page.Locator($"[data-item-id='{listingId}']").First;

        try
        {
            if (await card.CountAsync() == 0)
            {
                return;
            }

            await card.ScrollIntoViewIfNeededAsync();
        }
        catch (PlaywrightException) { }
    }

    public static bool LooksLikeListingPage(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        try
        {
            var path = new Uri(url).AbsolutePath;
            // РўРёРїРёС‡РЅС‹Р№ path РѕР±СЉСЏРІР»РµРЅРёСЏ: /moskva/.../title_1234567890
            return ListingIdInPathRegex().IsMatch(path);
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    private static bool IsListingPage(string url, string listingId)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(listingId))
        {
            return false;
        }

        var normalizedId = NormalizeListingId(listingId);

        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return false;
        }

        try
        {
            var uri = new Uri(url);
            return uri.AbsolutePath.Contains(normalizedId, StringComparison.Ordinal)
                || uri.Query.Contains(normalizedId, StringComparison.Ordinal);
        }
        catch (UriFormatException)
        {
            return url.Contains(normalizedId, StringComparison.Ordinal)
                && !url.Contains("?q=", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static string NormalizeListingId(string rawId)
    {
        if (string.IsNullOrWhiteSpace(rawId))
        {
            return string.Empty;
        }

        var match = DigitsIdRegex().Match(rawId);
        return match.Success ? match.Value : rawId.Trim();
    }

    [GeneratedRegex(@"_\d{6,12}(?:/|$)", RegexOptions.CultureInvariant)]
    private static partial Regex ListingIdInPathRegex();

    [GeneratedRegex(@"\d{6,12}", RegexOptions.CultureInvariant)]
    private static partial Regex DigitsIdRegex();

    private static async Task PerformSearchAsync(
        IPage page,
        SearchCriteria criteria,
        AvitoOptions options,
        CancellationToken cancellationToken
    )
    {
        var query =
            criteria.Keywords.FirstOrDefault(static k => !string.IsNullOrWhiteSpace(k))?.Trim()
            ?? string.Empty;
        var locationUrl = AvitoUrlBuilder.BuildSearchUrl(options, criteria, 1);

        if (
            !string.IsNullOrWhiteSpace(query)
            && AvitoUrlBuilder.UrlMatchesSearchQuery(page.Url, query)
            && AvitoUrlBuilder.SearchLocationMatches(page.Url, locationUrl)
            && await IsOnSearchResultsPageAsync(page)
        )
        {
            await WaitForSearchResultsAsync(page, options.SearchResultsWaitMs, cancellationToken);
            return;
        }

        // Регион без строки q — при смене локации / отсутствии поля поиска.
        if (
            !AvitoUrlBuilder.SearchLocationMatches(page.Url, locationUrl)
            || await FindVisibleLocatorAsync(page, SearchInputSelectors, cancellationToken) is null
        )
        {
            await page.GotoAsync(
                locationUrl,
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = options.NavigationTimeoutMs,
                }
            );
            await page.HumanPauseRangeAsync(400, 800, cancellationToken);
        }

        if (!await TrySearchViaFormAsync(page, query, options, cancellationToken))
        {
            throw new InvalidOperationException(
                "Не удалось выполнить поиск через форму Avito (поле или кнопка «Найти»)."
            );
        }

        await WaitForSearchResultsAsync(page, options.SearchResultsWaitMs, cancellationToken);
    }

    /// <summary>
    /// Очистить строку поиска → напечатать запрос → «Найти» (или Enter).
    /// </summary>
    private static async Task<bool> TrySearchViaFormAsync(
        IPage page,
        string query,
        AvitoOptions options,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var searchInput = await FindVisibleLocatorAsync(
            page,
            SearchInputSelectors,
            cancellationToken
        );
        if (searchInput is null)
        {
            return false;
        }

        await page.HumanPauseRangeAsync(500, 1_100, cancellationToken);
        await page.HumanClickAsync(
            searchInput,
            options.NavigationTimeoutMs,
            cancellationToken,
            isTopControl: true
        );
        await page.HumanPauseRangeAsync(350, 700, cancellationToken);
        await page.HumanKeyPressAsync("Control+A", cancellationToken);
        await page.HumanPauseRangeAsync(80, 180, cancellationToken);
        await page.HumanKeyPressAsync("Backspace", cancellationToken);
        await page.HumanPauseRangeAsync(200, 450, cancellationToken);
        await page.HumanTypeAsync(
            searchInput,
            query,
            options.NavigationTimeoutMs,
            cancellationToken,
            clickFirst: false
        );
        await page.HumanPauseRangeAsync(400, 900, cancellationToken);

        var submit = await FindVisibleLocatorAsync(page, SearchSubmitSelectors, cancellationToken);
        if (submit is not null)
        {
            await page.HumanClickAsync(
                submit,
                options.NavigationTimeoutMs,
                cancellationToken,
                isTopControl: true
            );
        }
        else
        {
            await page.HumanKeyPressAsync("Enter", cancellationToken);
        }

        try
        {
            await page.WaitForLoadStateAsync(
                LoadState.DOMContentLoaded,
                new PageWaitForLoadStateOptions { Timeout = options.NavigationTimeoutMs }
            );
        }
        catch (PlaywrightException)
        {
            // SPA может не давать full load — дальше ждём карточки.
        }

        await page.HumanPauseRangeAsync(400, 800, cancellationToken);
        return true;
    }

    /// <summary>
    /// Доставка, продавец и сортировка — кликами по меню на выдаче.
    /// </summary>
    public static async Task ApplyListingFiltersViaUiAsync(
        IPage page,
        SearchCriteria criteria,
        AvitoOptions options,
        CancellationToken cancellationToken
    )
    {
        var sortMode = AvitoSort.TryResolve(criteria.Sort, out _, out var parsedSort)
            ? parsedSort
            : AvitoSortMode.Date;
        var sellerMode = AvitoSellerType.TryResolve(criteria.SellerType, out _, out var parsedSeller)
            ? parsedSeller
            : AvitoSellerTypeMode.All;

        var needSort = !await IsSortAppliedAsync(page, sortMode, cancellationToken);
        var needDelivery = criteria.DeliveryOnly && !await IsDeliveryAppliedAsync(page, cancellationToken);
        var needSeller = sellerMode is not AvitoSellerTypeMode.All
            && !await IsSellerAppliedAsync(page, sellerMode, cancellationToken);

        if (!needSort && !needDelivery && !needSeller)
        {
            return;
        }

        await PauseFilterLookAsync(page, cancellationToken);

        if (needDelivery)
        {
            await ApplyDeliveryFilterAsync(page, criteria, options, cancellationToken);
            if (needSeller || needSort)
            {
                await PauseBetweenFiltersAsync(page, cancellationToken);
            }
        }

        if (needSeller)
        {
            await ApplySellerFilterAsync(page, criteria, options, cancellationToken);
            if (needSort)
            {
                await PauseBetweenFiltersAsync(page, cancellationToken);
            }
        }

        if (needSort)
        {
            await ApplySortAsync(page, criteria, options, cancellationToken);
        }
    }

    private static Task PauseFilterLookAsync(IPage page, CancellationToken cancellationToken) =>
        page.HumanPauseRangeAsync(800, 1_800, cancellationToken);

    private static Task PauseFilterMenuAsync(IPage page, CancellationToken cancellationToken) =>
        page.HumanPauseRangeAsync(700, 1_600, cancellationToken);

    private static Task PauseBetweenFiltersAsync(IPage page, CancellationToken cancellationToken) =>
        page.HumanPauseRangeAsync(900, 2_000, cancellationToken);

    private static Task PauseFilterSettleAsync(IPage page, CancellationToken cancellationToken) =>
        page.HumanPauseRangeAsync(1_000, 2_200, cancellationToken);

    /// <summary>
    /// На выдаче включает «С Авито Доставкой» кликом по меню. Без перехода по URL.
    /// </summary>
    public static async Task ApplyDeliveryFilterAsync(
        IPage page,
        SearchCriteria criteria,
        AvitoOptions options,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await PauseFilterLookAsync(page, cancellationToken);

            var optionLabels = criteria.DeliveryOnly
                ? (IReadOnlyList<string>)["С Авито Доставкой", "Авито Доставка", "С доставкой"]
                : ["Неважно", "Любая", "Все объявления", "Без фильтра"];

            string[] openSelectors =
            [
                "[data-marker*='delivery'][data-marker*='title']",
                "[data-marker*='delivery'][data-marker*='button']",
                "[data-marker*='params'][data-marker*='delivery']",
                "[data-marker='search-form/delivery']",
                "[data-marker*='filter'] >> text=Доставка",
                "button:has-text('Доставка')",
                "button:has-text('С Авито Доставкой')",
            ];

            var optionSelectors = new List<string>();
            foreach (var label in optionLabels)
            {
                var escaped = EscapeForSelector(label);
                optionSelectors.Add($"[data-marker*='delivery'][data-marker*='custom-option']:has-text('{escaped}')");
                optionSelectors.Add($"[data-marker*='delivery'] >> text={label}");
                optionSelectors.Add($"[role='listbox'] [role='option']:has-text('{escaped}')");
                optionSelectors.Add($"[data-marker*='popup'] >> text={label}");
                optionSelectors.Add($"label:has-text('{escaped}')");
                optionSelectors.Add($"li:has-text('{escaped}')");
            }

            var clicked = await TryClickMenuOptionAsync(
                page,
                options,
                openSelectors,
                optionSelectors,
                cancellationToken
            );

            if (!clicked)
            {
                clicked = await TrySetDeliveryToggleAsync(
                    page,
                    options,
                    criteria.DeliveryOnly,
                    cancellationToken
                );
            }

            if (!clicked)
            {
                clicked = await TryClickByExactTextAsync(
                    page,
                    optionLabels,
                    Math.Min(options.NavigationTimeoutMs, 8_000),
                    cancellationToken
                );
            }

            if (clicked)
            {
                await WaitForSearchResultsAsync(page, options.SearchResultsWaitMs, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Фильтр доставки не должен валить цикл поиска.
        }
    }

    /// <summary>
    /// На выдаче выбирает тип продавца кликом по меню. Без перехода по URL.
    /// </summary>
    public static async Task ApplySellerFilterAsync(
        IPage page,
        SearchCriteria criteria,
        AvitoOptions options,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await PauseFilterLookAsync(page, cancellationToken);

            var mode = AvitoSellerType.TryResolve(criteria.SellerType, out _, out var parsed)
                ? parsed
                : AvitoSellerTypeMode.All;

            var labels = AvitoSellerType.OptionLabels(mode);
            var sellerCode = AvitoSellerType.UrlValue(mode);

            string[] openSelectors =
            [
                "[data-marker*='user'][data-marker*='title']",
                "[data-marker*='user'][data-marker*='button']",
                "[data-marker='params[user]']",
                "[data-marker*='params'][data-marker*='user']",
                "[data-marker*='filter'] >> text=Продавец",
                "button:has-text('Продавец')",
            ];

            var optionSelectors = new List<string>();
            if (sellerCode is not null)
            {
                optionSelectors.Add($"[data-marker='params[user]/custom-option({sellerCode})']");
                optionSelectors.Add($"[data-marker='user/custom-option({sellerCode})']");
                optionSelectors.Add($"[data-marker*='user'][data-marker*='custom-option({sellerCode})']");
            }
            else
            {
                optionSelectors.Add("[data-marker='params[user]/custom-option()']");
                optionSelectors.Add("[data-marker='params[user]/custom-option(0)']");
            }

            foreach (var label in labels)
            {
                var escaped = EscapeForSelector(label);
                optionSelectors.Add($"[data-marker*='user'][data-marker*='custom-option']:has-text('{escaped}')");
                optionSelectors.Add($"[role='listbox'] [role='option']:has-text('{escaped}')");
                optionSelectors.Add($"[data-marker*='popup'] >> text={label}");
                optionSelectors.Add($"li:has-text('{escaped}')");
            }

            var clicked = await TryClickMenuOptionAsync(
                page,
                options,
                openSelectors,
                optionSelectors,
                cancellationToken
            );
            if (
                !clicked
                && mode is not AvitoSellerTypeMode.All
                && await TryClickByExactTextAsync(
                    page,
                    labels,
                    Math.Min(options.NavigationTimeoutMs, 8_000),
                    cancellationToken
                )
            )
            {
                clicked = true;
            }

            if (clicked)
            {
                await WaitForSearchResultsAsync(page, options.SearchResultsWaitMs, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Фильтр продавца не должен валить цикл поиска.
        }
    }

    /// <summary>
    /// Сортировка кликом по меню на выдаче. Без перехода по URL.
    /// </summary>
    public static async Task<bool> ApplySortAsync(
        IPage page,
        SearchCriteria criteria,
        AvitoOptions options,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var mode = AvitoSort.TryResolve(criteria.Sort, out _, out var parsed)
                ? parsed
                : AvitoSortMode.Date;

            if (await IsSortAppliedAsync(page, mode, cancellationToken))
            {
                return true;
            }

            await PauseFilterLookAsync(page, cancellationToken);
            var applied = await TrySelectSortViaUiAsync(page, mode, options, cancellationToken);
            if (applied)
            {
                await PauseFilterSettleAsync(page, cancellationToken);
            }

            return applied;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// После поиска формой дописывает цену / состояние / localPriority в текущий URL выдачи.
    /// </summary>
    public static async Task<bool> ApplyCriteriaFiltersAsync(
        IPage page,
        SearchCriteria criteria,
        AvitoOptions options,
        CancellationToken cancellationToken
    )
    {
        try
        {
            if (!AvitoUrlBuilder.TryMergeCriteriaFilters(page.Url, criteria, out var targetUrl))
            {
                // Фильтры уже в URL (path мог смениться на категорию — это нормально).
                return AvitoUrlBuilder.HasCriteriaFilters(page.Url, criteria);
            }

            await page.GotoAsync(
                targetUrl,
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = options.NavigationTimeoutMs,
                }
            );
            await WaitForSearchResultsAsync(page, options.SearchResultsWaitMs, cancellationToken);
            await page.HumanPauseRangeAsync(300, 600, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static async Task<bool> IsSortAppliedAsync(
        IPage page,
        AvitoSortMode mode,
        CancellationToken cancellationToken
    )
    {
        var triggerText = await ReadSortTriggerTextAsync(page, cancellationToken);
        return AvitoSort.IsApplied(page.Url, triggerText, mode);
    }

    public static async Task<bool> IsDeliveryAppliedAsync(
        IPage page,
        CancellationToken cancellationToken
    )
    {
        if (UrlHasQueryValue(page.Url, "d", "1"))
        {
            return true;
        }

        cancellationToken.ThrowIfCancellationRequested();
        string[] selectors =
        [
            "[data-marker*='delivery'][data-marker*='title']",
            "[data-marker='search-form/delivery']",
            "button:has-text('Доставка')",
            "button:has-text('С Авито Доставкой')",
        ];

        foreach (var selector in selectors)
        {
            try
            {
                var locator = page.Locator(selector).First;
                if (await locator.CountAsync() == 0 || !await locator.IsVisibleAsync())
                {
                    continue;
                }

                var text = (await locator.InnerTextAsync())?.Trim() ?? string.Empty;
                if (
                    text.Contains("Авито Доставк", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("С доставкой", StringComparison.OrdinalIgnoreCase)
                )
                {
                    return true;
                }
            }
            catch (PlaywrightException) { }
        }

        return false;
    }

    public static async Task<bool> IsSellerAppliedAsync(
        IPage page,
        AvitoSellerTypeMode mode,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var userValue = AvitoSellerType.UrlValue(mode);
        if (userValue is not null && UrlHasQueryValue(page.Url, "user", userValue))
        {
            return true;
        }

        if (mode is AvitoSellerTypeMode.All && !UrlHasQueryValue(page.Url, "user", "1") && !UrlHasQueryValue(page.Url, "user", "2"))
        {
            return true;
        }

        var labels = AvitoSellerType.OptionLabels(mode);
        string[] selectors =
        [
            "[data-marker*='user'][data-marker*='title']",
            "[data-marker='params[user]']",
            "button:has-text('Продавец')",
        ];

        foreach (var selector in selectors)
        {
            try
            {
                var locator = page.Locator(selector).First;
                if (await locator.CountAsync() == 0 || !await locator.IsVisibleAsync())
                {
                    continue;
                }

                var text = (await locator.InnerTextAsync())?.Trim() ?? string.Empty;
                foreach (var label in labels)
                {
                    if (text.Contains(label, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch (PlaywrightException) { }
        }

        return false;
    }

    private static async Task<bool> TrySelectSortViaUiAsync(
        IPage page,
        AvitoSortMode mode,
        AvitoOptions options,
        CancellationToken cancellationToken
    )
    {
        var labels = AvitoSort.OptionLabels(mode).ToList();
        if (mode is AvitoSortMode.Date)
        {
            labels.Add("Сначала новые");
            labels.Add("Новые");
        }

        var marker = AvitoSort.OptionMarker(mode);
        var timeout = Math.Min(options.NavigationTimeoutMs, 8_000);

        if (await IsSortAppliedAsync(page, mode, cancellationToken))
        {
            return true;
        }

        if (await TrySelectSortFromNativeSelectAsync(page, labels, cancellationToken))
        {
            return await WaitUntilSortAppliedAsync(page, mode, 8_000, cancellationToken);
        }

        if (
            await TryOpenSortMenuAsync(page, timeout, cancellationToken)
            && await TryClickSortOptionAsync(page, labels, marker, timeout, cancellationToken)
            && await WaitUntilSortAppliedAsync(page, mode, 8_000, cancellationToken)
        )
        {
            return true;
        }

        // Повтор только человеческой мышью — без JS-кликов по меню.
        await page.HumanPauseRangeAsync(400, 800, cancellationToken);
        if (
            await TryOpenSortMenuAsync(page, timeout, cancellationToken)
            && await TryClickSortOptionAsync(page, labels, marker, timeout, cancellationToken)
            && await WaitUntilSortAppliedAsync(page, mode, 8_000, cancellationToken)
        )
        {
            return true;
        }

        return await IsSortAppliedAsync(page, mode, cancellationToken);
    }

    private static async Task<string?> ReadSortTriggerTextAsync(
        IPage page,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        string[] selectors =
        [
            "a[data-marker='sort/title']",
            "[data-marker='sort/title'][role='button']",
            "[data-marker='sort/title']",
        ];

        foreach (var selector in selectors)
        {
            var locator = page.Locator(selector).First;
            try
            {
                if (await locator.CountAsync() == 0)
                {
                    continue;
                }

                var text = await locator.InnerTextAsync(new LocatorInnerTextOptions { Timeout = 1_500 });
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
            catch (PlaywrightException)
            {
                // следующий селектор
            }
        }

        return null;
    }

    private static async Task<bool> TryOpenSortMenuAsync(
        IPage page,
        int timeoutMs,
        CancellationToken cancellationToken
    )
    {
        string[] openSelectors =
        [
            "a[data-marker='sort/title']",
            "[data-marker='sort/title'][role='button']",
            "[data-marker='sort/title']",
            "[data-marker='sort/dropdown']",
            "a:has-text('Сортировка')",
            "[role='button']:has-text('Сортировка')",
        ];

        foreach (var selector in openSelectors)
        {
            var trigger = await FindVisibleLocatorAsync(page, [selector], cancellationToken);
            if (trigger is null)
            {
                continue;
            }

            try
            {
                await PauseFilterLookAsync(page, cancellationToken);
                await page.HumanClickAsync(trigger, timeoutMs, cancellationToken, isTopControl: true);
            }
            catch (PlaywrightException)
            {
                try
                {
                    await page.HumanClickAsync(trigger, timeoutMs, cancellationToken, isTopControl: true);
                }
                catch (PlaywrightException)
                {
                    continue;
                }
            }

            if (await WaitForSortDropdownAsync(page, 4_000, cancellationToken))
            {
                await page.HumanPauseRangeAsync(120, 260, cancellationToken);
                return true;
            }
        }

        return await WaitForSortDropdownAsync(page, 500, cancellationToken);
    }

    private static async Task<bool> WaitForSortDropdownAsync(
        IPage page,
        int timeoutMs,
        CancellationToken cancellationToken
    )
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dropdown = page.Locator("[data-marker='sort/dropdown']").First;
            try
            {
                if (await dropdown.IsVisibleAsync())
                {
                    return true;
                }
            }
            catch (PlaywrightException)
            {
                // ещё не появилось
            }

            var option = page.Locator("[data-marker*='sort/custom-option']").First;
            try
            {
                if (await option.IsVisibleAsync())
                {
                    return true;
                }
            }
            catch (PlaywrightException)
            {
                // ещё не появилось
            }

            await Task.Delay(150, cancellationToken);
        }

        return false;
    }

    private static async Task<bool> TryClickSortOptionAsync(
        IPage page,
        IReadOnlyList<string> labels,
        string? marker,
        int timeoutMs,
        CancellationToken cancellationToken
    )
    {
        var optionSelectors = new List<string>();
        if (!string.IsNullOrWhiteSpace(marker))
        {
            optionSelectors.Add($"[data-marker='sort/custom-option({marker})']");
        }

        foreach (var label in labels)
        {
            var escaped = EscapeForSelector(label);
            optionSelectors.Add($"[data-marker='sort/dropdown'] [role='checkbox']:has-text('{escaped}')");
            optionSelectors.Add($"[data-marker*='sort/custom-option']:has-text('{escaped}')");
            optionSelectors.Add($"[role='checkbox']:has-text('{escaped}')");
            optionSelectors.Add($"[data-marker='sort/dropdown'] button:has-text('{escaped}')");
        }

        var option = await WaitForVisibleLocatorAsync(page, optionSelectors, 4_000, cancellationToken);
        if (option is null)
        {
            foreach (var label in labels)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var checkbox = page.GetByRole(AriaRole.Checkbox, new() { Name = label, Exact = true });
                try
                {
                    if (await checkbox.CountAsync() > 0 && await checkbox.First.IsVisibleAsync())
                    {
                        option = checkbox.First;
                        break;
                    }
                }
                catch (PlaywrightException)
                {
                    // следующий label
                }
            }
        }

        if (option is null)
        {
            return false;
        }

        await PauseFilterMenuAsync(page, cancellationToken);
        // Микропауза перед «сканированием» пунктов сортировки.
        await page.HumanPauseRangeAsync(100, 220, cancellationToken);
        await HumanBrowseMenuToOptionAsync(
            page,
            option,
            cancellationToken,
            dwellMinMs: 110,
            dwellMaxMs: 240
        );
        // Короткая задержка на пункте перед кликом.
        await page.HumanPauseRangeAsync(120, 280, cancellationToken);

        try
        {
            await page.HumanClickAsync(option, timeoutMs, cancellationToken, isTopControl: true);
            await page.HumanPauseRangeAsync(90, 200, cancellationToken);
            return true;
        }
        catch (PlaywrightException)
        {
            return await TryClickLocatorAsync(page, option, timeoutMs, cancellationToken);
        }
    }

    private const string OpenFilterMenuItemsSelector =
        "[data-marker='sort/dropdown'] [data-marker*='sort/custom-option'], "
        + "[data-marker='sort/dropdown'] [role='checkbox'], "
        + "[data-marker='sort/dropdown'] [role='option'], "
        + "[data-marker='sort/dropdown'] li, "
        + "[data-marker='sort/dropdown'] button, "
        + "[data-marker*='delivery'][data-marker*='custom-option'], "
        + "[data-marker*='user'][data-marker*='custom-option'], "
        + "[data-marker*='popup'] [role='option'], "
        + "[data-marker*='popup'] [role='menuitem'], "
        + "[data-marker*='popup'] li, "
        + "[role='listbox'] [role='option'], "
        + "[role='menu'] [role='menuitem']";

    /// <summary>
    /// Курсор плавно спускается по открытому меню (сортировка / доставка / продавец) до пункта.
    /// </summary>
    private static async Task HumanBrowseMenuToOptionAsync(
        IPage page,
        ILocator target,
        CancellationToken cancellationToken,
        int dwellMinMs = 90,
        int dwellMaxMs = 220
    )
    {
        try
        {
            var targetBox = await target.BoundingBoxAsync();
            if (targetBox is null || targetBox.Height < 1)
            {
                return;
            }

            var targetY = (float)(targetBox.Y + targetBox.Height * 0.5);
            var targetX = (float)(targetBox.X + targetBox.Width * (0.35 + Random.Shared.NextDouble() * 0.3));

            var itemLocator = page.Locator(OpenFilterMenuItemsSelector);

            var waypoints = new List<(float X, float Y)>();
            int count;
            try
            {
                count = await itemLocator.CountAsync();
            }
            catch (PlaywrightException)
            {
                count = 0;
            }

            for (var i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var item = itemLocator.Nth(i);
                    if (!await item.IsVisibleAsync())
                    {
                        continue;
                    }

                    var box = await item.BoundingBoxAsync();
                    if (box is null || box.Height < 1)
                    {
                        continue;
                    }

                    var y = (float)(box.Y + box.Height * 0.5);
                    // Только пункты выше или на уровне цели — «сканируем» сверху вниз.
                    if (y > targetY + 8)
                    {
                        continue;
                    }

                    // Игнорируем пункты далеко по X (другое открытое меню / сайдбар).
                    if (Math.Abs(box.X - targetBox.X) > Math.Max(220, targetBox.Width * 2))
                    {
                        continue;
                    }

                    var x = (float)(box.X + box.Width * (0.35 + Random.Shared.NextDouble() * 0.3));
                    waypoints.Add((x, y));
                }
                catch (PlaywrightException)
                {
                    // skip
                }
            }

            waypoints = [.. waypoints.OrderBy(static p => p.Y)];

            // Не больше 6 промежуточных остановок — иначе слишком долго.
            if (waypoints.Count > 6)
            {
                var step = (waypoints.Count - 1) / 5.0;
                var thinned = new List<(float X, float Y)>();
                for (var i = 0; i < 5; i++)
                {
                    thinned.Add(waypoints[(int)Math.Round(i * step)]);
                }

                waypoints = thinned;
            }

            if (waypoints.Count == 0 || Math.Abs(waypoints[^1].Y - targetY) > 4)
            {
                waypoints.Add((targetX, targetY));
            }

            await page.MoveMouseDownThroughAsync(
                waypoints,
                cancellationToken,
                dwellMinMs,
                dwellMaxMs
            );
            await page.HumanPauseRangeAsync(
                Math.Max(120, dwellMinMs),
                Math.Max(280, dwellMaxMs),
                cancellationToken
            );
        }
        catch (PlaywrightException)
        {
            // Клик всё равно попробуем ниже.
        }
    }

    private static async Task<bool> TryClickLocatorAsync(
        IPage page,
        ILocator locator,
        int timeoutMs,
        CancellationToken cancellationToken
    )
    {
        try
        {
            if (await locator.CountAsync() == 0 || !await locator.First.IsVisibleAsync())
            {
                return false;
            }

            await PauseFilterMenuAsync(page, cancellationToken);
            await page.HumanClickAsync(locator.First, timeoutMs, cancellationToken);
            return true;
        }
        catch (PlaywrightException)
        {
            try
            {
                await page.HumanClickAsync(locator.First, timeoutMs, cancellationToken, isTopControl: true);
                return true;
            }
            catch (PlaywrightException)
            {
                return false;
            }
        }
    }

    private static async Task<bool> TrySelectSortFromNativeSelectAsync(
        IPage page,
        IReadOnlyList<string> labels,
        CancellationToken cancellationToken
    )
    {
        var selects = page.Locator("select");
        int count;
        try
        {
            count = await selects.CountAsync();
        }
        catch (PlaywrightException)
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var select = selects.Nth(i);
            foreach (var label in labels)
            {
                var option = select.Locator("option", new LocatorLocatorOptions { HasText = label });
                try
                {
                    if (await option.CountAsync() == 0)
                    {
                        continue;
                    }

                    await select.SelectOptionAsync(new SelectOptionValue { Label = label });
                    return true;
                }
                catch (PlaywrightException)
                {
                    // try next
                }
            }
        }

        return false;
    }

    private static async Task<bool> WaitUntilSortAppliedAsync(
        IPage page,
        AvitoSortMode mode,
        int timeoutMs,
        CancellationToken cancellationToken
    )
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await IsSortAppliedAsync(page, mode, cancellationToken))
            {
                return true;
            }

            await Task.Delay(200, cancellationToken);
        }

        return await IsSortAppliedAsync(page, mode, cancellationToken);
    }

    private static async Task<bool> TryClickMenuOptionAsync(
        IPage page,
        AvitoOptions options,
        IReadOnlyList<string> openSelectors,
        IReadOnlyList<string> optionSelectors,
        CancellationToken cancellationToken
    )
    {
        var timeout = Math.Min(options.NavigationTimeoutMs, 8_000);

        if (await TryClickFirstVisibleAsync(page, optionSelectors, timeout, cancellationToken))
        {
            await AfterFilterClickAsync(page, options, cancellationToken);
            return true;
        }

        var opened = false;
        foreach (var selector in openSelectors)
        {
            var trigger = await FindVisibleLocatorAsync(page, [selector], cancellationToken);
            if (trigger is null)
            {
                continue;
            }

            try
            {
                await PauseFilterLookAsync(page, cancellationToken);
                await page.HumanClickAsync(trigger, timeout, cancellationToken, isTopControl: true);
                opened = true;
                await PauseFilterMenuAsync(page, cancellationToken);
                break;
            }
            catch (PlaywrightException)
            {
                // try next
            }
        }

        if (!opened)
        {
            return false;
        }

        var option = await WaitForVisibleLocatorAsync(
            page,
            optionSelectors,
            4_000,
            cancellationToken
        );
        if (option is null)
        {
            return false;
        }

        try
        {
            await PauseFilterMenuAsync(page, cancellationToken);
            await HumanBrowseMenuToOptionAsync(page, option, cancellationToken);
            await page.HumanClickAsync(option, timeout, cancellationToken, isTopControl: true);
            await AfterFilterClickAsync(page, options, cancellationToken);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task AfterFilterClickAsync(
        IPage page,
        AvitoOptions options,
        CancellationToken cancellationToken
    )
    {
        await TryClickApplyFilterAsync(page, options, cancellationToken);
        try
        {
            await page.WaitForLoadStateAsync(
                LoadState.DOMContentLoaded,
                new PageWaitForLoadStateOptions { Timeout = options.NavigationTimeoutMs }
            );
        }
        catch (PlaywrightException)
        {
            // Фильтр мог примениться без перезагрузки.
        }

        await PauseFilterSettleAsync(page, cancellationToken);
    }

    private static async Task<bool> TryClickFirstVisibleAsync(
        IPage page,
        IReadOnlyList<string> selectors,
        int timeoutMs,
        CancellationToken cancellationToken
    )
    {
        foreach (var selector in selectors)
        {
            var locator = await FindVisibleLocatorAsync(page, [selector], cancellationToken);
            if (locator is null)
            {
                continue;
            }

            try
            {
                await PauseFilterMenuAsync(page, cancellationToken);
                await page.HumanClickAsync(locator, timeoutMs, cancellationToken, isTopControl: true);
                return true;
            }
            catch (PlaywrightException)
            {
                // try next
            }
        }

        return false;
    }

    private static async Task<ILocator?> WaitForVisibleLocatorAsync(
        IPage page,
        IReadOnlyList<string> selectors,
        int timeoutMs,
        CancellationToken cancellationToken
    )
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var found = await FindVisibleLocatorAsync(page, selectors, cancellationToken);
            if (found is not null)
            {
                return found;
            }

            await Task.Delay(150, cancellationToken);
        }

        return null;
    }

    private static async Task TryClickApplyFilterAsync(
        IPage page,
        AvitoOptions options,
        CancellationToken cancellationToken
    )
    {
        string[] applySelectors =
        [
            "[data-marker='filters/apply']",
            "[data-marker*='popup'] button:has-text('Показать')",
            "[data-marker*='popup'] button:has-text('Применить')",
            "button:has-text('Показать объявления')",
            "button:has-text('Применить')",
        ];

        var apply = await FindVisibleLocatorAsync(page, applySelectors, cancellationToken);
        if (apply is null)
        {
            return;
        }

        try
        {
            await page.HumanPauseRangeAsync(300, 700, cancellationToken);
            await page.HumanClickAsync(apply, Math.Min(options.NavigationTimeoutMs, 5_000), cancellationToken, isTopControl: true);
        }
        catch (PlaywrightException)
        {
            // Кнопки «Показать» может не быть — фильтр применился сразу.
        }
    }

    private static async Task<bool> TrySetDeliveryToggleAsync(
        IPage page,
        AvitoOptions options,
        bool wantDelivery,
        CancellationToken cancellationToken
    )
    {
        string[] toggleSelectors =
        [
            "[data-marker*='delivery'] input[type='checkbox']",
            "[data-marker*='delivery'][role='switch']",
            "[data-marker*='delivery'][role='checkbox']",
            "label:has-text('Авито Доставк') input[type='checkbox']",
            "label:has-text('С доставкой') input[type='checkbox']",
            "button:has-text('С Авито Доставкой')",
        ];

        var toggle = await FindVisibleLocatorAsync(page, toggleSelectors, cancellationToken);
        if (toggle is null)
        {
            return false;
        }

        var isOn = false;
        try
        {
            isOn = await toggle.EvaluateAsync<bool>(
                """
                el => {
                  if (el.checked === true) return true;
                  if (el.checked === false) return false;
                  const aria = el.getAttribute('aria-checked') || el.getAttribute('aria-pressed');
                  if (aria === 'true') return true;
                  if (aria === 'false') return false;
                  const input = el.querySelector?.('input[type="checkbox"], input[type="radio"]');
                  if (input) return !!input.checked;
                  const cls = (el.className || '').toString().toLowerCase();
                  return cls.includes('checked') || cls.includes('selected') || cls.includes('active');
                }
                """
            );
        }
        catch (PlaywrightException)
        {
            isOn = false;
        }

        if (isOn == wantDelivery)
        {
            return true;
        }

        try
        {
            await page.HumanPauseRangeAsync(400, 900, cancellationToken);
            await page.HumanClickAsync(toggle, Math.Min(options.NavigationTimeoutMs, 8_000), cancellationToken, isTopControl: true);
            await page.WaitForLoadStateAsync(
                LoadState.DOMContentLoaded,
                new PageWaitForLoadStateOptions { Timeout = options.NavigationTimeoutMs }
            );
            await page.HumanPauseRangeAsync(800, 1500, cancellationToken);
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    private static async Task<bool> TryClickByExactTextAsync(
        IPage page,
        IReadOnlyList<string> labels,
        int timeoutMs,
        CancellationToken cancellationToken
    )
    {
        foreach (var label in labels)
        {
            var locator = page.GetByText(label, new PageGetByTextOptions { Exact = true }).First;
            try
            {
                if (await locator.CountAsync() == 0 || !await locator.IsVisibleAsync())
                {
                    continue;
                }

                await page.HumanPauseRangeAsync(400, 800, cancellationToken);
                await page.HumanClickAsync(locator, timeoutMs, cancellationToken, isTopControl: true);
                return true;
            }
            catch (PlaywrightException)
            {
                // try next
            }
        }

        return false;
    }

    private static bool UrlHasQueryValue(string url, string key, string value)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        foreach (
            var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        )
        {
            var eq = pair.IndexOf('=');
            var name = eq < 0 ? pair : pair[..eq];
            if (!name.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var raw = eq < 0 ? string.Empty : pair[(eq + 1)..];
            return string.Equals(raw, value, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static string EscapeForSelector(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);

    private static async Task GoToSearchPageAsync(
        IPage page,
        int targetPage,
        AvitoOptions options,
        CancellationToken cancellationToken
    )
    {
        for (var current = 1; current < targetPage; current++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var nextPage = await FindVisibleLocatorAsync(
                page,
                NextPageSelectors,
                cancellationToken
            );

            if (nextPage is null)
            {
                break;
            }

            await page.HumanClickAsync(nextPage, options.NavigationTimeoutMs, cancellationToken);
            await page.WaitForLoadStateAsync(
                LoadState.DOMContentLoaded,
                new PageWaitForLoadStateOptions { Timeout = options.NavigationTimeoutMs }
            );
            await page.HumanPauseRangeAsync(400, 900, cancellationToken);
        }
    }

    private static async Task<bool> WaitForSearchResultsAsync(
        IPage page,
        int timeoutMs,
        CancellationToken cancellationToken
    )
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await IsOnSearchResultsPageAsync(page))
            {
                return true;
            }

            await page.HumanPauseRangeAsync(250, 400, cancellationToken);
        }

        return false;
    }

    private static async Task<bool> IsOnSearchResultsPageAsync(IPage page)
    {
        // Явная выдача поиска важнее «хвостов» item-view в DOM.
        if (await IsSearchResultsVisibleAsync(page))
        {
            return true;
        }

        // URL поиска с ?q= и полем поиска — тоже выдача (карточки ещё грузятся).
        try
        {
            var url = page.Url;
            if (
                url.Contains("?q=", StringComparison.OrdinalIgnoreCase)
                || url.Contains("&q=", StringComparison.OrdinalIgnoreCase)
            )
            {
                var input = page.Locator("input[data-marker='search-form/input']").First;
                if (await input.CountAsync() > 0 && await input.IsVisibleAsync())
                {
                    return true;
                }
            }
        }
        catch (PlaywrightException) { }

        if (LooksLikeListingPage(page.Url))
        {
            return false;
        }

        return false;
    }

    private static async Task<bool> IsListingContentVisibleAsync(IPage page)
    {
        foreach (var selector in ListingOpenedSelectors)
        {
            try
            {
                var locator = page.Locator(selector).First;

                if (await locator.CountAsync() > 0 && await locator.IsVisibleAsync())
                {
                    return true;
                }
            }
            catch (PlaywrightException) { }
        }

        return false;
    }

    private static async Task<bool> IsSearchResultsVisibleAsync(IPage page)
    {
        foreach (var selector in SearchResultSelectors)
        {
            var locator = page.Locator(selector).First;

            try
            {
                if (await locator.CountAsync() > 0 && await locator.IsVisibleAsync())
                {
                    return true;
                }
            }
            catch (PlaywrightException) { }
        }

        return false;
    }

    private static async Task<ILocator?> FindVisibleLocatorAsync(
        IPage page,
        IEnumerable<string> selectors,
        CancellationToken cancellationToken
    )
    {
        foreach (var selector in selectors)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var locator = page.Locator(selector);
            int count;
            try
            {
                count = await locator.CountAsync();
            }
            catch (PlaywrightException)
            {
                continue;
            }

            for (var i = 0; i < count; i++)
            {
                var candidate = locator.Nth(i);
                try
                {
                    if (await candidate.IsVisibleAsync())
                    {
                        return candidate;
                    }
                }
                catch (PlaywrightException) { }
            }
        }

        return null;
    }
}

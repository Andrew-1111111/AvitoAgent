using System.Globalization;
using System.Text.RegularExpressions;
using AvitoAgent.Core.Models;
using Microsoft.Playwright;

namespace AvitoAgent.Avito.Parsing;

internal static partial class AvitoSearchParser
{
    /// <summary>
    /// Разделитель выдачи: «в других городах» / «в других категориях» / «смотрите также».
    /// null — нет; -1 — баннер над всей выдачей; иначе Y разделителя (брать карточки строго выше).
    /// </summary>
    private const string FindSerpCutoffBannerTopScript = """
        () => {
          const re = /(?:есть\s+в\s+)?других\s+(?:городах|категори\w*|раздел\w*)|смотрите\s+также|похожие\s+объявлен\w*|объявлен\w*\s+из\s+других\s+категори/i;
          const nodes = document.querySelectorAll('h1, h2, h3, p, span, div, button, a, section, li');
          const banners = [];
          for (const el of nodes) {
            if (el.children.length > 8) continue;
            const text = (el.textContent || '').replace(/\s+/g, ' ').trim();
            if (text.length < 10 || text.length > 180) continue;
            if (!re.test(text)) continue;
            const rect = el.getBoundingClientRect();
            if (rect.height < 8 || rect.width < 40) continue;
            if (rect.bottom < 0 || rect.top > (window.innerHeight + 4000)) continue;
            banners.push(rect.top + window.scrollY);
          }
          if (!banners.length) return null;

          const items = [...document.querySelectorAll(
            '[data-marker="catalog-serp"] [data-marker="item"][data-item-id], [data-marker="catalog-serp"] [data-item-id]'
          )].filter(el => !el.closest('[data-marker*="extra"], [data-marker*="recommend"], [data-marker*="similar"], [data-marker*="advice"]'));
          const itemTops = [];
          for (const el of items) {
            const rect = el.getBoundingClientRect();
            if (rect.height < 40) continue;
            itemTops.push(rect.top + window.scrollY);
          }
          if (!itemTops.length) return -1;

          banners.sort((a, b) => a - b);
          for (const bannerTop of banners) {
            const above = itemTops.some(top => top + 16 < bannerTop);
            const below = itemTops.some(top => top >= bannerTop);
            if (above && below) return bannerTop;
          }

          if (banners.some(b => itemTops.every(top => top + 16 >= b))) return -1;
          return null;
        }
        """;

    private const string ListItemIdsScript = """
        (bannerTop) => {
          const items = [...document.querySelectorAll(
            '[data-marker="catalog-serp"] [data-marker="item"][data-item-id], [data-marker="catalog-serp"] [data-item-id]'
          )];
          const rows = [];
          for (const el of items) {
            if (el.closest('[data-marker*="extra"], [data-marker*="recommend"], [data-marker*="similar"], [data-marker*="advice"]')) {
              continue;
            }
            const id = el.getAttribute('data-item-id');
            if (!id) continue;
            const rect = el.getBoundingClientRect();
            if (rect.height < 40) continue;
            const top = rect.top + window.scrollY;
            if (bannerTop != null && top + 16 >= bannerTop) continue;
            rows.push({ id, top });
          }
          rows.sort((a, b) => a.top - b.top);
          return [...new Set(rows.map(row => row.id))];
        }
        """;

    public static async Task<bool> HasOtherCitiesBannerAsync(IPage page)
    {
        var marker = await FindSerpCutoffBannerTopAsync(page);
        return marker is not null;
    }

    public static async Task<IReadOnlyList<Listing>> ParseSearchResultsAsync(
        IPage page,
        CancellationToken cancellationToken
    )
    {
        var bannerTop = await FindSerpCutoffBannerTopAsync(page);

        // -1: релевантной выдачи нет (только «другие города/категории»).
        if (bannerTop is < 0)
        {
            return [];
        }

        // Всегда берём id только из основной ленты (catalog-serp), без extra.
        return await ParseItemsByIdsAsync(
            page,
            await ListItemIdsAsync(page, bannerTop),
            cancellationToken
        );
    }

    private static async Task<double?> FindSerpCutoffBannerTopAsync(IPage page)
    {
        try
        {
            return await page.EvaluateAsync<double?>(FindSerpCutoffBannerTopScript);
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }

    private static async Task<IReadOnlyList<string>> ListItemIdsAsync(IPage page, double? bannerTop)
    {
        try
        {
            var ids = await page.EvaluateAsync<string[]>(ListItemIdsScript, bannerTop);
            return ids ?? [];
        }
        catch (PlaywrightException)
        {
            return [];
        }
    }

    private static async Task<IReadOnlyList<Listing>> ParseItemsByIdsAsync(
        IPage page,
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken
    )
    {
        var listings = new List<Listing>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
            {
                continue;
            }

            var element = await page.QuerySelectorAsync($"[data-item-id='{id}']");
            if (element is null)
            {
                continue;
            }

            var listing = await ParseCardAsync(element, page);
            if (listing is not null)
            {
                listings.Add(listing);
            }
        }

        return listings;
    }

    private static async Task<Listing?> ParseCardAsync(IElementHandle element, IPage page)
    {
        var id =
            await element.GetAttributeAsync("data-item-id")
            ?? await element.GetAttributeAsync("id");

        var titleElement =
            await element.QuerySelectorAsync("[data-marker='item-title']")
            ?? await element.QuerySelectorAsync("h3")
            ?? await element.QuerySelectorAsync("a[itemprop='name']");

        var title = titleElement is null ? null : (await titleElement.InnerTextAsync())?.Trim();

        var linkElement = titleElement ?? await element.QuerySelectorAsync("a[href*='/']");
        var href = linkElement is null ? null : await linkElement.GetAttributeAsync("href");
        var url = NormalizeUrl(page, href);

        if (string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(url))
        {
            id = ExtractIdFromUrl(url);
        }

        id = NormalizeListingId(id);

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var priceElement =
            await element.QuerySelectorAsync("[data-marker='item-price']")
            ?? await element.QuerySelectorAsync("[itemprop='price']")
            ?? await element.QuerySelectorAsync("meta[itemprop='price']");

        var priceText = priceElement is null
            ? null
            : await priceElement.GetAttributeAsync("content")
                ?? await priceElement.InnerTextAsync();

        var locationElement =
            await element.QuerySelectorAsync("[data-marker='item-address']")
            ?? await element.QuerySelectorAsync("[class*='geo-root']");

        var location = locationElement is null
            ? string.Empty
            : (await locationElement.InnerTextAsync())?.Trim() ?? string.Empty;

        var publishedAt = await ReadPublishedAtAsync(element);

        var imageElement = await element.QuerySelectorAsync("img");
        var imageUrl = imageElement is null
            ? null
            : await imageElement.GetAttributeAsync("src")
                ?? await imageElement.GetAttributeAsync("data-src");

        if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            imageUrl = UpgradePreviewImageUrl(NormalizeImageUrl(imageUrl));
        }

        return new Listing
        {
            Id = id,
            Title = title,
            Price = ParsePrice(priceText),
            Url = url ?? string.Empty,
            Location = location,
            Source = "Avito",
            PublishedAt = publishedAt,
            ImageUrls = string.IsNullOrWhiteSpace(imageUrl) ? [] : [imageUrl],
        };
    }

    private static async Task<DateTime?> ReadPublishedAtAsync(IElementHandle element)
    {
        foreach (
            var selector in new[]
            {
                "[data-marker='item-date']",
                "[data-marker='item-view/item-date']",
                "time",
                "[class*='item-date']",
                "p[class*='date']",
                "span[class*='date']",
            }
        )
        {
            var dateElement = await element.QuerySelectorAsync(selector);
            if (dateElement is null)
            {
                continue;
            }

            var fromAttribute = AvitoPublishedAtParser.Parse(
                await dateElement.GetAttributeAsync("datetime")
                    ?? await dateElement.GetAttributeAsync("content")
                    ?? await dateElement.GetAttributeAsync("title")
            );

            if (fromAttribute.HasValue)
            {
                return fromAttribute;
            }

            var fromText = AvitoPublishedAtParser.Parse(await dateElement.InnerTextAsync());
            if (fromText.HasValue)
            {
                return fromText;
            }
        }

        return null;
    }

    private static string NormalizeImageUrl(string url)
    {
        if (url.StartsWith("//", StringComparison.Ordinal))
        {
            return "https:" + url;
        }

        return url;
    }

    private static string UpgradePreviewImageUrl(string url)
    {
        foreach (
            var size in new[]
            {
                "140x105",
                "208x156",
                "240x180",
                "320x240",
                "432x324",
                "480x360",
                "640x480",
                "644x483",
                "832x624",
            }
        )
        {
            if (url.Contains(size, StringComparison.OrdinalIgnoreCase))
            {
                return url.Replace(size, "1280x960", StringComparison.OrdinalIgnoreCase);
            }
        }

        return url;
    }

    private static string? NormalizeUrl(IPage page, string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return href;
        }

        var baseUri = new Uri(page.Url);
        return new Uri(baseUri, href).ToString();
    }

    private static string ExtractIdFromUrl(string url)
    {
        var match = IdRegex().Match(url);
        return match.Success ? match.Value : url.GetHashCode(StringComparison.Ordinal).ToString();
    }

    private static string NormalizeListingId(string? rawId)
    {
        if (string.IsNullOrWhiteSpace(rawId))
        {
            return string.Empty;
        }

        var match = IdRegex().Match(rawId);
        return match.Success ? match.Value : rawId.Trim();
    }

    private static decimal? ParsePrice(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var digits = string
            .Concat(text.Where(ch => char.IsDigit(ch) || ch is '.' or ','))
            .Replace(" ", string.Empty)
            .Replace(',', '.');

        return decimal.TryParse(
            digits,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var price
        )
            ? price
            : null;
    }

    [GeneratedRegex(@"\d{6,12}")]
    private static partial Regex IdRegex();
}

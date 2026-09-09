using AvitoAgent.Playwright.Extensions;
using AvitoAgent.Shared.Configuration;
using Microsoft.Playwright;

namespace AvitoAgent.Avito.Parsing;

internal sealed class AvitoDetailResult
{
    public string Description { get; init; } = string.Empty;

    public IReadOnlyList<string> ImageUrls { get; init; } = [];

    /// <summary>Сколько слайдов в галерее Avito (до лимита сбора).</summary>
    public int GalleryTotalCount { get; init; }

    public string SellerName { get; init; } = string.Empty;

    public string Location { get; init; } = string.Empty;
}

internal static class AvitoDetailParser
{
    /// <summary>Максимум времени на странице объявления до готовности данных для LM Studio.</summary>
    private const int DetailBudgetMs = 25_000;

    private static readonly string[] DescriptionSelectors =
    [
        "[data-marker='item-view/item-description']",
        "[data-marker='item-view/description']",
        "[itemprop='description']",
    ];

    private static readonly string[] ReadMoreSelectors =
    [
        "[data-marker='item-view/item-description'] >> text=/читать полностью/i",
        "[data-marker='item-view/description'] >> text=/читать полностью/i",
        "button:has-text('Читать полностью')",
        "span:has-text('Читать полностью')",
        "a:has-text('Читать полностью')",
        "[role='button']:has-text('Читать полностью')",
        "[data-marker='item-view/item-description'] button",
        "[data-marker='item-view/item-description'] span[class*='expand']",
        "[data-marker='item-view/item-description'] span[class*='more']",
    ];

    public static async Task<AvitoDetailResult> ParseAsync(
        IPage page,
        AvitoOptions options,
        CancellationToken cancellationToken
    )
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(DetailBudgetMs);

        await page.HumanPauseAsync(
            options.DetailGalleryInitialDelayMs,
            options.DetailGalleryInitialDelayJitterMs,
            cancellationToken
        );

        // Сначала полное описание — иначе бюджет галереи мог съесть время и пропустить «Читать полностью».
        await ExpandDescriptionAsync(page, cancellationToken);
        var description = await ReadDescriptionAsync(page);

        var gallery = await AvitoGalleryParser.CollectPhotosAsync(page, options, cancellationToken);

        string seller = string.Empty;
        string location = string.Empty;
        if (DateTime.UtcNow < deadline)
        {
            seller = await ReadFirstTextAsync(
                page,
                ["[data-marker='seller-info/name']", "[data-marker='seller-link/link']"]
            );
            location = await ReadFirstTextAsync(
                page,
                ["[data-marker='item-view/item-address']", "[itemprop='address']"]
            );
        }

        return new AvitoDetailResult
        {
            Description = description,
            SellerName = seller,
            Location = location,
            ImageUrls = gallery.Urls,
            GalleryTotalCount = gallery.TotalCount,
        };
    }

    private static async Task ExpandDescriptionAsync(
        IPage page,
        CancellationToken cancellationToken
    )
    {
        foreach (var selector in DescriptionSelectors)
        {
            var description = page.Locator(selector).First;

            try
            {
                if (await description.CountAsync() == 0)
                {
                    continue;
                }

                await description.ScrollIntoViewIfNeededAsync();
                await page.HumanPauseRangeAsync(120, 280, cancellationToken);
                break;
            }
            catch (PlaywrightException) { }
        }

        foreach (var selector in ReadMoreSelectors)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var button = page.Locator(selector).First;

            try
            {
                if (await button.CountAsync() == 0 || !await button.IsVisibleAsync())
                {
                    continue;
                }

                var label = (await button.InnerTextAsync())?.Trim() ?? string.Empty;
                if (
                    label.Length > 0
                    && !label.Contains("Читать полностью", StringComparison.OrdinalIgnoreCase)
                    && !selector.Contains("Читать полностью", StringComparison.OrdinalIgnoreCase)
                    && !selector.Contains("читать полностью", StringComparison.OrdinalIgnoreCase)
                )
                {
                    // Широкие селекторы button/span — только если текст про раскрытие.
                    continue;
                }

                var before = await ReadDescriptionAsync(page);
                await page.HumanClickAsync(button, 2_000, cancellationToken);
                await WaitDescriptionExpandedAsync(page, before, cancellationToken);
                return;
            }
            catch (PlaywrightException) { }
        }
    }

    private static async Task WaitDescriptionExpandedAsync(
        IPage page,
        string before,
        CancellationToken cancellationToken
    )
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stillVisible = false;
            try
            {
                stillVisible = await page.GetByText("Читать полностью", new PageGetByTextOptions
                {
                    Exact = false,
                })
                    .First.IsVisibleAsync();
            }
            catch (PlaywrightException) { }

            var after = await ReadDescriptionAsync(page);
            if (!stillVisible || after.Length > before.Length + 20)
            {
                await page.HumanPauseRangeAsync(150, 320, cancellationToken);
                return;
            }

            await page.HumanPauseRangeAsync(80, 150, cancellationToken);
        }
    }

    private static async Task<string> ReadDescriptionAsync(IPage page)
    {
        var text = await ReadFirstTextAsync(page, DescriptionSelectors);
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // Убираем хвост кнопки, если остался в InnerText.
        const string marker = "Читать полностью";
        var idx = text.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            text = text[..idx].TrimEnd();
        }

        return text.Trim();
    }

    private static async Task<string> ReadFirstTextAsync(IPage page, IEnumerable<string> selectors)
    {
        foreach (var selector in selectors)
        {
            var element = await page.QuerySelectorAsync(selector);

            if (element is null)
            {
                continue;
            }

            var text = (await element.InnerTextAsync())?.Trim();

            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return string.Empty;
    }
}

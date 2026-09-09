using System.Security.Cryptography;
using AvitoAgent.Avito.Logging;
using AvitoAgent.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AvitoAgent.Avito.Parsing;

/// <summary>
/// Качает полноразмерные фото через контекст открытой вкладки (Referer, cookies).
/// Отдельный HttpClient к CDN Avito часто отвечает 400/403.
/// </summary>
internal static class AvitoPageImageDownloader
{
    public static async Task<IReadOnlyList<ListingPhoto>> DownloadAsync(
        IPage page,
        IReadOnlyList<string> imageUrls,
        ILogger logger,
        CancellationToken cancellationToken = default,
        int maxPhotos = 5
    )
    {
        var limit = maxPhotos <= 0 ? 50 : Math.Clamp(maxPhotos, 1, 50);
        var photos = new List<ListingPhoto>(limit);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenHashes = new HashSet<string>(StringComparer.Ordinal);
        var referer = string.IsNullOrWhiteSpace(page.Url) ? "https://www.avito.ru/" : page.Url;

        foreach (var rawUrl in imageUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (photos.Count >= limit)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                continue;
            }

            var url = AvitoImageUrl.NormalizeAndUpgrade(rawUrl) ?? rawUrl.Trim();
            if (AvitoImageUrl.IsPreview(url) || !seenIds.Add(url))
            {
                continue;
            }

            var downloaded = await TryDownloadUrlAsync(page, url, referer, logger);
            if (downloaded is null && !string.Equals(url, rawUrl.Trim(), StringComparison.Ordinal))
            {
                downloaded = await TryDownloadUrlAsync(page, rawUrl.Trim(), referer, logger);
            }

            if (downloaded is null || downloaded.Content.Length < 1_500)
            {
                continue;
            }

            var hash = Convert.ToHexString(SHA256.HashData(downloaded.Content));
            if (!seenHashes.Add(hash))
            {
                continue;
            }

            photos.Add(
                new ListingPhoto
                {
                    SortOrder = photos.Count,
                    Content = downloaded.Content,
                    ContentType = downloaded.ContentType,
                }
            );
        }

        return photos;
    }

    private static async Task<ListingPhoto?> TryDownloadUrlAsync(
        IPage page,
        string url,
        string referer,
        ILogger logger
    )
    {
        try
        {
            var response = await page.APIRequest.GetAsync(
                url,
                new APIRequestContextOptions
                {
                    Timeout = 20_000,
                    Headers = new Dictionary<string, string>
                    {
                        ["Referer"] = referer,
                        ["Accept"] = "image/avif,image/webp,image/apng,image/*,*/*;q=0.8",
                    },
                }
            );

            if (!response.Ok)
            {
                AvitoLog.ImageDownloadRejected(logger, url, response.Status);
                return null;
            }

            var contentType = ReadContentType(response);
            if (
                !string.IsNullOrWhiteSpace(contentType)
                && !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                && !contentType.Contains("octet-stream", StringComparison.OrdinalIgnoreCase)
            )
            {
                AvitoLog.ImageDownloadRejected(logger, url, response.Status);
                return null;
            }

            var content = await response.BodyAsync();
            if (content.Length == 0)
            {
                return null;
            }

            return new ListingPhoto
            {
                Content = content,
                ContentType = NormalizeContentType(contentType),
            };
        }
        catch (PlaywrightException ex)
        {
            AvitoLog.ImageDownloadViaBrowserFailed(logger, url, ex);
            return null;
        }
    }

    private static string NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return "image/jpeg";
        }

        var trimmed = contentType.Split(';')[0].Trim();
        if (
            trimmed.Contains("octet-stream", StringComparison.OrdinalIgnoreCase)
            || !trimmed.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
        )
        {
            return "image/jpeg";
        }

        return trimmed;
    }

    private static string ReadContentType(IAPIResponse response)
    {
        foreach (var header in response.Headers)
        {
            if (header.Key.Equals("content-type", StringComparison.OrdinalIgnoreCase))
            {
                return header.Value;
            }
        }

        return string.Empty;
    }
}

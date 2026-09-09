using AvitoAgent.Core.Interfaces;
using AvitoAgent.Core.Models;
using AvitoAgent.Shared;
using AvitoAgent.Shared.Configuration;
using AvitoAgent.Storage.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AvitoAgent.Storage.Services;

public sealed class ListingPhotoArchiver(
    IHttpClientFactory httpClientFactory,
    ApplicationPaths paths,
    IOptions<DebugOptions> debug,
    ILogger<ListingPhotoArchiver> logger
) : IListingPhotoArchiver
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ApplicationPaths _paths = paths;
    private readonly DebugOptions _debug = debug.Value;
    private readonly ILogger<ListingPhotoArchiver> _logger = logger;

    public async Task<int> ArchiveAsync(
        string listingId,
        IReadOnlyList<string> imageUrls,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !_debug.ExportListingPhotos
            || string.IsNullOrWhiteSpace(listingId)
            || imageUrls.Count == 0
        )
        {
            return 0;
        }

        var photos = await DownloadAsync(listingId, imageUrls, cancellationToken);
        if (photos.Count == 0)
        {
            return 0;
        }

        await ExportToDiskAsync(listingId, photos, cancellationToken);
        return photos.Count;
    }

    public async Task<int> ArchiveAsync(
        string listingId,
        IReadOnlyList<ListingPhoto> photos,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_debug.ExportListingPhotos || string.IsNullOrWhiteSpace(listingId))
        {
            return 0;
        }

        if (photos.Count == 0)
        {
            StorageLog.ImagesExportSkipped(_logger, listingId);
            return 0;
        }

        await ExportToDiskAsync(listingId, photos, cancellationToken);
        return photos.Count;
    }

    private async Task<IReadOnlyList<ListingPhoto>> DownloadAsync(
        string listingId,
        IReadOnlyList<string> imageUrls,
        CancellationToken cancellationToken
    )
    {
        var client = _httpClientFactory.CreateClient("Avito");
        var photos = new List<ListingPhoto>(imageUrls.Count);

        for (var i = 0; i < imageUrls.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var url = imageUrls[i];

            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Referrer = new Uri("https://www.avito.ru/");
                request.Headers.TryAddWithoutValidation(
                    "Accept",
                    "image/avif,image/webp,image/apng,image/*,*/*;q=0.8"
                );

                using var response = await client.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);

                if (content.Length == 0)
                {
                    continue;
                }

                photos.Add(
                    new ListingPhoto
                    {
                        SortOrder = photos.Count,
                        Content = content,
                        ContentType =
                            response.Content.Headers.ContentType?.MediaType
                            ?? GuessContentType(url),
                    }
                );
            }
            catch (Exception ex)
            {
                StorageLog.ImageDownloadFailed(_logger, listingId, url, ex);
            }
        }

        return photos;
    }

    private async Task ExportToDiskAsync(
        string listingId,
        IReadOnlyList<ListingPhoto> photos,
        CancellationToken cancellationToken
    )
    {
        var directory = Path.Combine(ResolveExportRoot(), SanitizeFolderName(listingId));

        try
        {
            Directory.CreateDirectory(directory);

            var written = 0;

            for (var i = 0; i < photos.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var photo = photos[i];
                if (photo.Content is not { Length: > 0 })
                {
                    continue;
                }

                var name = $"{i + 1:00}{ExtensionFromContentType(photo.ContentType)}";
                await File.WriteAllBytesAsync(
                    Path.Combine(directory, name),
                    photo.Content,
                    cancellationToken
                );
                written++;
            }

            if (written > 0)
            {
                StorageLog.ImagesExported(_logger, listingId, directory, written);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            StorageLog.ImageExportFailed(
                _logger,
                listingId,
                directory,
                BrowserErrorText.Describe(ex)
            );
        }
    }

    private string ResolveExportRoot()
    {
        var configured = string.IsNullOrWhiteSpace(_debug.ListingPhotosDirectory)
            ? "debug/listing-photos"
            : _debug.ListingPhotosDirectory;

        return Path.IsPathRooted(configured) ? configured : Path.Combine(_paths.Root, configured);
    }

    private static string SanitizeFolderName(string listingId)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = listingId.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var name = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(name) ? "unknown" : name;
    }

    private static string ExtensionFromContentType(string contentType)
    {
        if (contentType.Contains("png", StringComparison.OrdinalIgnoreCase))
        {
            return ".png";
        }

        if (contentType.Contains("webp", StringComparison.OrdinalIgnoreCase))
        {
            return ".webp";
        }

        if (contentType.Contains("gif", StringComparison.OrdinalIgnoreCase))
        {
            return ".gif";
        }

        return ".jpg";
    }

    private static string GuessContentType(string url)
    {
        if (url.Contains(".png", StringComparison.OrdinalIgnoreCase))
        {
            return "image/png";
        }

        if (url.Contains(".webp", StringComparison.OrdinalIgnoreCase))
        {
            return "image/webp";
        }

        return "image/jpeg";
    }
}

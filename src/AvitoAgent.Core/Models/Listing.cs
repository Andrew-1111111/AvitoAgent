namespace AvitoAgent.Core.Models;

public sealed class Listing
{
    public string Id { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public decimal? Price { get; init; }

    public string Currency { get; init; } = "RUB";

    public string Url { get; init; } = string.Empty;

    public string Location { get; init; } = string.Empty;

    public string SellerName { get; init; } = string.Empty;

    public DateTime? PublishedAt { get; init; }

    public DateTime FirstSeenAt { get; init; } = DateTime.UtcNow;

    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    public IReadOnlyList<string> ImageUrls { get; init; } = [];

    /// <summary>
    /// Байты фото, уже скачанные через браузер (без повторного запроса к CDN).
    /// </summary>
    public IReadOnlyList<ListingPhoto> Images { get; init; } = [];

    public string Source { get; init; } = string.Empty;
}

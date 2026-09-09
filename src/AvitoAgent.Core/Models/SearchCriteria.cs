namespace AvitoAgent.Core.Models;

public sealed record SearchCriteria
{
    public IReadOnlyList<string> Keywords { get; init; } = [];

    public decimal? MinPrice { get; init; }

    public decimal? MaxPrice { get; init; }

    public IReadOnlyList<string> ExcludedKeywords { get; init; } = [];

    public int MaxResults { get; init; } = 100;

    public IReadOnlyList<string> LocationSlugs { get; init; } = ["rossiya"];

    public string Sort { get; init; } = "По дате";

    public bool DeliveryOnly { get; init; }

    public string Condition { get; init; } = "Все";

    public string SellerType { get; init; } = "Все";

    /// <summary>
    /// UTC: брать объявления не старше этого момента. null — без фильтра по дате.
    /// </summary>
    public DateTime? PublishedAfterUtc { get; init; }
}

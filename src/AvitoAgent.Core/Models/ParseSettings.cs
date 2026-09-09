namespace AvitoAgent.Core.Models;

public sealed record ParseSettings
{
    public IReadOnlyList<string> Keywords { get; init; } = [];

    public IReadOnlyList<string> ExcludedKeywords { get; init; } = [];

    public int MinPrice { get; init; }

    public int MaxPrice { get; init; }

    public int MaxResults { get; init; } = 50;

    public IReadOnlyList<string> LocationSlugs { get; init; } = ["rossiya"];

    public string Sort { get; init; } = "По дате";

    public bool DeliveryOnly { get; init; }

    public string Condition { get; init; } = "Все";

    public string SellerType { get; init; } = "Все";

    /// <summary>
    /// Только объявления с даты/времени старта поиска.
    /// </summary>
    public bool FromCurrentDateTime { get; init; }

    /// <summary>
    /// Пауза между циклами поиска, минуты.
    /// </summary>
    public int PollingIntervalMinutes { get; init; } = 5;

    /// <summary>
    /// Час начала сна (0–23). null — сон выключен.
    /// </summary>
    public int? SleepFromHour { get; init; }

    /// <summary>
    /// Час окончания сна (0–23). Можно меньше From (через полночь).
    /// </summary>
    public int? SleepToHour { get; init; }
}

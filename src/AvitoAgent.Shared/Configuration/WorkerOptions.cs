namespace AvitoAgent.Shared.Configuration;

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    public int PollingIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// Час начала сна агента (0-23). null - без окна сна.
    /// </summary>
    public int? SleepFromHour { get; set; }

    /// <summary>
    /// Час окончания сна (0-23). Можно через полночь, например From=23, To=7.
    /// </summary>
    public int? SleepToHour { get; set; }

    /// <summary>
    /// Часовой пояс для окна сна (IANA), например Europe/Moscow.
    /// </summary>
    public string TimezoneId { get; set; } = "Europe/Moscow";

    public string[] Keywords { get; set; } = [];

    public string[] ExcludedKeywords { get; set; } = [];

    /// <summary>
    /// Минимальная цена, ₽. 0 - без нижней границы. Диапазон: 0…int.MaxValue.
    /// </summary>
    public int MinPrice { get; set; }

    /// <summary>
    /// Максимальная цена, ₽. 0 - без верхней границы. Диапазон: 0…int.MaxValue.
    /// </summary>
    public int MaxPrice { get; set; }

    public int GetMinPrice() => Math.Clamp(MinPrice, 0, int.MaxValue);

    public int GetMaxPrice() => Math.Clamp(MaxPrice, 0, int.MaxValue);

    public int MaxResults { get; set; } = 30;

    /// <summary>
    /// Минимальный score соответствия поисковому запросу (0-100) для отправки в Telegram.
    /// </summary>
    public int MinAuthenticityScore { get; set; } = 60;
}

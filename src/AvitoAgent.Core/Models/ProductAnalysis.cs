namespace AvitoAgent.Core.Models;

public sealed class ProductAnalysis
{
    public int Score { get; init; }

    public bool IsRelevant { get; init; }

    public bool? IsAuthentic { get; init; }

    public int AuthenticityScore { get; init; }

    public string Brand { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string Condition { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;

    public IReadOnlyList<string> DetectedFeatures { get; init; } = [];

    public IReadOnlyList<string> CounterfeitIndicators { get; init; } = [];

    /// <summary>Идентификатор модели в LM Studio (для подписи в Telegram).</summary>
    public string AnalyzerModel { get; init; } = string.Empty;
}

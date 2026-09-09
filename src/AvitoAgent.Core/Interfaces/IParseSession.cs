using AvitoAgent.Core.Models;

namespace AvitoAgent.Core.Interfaces;

public interface IParseSession
{
    bool IsRunning { get; }

    CancellationToken RunningToken { get; }

    /// <summary>
    /// Нижняя граница даты публикации (UTC), если включён режим «с текущей даты и времени».
    /// </summary>
    DateTime? PublishedAfterUtc { get; }

    ParseSettings Snapshot();

    Task WaitUntilRunningAsync(CancellationToken cancellationToken);

    bool TryStart(out string? error);

    void Stop();

    void SetKeywords(IReadOnlyList<string> keywords);

    void AppendKeywords(IReadOnlyList<string> keywords);

    void SetExcludedKeywords(IReadOnlyList<string> keywords);

    void SetPrice(int minPrice, int maxPrice);

    void SetLocations(IReadOnlyList<string> slugs);

    void SetSort(string sort);

    void SetDeliveryOnly(bool deliveryOnly);

    void SetCondition(string condition);

    void SetSellerType(string sellerType);

    void SetFromCurrentDateTime(bool enabled);

    void SetPollingIntervalMinutes(int minutes);

    void SetSleepHours(int? fromHour, int? toHour);
}

using AvitoAgent.Shared.Configuration;

namespace AvitoAgent.Avito;

internal static class AvitoNavigationDelays
{
    public static int WithJitter(int baseMs, int jitterMs) =>
        baseMs + (jitterMs > 0 ? Random.Shared.Next(0, jitterMs + 1) : 0);

    public static Task DelayAsync(int baseMs, int jitterMs, CancellationToken cancellationToken)
    {
        var delay = WithJitter(baseMs, jitterMs);

        return delay > 0 ? Task.Delay(delay, cancellationToken) : Task.CompletedTask;
    }

    public static Task DelayAfterDetailAsync(
        AvitoOptions options,
        CancellationToken cancellationToken
    ) =>
        DelayAsync(options.DetailDelayAfterMs, options.DetailDelayAfterJitterMs, cancellationToken);

    public static Task DelayBetweenSearchPagesAsync(
        AvitoOptions options,
        CancellationToken cancellationToken
    ) => DelayAsync(options.SearchPageDelayMs, options.SearchPageDelayJitterMs, cancellationToken);

    public static Task DelayOnPageSettleAsync(
        AvitoOptions options,
        CancellationToken cancellationToken
    ) =>
        DelayAsync(options.DetailPageSettleMs, options.DetailPageSettleJitterMs, cancellationToken);

    /// <summary>
    /// Пауза на открытом объявлении перед возвратом к выдаче.
    /// </summary>
    public static Task DelayOnListingBeforeReturnAsync(CancellationToken cancellationToken) =>
        Task.Delay(Random.Shared.Next(1_200, 2_800), cancellationToken);

    public static Task SearchWarmupAsync(
        AvitoOptions options,
        CancellationToken cancellationToken
    ) => DelayAsync(options.SearchWarmupMs, options.SearchWarmupJitterMs, cancellationToken);
}

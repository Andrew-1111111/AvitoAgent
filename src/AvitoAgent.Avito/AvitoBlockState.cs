namespace AvitoAgent.Avito;

/// <summary>
/// Счётчик блокировок Avito (Qrator отдаёт 429 «Доступ ограничен: проблема с IP»).
/// Капча снимает блок только для текущего браузера; если сразу продолжить в прежнем темпе,
/// тот же IP банится снова. Поэтому пауза после капчи растёт с каждым повтором.
/// </summary>
internal static class AvitoBlockState
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);
    private static readonly TimeSpan MaxCooldown = TimeSpan.FromMinutes(30);
    private static readonly List<DateTime> Blocks = [];
    private static readonly Lock Gate = new();

    /// <summary>Регистрирует блокировку и возвращает их число за последний час.</summary>
    public static int RegisterBlock()
    {
        lock (Gate)
        {
            var now = DateTime.UtcNow;
            Blocks.RemoveAll(moment => now - moment > Window);
            Blocks.Add(now);
            return Blocks.Count;
        }
    }

    /// <summary>
    /// Пауза после снятия блокировки: базовая задержка, удвоенная за каждый повтор в течение часа.
    /// </summary>
    public static TimeSpan ResolveCooldown(int baseDelayMs)
    {
        var repeats = RecentBlockCount();
        if (baseDelayMs <= 0 || repeats <= 0)
        {
            return TimeSpan.FromMilliseconds(Math.Max(0, baseDelayMs));
        }

        var factor = 1L << Math.Min(repeats - 1, 8);
        var milliseconds = Math.Min((long)baseDelayMs * factor, (long)MaxCooldown.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private static int RecentBlockCount()
    {
        lock (Gate)
        {
            var now = DateTime.UtcNow;
            Blocks.RemoveAll(moment => now - moment > Window);
            return Blocks.Count;
        }
    }
}

namespace AvitoAgent.Core;

/// <summary>
/// Окно «сна» агента по часам локальной зоны (включительно с From, до To не включая).
/// Поддерживает переход через полночь (например 23→7).
/// </summary>
public static class SleepSchedule
{
    /// <summary>
    /// true — сейчас сон; <paramref name="localUntil"/> — локальный момент окончания окна.
    /// </summary>
    public static bool TryGetActiveUntil(
        int? fromHour,
        int? toHour,
        TimeZoneInfo timeZone,
        DateTime utcNow,
        out DateTime localUntil
    )
    {
        localUntil = default;
        if (!IsConfigured(fromHour, toHour))
        {
            return false;
        }

        var from = fromHour!.Value;
        var to = toHour!.Value;
        var utc = utcNow.Kind switch
        {
            DateTimeKind.Utc => utcNow,
            DateTimeKind.Local => utcNow.ToUniversalTime(),
            _ => DateTime.SpecifyKind(utcNow, DateTimeKind.Utc),
        };

        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone);
        if (!IsInWindow(local.Hour, from, to))
        {
            return false;
        }

        localUntil = NextLocalBoundary(local, to);
        return true;
    }

    public static bool IsConfigured(int? fromHour, int? toHour) =>
        fromHour is >= 0 and <= 23 && toHour is >= 0 and <= 23 && fromHour != toHour;

    public static bool IsInWindow(int hour, int fromHour, int toHour)
    {
        if (fromHour < toHour)
        {
            return hour >= fromHour && hour < toHour;
        }

        return hour >= fromHour || hour < toHour;
    }

    private static DateTime NextLocalBoundary(DateTime local, int toHour)
    {
        var candidate = new DateTime(
            local.Year,
            local.Month,
            local.Day,
            toHour,
            0,
            0,
            DateTimeKind.Unspecified
        );
        if (candidate <= local)
        {
            candidate = candidate.AddDays(1);
        }

        return candidate;
    }
}

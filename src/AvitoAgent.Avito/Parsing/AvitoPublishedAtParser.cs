using System.Globalization;
using System.Text.RegularExpressions;

namespace AvitoAgent.Avito.Parsing;

internal static partial class AvitoPublishedAtParser
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    private static readonly Dictionary<string, int> Months = new(StringComparer.OrdinalIgnoreCase)
    {
        ["января"] = 1,
        ["январь"] = 1,
        ["янв"] = 1,
        ["янв."] = 1,
        ["февраля"] = 2,
        ["февраль"] = 2,
        ["фев"] = 2,
        ["фев."] = 2,
        ["марта"] = 3,
        ["март"] = 3,
        ["мар"] = 3,
        ["мар."] = 3,
        ["апреля"] = 4,
        ["апрель"] = 4,
        ["апр"] = 4,
        ["апр."] = 4,
        ["мая"] = 5,
        ["май"] = 5,
        ["июня"] = 6,
        ["июнь"] = 6,
        ["июн"] = 6,
        ["июн."] = 6,
        ["июля"] = 7,
        ["июль"] = 7,
        ["июл"] = 7,
        ["июл."] = 7,
        ["августа"] = 8,
        ["август"] = 8,
        ["авг"] = 8,
        ["авг."] = 8,
        ["сентября"] = 9,
        ["сентябрь"] = 9,
        ["сен"] = 9,
        ["сен."] = 9,
        ["сент"] = 9,
        ["сент."] = 9,
        ["октября"] = 10,
        ["октябрь"] = 10,
        ["окт"] = 10,
        ["окт."] = 10,
        ["ноября"] = 11,
        ["ноябрь"] = 11,
        ["ноя"] = 11,
        ["ноя."] = 11,
        ["нояб"] = 11,
        ["нояб."] = 11,
        ["декабря"] = 12,
        ["декабрь"] = 12,
        ["дек"] = 12,
        ["дек."] = 12,
    };

    public static DateTime? Parse(string? raw, DateTime? utcNow = null)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = Normalize(raw);

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (
            DateTimeOffset.TryParse(
                raw.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var iso
            )
        )
        {
            return iso.UtcDateTime;
        }

        var nowUtc = utcNow ?? DateTime.UtcNow;
        var moscowNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, MoscowTimeZone());

        if (
            text.Contains("только что", StringComparison.Ordinal)
            || text.Contains("сейчас", StringComparison.Ordinal)
        )
        {
            return nowUtc;
        }

        var relative = TryParseRelative(text, moscowNow);
        if (relative.HasValue)
        {
            return ToUtc(relative.Value);
        }

        var named = TryParseNamedDate(text, moscowNow);
        if (named.HasValue)
        {
            return ToUtc(named.Value);
        }

        if (
            DateTime.TryParse(
                text,
                Russian,
                DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces,
                out var parsed
            )
        )
        {
            var unspecified = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
            return ToUtc(unspecified);
        }

        return null;
    }

    private static DateTime? TryParseRelative(string text, DateTime moscowNow)
    {
        var today = TodayTimeRegex().Match(text);
        if (today.Success)
        {
            return Combine(moscowNow.Date, today);
        }

        if (text.Contains("сегодня", StringComparison.Ordinal))
        {
            return moscowNow;
        }

        var yesterday = YesterdayTimeRegex().Match(text);
        if (yesterday.Success)
        {
            return Combine(moscowNow.Date.AddDays(-1), yesterday);
        }

        if (text.Contains("вчера", StringComparison.Ordinal))
        {
            return moscowNow.Date.AddDays(-1).AddHours(12);
        }

        if (text.Contains("минуту назад", StringComparison.Ordinal))
        {
            return moscowNow.AddMinutes(-1);
        }

        if (text.Contains("час назад", StringComparison.Ordinal) && !HoursAgoRegex().IsMatch(text))
        {
            return moscowNow.AddHours(-1);
        }

        if (
            text.Contains("день назад", StringComparison.Ordinal)
            || text.Contains("сутки назад", StringComparison.Ordinal)
        )
        {
            return moscowNow.Date.AddDays(-1).AddHours(12);
        }

        var minutes = MinutesAgoRegex().Match(text);
        if (minutes.Success && int.TryParse(minutes.Groups[1].Value, out var minuteCount))
        {
            return moscowNow.AddMinutes(-minuteCount);
        }

        var hours = HoursAgoRegex().Match(text);
        if (hours.Success && int.TryParse(hours.Groups[1].Value, out var hourCount))
        {
            return moscowNow.AddHours(-hourCount);
        }

        var days = DaysAgoRegex().Match(text);
        if (days.Success && int.TryParse(days.Groups[1].Value, out var dayCount))
        {
            return moscowNow.Date.AddDays(-dayCount).AddHours(12);
        }

        var weeks = WeeksAgoRegex().Match(text);
        if (weeks.Success && int.TryParse(weeks.Groups[1].Value, out var weekCount))
        {
            return moscowNow.Date.AddDays(-7 * weekCount).AddHours(12);
        }

        return null;
    }

    private static DateTime? TryParseNamedDate(string text, DateTime moscowNow)
    {
        var match = NamedDateRegex().Match(text);
        if (!match.Success)
        {
            return null;
        }

        if (
            !int.TryParse(match.Groups[1].Value, out var day)
            || !Months.TryGetValue(match.Groups[2].Value, out var month)
        )
        {
            return null;
        }

        var year = moscowNow.Year;
        if (match.Groups[3].Success && int.TryParse(match.Groups[3].Value, out var parsedYear))
        {
            year = parsedYear;
        }

        var hour = 12;
        var minute = 0;
        if (match.Groups[4].Success)
        {
            hour = int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
            minute = int.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture);
        }

        try
        {
            var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
            if (!match.Groups[3].Success && local > moscowNow.AddDays(1))
            {
                local = local.AddYears(-1);
            }

            return local;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static DateTime Combine(DateTime date, Match timeMatch)
    {
        var hour = int.Parse(timeMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        var minute = int.Parse(timeMatch.Groups[2].Value, CultureInfo.InvariantCulture);
        return date.AddHours(hour).AddMinutes(minute);
    }

    private static string Normalize(string raw)
    {
        var text = raw.Replace('\u00a0', ' ').Trim().ToLowerInvariant();
        text = PrefixRegex().Replace(text, string.Empty).Trim();
        return WhitespaceRegex().Replace(text, " ");
    }

    private static DateTime ToUtc(DateTime localMoscow)
    {
        var unspecified = DateTime.SpecifyKind(localMoscow, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, MoscowTimeZone());
    }

    private static TimeZoneInfo MoscowTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone(
                "MSK",
                TimeSpan.FromHours(3),
                "Moscow",
                "Moscow"
            );
        }
    }

    [GeneratedRegex(@"сегодня(?:\s+в)?\s+(\d{1,2})[:.](\d{2})")]
    private static partial Regex TodayTimeRegex();

    [GeneratedRegex(@"вчера(?:\s+в)?\s+(\d{1,2})[:.](\d{2})")]
    private static partial Regex YesterdayTimeRegex();

    [GeneratedRegex(@"(\d+)\s+мин")]
    private static partial Regex MinutesAgoRegex();

    [GeneratedRegex(@"(\d+)\s+час")]
    private static partial Regex HoursAgoRegex();

    [GeneratedRegex(@"(\d+)\s+дн")]
    private static partial Regex DaysAgoRegex();

    [GeneratedRegex(@"(\d+)\s+нед")]
    private static partial Regex WeeksAgoRegex();

    [GeneratedRegex(@"(\d{1,2})\s+([а-яё.]+)(?:\s+(\d{4}))?(?:\s+в)?(?:\s+(\d{1,2})[:.](\d{2}))?")]
    private static partial Regex NamedDateRegex();

    [GeneratedRegex(@"^(размещено|опубликовано|обновлено)\s+")]
    private static partial Regex PrefixRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}

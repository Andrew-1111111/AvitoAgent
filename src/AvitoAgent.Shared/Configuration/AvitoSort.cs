namespace AvitoAgent.Shared.Configuration;

public enum AvitoSortMode
{
    Default,
    Cheaper,
    Expensive,
    Date,
    Discount,
}

public static class AvitoSort
{
    public static readonly string[] AllowedValues =
    [
        "По умолчанию",
        "Дешевле",
        "Дороже",
        "По дате",
        "По размеру скидки",
    ];

    public static bool TryResolve(string? value, out string canonical, out AvitoSortMode mode)
    {
        canonical = string.Empty;
        mode = AvitoSortMode.Date;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = Normalize(value);
        foreach (var allowed in AllowedValues)
        {
            if (Normalize(allowed) != normalized)
            {
                continue;
            }

            canonical = allowed;
            mode = FromCanonical(allowed);
            return true;
        }

        return false;
    }

    public static bool IsAllowed(string? value) => TryResolve(value, out _, out _);

    public static AvitoSortMode Parse(string? value) =>
        TryResolve(value, out _, out var mode)
            ? mode
            : throw new InvalidOperationException(FormatInvalidSort(value));

    public static string FormatInvalidSort(string? value) =>
        SettingsError.OneOf(
            "Avito:Filters:Sort",
            string.IsNullOrWhiteSpace(value) ? null : value.Trim(),
            AllowedValues
        );

    private static AvitoSortMode FromCanonical(string canonical) =>
        canonical switch
        {
            "По умолчанию" => AvitoSortMode.Default,
            "Дешевле" => AvitoSortMode.Cheaper,
            "Дороже" => AvitoSortMode.Expensive,
            "По дате" => AvitoSortMode.Date,
            "По размеру скидки" => AvitoSortMode.Discount,
            _ => throw new InvalidOperationException(FormatInvalidSort(canonical)),
        };

    private static string Normalize(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Replace('ё', 'е').Replace('-', ' ');
        while (normalized.Contains("  ", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
        }

        return normalized;
    }

    public static string DisplayName(AvitoSortMode mode) =>
        mode switch
        {
            AvitoSortMode.Default => "По умолчанию",
            AvitoSortMode.Cheaper => "Дешевле",
            AvitoSortMode.Expensive => "Дороже",
            AvitoSortMode.Date => "По дате",
            AvitoSortMode.Discount => "По размеру скидки",
            _ => "По дате",
        };

    public static IReadOnlyList<string> OptionLabels(AvitoSortMode mode) =>
        mode switch
        {
            AvitoSortMode.Default => ["По умолчанию", "Рекомендации"],
            AvitoSortMode.Cheaper => ["Дешевле"],
            AvitoSortMode.Expensive => ["Дороже"],
            AvitoSortMode.Date => ["По дате"],
            AvitoSortMode.Discount => ["По размеру скидки"],
            _ => ["По дате"],
        };

    /// <summary>
    /// Значение query-параметра <c>s</c>. Так сортировка работала до 16.08.
    /// </summary>
    public static int? UrlValue(AvitoSortMode mode) =>
        mode switch
        {
            AvitoSortMode.Cheaper => 1,
            AvitoSortMode.Expensive => 2,
            AvitoSortMode.Date => 104,
            AvitoSortMode.Default => 101,
            AvitoSortMode.Discount => 105,
            _ => null,
        };

    /// <summary>
    /// Код пункта в <c>data-marker="sort/custom-option(...)"</c>.
    /// </summary>
    public static string? OptionMarker(AvitoSortMode mode) =>
        mode switch
        {
            AvitoSortMode.Cheaper => "1",
            AvitoSortMode.Expensive => "2",
            AvitoSortMode.Date => "104",
            AvitoSortMode.Default => "101",
            AvitoSortMode.Discount => "172297_desc",
            _ => null,
        };

    public static bool UrlMatches(string url, AvitoSortMode mode)
    {
        var sort = ReadSortParam(url);

        return mode switch
        {
            AvitoSortMode.Default => sort is null or "101",
            AvitoSortMode.Cheaper => sort == "1",
            AvitoSortMode.Expensive => sort == "2",
            AvitoSortMode.Date => sort == "104",
            AvitoSortMode.Discount => sort is "5" or "9" or "105" or "172297_desc",
            _ => false,
        };
    }

    /// <summary>
    /// Подпись кнопки «Сортировка» после выбора: «По дате», «Дешевле» и т.д.
    /// В режиме по умолчанию Avito оставляет текст «Сортировка».
    /// </summary>
    public static bool TriggerShows(string? triggerText, AvitoSortMode mode)
    {
        var text = Normalize(triggerText ?? string.Empty);
        if (text.Length == 0)
        {
            return false;
        }

        if (mode is AvitoSortMode.Default)
        {
            return text is "сортировка" or "по умолчанию" or "рекомендации" or "сначала рекомендации";
        }

        foreach (var label in OptionLabels(mode))
        {
            if (text.Contains(Normalize(label), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsApplied(string url, string? triggerText, AvitoSortMode mode)
    {
        if (mode is not AvitoSortMode.Default && UrlMatches(url, mode))
        {
            return true;
        }

        if (TriggerShows(triggerText, mode))
        {
            return true;
        }

        return mode is AvitoSortMode.Default && UrlMatches(url, mode) && string.IsNullOrWhiteSpace(triggerText);
    }

    private static string? ReadSortParam(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        foreach (
            var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        )
        {
            var eq = pair.IndexOf('=');
            var key = eq < 0 ? pair : pair[..eq];
            if (!key.Equals("s", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = eq < 0 ? string.Empty : pair[(eq + 1)..];
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        return null;
    }
}

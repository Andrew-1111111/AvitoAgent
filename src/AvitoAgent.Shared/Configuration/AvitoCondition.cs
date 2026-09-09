namespace AvitoAgent.Shared.Configuration;

public enum AvitoConditionMode
{
    All,
    New,
    Used,
}

public static class AvitoCondition
{
    public static readonly string[] AllowedValues = ["Все", "Новое", "Б/у"];

    public static bool TryResolve(string? value, out string canonical, out AvitoConditionMode mode)
    {
        canonical = string.Empty;
        mode = AvitoConditionMode.All;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = Normalize(value);
        mode = normalized switch
        {
            "все" or "все товары" => AvitoConditionMode.All,
            "новое" or "новый" or "новый товар" or "новые" or "новая" => AvitoConditionMode.New,
            "б/у" or "б\\у" or "бу" or "б у" or "б.у." or "б.у" or "подержанный" or "подержанное" =>
                AvitoConditionMode.Used,
            _ => (AvitoConditionMode)(-1),
        };

        if ((int)mode < 0)
        {
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

        canonical = DisplayName(mode);
        return true;
    }

    public static bool IsAllowed(string? value) => TryResolve(value, out _, out _);

    public static AvitoConditionMode Parse(string? value) =>
        TryResolve(value, out _, out var mode)
            ? mode
            : throw new InvalidOperationException(FormatInvalid(value));

    public static string FormatInvalid(string? value) =>
        SettingsError.OneOf("Avito:Filters:Condition", value, AllowedValues);

    public static string DisplayName(AvitoConditionMode mode) =>
        mode switch
        {
            AvitoConditionMode.New => "Новое",
            AvitoConditionMode.Used => "Б/у",
            _ => "Все",
        };

    private static AvitoConditionMode FromCanonical(string canonical) =>
        canonical switch
        {
            "Новое" => AvitoConditionMode.New,
            "Б/у" => AvitoConditionMode.Used,
            _ => AvitoConditionMode.All,
        };

    private static string Normalize(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Replace('ё', 'е').Replace('\\', '/');
        while (normalized.Contains("  ", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
        }

        return normalized;
    }
}

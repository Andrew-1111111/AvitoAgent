namespace AvitoAgent.Shared.Configuration;

public enum AvitoSellerTypeMode
{
    All,
    Private,
    Company,
}

public static class AvitoSellerType
{
    public static readonly string[] AllowedValues = ["Все", "Частные", "Компании"];

    public static bool TryResolve(string? value, out string canonical, out AvitoSellerTypeMode mode)
    {
        canonical = string.Empty;
        mode = AvitoSellerTypeMode.All;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = Normalize(value);
        mode = normalized switch
        {
            "все" or "все продавцы" or "любые" => AvitoSellerTypeMode.All,
            "частные" or "частные лица" or "частник" or "частники" => AvitoSellerTypeMode.Private,
            "компании" or "компания" or "магазины" or "магазин" => AvitoSellerTypeMode.Company,
            _ => (AvitoSellerTypeMode)(-1),
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

    public static AvitoSellerTypeMode Parse(string? value) =>
        TryResolve(value, out _, out var mode)
            ? mode
            : throw new InvalidOperationException(FormatInvalid(value));

    public static string FormatInvalid(string? value) =>
        SettingsError.OneOf("Avito:Filters:SellerType", value, AllowedValues);

    public static string DisplayName(AvitoSellerTypeMode mode) =>
        mode switch
        {
            AvitoSellerTypeMode.Private => "Частные",
            AvitoSellerTypeMode.Company => "Компании",
            _ => "Все",
        };

    public static IReadOnlyList<string> OptionLabels(AvitoSellerTypeMode mode) =>
        mode switch
        {
            AvitoSellerTypeMode.Private => ["Частные", "Частные лица"],
            AvitoSellerTypeMode.Company => ["Компании"],
            _ => ["Все продавцы", "Все"],
        };

    /// <summary>
    /// Query-параметр <c>user</c>: 1 - частные, 2 - компании. Для «Все» параметр не задаём.
    /// </summary>
    public static string? UrlValue(AvitoSellerTypeMode mode) =>
        mode switch
        {
            AvitoSellerTypeMode.Private => "1",
            AvitoSellerTypeMode.Company => "2",
            _ => null,
        };

    private static AvitoSellerTypeMode FromCanonical(string canonical) =>
        canonical switch
        {
            "Частные" => AvitoSellerTypeMode.Private,
            "Компании" => AvitoSellerTypeMode.Company,
            _ => AvitoSellerTypeMode.All,
        };

    private static string Normalize(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Replace('ё', 'е');
        while (normalized.Contains("  ", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
        }

        return normalized;
    }
}

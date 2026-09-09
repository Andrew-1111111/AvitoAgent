namespace AvitoAgent.Shared.Configuration;

public static class SettingsError
{
    public static string FormatValue(object? value)
    {
        if (value is null)
        {
            return "(пусто)";
        }

        var text = Convert
            .ToString(value, System.Globalization.CultureInfo.InvariantCulture)
            ?.Trim();
        return string.IsNullOrEmpty(text) ? "(пусто)" : text;
    }

    public static string Invalid(string path, object? actual, string hint) =>
        $"Неверное значение {path} в appsettings.json: «{FormatValue(
        actual)}». {hint}";

    public static string Range(string path, object actual, object min, object max) =>
        Invalid(path, actual, $"Допустимый диапазон: {min}…{max}.");

    public static string OneOf(string path, object? actual, IReadOnlyList<string> values) =>
        Invalid(path, actual, $"Допустимые значения: {string.Join(", ", values)}.");

    public static string Required(string path, string hint) =>
        $"Не задано {path} в appsettings.json. {hint}";

    public static string BindFailed(string path, string typeName)
    {
        var hint = typeName switch
        {
            "System.Int32" or "Int32" =>
                $"Ожидается целое число. Допустимый диапазон: {int.MinValue}…{int.MaxValue}.",
            "System.Int64" or "Int64" => "Ожидается целое число.",
            "System.Boolean" or "Boolean" => "Ожидается true или false.",
            "System.Double" or "Double" or "System.Single" or "Single" => "Ожидается число.",
            _ when typeName.Contains("BrowserEngine", StringComparison.Ordinal) =>
                "Допустимые значения: Chromium, Firefox, WebKit.",
            _ => "Проверьте тип и формат значения.",
        };

        return $"Неверное значение {path} в appsettings.json: не удалось прочитать. {hint}";
    }

    public static void RequireRange(
        ICollection<string> failures,
        string path,
        int value,
        int min,
        int max
    )
    {
        if (value < min || value > max)
        {
            failures.Add(Range(path, value, min, max));
        }
    }

    public static void RequireRange(
        ICollection<string> failures,
        string path,
        double value,
        double min,
        double max
    )
    {
        if (value < min || value > max)
        {
            failures.Add(Range(path, value, min, max));
        }
    }

    public static void RequireHttpUrl(ICollection<string> failures, string path, string? value)
    {
        if (
            Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        )
        {
            return;
        }

        failures.Add(Invalid(path, value, "Укажите URL с http:// или https://."));
    }

    public static void RequireNotEmpty(
        ICollection<string> failures,
        string path,
        string? value,
        string hint
    )
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add(Required(path, hint));
        }
    }

    public static void RequireOneOf(
        ICollection<string> failures,
        string path,
        string? value,
        IReadOnlyList<string> allowed
    )
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add(OneOf(path, value, allowed));
            return;
        }

        if (!allowed.Any(item => item.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            failures.Add(OneOf(path, value, allowed));
        }
    }
}

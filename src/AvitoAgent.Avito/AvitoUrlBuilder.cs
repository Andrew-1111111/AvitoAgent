using AvitoAgent.Core.Models;
using AvitoAgent.Shared.Configuration;

namespace AvitoAgent.Avito;

internal static class AvitoUrlBuilder
{
    public static string ResolveLocation(AvitoOptions options, SearchCriteria? criteria = null)
    {
        var fromCriteria = criteria?.LocationSlugs.FirstOrDefault(slug =>
            !string.IsNullOrWhiteSpace(slug)
        );
        if (!string.IsNullOrWhiteSpace(fromCriteria))
        {
            return fromCriteria.Trim().Trim('/');
        }

        return options.GetLocationSlugs().FirstOrDefault() ?? "rossiya";
    }

    public static string BuildSearchUrl(AvitoOptions options, SearchCriteria criteria, int page)
    {
        var location = ResolveLocation(options, criteria);
        var baseUrl = options.BaseUrl.TrimEnd('/');

        // Запрос (q) в URL не ставим — только печать в поле поиска и кнопка «Найти».
        var url = $"{baseUrl}/{location}";
        var query = new List<string>();

        // Иначе Avito часто ставит localPriority=0 и сверху сразу «другие города».
        if (!IsNationwide(location))
        {
            query.Add("localPriority=1");
        }

        if (criteria.MinPrice.HasValue)
        {
            query.Add(
                $"pmin={criteria.MinPrice.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            );
        }

        if (criteria.MaxPrice.HasValue)
        {
            query.Add(
                $"pmax={criteria.MaxPrice.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            );
        }

        var condition = AvitoCondition.TryResolve(criteria.Condition, out _, out var parsedCondition)
            ? parsedCondition
            : AvitoConditionMode.All;
        if (condition is AvitoConditionMode.New)
        {
            query.Add("condition=1");
        }
        else if (condition is AvitoConditionMode.Used)
        {
            query.Add("condition=2");
        }

        if (criteria.DeliveryOnly)
        {
            query.Add("d=1");
        }

        var sellerMode = AvitoSellerType.TryResolve(criteria.SellerType, out _, out var parsedSeller)
            ? parsedSeller
            : AvitoSellerTypeMode.All;
        var sellerValue = AvitoSellerType.UrlValue(sellerMode);
        if (sellerValue is not null)
        {
            query.Add($"user={sellerValue}");
        }

        if (page > 1)
        {
            query.Add($"p={page}");
        }

        if (query.Count > 0)
        {
            url += "?" + string.Join('&', query);
        }

        return url;
    }

    /// <summary>
    /// Дописывает в текущий URL выдачи цену, состояние, доставку, продавца и localPriority,
    /// не трогая q (запрос только через форму) и path. Сортировка — кликом по меню.
    /// </summary>
    public static bool TryMergeCriteriaFilters(
        string currentUrl,
        SearchCriteria criteria,
        out string targetUrl
    )
    {
        targetUrl = currentUrl;

        if (!Uri.TryCreate(currentUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var map = ParseQuery(uri.Query);
        var changed = false;

        changed |= SetQueryValue(map, "d", criteria.DeliveryOnly ? "1" : null);

        var sellerMode = AvitoSellerType.TryResolve(criteria.SellerType, out _, out var parsedSeller)
            ? parsedSeller
            : AvitoSellerTypeMode.All;
        changed |= SetQueryValue(map, "user", AvitoSellerType.UrlValue(sellerMode));

        changed |= SetQueryValue(
            map,
            "pmin",
            criteria.MinPrice?.ToString(System.Globalization.CultureInfo.InvariantCulture)
        );
        changed |= SetQueryValue(
            map,
            "pmax",
            criteria.MaxPrice?.ToString(System.Globalization.CultureInfo.InvariantCulture)
        );

        var condition = AvitoCondition.TryResolve(criteria.Condition, out _, out var parsedCondition)
            ? parsedCondition
            : AvitoConditionMode.All;
        var conditionValue = condition switch
        {
            AvitoConditionMode.New => "1",
            AvitoConditionMode.Used => "2",
            _ => null,
        };
        changed |= SetQueryValue(map, "condition", conditionValue);

        var path = uri.AbsolutePath.Trim('/');
        var slash = path.IndexOf('/');
        var location = slash < 0 ? path : path[..slash];
        if (!IsNationwide(location))
        {
            changed |= SetQueryValue(map, "localPriority", "1");
        }

        if (!changed)
        {
            return false;
        }

        targetUrl = BuildUrl(uri, map);
        return true;
    }

    /// <summary>
    /// Совпадают ли в URL фильтры цены / состояния / доставки / продавца с критериями.
    /// Путь категории Avito может отличаться от BuildSearchUrl — path не сравниваем.
    /// </summary>
    public static bool HasCriteriaFilters(string currentUrl, SearchCriteria criteria)
    {
        if (!Uri.TryCreate(currentUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var map = ParseQuery(uri.Query);

        if (criteria.DeliveryOnly)
        {
            if (!map.TryGetValue("d", out var d) || d != "1")
            {
                return false;
            }
        }

        var sellerMode = AvitoSellerType.TryResolve(criteria.SellerType, out _, out var parsedSeller)
            ? parsedSeller
            : AvitoSellerTypeMode.All;
        var sellerValue = AvitoSellerType.UrlValue(sellerMode);
        if (sellerValue is not null)
        {
            if (!map.TryGetValue("user", out var user)
                || !string.Equals(user, sellerValue, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        var condition = AvitoCondition.TryResolve(criteria.Condition, out _, out var parsedCondition)
            ? parsedCondition
            : AvitoConditionMode.All;
        if (condition is not AvitoConditionMode.All)
        {
            var expected = condition is AvitoConditionMode.New ? "1" : "2";
            if (!map.TryGetValue("condition", out var current)
                || !string.Equals(current, expected, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (criteria.MinPrice.HasValue)
        {
            var expected = criteria.MinPrice.Value.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            );
            if (!map.TryGetValue("pmin", out var pmin)
                || !string.Equals(pmin, expected, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (criteria.MaxPrice.HasValue)
        {
            var expected = criteria.MaxPrice.Value.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            );
            if (!map.TryGetValue("pmax", out var pmax)
                || !string.Equals(pmax, expected, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsNationwide(string? locationSlug)
    {
        var key = locationSlug?.Trim().Trim('/') ?? string.Empty;
        return key.Length == 0
            || key.Equals("rossiya", StringComparison.OrdinalIgnoreCase)
            || key.Equals("all", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Сравнивает параметры поиска, игнорируя служебный context= от Avito.
    /// </summary>
    public static bool SearchUrlsAligned(string currentUrl, string targetUrl)
    {
        if (
            !Uri.TryCreate(currentUrl, UriKind.Absolute, out var current)
            || !Uri.TryCreate(targetUrl, UriKind.Absolute, out var target)
        )
        {
            return false;
        }

        if (
            !string.Equals(
                current.AbsolutePath.TrimEnd('/'),
                target.AbsolutePath.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return false;
        }

        var currentParams = ParseQuery(current.Query);
        var targetParams = ParseQuery(target.Query);

        foreach (var key in SignificantQueryKeys)
        {
            currentParams.TryGetValue(key, out var left);
            targetParams.TryGetValue(key, out var right);
            if (!string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Совпадает ли регион (первый сегмент path). Категория после региона может отличаться.
    /// </summary>
    public static bool SearchLocationMatches(string currentUrl, string targetUrl)
    {
        if (
            !Uri.TryCreate(currentUrl, UriKind.Absolute, out var current)
            || !Uri.TryCreate(targetUrl, UriKind.Absolute, out var target)
        )
        {
            return false;
        }

        return string.Equals(
            FirstPathSegment(current.AbsolutePath),
            FirstPathSegment(target.AbsolutePath),
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static string FirstPathSegment(string absolutePath)
    {
        var parts = absolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : string.Empty;
    }

    public static bool IsConditionApplied(string url, AvitoConditionMode mode)
    {
        if (mode is AvitoConditionMode.All)
        {
            return true;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var map = ParseQuery(uri.Query);
        var expected = mode is AvitoConditionMode.New ? "1" : "2";
        return map.TryGetValue("condition", out var current)
            && string.Equals(current, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly string[] SignificantQueryKeys =
    [
        "pmin",
        "pmax",
        "condition",
        "d",
        "user",
        "localPriority",
        "p",
    ];

    private static bool SetQueryValue(
        Dictionary<string, string> map,
        string key,
        string? value
    )
    {
        map.TryGetValue(key, out var current);

        if (value is null)
        {
            return map.Remove(key);
        }

        if (string.Equals(current ?? string.Empty, value, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        map[key] = value;
        return true;
    }

    private static string BuildUrl(Uri uri, Dictionary<string, string> map)
    {
        var query = string.Join(
            '&',
            map.Select(pair =>
                string.IsNullOrEmpty(pair.Value) ? pair.Key : $"{pair.Key}={pair.Value}"
            )
        );

        return new UriBuilder(uri) { Query = query }.Uri.ToString();
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (
            var pair in query
                .TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
        )
        {
            var eq = pair.IndexOf('=');
            var key = eq < 0 ? pair : pair[..eq];
            var value = eq < 0 ? string.Empty : pair[(eq + 1)..];
            map[key] = value;
        }

        return map;
    }

    public static bool UrlMatchesSearchQuery(string url, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        if (!TryGetSearchQuery(url, out var actual))
        {
            return false;
        }

        return string.Equals(
            NormalizeQuery(actual),
            NormalizeQuery(query),
            StringComparison.OrdinalIgnoreCase
        );
    }

    public static bool TryGetSearchQuery(string url, out string query)
    {
        query = string.Empty;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var raw = uri.Query.TrimStart('?');
        if (raw.Length == 0)
        {
            return false;
        }

        foreach (var pair in raw.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var key = eq < 0 ? pair : pair[..eq];
            if (!key.Equals("q", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = eq < 0 ? string.Empty : pair[(eq + 1)..];
            query = Uri.UnescapeDataString(value.Replace('+', ' '));
            return true;
        }

        return false;
    }

    private static string NormalizeQuery(string value) =>
        string.Join(
            ' ',
            value
                .Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        );
}

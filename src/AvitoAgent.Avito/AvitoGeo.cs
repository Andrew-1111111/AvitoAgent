using System.Text.RegularExpressions;

namespace AvitoAgent.Avito;

internal static partial class AvitoGeo
{
    private readonly record struct Place(string City, string Region);

    private static readonly Dictionary<string, Place> Locations = CreateLocations();

    private static readonly HashSet<string> FederalSubjects = CreateFederalSubjects();

    private static readonly Dictionary<string, string> RegionByPlaceName =
        CreateRegionByPlaceName();

    public static bool TryFromUrl(string url, out string city, out string region)
    {
        city = string.Empty;
        region = string.Empty;

        if (!TryPathSlug(url, out var slug) || slug is "rossiya" or "all" or "www" or "item")
        {
            return false;
        }

        if (!Locations.TryGetValue(slug, out var place))
        {
            return false;
        }

        city = place.City;
        region = place.Region;
        return true;
    }

    public static bool TryFromSearchSlug(string? slug, out string region)
    {
        region = string.Empty;
        if (string.IsNullOrWhiteSpace(slug))
        {
            return false;
        }

        var key = slug.Trim().Trim('/');
        if (!Locations.TryGetValue(key, out var place))
        {
            return false;
        }

        region = place.Region;
        return true;
    }

    public static string? InferRegion(IEnumerable<string> parts)
    {
        foreach (var part in parts)
        {
            if (RegionByPlaceName.TryGetValue(part, out var region))
            {
                return region;
            }
        }

        return null;
    }

    /// <summary>
    /// Относится ли объявление к региону/городу текущего поиска.
    /// null - не удалось определить (не отбрасываем).
    /// </summary>
    public static bool? TryBelongsToSearchLocation(
        string? listingUrl,
        string? address,
        string? searchSlug
    )
    {
        if (string.IsNullOrWhiteSpace(searchSlug))
        {
            return null;
        }

        var key = searchSlug.Trim().Trim('/');
        if (key is "rossiya" or "all")
        {
            return true;
        }

        if (!Locations.TryGetValue(key, out var searchPlace))
        {
            return null;
        }

        ResolveListingGeo(listingUrl, address, out var listingCity, out var listingRegion);
        if (string.IsNullOrWhiteSpace(listingRegion))
        {
            return null;
        }

        if (!listingRegion.Equals(searchPlace.Region, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Поиск по конкретному городу - не берём другие города той же области.
        if (!string.IsNullOrEmpty(searchPlace.City))
        {
            if (!string.IsNullOrWhiteSpace(listingCity))
            {
                return listingCity.Equals(searchPlace.City, StringComparison.OrdinalIgnoreCase);
            }

            foreach (var part in SplitAddressParts(address))
            {
                if (part.Equals(searchPlace.City, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return null;
        }

        return true;
    }

    private static void ResolveListingGeo(
        string? listingUrl,
        string? address,
        out string city,
        out string region
    )
    {
        city = string.Empty;
        region = string.Empty;

        if (
            TryFromUrl(listingUrl ?? string.Empty, out city, out region)
            && !string.IsNullOrWhiteSpace(region)
        )
        {
            return;
        }

        if (
            TryPathSlug(listingUrl ?? string.Empty, out var slug)
            && Locations.TryGetValue(slug, out var place)
        )
        {
            city = place.City;
            region = place.Region;
            if (!string.IsNullOrWhiteSpace(region))
            {
                return;
            }
        }

        var parts = SplitAddressParts(address).ToArray();
        region = InferRegion(parts) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(city))
        {
            foreach (var part in parts)
            {
                if (
                    RegionByPlaceName.TryGetValue(part, out var partRegion)
                    && !LooksLikeFederalSubject(part)
                    && Locations.Values.Any(place =>
                        place.City.Equals(part, StringComparison.OrdinalIgnoreCase)
                    )
                )
                {
                    city = part;
                    if (string.IsNullOrWhiteSpace(region))
                    {
                        region = partRegion;
                    }

                    break;
                }
            }
        }
    }

    private static IEnumerable<string> SplitAddressParts(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            yield break;
        }

        foreach (
            var part in address.Split(
                [',', ';', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            if (!string.IsNullOrWhiteSpace(part))
            {
                yield return part;
            }
        }
    }

    public static bool LooksLikeFederalSubject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        var normalized = NormalizeGeoKey(value);
        if (FederalSubjects.Contains(value) || FederalSubjects.Contains(normalized))
        {
            return true;
        }

        return FederalSubjectRegex().IsMatch(value) || FederalSubjectRegex().IsMatch(normalized);
    }

    public static string DisplayName(string slug)
    {
        var key = slug.Trim().Trim('/').ToLowerInvariant();
        if (key is "rossiya" or "all")
        {
            return "Россия";
        }

        if (Locations.TryGetValue(key, out var place))
        {
            return string.IsNullOrEmpty(place.City) ? place.Region : place.City;
        }

        return slug.Replace('_', ' ').Replace('-', ' ');
    }

    /// <summary>
    /// Адрес для уведомления: исходный текст + регион из URL/справочника, если его ещё нет.
    /// </summary>
    public static string FormatListingAddress(string? address, string? listingUrl)
    {
        var cleaned = (address ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace("  ", " ", StringComparison.Ordinal)
            .Trim()
            .Trim(',', ' ');

        ResolveListingGeo(listingUrl, cleaned, out var city, out var region);

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            if (!string.IsNullOrWhiteSpace(city) && !string.IsNullOrWhiteSpace(region))
            {
                return $"{city}, {region}";
            }

            if (!string.IsNullOrWhiteSpace(region))
            {
                return region;
            }

            if (!string.IsNullOrWhiteSpace(city))
            {
                return city;
            }

            return "не указан";
        }

        if (
            !string.IsNullOrWhiteSpace(region)
            && !cleaned.Contains(region, StringComparison.OrdinalIgnoreCase)
        )
        {
            return $"{cleaned}, {region}";
        }

        return cleaned;
    }

    public static bool TryResolveInput(string? input, out string slug, out string displayName)
    {
        slug = string.Empty;
        displayName = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var key = input.Trim().Trim('/');
        var normalized = NormalizeGeoKey(key);
        if (
            normalized is "rossiya" or "all" or "россия" or "вся россия"
            || normalized.Equals("russia", StringComparison.Ordinal)
        )
        {
            slug = "rossiya";
            displayName = "Россия";
            return true;
        }

        // Slug: moskovskaya_oblast / Moskovskaya_Oblast / moskovskaya oblast
        if (TryResolveAsSlug(key, normalized, out slug, out displayName))
        {
            return true;
        }

        var inputCore = CoreGeoName(normalized);
        var preferSubject = LooksLikeFederalSubject(key) || LooksLikeFederalSubject(normalized);
        string? citySlug = null;
        string? cityName = null;
        string? subjectSlug = null;
        string? subjectName = null;

        foreach (var pair in Locations)
        {
            var regionNormalized = NormalizeGeoKey(pair.Value.Region);
            if (
                GeoNamesMatch(normalized, inputCore, pair.Value.Region)
                || regionNormalized.Equals(normalized, StringComparison.OrdinalIgnoreCase)
            )
            {
                // Запись субъекта (City пустой), а не последний город в области.
                if (string.IsNullOrWhiteSpace(pair.Value.City))
                {
                    subjectSlug = pair.Key;
                    subjectName = pair.Value.Region;
                }
                else if (subjectSlug is null)
                {
                    subjectSlug = pair.Key;
                    subjectName = pair.Value.Region;
                }
            }

            if (
                !string.IsNullOrWhiteSpace(pair.Value.City)
                && GeoNamesMatch(normalized, inputCore, pair.Value.City)
            )
            {
                citySlug = pair.Key;
                cityName = pair.Value.City;
            }
        }

        if (preferSubject && subjectSlug is not null)
        {
            slug = subjectSlug;
            displayName = subjectName!;
            return true;
        }

        if (citySlug is not null)
        {
            slug = citySlug;
            displayName = cityName!;
            return true;
        }

        if (subjectSlug is not null)
        {
            slug = subjectSlug;
            displayName = subjectName!;
            return true;
        }

        return false;
    }

    private static bool TryResolveAsSlug(
        string key,
        string normalized,
        out string slug,
        out string displayName
    )
    {
        slug = string.Empty;
        displayName = string.Empty;

        foreach (var candidate in EnumerateSlugCandidates(key, normalized))
        {
            if (!Locations.TryGetValue(candidate, out _))
            {
                continue;
            }

            slug = CanonicalSlug(candidate);
            displayName = DisplayName(slug);
            return true;
        }

        return false;
    }

    private static string CanonicalSlug(string candidate)
    {
        foreach (var known in Locations.Keys)
        {
            if (known.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        return candidate.ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
    }

    private static IEnumerable<string> EnumerateSlugCandidates(string key, string normalized)
    {
        yield return key;
        yield return key.ToLowerInvariant();
        yield return key.Replace(' ', '_').Replace('-', '_');
        yield return normalized.Replace(' ', '_');
        yield return normalized.Replace(' ', '-');
    }

    private static bool GeoNamesMatch(string normalizedInput, string inputCore, string name)
    {
        var normalizedName = NormalizeGeoKey(name);
        if (normalizedName.Length == 0)
        {
            return false;
        }

        if (normalizedInput.Equals(normalizedName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var nameCore = CoreGeoName(normalizedName);
        return inputCore.Length >= 3
            && nameCore.Length >= 3
            && inputCore.Equals(nameCore, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeGeoKey(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var raw in value.Trim().ToLowerInvariant().Replace('ё', 'е'))
        {
            var c = raw is '\u00a0' or '\u202f' or '\u2007' or '\u2009' or '_' or '-'
                ? ' '
                : raw;
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (c is '.' or '«' or '»' or '"' or '\'' or '’')
            {
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    private static HashSet<string> CreateFederalSubjects()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var place in Locations.Values)
        {
            if (string.IsNullOrEmpty(place.City))
            {
                set.Add(place.Region);
            }
        }

        return set;
    }

    private static string CoreGeoName(string normalized)
    {
        var value = normalized;
        if (value.StartsWith("республика ", StringComparison.Ordinal))
        {
            value = value["республика ".Length..].Trim();
        }

        foreach (
            var suffix in new[]
            {
                " автономный округ",
                " автономная область",
                " область",
                " край",
                " республика",
                " обл",
            }
        )
        {
            if (value.EndsWith(suffix, StringComparison.Ordinal))
            {
                return value[..^suffix.Length].Trim();
            }
        }

        return value;
    }

    public static string FormatSlugCatalog()
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("rossiya - Россия");
        builder.AppendLine();

        foreach (
            var group in Locations
                .GroupBy(pair => pair.Value.Region, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
        )
        {
            builder.AppendLine(group.Key);
            foreach (
                var pair in group
                    .OrderBy(item => string.IsNullOrEmpty(item.Value.City) ? 0 : 1)
                    .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            )
            {
                var name = string.IsNullOrEmpty(pair.Value.City)
                    ? pair.Value.Region
                    : pair.Value.City;
                builder.Append(" ");
                builder.Append(pair.Key);
                builder.Append(" - ");
                builder.AppendLine(name);
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static Dictionary<string, string> CreateRegionByPlaceName()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var place in Locations.Values)
        {
            map.TryAdd(place.Region, place.Region);
            if (!string.IsNullOrEmpty(place.City))
            {
                map.TryAdd(place.City, place.Region);
            }
        }

        return map;
    }

    private static bool TryPathSlug(string url, out string slug)
    {
        slug = string.Empty;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var path = uri.AbsolutePath.Trim('/');
        if (path.Length == 0)
        {
            return false;
        }

        var slash = path.IndexOf('/');
        slug = (slash < 0 ? path : path[..slash]).ToLowerInvariant();
        return slug.Length > 0;
    }

    [GeneratedRegex(
        @"област|\bобл\.|край\b|республик|\bАО\b|автономн",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex FederalSubjectRegex();
}

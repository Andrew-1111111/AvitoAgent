using System.Text.RegularExpressions;

namespace AvitoAgent.Avito.Parsing;

internal static partial class AvitoImageUrl
{
    public const string FullSize = "1280x960";

    public static string? NormalizeAndUpgrade(string? raw)
    {
        if (
            string.IsNullOrWhiteSpace(raw)
            || raw.Contains("data:image", StringComparison.OrdinalIgnoreCase)
        )
        {
            return null;
        }

        var url = Unescape(raw.Trim());

        if (url.StartsWith("//", StringComparison.Ordinal))
        {
            url = "https:" + url;
        }

        if (IsJunk(url))
        {
            return null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        var path = uri.AbsolutePath;
        var upgraded = UpgradePath(path);
        if (string.Equals(upgraded, path, StringComparison.Ordinal))
        {
            return uri.ToString();
        }

        // cqp= подписывает исходный путь. После смены размера подпись недействительна.
        var builder = new UriBuilder(uri)
        {
            Path = upgraded,
            Query = string.Empty,
            Fragment = string.Empty,
        };
        return builder.Uri.ToString();
    }

    public static string Identity(string url)
    {
        var path = PathWithoutQuery(url);

        var imageId = ImageStemRegex().Match(path);
        if (imageId.Success)
        {
            return imageId.Groups[1].Value.ToLowerInvariant();
        }

        var classic = ClassicFileRegex().Match(path);
        if (classic.Success)
        {
            return classic.Groups[1].Value.ToLowerInvariant();
        }

        var normalized = SizeTokenRegex().Replace(path, "WxH");
        normalized = IoSuffixRegex().Replace(normalized, string.Empty);
        if (normalized.EndsWith("/WxH", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        return normalized.TrimEnd('/').ToLowerInvariant();
    }

    public static bool IsPreview(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return true;
        }

        if (
            url.Contains("/io/preview", StringComparison.OrdinalIgnoreCase)
            || url.Contains("/preview/", StringComparison.OrdinalIgnoreCase)
            || url.Contains("/preview?", StringComparison.OrdinalIgnoreCase)
            || url.EndsWith("/preview", StringComparison.OrdinalIgnoreCase)
        )
        {
            return true;
        }

        // 640×480 — обычный кадр галереи, не превью. Превью: 140/208/240 и /preview.
        var width = WidthHint(url);
        return width is > 0 and < 400;
    }

    public static int? WidthHint(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var match = SizeTokenRegex().Match(PathWithoutQuery(url));
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var width) || width <= 0)
        {
            return null;
        }

        return width;
    }

    public static int Quality(string url)
    {
        if (IsPreview(url))
        {
            return 0;
        }

        var width = WidthHint(url);
        if (width is > 0)
        {
            return width.Value;
        }

        if (
            url.Contains("/io/", StringComparison.OrdinalIgnoreCase)
            || url.EndsWith("/io", StringComparison.OrdinalIgnoreCase)
        )
        {
            return 0;
        }

        // Подписанный URL без WxH — качество неизвестно из пути; не занижаем против явных 1280x960.
        if (url.Contains("/image/1/", StringComparison.OrdinalIgnoreCase))
        {
            return 900;
        }

        return 800;
    }

    private static string UpgradePath(string path)
    {
        // Подписанный CDN: любой rewrite пути (в т.ч. 640x480 → 1280x960) даёт 400.
        if (path.Contains("/image/1/", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        if (IoSuffixRegex().IsMatch(path))
        {
            return IoSuffixRegex().Replace(path, "/" + FullSize);
        }

        if (SizeTokenRegex().IsMatch(path))
        {
            return SizeTokenRegex().Replace(
                path,
                static match =>
                {
                    if (!int.TryParse(match.Groups[1].Value, out var width) || width >= 1280)
                    {
                        return match.Value;
                    }

                    return FullSize;
                }
            );
        }

        return path;
    }

    private static string Unescape(string url)
    {
        return url.Replace("\\u002F", "/", StringComparison.OrdinalIgnoreCase)
            .Replace("\\u002f", "/", StringComparison.OrdinalIgnoreCase)
            .Replace("\\/", "/")
            .Replace("&amp;", "&");
    }

    private static string PathWithoutQuery(string url)
    {
        var hashIndex = url.IndexOf('#', StringComparison.Ordinal);
        if (hashIndex >= 0)
        {
            url = url[..hashIndex];
        }

        var queryIndex = url.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex >= 0)
        {
            url = url[..queryIndex];
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri.AbsolutePath;
        }

        return url;
    }

    private static bool IsJunk(string url) =>
        url.Contains("avatar", StringComparison.OrdinalIgnoreCase)
        || url.Contains("favicon", StringComparison.OrdinalIgnoreCase)
        || url.Contains("/logo", StringComparison.OrdinalIgnoreCase)
        || url.Contains("sprite", StringComparison.OrdinalIgnoreCase)
        || url.Contains("placeholder", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(
        @"(\d{2,4})x(\d{2,4})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex SizeTokenRegex();

    [GeneratedRegex(
        @"/image/1/(1\.[^./]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex ImageStemRegex();

    [GeneratedRegex(
        @"/\d{2,4}x\d{2,4}/([^/]+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex ClassicFileRegex();

    [GeneratedRegex(@"/io(?:/preview)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IoSuffixRegex();
}

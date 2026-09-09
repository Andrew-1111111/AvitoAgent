namespace AvitoAgent.Playwright.Browser;

/// <summary>
/// Блокировка рекламы/аналитики/трекеров в браузере (меньше шума и лишних запросов).
/// Не блокирует www.avito.ru и критичные сервисы (капча/fonts).
/// </summary>
internal static class TrackerBlocklist
{
    private static readonly string[] HostSuffixes =
    [
        // Google ads / analytics (не google.com целиком — там может быть reCAPTCHA)
        "google-analytics.com",
        "analytics.google.com",
        "googletagmanager.com",
        "googletagservices.com",
        "googleadservices.com",
        "googlesyndication.com",
        "doubleclick.net",
        "googleads.g.doubleclick.net",
        // Meta pixels
        "facebook.net",
        "connect.facebook.net",
        // Yandex ads / metrica
        "mc.yandex.ru",
        "mc.yandex.com",
        "an.yandex.ru",
        "ads.yandex.ru",
        "metrika.yandex.ru",
        // Mail.ru counters
        "top-fwz1.mail.ru",
        "top.mail.ru",
        // Seen in Avito debug log
        "simbad.pro",
        "silvermob.com",
        "buzzoola.com",
        "adriver.ru",
        "ad.adriver.ru",
        "tns-counter.ru",
        // Common third-party trackers
        "hotjar.com",
        "amplitude.com",
        "mixpanel.com",
        "segment.io",
        "segment.com",
        "sentry.io",
        "clarity.ms",
        "criteo.com",
        "taboola.com",
        "outbrain.com",
        "rubiconproject.com",
        "pubmatic.com",
        "openx.net",
        "adnxs.com",
        "adsrvr.org",
        "scorecardresearch.com",
        "quantserve.com",
    ];

    private static readonly string[] PathHints =
    [
        "google.com/ccm/collect",
        "google.com/g/collect",
        "/pagead/",
        "pagead2.googlesyndication.com",
    ];

    public static bool ShouldBlock(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host;

        // Основной сайт и CDN Avito не режем.
        if (IsAvitoContentHost(host))
        {
            return false;
        }

        foreach (var suffix in HostSuffixes)
        {
            if (HostMatches(host, suffix))
            {
                return true;
            }
        }

        var full = uri.AbsoluteUri;
        foreach (var hint in PathHints)
        {
            if (full.Contains(hint, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // Первопартийные хосты не режем: их телеметрию антибот Avito (Qrator) считает
    // признаком живого браузера, а обрыв — признаком автоматизации.
    private static bool IsAvitoContentHost(string host) =>
        host.Equals("avito.ru", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".avito.ru", StringComparison.OrdinalIgnoreCase)
        || host.Equals("avito.st", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".avito.st", StringComparison.OrdinalIgnoreCase);

    private static bool HostMatches(string host, string suffix) =>
        host.Equals(suffix, StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase);
}

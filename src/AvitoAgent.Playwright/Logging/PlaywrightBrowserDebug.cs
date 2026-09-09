using AvitoAgent.Playwright.Browser;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AvitoAgent.Playwright.Logging;

/// <summary>
/// Подписка на события страницы/контекста для опционального debug-лога.
/// SourceContext: AvitoAgent.BrowserDebug → файл logs/debug-*.log.
/// </summary>
public sealed class PlaywrightBrowserDebug(ILogger logger)
{
    public const string SourceContext = "AvitoAgent.BrowserDebug";

    private readonly ILogger _logger = logger;
    private readonly HashSet<IPage> _attached = [];
    private readonly object _gate = new();

    public void Attach(IBrowserContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Page += (_, page) => AttachPage(page);
        context.Close += (_, _) =>
            _logger.LogInformation("Browser context закрыт");

        foreach (var page in context.Pages)
        {
            AttachPage(page);
        }

        _logger.LogInformation("Browser debug log активен (page/console/network)");
    }

    private void AttachPage(IPage page)
    {
        lock (_gate)
        {
            if (!_attached.Add(page))
            {
                return;
            }
        }

        page.Close += (_, _) =>
        {
            lock (_gate)
            {
                _attached.Remove(page);
            }
        };

        page.PageError += (_, error) =>
            _logger.LogError("PageError [{Url}]: {Error}", SafeUrl(page), Truncate(error));

        page.Crash += (_, _) =>
            _logger.LogError("Page crash [{Url}]", SafeUrl(page));

        page.Console += (_, msg) =>
        {
            var type = msg.Type ?? string.Empty;
            if (!IsInterestingConsole(type))
            {
                return;
            }

            // Chromium: «Failed to load resource» при route.abort (BlockTrackers) -
            // URL ресурса в тексте нет, только page.Url → бесполезный шум.
            if (IsBlockedResourceConsoleNoise(msg.Text))
            {
                return;
            }

            if (string.Equals(type, "error", StringComparison.OrdinalIgnoreCase))
            {
                if (!_logger.IsEnabled(LogLevel.Warning))
                {
                    return;
                }

                _logger.LogWarning(
                    "Console error [{Url}]: {Text}",
                    SafeUrl(page),
                    Truncate(msg.Text)
                );
            }
            else if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Console {Type} [{Url}]: {Text}",
                    type,
                    SafeUrl(page),
                    Truncate(msg.Text)
                );
            }
        };

        page.RequestFailed += (_, request) =>
        {
            if (ShouldIgnoreUrl(request.Url))
            {
                return;
            }

            var failure = string.IsNullOrWhiteSpace(request.Failure)
                ? "(без текста)"
                : Truncate(request.Failure);
            _logger.LogWarning(
                "RequestFailed {Method} {Url}: {Failure}",
                request.Method,
                Truncate(request.Url, 240),
                failure
            );
        };

        page.Response += (_, response) =>
        {
            if (response.Status < 400 || ShouldIgnoreUrl(response.Url))
            {
                return;
            }

            var level = response.Status >= 500 ? LogLevel.Warning : LogLevel.Debug;
            if (!_logger.IsEnabled(level))
            {
                return;
            }

            _logger.Log(
                level,
                "HTTP {Status} {Method} {Url}",
                response.Status,
                response.Request.Method,
                Truncate(response.Url, 240)
            );
        };
    }

    private static bool IsInterestingConsole(string type) =>
        type.Equals("error", StringComparison.OrdinalIgnoreCase)
        || type.Equals("warning", StringComparison.OrdinalIgnoreCase)
        || type.Equals("assert", StringComparison.OrdinalIgnoreCase);

    private static bool IsBlockedResourceConsoleNoise(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (!text.Contains("Failed to load resource", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return text.Contains("net::ERR_FAILED", StringComparison.OrdinalIgnoreCase)
            || text.Contains("net::ERR_ABORTED", StringComparison.OrdinalIgnoreCase)
            || text.Contains("net::ERR_BLOCKED_BY_CLIENT", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldIgnoreUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return true;
        }

        return url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("chrome-extension:", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
            || TrackerBlocklist.ShouldBlock(url);
    }

    private static string SafeUrl(IPage page)
    {
        try
        {
            return Truncate(page.Url, 240);
        }
        catch
        {
            return "(closed)";
        }
    }

    private static string Truncate(string? value, int max = 500)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= max ? value : value[..max] + "…";
    }
}

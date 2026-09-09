using AvitoAgent.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace AvitoAgent.Playwright.Options;

internal sealed class PlaywrightOptionsValidator : IValidateOptions<PlaywrightOptions>
{
    private static readonly string[] AllowedChannels =
    [
        "chrome",
        "msedge",
        "chrome-beta",
        "chrome-dev",
        "chrome-canary",
        "msedge-beta",
        "msedge-dev",
    ];

    public ValidateOptionsResult Validate(string? name, PlaywrightOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        ValidateLaunch(options.Launch, failures);
        ValidateContext(options.Context, failures);
        ValidateProxy(options.Proxy, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateLaunch(LaunchOptions options, List<string> failures)
    {
        SettingsError.RequireRange(
            failures,
            "Playwright:Launch:Timeout",
            options.Timeout,
            1,
            int.MaxValue
        );
        SettingsError.RequireRange(
            failures,
            "Playwright:Launch:SlowMo",
            options.SlowMo,
            0,
            int.MaxValue
        );

        if (
            !string.IsNullOrWhiteSpace(options.Channel)
            && !AllowedChannels.Contains(options.Channel, StringComparer.OrdinalIgnoreCase)
        )
        {
            failures.Add(
                SettingsError.OneOf("Playwright:Launch:Channel", options.Channel, AllowedChannels)
            );
        }

        if (
            !string.IsNullOrWhiteSpace(options.ExecutablePath)
            && !File.Exists(options.ExecutablePath)
        )
        {
            failures.Add(
                SettingsError.Invalid(
                    "Playwright:Launch:ExecutablePath",
                    options.ExecutablePath,
                    "Указанный файл браузера не существует."
                )
            );
        }
    }

    private static void ValidateContext(ContextOptions options, List<string> failures)
    {
        // Пустой UserAgent допустим: Playwright/Chrome подставят свой.
        if (!string.IsNullOrWhiteSpace(options.UserAgent) && options.UserAgent.Length < 10)
        {
            failures.Add(
                SettingsError.Invalid(
                    "Playwright:Context:UserAgent",
                    options.UserAgent,
                    "Укажите полный User-Agent или оставьте пустым."
                )
            );
        }

        if (options.EmulateViewport)
        {
            SettingsError.RequireRange(
                failures,
                "Playwright:Context:Viewport:Width",
                options.Viewport.Width,
                1,
                int.MaxValue
            );
            SettingsError.RequireRange(
                failures,
                "Playwright:Context:Viewport:Height",
                options.Viewport.Height,
                1,
                int.MaxValue
            );
            SettingsError.RequireRange(
                failures,
                "Playwright:Context:DeviceScaleFactor",
                options.DeviceScaleFactor,
                0.1,
                8
            );
        }

        if (options.Geolocation is not null && !options.Geolocation.IsConfigured)
        {
            failures.Add(
                SettingsError.Invalid(
                    "Playwright:Context:Geolocation",
                    null,
                    "Укажите корректные координаты."
                )
            );
        }

        if (options.Video is not null && options.Video.Enabled)
        {
            SettingsError.RequireNotEmpty(
                failures,
                "Playwright:Context:Video:Directory",
                options.Video.Directory,
                "Укажите каталог записи видео."
            );
            SettingsError.RequireRange(
                failures,
                "Playwright:Context:Video:Width",
                options.Video.Width,
                1,
                int.MaxValue
            );
            SettingsError.RequireRange(
                failures,
                "Playwright:Context:Video:Height",
                options.Video.Height,
                1,
                int.MaxValue
            );
        }
    }

    private static void ValidateProxy(ProxyOptions options, List<string> failures)
    {
        if (!options.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.Server))
        {
            failures.Add(
                SettingsError.Required("Playwright:Proxy:Server", "Укажите адрес прокси-сервера.")
            );
            return;
        }

        if (!Uri.TryCreate(options.Server, UriKind.Absolute, out _))
        {
            failures.Add(
                SettingsError.Invalid(
                    "Playwright:Proxy:Server",
                    options.Server,
                    "Укажите абсолютный URI прокси-сервера."
                )
            );
        }
    }
}

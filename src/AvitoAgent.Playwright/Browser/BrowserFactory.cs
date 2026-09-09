using AvitoAgent.Playwright.Exceptions;
using AvitoAgent.Playwright.Logging;
using AvitoAgent.Playwright.Options;
using AvitoAgent.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using PWContextOptions = Microsoft.Playwright.BrowserNewContextOptions;
using PWLaunchOptions = Microsoft.Playwright.BrowserTypeLaunchOptions;
using PWPersistentOptions = Microsoft.Playwright.BrowserTypeLaunchPersistentContextOptions;
using PWProxy = Microsoft.Playwright.Proxy;
using PWViewportSize = Microsoft.Playwright.ViewportSize;

namespace AvitoAgent.Playwright.Browser;

internal static class BrowserFactory
{
    private const string DisableAutomationBlink = "--disable-blink-features=AutomationControlled";

    private static readonly string[] FingerprintDefaultArgs =
    [
        "--enable-automation",
    ];

    public static async Task<IBrowser> LaunchAsync(
        IPlaywright playwright,
        PlaywrightOptions options,
        ILogger? logger = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var launch = options.Launch;
        var browserType = GetBrowserType(playwright, launch.Browser);
        var launchOptions = BuildLaunchOptions(launch);

        try
        {
            return await browserType.LaunchAsync(launchOptions);
        }
        catch (Exception) when (!string.IsNullOrWhiteSpace(launch.Channel) && logger is not null)
        {
            PlaywrightLog.Warning(
                logger,
                $"Не удалось запустить браузер через channel '{launch.Channel}', пробуем bundled Chromium."
            );

            launchOptions.Channel = null;

            try
            {
                return await browserType.LaunchAsync(launchOptions);
            }
            catch (Exception fallbackEx)
            {
                throw new Exceptions.PlaywrightException(
                    "Не удалось запустить браузер Playwright.",
                    fallbackEx
                );
            }
        }
        catch (Exception ex)
        {
            throw new Exceptions.PlaywrightException(
                "Не удалось запустить браузер Playwright.",
                ex
            );
        }
    }

    public static async Task<IBrowserContext> CreateContextAsync(
        IBrowser browser,
        PlaywrightOptions options,
        string? storageStatePathOverride = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var contextOptions = BuildNewContextOptions(options, storageStatePathOverride);

        try
        {
            var browserContext = await browser.NewContextAsync(contextOptions);
            await PrepareContextAsync(browserContext, options, logger: null);
            return browserContext;
        }
        catch (Exception ex)
        {
            throw new Exceptions.PlaywrightException("Не удалось создать контекст браузера.", ex);
        }
    }

    public static async Task<IBrowserContext> LaunchPersistentContextAsync(
        IPlaywright playwright,
        PlaywrightOptions options,
        string userDataDir,
        string? storageStatePathOverride,
        ILogger? logger = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(userDataDir);

        var launch = options.Launch;
        var browserType = GetBrowserType(playwright, launch.Browser);
        var persistentOptions = BuildPersistentContextOptions(options);

        try
        {
            var browserContext = await browserType.LaunchPersistentContextAsync(
                userDataDir,
                persistentOptions
            );
            await PrepareContextAsync(browserContext, options, logger);
            await StorageStateImporter.ImportIfNeededAsync(
                browserContext,
                storageStatePathOverride ?? options.Context.StorageStatePath,
                cancellationToken
            );

            return browserContext;
        }
        catch (Exception ex) when (!string.IsNullOrWhiteSpace(launch.Channel) && logger is not null)
        {
            PlaywrightLog.Warning(
                logger,
                $"Не удалось запустить установленный Chrome ({launch.Channel}): {BrowserErrorText.Describe(ex)}"
            );

            throw new Exceptions.PlaywrightException(
                "Не удалось открыть Chrome. Закройте все окна Chrome (в том числе оставшиеся от прошлого запуска) и запустите агент снова.",
                ex
            );
        }
        catch (Exception ex)
        {
            throw new Exceptions.PlaywrightException("Не удалось запустить профиль браузера.", ex);
        }
    }

    private static async Task PrepareContextAsync(
        IBrowserContext browserContext,
        PlaywrightOptions options,
        ILogger? logger
    )
    {
        await browserContext.AddInitScriptAsync(StealthScripts.Main);

        if (!options.BlockTrackers)
        {
            return;
        }

        // Перехватываем только трекеры. Route на "**/*" прогонял бы весь трафик Avito
        // через сеть Playwright (иной порядок заголовков, лишний CDP) - это детектится антиботом.
        await browserContext.RouteAsync(
            TrackerBlocklist.ShouldBlock,
            route => route.AbortAsync()
        );

        if (logger is not null)
        {
            PlaywrightLog.TrackersBlocked(logger);
        }
    }

    private static PWPersistentOptions BuildPersistentContextOptions(PlaywrightOptions options)
    {
        var launch = options.Launch;
        var context = options.Context;
        var persistentOptions = new PWPersistentOptions
        {
            Headless = launch.Headless,
            SlowMo = launch.SlowMo,
            Timeout = launch.Timeout,
            Channel = launch.Channel,
            ExecutablePath = launch.ExecutablePath,
            ChromiumSandbox = launch.ChromiumSandbox,
            IgnoreHTTPSErrors = context.IgnoreHttpsErrors || launch.IgnoreHttpsErrors,
            AcceptDownloads = context.AcceptDownloads,
            JavaScriptEnabled = context.JavaScriptEnabled,
            Offline = context.Offline,
            Args = BuildChromeArguments(launch),
            Proxy = BuildProxy(options.Proxy),
        };

        ApplyDefaultArgPolicy(launch, ignoreAll => persistentOptions.IgnoreAllDefaultArgs = ignoreAll,
            ignore => persistentOptions.IgnoreDefaultArgs = ignore);

        ApplyOptionalEmulation(persistentOptions, launch, context);
        return persistentOptions;
    }

    private static PWLaunchOptions BuildLaunchOptions(LaunchOptions launch)
    {
        var launchOptions = new PWLaunchOptions
        {
            Headless = launch.Headless,
            SlowMo = launch.SlowMo,
            Timeout = launch.Timeout,
            Channel = launch.Channel,
            ExecutablePath = launch.ExecutablePath,
            ChromiumSandbox = launch.ChromiumSandbox,
            Args = BuildChromeArguments(launch),
        };

        ApplyDefaultArgPolicy(launch, ignoreAll => launchOptions.IgnoreAllDefaultArgs = ignoreAll,
            ignore => launchOptions.IgnoreDefaultArgs = ignore);

        return launchOptions;
    }

    private static PWContextOptions BuildNewContextOptions(
        PlaywrightOptions options,
        string? storageStatePathOverride
    )
    {
        var launch = options.Launch;
        var context = options.Context;
        var contextOptions = new PWContextOptions
        {
            IgnoreHTTPSErrors = context.IgnoreHttpsErrors || launch.IgnoreHttpsErrors,
            AcceptDownloads = context.AcceptDownloads,
            JavaScriptEnabled = context.JavaScriptEnabled,
            Offline = context.Offline,
            Proxy = BuildProxy(options.Proxy),
        };

        ApplyOptionalEmulation(contextOptions, launch, context);

        var storagePath = storageStatePathOverride ?? context.StorageStatePath;
        if (!string.IsNullOrWhiteSpace(storagePath) && File.Exists(storagePath))
        {
            contextOptions.StorageStatePath = storagePath;
        }

        return contextOptions;
    }

    private static void ApplyOptionalEmulation(
        PWPersistentOptions target,
        LaunchOptions launch,
        ContextOptions context
    )
    {
        if (!UsesInstalledBrowser(launch))
        {
            if (!string.IsNullOrWhiteSpace(context.Locale))
            {
                target.Locale = context.Locale;
            }

            if (!string.IsNullOrWhiteSpace(context.TimezoneId))
            {
                target.TimezoneId = context.TimezoneId;
            }
        }

        if (!string.IsNullOrWhiteSpace(context.UserAgent))
        {
            target.UserAgent = context.UserAgent;
        }

        if (context.EmulateViewport)
        {
            target.ViewportSize = new PWViewportSize
            {
                Width = context.Viewport.Width,
                Height = context.Viewport.Height,
            };
            target.DeviceScaleFactor = (float?)context.DeviceScaleFactor;
            target.HasTouch = context.HasTouch;
            target.IsMobile = context.IsMobile;
        }
        else
        {
            target.ViewportSize = PWViewportSize.NoViewport;
        }

        var color = ParseColorScheme(context.ColorScheme);
        if (color is not null)
        {
            target.ColorScheme = color;
        }

        var motion = ParseReducedMotion(context.ReducedMotion);
        if (motion is not null)
        {
            target.ReducedMotion = motion;
        }

        if (context.Permissions.Count > 0)
        {
            target.Permissions = context.Permissions;
        }

        ApplyGeolocationAndVideo(target, context);
    }

    private static void ApplyOptionalEmulation(
        PWContextOptions target,
        LaunchOptions launch,
        ContextOptions context
    )
    {
        if (!UsesInstalledBrowser(launch))
        {
            if (!string.IsNullOrWhiteSpace(context.Locale))
            {
                target.Locale = context.Locale;
            }

            if (!string.IsNullOrWhiteSpace(context.TimezoneId))
            {
                target.TimezoneId = context.TimezoneId;
            }
        }

        if (!string.IsNullOrWhiteSpace(context.UserAgent))
        {
            target.UserAgent = context.UserAgent;
        }

        if (context.EmulateViewport)
        {
            target.ViewportSize = new PWViewportSize
            {
                Width = context.Viewport.Width,
                Height = context.Viewport.Height,
            };
            target.DeviceScaleFactor = (float?)context.DeviceScaleFactor;
            target.HasTouch = context.HasTouch;
            target.IsMobile = context.IsMobile;
        }
        else
        {
            target.ViewportSize = PWViewportSize.NoViewport;
        }

        var color = ParseColorScheme(context.ColorScheme);
        if (color is not null)
        {
            target.ColorScheme = color;
        }

        var motion = ParseReducedMotion(context.ReducedMotion);
        if (motion is not null)
        {
            target.ReducedMotion = motion;
        }

        if (context.Permissions.Count > 0)
        {
            target.Permissions = context.Permissions;
        }

        if (context.Geolocation?.IsConfigured == true)
        {
            target.Geolocation = new Geolocation
            {
                Latitude = (float)context.Geolocation.Latitude,
                Longitude = (float)context.Geolocation.Longitude,
                Accuracy = (float?)context.Geolocation.Accuracy,
            };
        }

        if (context.Video?.IsConfigured == true)
        {
            target.RecordVideoDir = context.Video.Directory;
            target.RecordVideoSize = new RecordVideoSize
            {
                Width = context.Video.Width,
                Height = context.Video.Height,
            };
        }
    }

    private static void ApplyGeolocationAndVideo(PWPersistentOptions target, ContextOptions context)
    {
        if (context.Geolocation?.IsConfigured == true)
        {
            target.Geolocation = new Geolocation
            {
                Latitude = (float)context.Geolocation.Latitude,
                Longitude = (float)context.Geolocation.Longitude,
                Accuracy = (float?)context.Geolocation.Accuracy,
            };
        }

        if (context.Video?.IsConfigured == true)
        {
            target.RecordVideoDir = context.Video.Directory;
            target.RecordVideoSize = new RecordVideoSize
            {
                Width = context.Video.Width,
                Height = context.Video.Height,
            };
        }
    }

    private static List<string> BuildChromeArguments(LaunchOptions launch)
    {
        var args = new List<string> { DisableAutomationBlink };

        if (launch.DevTools)
        {
            args.Add("--auto-open-devtools-for-tabs");
        }

        foreach (var argument in launch.Arguments)
        {
            if (string.IsNullOrWhiteSpace(argument))
            {
                continue;
            }

            if (!args.Contains(argument, StringComparer.OrdinalIgnoreCase))
            {
                args.Add(argument);
            }
        }

        return args;
    }

    private static void ApplyDefaultArgPolicy(
        LaunchOptions launch,
        Action<bool> setIgnoreAll,
        Action<IEnumerable<string>> setIgnoreList
    )
    {
        if (launch.IgnoreAllDefaultArguments)
        {
            setIgnoreAll(true);
            return;
        }

        var ignored = new List<string>(FingerprintDefaultArgs);
        foreach (var argument in launch.IgnoreDefaultArguments)
        {
            if (
                !string.IsNullOrWhiteSpace(argument)
                && !ignored.Contains(argument, StringComparer.OrdinalIgnoreCase)
            )
            {
                ignored.Add(argument);
            }
        }

        setIgnoreList(ignored);
    }

    private static bool UsesInstalledBrowser(LaunchOptions launch) =>
        !string.IsNullOrWhiteSpace(launch.Channel)
        || !string.IsNullOrWhiteSpace(launch.ExecutablePath);

    private static PWProxy? BuildProxy(ProxyOptions proxy)
    {
        if (!proxy.IsConfigured)
        {
            return null;
        }

        return new PWProxy
        {
            Server = proxy.Server!,
            Username = proxy.Username,
            Password = proxy.Password,
            Bypass = proxy.Bypass,
        };
    }

    private static IBrowserType GetBrowserType(IPlaywright playwright, BrowserEngine engine) =>
        engine switch
        {
            BrowserEngine.Firefox => playwright.Firefox,
            BrowserEngine.WebKit => playwright.Webkit,
            _ => playwright.Chromium,
        };

    private static ColorScheme? ParseColorScheme(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "dark" => ColorScheme.Dark,
            "light" => ColorScheme.Light,
            "no-preference" => ColorScheme.NoPreference,
            _ => null,
        };

    private static ReducedMotion? ParseReducedMotion(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "reduce" => ReducedMotion.Reduce,
            "no-preference" => ReducedMotion.NoPreference,
            _ => null,
        };
}

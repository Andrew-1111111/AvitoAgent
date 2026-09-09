using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using AvitoAgent.Playwright.Options;
using AvitoAgent.Shared.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog;

namespace AvitoAgent.App;

internal static partial class ConfigurationErrors
{
    private static readonly string[] SerilogLevels =
    [
        "Verbose",
        "Debug",
        "Information",
        "Warning",
        "Error",
        "Fatal",
    ];

    [GeneratedRegex(
        @"(?:configuration value at|value at)\s+'(?<path>[^']+)'\s+to\s+type\s+'?(?<type>[\w.]+)'?",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex BindPathRegex();

    public static bool TryValidate(IServiceProvider services)
    {
        var errors = new List<string>();
        var configuration = services.GetRequiredService<IConfiguration>();

        ValidateSerilog(configuration, errors);
        CheckUnknownKeys(errors);
        Check<Socks5Options>(services, errors);
        Check<NetworkOptions>(services, errors);
        Check<AvitoOptions>(services, errors);
        Check<WorkerOptions>(services, errors);
        Check<LmStudioOptions>(services, errors);
        Check<TelegramOptions>(services, errors);
        Check<PlaywrightOptions>(services, errors);
        Check<DebugOptions>(services, errors);
        Check<PathOptions>(services, errors);

        if (errors.Count == 0)
        {
            return true;
        }

        Write(errors);
        return false;
    }

    public static bool IsCancellation(Exception exception)
    {
        foreach (var current in Flatten(exception))
        {
            if (current is OperationCanceledException)
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryWrite(Exception ex)
    {
        if (IsCancellation(ex))
        {
            return false;
        }

        var errors = new List<string>();
        Collect(ex, errors);
        if (errors.Count == 0)
        {
            return false;
        }

        Write(errors);
        return true;
    }

    public static void WriteSnapshot(IServiceProvider services)
    {
        var avito = services.GetRequiredService<IOptions<AvitoOptions>>().Value;
        var worker = services.GetRequiredService<IOptions<WorkerOptions>>().Value;
        var lm = services.GetRequiredService<IOptions<LmStudioOptions>>().Value;
        var telegram = services.GetRequiredService<IOptions<TelegramOptions>>().Value;
        var playwright = services.GetRequiredService<IOptions<PlaywrightOptions>>().Value;
        var debug = services.GetRequiredService<IOptions<DebugOptions>>().Value;
        var socks5 = services.GetRequiredService<IOptions<Socks5Options>>().Value;
        var network = services.GetRequiredService<IOptions<NetworkOptions>>().Value;

        Log.Information("appsettings.json: все секции прочитаны, валидация успешна.");
        Log.Information(
            "LmStudio: Enabled={Enabled}, Model={Model}, Timeout={Timeout}s",
            lm.Enabled,
            lm.Model,
            lm.RequestTimeoutSeconds
        );
        Log.Information(
            "Telegram: Enabled={Enabled}, Control={Control}, Socks5={Socks5}",
            telegram.Enabled,
            telegram.ControlEnabled,
            telegram.Socks5.Enabled
        );
        Log.Information(
            "Worker: Interval={Minutes} мин, Sleep={From}-{To} {Tz}, MaxResults={MaxResults}",
            worker.PollingIntervalMinutes,
            worker.SleepFromHour,
            worker.SleepToHour,
            worker.TimezoneId,
            worker.MaxResults
        );
        Log.Information(
            "Avito: Sort={Sort}, Location={Location}, DeliveryOnly={Delivery}, MaxPages={Pages}",
            avito.Filters.Sort,
            string.Join(", ", avito.GetLocationSlugs()),
            avito.DeliveryOnly,
            avito.MaxPages
        );
        Log.Information(
            "Avito.Auth: Enabled={Enabled}, Phone={Phone}",
            avito.Auth.Enabled,
            string.IsNullOrWhiteSpace(avito.Auth.Phone) ? "(пусто)" : avito.Auth.Phone
        );
        Log.Information(
            "Playwright: {Browser}/{Channel}, Headless={Headless}, Persistent={Persistent}, BlockTrackers={BlockTrackers}",
            playwright.Launch.Browser,
            playwright.Launch.Channel,
            playwright.Launch.Headless,
            playwright.UsePersistentProfile,
            playwright.BlockTrackers
        );
        Log.Information(
            "Debug: BrowserLog={BrowserLog}, ExportListingPhotos={Export}, Dir={Dir}",
            debug.BrowserLog,
            debug.ExportListingPhotos,
            debug.ListingPhotosDirectory
        );
        Log.Information(
            "Socks5: Enabled={Enabled}, Network.Interface={Interface}",
            socks5.Enabled,
            string.IsNullOrWhiteSpace(network.Interface) ? "(по умолчанию ОС)" : network.Interface
        );
    }

    private static readonly Dictionary<string, Type> TypedSections = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["LmStudio"] = typeof(LmStudioOptions),
        ["Telegram"] = typeof(TelegramOptions),
        ["Worker"] = typeof(WorkerOptions),
        ["Avito"] = typeof(AvitoOptions),
        ["Playwright"] = typeof(PlaywrightOptions),
        ["Socks5"] = typeof(Socks5Options),
        ["Network"] = typeof(NetworkOptions),
        ["Debug"] = typeof(DebugOptions),
        ["Paths"] = typeof(PathOptions),
    };

    private static void CheckUnknownKeys(List<string> errors)
    {
        var jsonPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(jsonPath))
        {
            return;
        }

        IConfiguration json;
        try
        {
            json = new ConfigurationBuilder().AddJsonFile(jsonPath, optional: false).Build();
        }
        catch (Exception ex)
        {
            errors.Add($"Не удалось прочитать appsettings.json: {ex.Message}");
            return;
        }

        foreach (var child in json.GetChildren())
        {
            if (child.Key.Equals("Serilog", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TypedSections.TryGetValue(child.Key, out var type))
            {
                errors.Add(
                    $"Неизвестная секция «{child.Key}» в appsettings.json. Проверьте имя ключа."
                );
                continue;
            }

            CollectUnknownKeys(child, type, child.Key, errors);
        }
    }

    private static void CollectUnknownKeys(
        IConfiguration section,
        Type type,
        string path,
        List<string> errors
    )
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.GetIndexParameters().Length == 0)
            .ToDictionary(static property => property.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var child in section.GetChildren())
        {
            if (int.TryParse(child.Key, out _))
            {
                continue;
            }

            if (!properties.TryGetValue(child.Key, out var property))
            {
                errors.Add($"Неизвестный ключ {path}:{child.Key} в appsettings.json.");
                continue;
            }

            if (!child.GetChildren().Any() || IsLeafType(property.PropertyType))
            {
                continue;
            }

            var nested = Unwrap(property.PropertyType);
            if (nested is not null)
            {
                CollectUnknownKeys(child, nested, $"{path}:{child.Key}", errors);
            }
        }
    }

    private static bool IsLeafType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive
            || type.IsEnum
            || type == typeof(string)
            || type == typeof(decimal)
            || (typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string));
    }

    private static Type? Unwrap(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (
            type.IsPrimitive
            || type.IsEnum
            || type == typeof(string)
            || type == typeof(decimal)
            || (typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string))
        )
        {
            return null;
        }

        return type.IsClass ? type : null;
    }

    private static void Check<TOptions>(IServiceProvider services, List<string> errors)
        where TOptions : class
    {
        try
        {
            _ = services.GetRequiredService<IOptions<TOptions>>().Value;
        }
        catch (Exception ex)
        {
            var before = errors.Count;
            Collect(ex, errors);
            if (errors.Count == before)
            {
                errors.Add(
                    "Не удалось проверить настройки приложения: " + ex.GetBaseException().Message
                );
            }
        }
    }

    private static void Collect(Exception exception, List<string> errors)
    {
        foreach (var current in Flatten(exception))
        {
            if (current is OptionsValidationException validation)
            {
                errors.AddRange(
                    validation
                        .Failures.Where(static failure => !string.IsNullOrWhiteSpace(failure))
                        .Select(static failure => failure.Trim())
                );
                continue;
            }

            var match = BindPathRegex().Match(current.Message);
            if (match.Success)
            {
                errors.Add(
                    SettingsError.BindFailed(match.Groups["path"].Value, match.Groups["type"].Value)
                );
            }
        }
    }

    private static void ValidateSerilog(IConfiguration configuration, List<string> errors)
    {
        var defaultLevel = configuration["Serilog:MinimumLevel:Default"];
        if (!string.IsNullOrWhiteSpace(defaultLevel))
        {
            SettingsError.RequireOneOf(
                errors,
                "Serilog:MinimumLevel:Default",
                defaultLevel,
                SerilogLevels
            );
        }

        foreach (
            var child in configuration.GetSection("Serilog:MinimumLevel:Override").GetChildren()
        )
        {
            SettingsError.RequireOneOf(
                errors,
                $"Serilog:MinimumLevel:Override:{child.Key}",
                child.Value,
                SerilogLevels
            );
        }
    }

    private static void Write(IReadOnlyList<string> details)
    {
        var unique = details
            .Where(static detail => !string.IsNullOrWhiteSpace(detail))
            .Select(static detail => detail.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Log.Error("Ошибка в appsettings.json. Исправьте значение и запустите программу снова.");
        foreach (var detail in unique)
        {
            Log.Error("{Detail}", detail);
        }
    }

    private static IEnumerable<Exception> Flatten(Exception exception)
    {
        var queue = new Queue<Exception>();
        queue.Enqueue(exception);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            yield return current;

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    queue.Enqueue(inner);
                }
            }
            else if (current.InnerException is not null)
            {
                queue.Enqueue(current.InnerException);
            }
        }
    }
}

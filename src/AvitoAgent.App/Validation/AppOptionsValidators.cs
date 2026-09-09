using AvitoAgent.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace AvitoAgent.App.Validation;

internal sealed class WorkerOptionsValidator : IValidateOptions<WorkerOptions>
{
    public ValidateOptionsResult Validate(string? name, WorkerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        SettingsError.RequireRange(
            failures,
            "Worker:PollingIntervalMinutes",
            options.PollingIntervalMinutes,
            1,
            int.MaxValue
        );
        SettingsError.RequireRange(failures, "Worker:MinPrice", options.MinPrice, 0, int.MaxValue);
        SettingsError.RequireRange(failures, "Worker:MaxPrice", options.MaxPrice, 0, int.MaxValue);
        SettingsError.RequireRange(
            failures,
            "Worker:MaxResults",
            options.MaxResults,
            1,
            int.MaxValue
        );
        SettingsError.RequireRange(
            failures,
            "Worker:MinAuthenticityScore",
            options.MinAuthenticityScore,
            0,
            100
        );

        if (options.SleepFromHour is not null)
        {
            SettingsError.RequireRange(
                failures,
                "Worker:SleepFromHour",
                options.SleepFromHour.Value,
                0,
                23
            );
        }

        if (options.SleepToHour is not null)
        {
            SettingsError.RequireRange(
                failures,
                "Worker:SleepToHour",
                options.SleepToHour.Value,
                0,
                23
            );
        }

        if ((options.SleepFromHour is null) != (options.SleepToHour is null))
        {
            failures.Add("Worker:SleepFromHour и Worker:SleepToHour должны быть заданы вместе.");
        }

        if (!string.IsNullOrWhiteSpace(options.TimezoneId))
        {
            try
            {
                _ = TimeZoneInfo.FindSystemTimeZoneById(options.TimezoneId.Trim());
            }
            catch (Exception)
            {
                failures.Add(
                    $"Неверное значение Worker:TimezoneId в appsettings.json: «{options.TimezoneId.Trim()}». "
                        + "Укажите IANA-идентификатор, например Europe/Moscow."
                );
            }
        }

        if (options.MinPrice > 0 && options.MaxPrice > 0 && options.MinPrice > options.MaxPrice)
        {
            failures.Add(
                $"Неверное значение Worker:MinPrice в appsettings.json: «{options.MinPrice}». "
                    + $"Оно больше Worker:MaxPrice («{options.MaxPrice}»). Укажите MinPrice ≤ MaxPrice либо 0 (без границы)."
            );
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

internal sealed class LmStudioOptionsValidator : IValidateOptions<LmStudioOptions>
{
    public ValidateOptionsResult Validate(string? name, LmStudioOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        SettingsError.RequireHttpUrl(failures, "LmStudio:BaseUrl", options.BaseUrl);
        SettingsError.RequireRange(
            failures,
            "LmStudio:MaxImages",
            options.MaxImages,
            0,
            int.MaxValue
        );
        SettingsError.RequireRange(
            failures,
            "LmStudio:ContextUsagePercent",
            options.ContextUsagePercent,
            1,
            100
        );
        SettingsError.RequireRange(
            failures,
            "LmStudio:MaxCompletionTokens",
            options.MaxCompletionTokens,
            1,
            int.MaxValue
        );
        SettingsError.RequireRange(
            failures,
            "LmStudio:MaxImageSidePx",
            options.MaxImageSidePx,
            1,
            int.MaxValue
        );
        SettingsError.RequireRange(
            failures,
            "LmStudio:ImageJpegQuality",
            options.ImageJpegQuality,
            1,
            100
        );
        SettingsError.RequireRange(failures, "LmStudio:Temperature", options.Temperature, 0, 2);
        SettingsError.RequireRange(
            failures,
            "LmStudio:RequestTimeoutSeconds",
            options.RequestTimeoutSeconds,
            1,
            int.MaxValue
        );
        SettingsError.RequireRange(
            failures,
            "LmStudio:LoadContextLength",
            options.LoadContextLength,
            0,
            int.MaxValue
        );

        if (options.Enabled && !options.AutoSelectModel)
        {
            SettingsError.RequireNotEmpty(
                failures,
                "LmStudio:Model",
                options.Model,
                "Укажите модель или включите AutoSelectModel."
            );
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

internal sealed class TelegramOptionsValidator : IValidateOptions<TelegramOptions>
{
    public ValidateOptionsResult Validate(string? name, TelegramOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.Enabled)
        {
            SettingsError.RequireNotEmpty(
                failures,
                "Telegram:BotToken",
                options.BotToken,
                "Укажите токен бота."
            );
            SettingsError.RequireNotEmpty(
                failures,
                "Telegram:ChatId",
                options.ChatId,
                "Укажите идентификатор чата."
            );
        }

        Socks5Validation.ValidateSocks5(options.Socks5 ??= new(), "Telegram:Socks5", failures);
        NetworkInterfaceValidation.Validate(
            failures,
            "Telegram:NetworkInterface",
            options.NetworkInterface
        );
        SettingsError.RequireRange(failures, "Telegram:MaxPhotos", options.MaxPhotos, 1, 10);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

internal sealed class NetworkOptionsValidator : IValidateOptions<NetworkOptions>
{
    public ValidateOptionsResult Validate(string? name, NetworkOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        NetworkInterfaceValidation.Validate(failures, "Network:Interface", options.Interface);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

internal sealed class Socks5OptionsValidator : IValidateOptions<Socks5Options>
{
    public ValidateOptionsResult Validate(string? name, Socks5Options options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        Socks5Validation.ValidateSocks5(options, "Socks5", failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

file static class NetworkInterfaceValidation
{
    public static void Validate(List<string> failures, string path, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (
            !AvitoAgent.Infrastructure.Http.NetworkInterfaceResolver.TryResolve(
                value,
                out _,
                out var error
            )
        )
        {
            failures.Add($"Неверное значение {path} в appsettings.json: «{value.Trim()}». {error}");
        }
    }
}

file static class Socks5Validation
{
    public static void ValidateSocks5(Socks5Options socks5, string path, List<string> failures)
    {
        if (!socks5.Enabled)
        {
            return;
        }

        SettingsError.RequireNotEmpty(
            failures,
            $"{path}:Host",
            socks5.Host,
            "Укажите адрес SOCKS5-прокси."
        );
        SettingsError.RequireRange(failures, $"{path}:Port", socks5.Port, 1, 65_535);
    }
}

internal sealed class DebugOptionsValidator : IValidateOptions<DebugOptions>
{
    public ValidateOptionsResult Validate(string? name, DebugOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.ExportListingPhotos)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        SettingsError.RequireNotEmpty(
            failures,
            "Debug:ListingPhotosDirectory",
            options.ListingPhotosDirectory,
            "Укажите каталог выгрузки фото."
        );

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

internal sealed class PathOptionsValidator : IValidateOptions<PathOptions>
{
    public ValidateOptionsResult Validate(string? name, PathOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        SettingsError.RequireNotEmpty(
            failures,
            "Paths:Data",
            options.Data,
            "Укажите каталог данных."
        );
        SettingsError.RequireNotEmpty(
            failures,
            "Paths:Logs",
            options.Logs,
            "Укажите каталог логов."
        );
        SettingsError.RequireNotEmpty(
            failures,
            "Paths:Prompts",
            options.Prompts,
            "Укажите каталог промптов."
        );

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

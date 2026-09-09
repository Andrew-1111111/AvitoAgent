using AvitoAgent.App;
using AvitoAgent.App.Validation;
using AvitoAgent.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace AvitoAgent.Tests.App;

public sealed class AppOptionsValidatorTests
{
    [Fact]
    public void Worker_valid_defaults()
    {
        var result = new WorkerOptionsValidator().Validate(null, new WorkerOptions());
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Worker_sleep_pair_incomplete_and_min_gt_max()
    {
        var incomplete = new WorkerOptions { SleepFromHour = 23, SleepToHour = null };
        Assert.True(new WorkerOptionsValidator().Validate(null, incomplete).Failed);

        var badRange = new WorkerOptions { MinPrice = 5000, MaxPrice = 1000 };
        var result = new WorkerOptionsValidator().Validate(null, badRange);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("MinPrice", StringComparison.Ordinal));
    }

    [Fact]
    public void Worker_bad_timezone()
    {
        var options = new WorkerOptions { TimezoneId = "Not/AZone" };
        Assert.True(new WorkerOptionsValidator().Validate(null, options).Failed);
    }

    [Fact]
    public void LmStudio_requires_model_when_enabled_without_autoselect()
    {
        var options = new LmStudioOptions
        {
            Enabled = true,
            AutoSelectModel = false,
            Model = "",
            BaseUrl = "http://localhost:1234/v1/",
        };
        Assert.True(new LmStudioOptionsValidator().Validate(null, options).Failed);
    }

    [Fact]
    public void LmStudio_range_checks()
    {
        var options = new LmStudioOptions
        {
            BaseUrl = "http://localhost:1234/v1/",
            ContextUsagePercent = 0,
            Temperature = 5,
        };
        Assert.True(new LmStudioOptionsValidator().Validate(null, options).Failed);
    }

    [Fact]
    public void Telegram_enabled_requires_token_and_chat()
    {
        var options = new TelegramOptions { Enabled = true, BotToken = "", ChatId = "" };
        var result = new TelegramOptionsValidator().Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("BotToken", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, f => f.Contains("ChatId", StringComparison.Ordinal));
    }

    [Fact]
    public void Telegram_max_photos_and_socks5()
    {
        var options = new TelegramOptions
        {
            Enabled = false,
            MaxPhotos = 20,
            Socks5 = new Socks5Options { Enabled = true, Host = "", Port = 0 },
        };
        Assert.True(new TelegramOptionsValidator().Validate(null, options).Failed);
    }

    [Fact]
    public void Socks5_and_debug_and_paths()
    {
        Assert.True(
            new Socks5OptionsValidator()
                .Validate(null, new Socks5Options { Enabled = true, Host = "h", Port = 99999 })
                .Failed
        );

        Assert.True(
            new DebugOptionsValidator()
                .Validate(
                    null,
                    new DebugOptions { ExportListingPhotos = true, ListingPhotosDirectory = "" }
                )
                .Failed
        );

        Assert.Equal(
            ValidateOptionsResult.Success,
            new DebugOptionsValidator().Validate(
                null,
                new DebugOptions { ExportListingPhotos = false }
            )
        );

        Assert.True(
            new PathOptionsValidator()
                .Validate(null, new PathOptions { Data = "", Logs = "", Prompts = "" })
                .Failed
        );
        Assert.Equal(
            ValidateOptionsResult.Success,
            new PathOptionsValidator().Validate(
                null,
                new PathOptions
                {
                    Data = "data",
                    Logs = "logs",
                    Prompts = "prompts",
                }
            )
        );
    }

    [Fact]
    public void ConfigurationErrors_IsCancellation()
    {
        Assert.True(ConfigurationErrors.IsCancellation(new OperationCanceledException()));
        Assert.True(
            ConfigurationErrors.IsCancellation(
                new AggregateException(new TaskCanceledException())
            )
        );
        Assert.False(ConfigurationErrors.IsCancellation(new InvalidOperationException()));
    }
}

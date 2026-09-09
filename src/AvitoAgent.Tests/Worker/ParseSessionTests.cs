using System.Text.Json;
using AvitoAgent.AI;
using AvitoAgent.Shared.Configuration;
using AvitoAgent.Worker;
using Microsoft.Extensions.Options;

namespace AvitoAgent.Tests.Worker;

public sealed class ParseSessionTests
{
    [Fact]
    public void Starts_stopped_when_telegram_control_enabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "avito-parse-" + Guid.NewGuid());
        try
        {
            var paths = new ApplicationPaths(root);
            var session = new ParseSession(
                Options.Create(
                    new WorkerOptions
                    {
                        Keywords = ["генератор"],
                        MaxResults = 10,
                        PollingIntervalMinutes = 5,
                    }
                ),
                Options.Create(
                    new AvitoOptions
                    {
                        Filters = new AvitoFiltersOptions
                        {
                            LocationSlug = ["moskva"],
                            Sort = "По дате",
                            DeliveryOnly = true,
                        },
                    }
                ),
                Options.Create(new TelegramOptions { Enabled = true, ControlEnabled = true }),
                paths
            );

            Assert.False(session.IsRunning);
            Assert.True(session.TryStart(out var error));
            Assert.Null(error);
            Assert.True(session.IsRunning);
            session.Stop();
            Assert.False(session.IsRunning);

            var snap = session.Snapshot();
            Assert.Contains("генератор", snap.Keywords);
            Assert.True(snap.DeliveryOnly);
            Assert.Equal(10, snap.MaxResults);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void SetSort_and_SetCondition_ignore_invalid()
    {
        var root = Path.Combine(Path.GetTempPath(), "avito-parse-" + Guid.NewGuid());
        try
        {
            var session = CreateOpenSession(root);
            session.SetSort("рандом");
            session.SetCondition("zzz");
            var snap = session.Snapshot();
            Assert.Equal("По дате", snap.Sort);
            Assert.Equal("Все", snap.Condition);

            session.SetSort("Дешевле");
            session.SetCondition("Б/у");
            snap = session.Snapshot();
            Assert.Equal("Дешевле", snap.Sort);
            Assert.Equal("Б/у", snap.Condition);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CleanKeywords_dedupes_and_trims()
    {
        var root = Path.Combine(Path.GetTempPath(), "avito-parse-" + Guid.NewGuid());
        try
        {
            var session = CreateOpenSession(root);
            session.SetKeywords(["  a ", "a", "b", ""]);
            Assert.Equal(["a", "b"], session.Snapshot().Keywords);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void TryStart_without_keywords_fails()
    {
        var root = Path.Combine(Path.GetTempPath(), "avito-parse-" + Guid.NewGuid());
        try
        {
            var session = CreateOpenSession(root);
            session.SetKeywords([]);
            Assert.False(session.TryStart(out var error));
            Assert.Contains("запрос", error!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Persist_and_reload_when_telegram_control_on()
    {
        var root = Path.Combine(Path.GetTempPath(), "avito-parse-" + Guid.NewGuid());
        try
        {
            var first = CreateControlSession(root);
            first.SetKeywords(["генератор"]);
            first.SetPrice(1000, 5000);
            first.SetLocations([]);
            first.SetPollingIntervalMinutes(12);
            first.SetSleepHours(23, 7);
            first.SetDeliveryOnly(true);
            first.SetSellerType("Частные");
            first.SetFromCurrentDateTime(true);

            var second = CreateControlSession(root);
            var snap = second.Snapshot();
            Assert.Contains("генератор", snap.Keywords);
            Assert.Equal(1000, snap.MinPrice);
            Assert.Equal(5000, snap.MaxPrice);
            Assert.Equal(["rossiya"], snap.LocationSlugs);
            Assert.Equal(12, snap.PollingIntervalMinutes);
            Assert.Equal(23, snap.SleepFromHour);
            Assert.Equal(7, snap.SleepToHour);
            Assert.True(snap.DeliveryOnly);
            Assert.Equal("Частные", snap.SellerType);
            Assert.True(snap.FromCurrentDateTime);
            Assert.False(second.IsRunning);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void AppendKeywords_and_exclusions_and_price_clamp()
    {
        var root = Path.Combine(Path.GetTempPath(), "avito-parse-" + Guid.NewGuid());
        try
        {
            var session = CreateOpenSession(root);
            session.AppendKeywords(["два"]);
            session.SetExcludedKeywords(["б/у"]);
            session.SetPrice(-5, -1);
            var snap = session.Snapshot();
            Assert.Contains("тест", snap.Keywords);
            Assert.Contains("два", snap.Keywords);
            Assert.Equal(["б/у"], snap.ExcludedKeywords);
            Assert.Equal(0, snap.MinPrice);
            Assert.Equal(0, snap.MaxPrice);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Control_off_starts_running()
    {
        var root = Path.Combine(Path.GetTempPath(), "avito-parse-" + Guid.NewGuid());
        try
        {
            Assert.True(CreateOpenSession(root).IsRunning);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Corrupt_json_falls_back_to_defaults()
    {
        var root = Path.Combine(Path.GetTempPath(), "avito-parse-" + Guid.NewGuid());
        try
        {
            var paths = new ApplicationPaths(root);
            Directory.CreateDirectory(paths.Data);
            File.WriteAllText(Path.Combine(paths.Data, "parse-session.json"), "{not-json");
            var session = CreateControlSession(root);
            Assert.Contains("тест", session.Snapshot().Keywords);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static ParseSession CreateOpenSession(string root)
    {
        var paths = new ApplicationPaths(root);
        return new ParseSession(
            Options.Create(
                new WorkerOptions
                {
                    Keywords = ["тест"],
                    MaxResults = 5,
                    PollingIntervalMinutes = 1,
                }
            ),
            Options.Create(new AvitoOptions { Filters = new AvitoFiltersOptions() }),
            Options.Create(new TelegramOptions { Enabled = false, ControlEnabled = false }),
            paths
        );
    }

    private static ParseSession CreateControlSession(string root)
    {
        return new ParseSession(
            Options.Create(
                new WorkerOptions
                {
                    Keywords = ["тест"],
                    MaxResults = 5,
                    PollingIntervalMinutes = 1,
                }
            ),
            Options.Create(new AvitoOptions { Filters = new AvitoFiltersOptions() }),
            Options.Create(new TelegramOptions { Enabled = true, ControlEnabled = true }),
            new ApplicationPaths(root)
        );
    }
}

public sealed class ProductAnalysisResponseFormatTests
{
    [Fact]
    public void Create_json_schema()
    {
        var format = ProductAnalysisResponseFormat.Create();
        Assert.Equal("json_schema", format.Type);
        Assert.NotNull(format.JsonSchema);
        Assert.Equal("product_analysis", format.JsonSchema!.Name);
        Assert.False(format.JsonSchema.Strict);
        Assert.Equal(JsonValueKind.Object, format.JsonSchema.Schema.ValueKind);
        Assert.True(format.JsonSchema.Schema.TryGetProperty("required", out _));
    }
}

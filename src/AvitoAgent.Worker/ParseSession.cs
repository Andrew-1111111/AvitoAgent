using System.Text.Json;
using AvitoAgent.Core.Interfaces;
using AvitoAgent.Core.Models;
using AvitoAgent.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace AvitoAgent.Worker;

public sealed class ParseSession : IParseSession
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly object _gate = new();
    private readonly string _filePath;
    private readonly int _maxResults;
    private ParseSettings _settings;
    private DateTime? _publishedAfterUtc;
    private TaskCompletionSource _started = NewGate();
    private CancellationTokenSource _runCts = new();
    private bool _running;

    public ParseSession(
        IOptions<WorkerOptions> worker,
        IOptions<AvitoOptions> avito,
        IOptions<TelegramOptions> telegram,
        ApplicationPaths paths
    )
    {
        _filePath = Path.Combine(paths.Data, "parse-session.json");
        _maxResults = Math.Max(1, worker.Value.MaxResults);
        var control = telegram.Value.IsControlActive;
        _settings = Load(worker.Value, avito.Value.Filters ?? new(), persistFromTelegram: control);
        _running = !control;
        if (_running)
        {
            _started.TrySetResult();
            RefreshPublishedAfterCutoff();
        }
        else
        {
            _runCts.Cancel();
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _running;
            }
        }
    }

    public CancellationToken RunningToken
    {
        get
        {
            lock (_gate)
            {
                return _runCts.Token;
            }
        }
    }

    public DateTime? PublishedAfterUtc
    {
        get
        {
            lock (_gate)
            {
                return _publishedAfterUtc;
            }
        }
    }

    public ParseSettings Snapshot()
    {
        lock (_gate)
        {
            return Clone(_settings);
        }
    }

    public async Task WaitUntilRunningAsync(CancellationToken cancellationToken)
    {
        Task wait;
        lock (_gate)
        {
            if (_running)
            {
                return;
            }

            wait = _started.Task;
        }

        await wait.WaitAsync(cancellationToken);
    }

    public bool TryStart(out string? error)
    {
        lock (_gate)
        {
            if (_settings.Keywords.Count == 0)
            {
                error = "Сначала задайте запрос.";
                return false;
            }

            error = null;
            if (_running)
            {
                return true;
            }

            _runCts.Dispose();
            _runCts = new CancellationTokenSource();
            _running = true;
            RefreshPublishedAfterCutoff();
            _started.TrySetResult();
            return true;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            _publishedAfterUtc = null;
            _runCts.Cancel();
            _started = NewGate();
        }
    }

    public void SetKeywords(IReadOnlyList<string> keywords) =>
        Update(settings => settings with { Keywords = Clean(keywords) });

    public void AppendKeywords(IReadOnlyList<string> keywords) =>
        Update(settings => settings with { Keywords = Clean(settings.Keywords.Concat(keywords)) });

    public void SetExcludedKeywords(IReadOnlyList<string> keywords) =>
        Update(settings => settings with { ExcludedKeywords = Clean(keywords) });

    public void SetPrice(int minPrice, int maxPrice) =>
        Update(settings =>
            settings with
            {
                MinPrice = Math.Clamp(minPrice, 0, int.MaxValue),
                MaxPrice = Math.Clamp(maxPrice, 0, int.MaxValue),
            }
        );

    public void SetLocations(IReadOnlyList<string> slugs) =>
        Update(settings =>
            settings with
            {
                LocationSlugs = slugs.Count > 0 ? Clean(slugs) : ["rossiya"],
            }
        );

    public void SetSort(string sort) =>
        Update(settings =>
            settings with
            {
                Sort = AvitoSort.TryResolve(sort, out var canonical, out _)
                    ? canonical
                    : settings.Sort,
            }
        );

    public void SetDeliveryOnly(bool deliveryOnly) =>
        Update(settings => settings with { DeliveryOnly = deliveryOnly });

    public void SetCondition(string condition) =>
        Update(settings =>
            settings with
            {
                Condition = AvitoCondition.TryResolve(condition, out var canonical, out _)
                    ? canonical
                    : settings.Condition,
            }
        );

    public void SetSellerType(string sellerType) =>
        Update(settings =>
            settings with
            {
                SellerType = AvitoSellerType.TryResolve(sellerType, out var canonical, out _)
                    ? canonical
                    : settings.SellerType,
            }
        );

    public void SetFromCurrentDateTime(bool enabled)
    {
        lock (_gate)
        {
            _settings = _settings with { FromCurrentDateTime = enabled };
            Save(_settings);
            if (enabled && _running)
            {
                _publishedAfterUtc = DateTime.UtcNow;
            }
            else if (!enabled)
            {
                _publishedAfterUtc = null;
            }
        }
    }

    public void SetPollingIntervalMinutes(int minutes) =>
        Update(settings =>
            settings with
            {
                PollingIntervalMinutes = Math.Clamp(minutes, 1, int.MaxValue),
            }
        );

    public void SetSleepHours(int? fromHour, int? toHour) =>
        Update(settings => settings with { SleepFromHour = fromHour, SleepToHour = toHour });

    private void RefreshPublishedAfterCutoff()
    {
        _publishedAfterUtc = _settings.FromCurrentDateTime ? DateTime.UtcNow : null;
    }

    private void Update(Func<ParseSettings, ParseSettings> change)
    {
        lock (_gate)
        {
            _settings = change(_settings);
            Save(_settings);
        }
    }

    private ParseSettings Load(
        WorkerOptions worker,
        AvitoFiltersOptions filters,
        bool persistFromTelegram
    )
    {
        var defaults = new ParseSettings
        {
            Keywords = Clean(worker.Keywords),
            ExcludedKeywords = Clean(worker.ExcludedKeywords),
            MinPrice = worker.GetMinPrice(),
            MaxPrice = worker.GetMaxPrice(),
            MaxResults = _maxResults,
            LocationSlugs = filters.GetLocationSlugs(),
            Sort = filters.Sort,
            DeliveryOnly = filters.DeliveryOnly,
            Condition = filters.Condition,
            SellerType = filters.SellerType,
            FromCurrentDateTime = filters.FromCurrentDateTime,
            PollingIntervalMinutes = Math.Max(1, worker.PollingIntervalMinutes),
            SleepFromHour = worker.SleepFromHour,
            SleepToHour = worker.SleepToHour,
        };

        if (!persistFromTelegram || !File.Exists(_filePath))
        {
            return defaults;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<StoredSettings>(
                File.ReadAllText(_filePath),
                Json
            );
            if (stored is null)
            {
                return defaults;
            }

            return defaults with
            {
                Keywords = stored.Keywords is { Length: > 0 }
                    ? Clean(stored.Keywords)
                    : defaults.Keywords,
                ExcludedKeywords = stored.ExcludedKeywords is null
                    ? defaults.ExcludedKeywords
                    : Clean(stored.ExcludedKeywords),
                MinPrice = stored.MinPrice ?? defaults.MinPrice,
                MaxPrice = stored.MaxPrice ?? defaults.MaxPrice,
                LocationSlugs = stored.LocationSlugs is { Length: > 0 }
                    ? Clean(stored.LocationSlugs)
                    : defaults.LocationSlugs,
                Sort = string.IsNullOrWhiteSpace(stored.Sort) ? defaults.Sort : stored.Sort.Trim(),
                DeliveryOnly = stored.DeliveryOnly ?? defaults.DeliveryOnly,
                Condition = string.IsNullOrWhiteSpace(stored.Condition)
                    ? defaults.Condition
                    : stored.Condition.Trim(),
                SellerType = string.IsNullOrWhiteSpace(stored.SellerType)
                    ? defaults.SellerType
                    : stored.SellerType.Trim(),
                FromCurrentDateTime = stored.FromCurrentDateTime ?? defaults.FromCurrentDateTime,
                PollingIntervalMinutes = stored.PollingIntervalMinutes is > 0
                    ? stored.PollingIntervalMinutes.Value
                    : defaults.PollingIntervalMinutes,
                SleepFromHour =
                    stored.SleepConfigured == true ? stored.SleepFromHour : defaults.SleepFromHour,
                SleepToHour =
                    stored.SleepConfigured == true ? stored.SleepToHour : defaults.SleepToHour,
            };
        }
        catch (Exception)
        {
            return defaults;
        }
    }

    private void Save(ParseSettings settings)
    {
        var stored = new StoredSettings
        {
            Keywords = [.. settings.Keywords],
            ExcludedKeywords = [.. settings.ExcludedKeywords],
            MinPrice = settings.MinPrice,
            MaxPrice = settings.MaxPrice,
            LocationSlugs = [.. settings.LocationSlugs],
            Sort = settings.Sort,
            DeliveryOnly = settings.DeliveryOnly,
            Condition = settings.Condition,
            SellerType = settings.SellerType,
            FromCurrentDateTime = settings.FromCurrentDateTime,
            PollingIntervalMinutes = settings.PollingIntervalMinutes,
            SleepConfigured = true,
            SleepFromHour = settings.SleepFromHour,
            SleepToHour = settings.SleepToHour,
        };

        var json = JsonSerializer.Serialize(stored, Json);
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_filePath, json);
    }

    private static ParseSettings Clone(ParseSettings settings) =>
        settings with
        {
            Keywords = [.. settings.Keywords],
            ExcludedKeywords = [.. settings.ExcludedKeywords],
            LocationSlugs = [.. settings.LocationSlugs],
        };

    private static string[] Clean(IEnumerable<string>? values) =>
        [
            .. (values ?? [])
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];

    private static TaskCompletionSource NewGate() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class StoredSettings
    {
        public string[]? Keywords { get; set; }

        public string[]? ExcludedKeywords { get; set; }

        public int? MinPrice { get; set; }

        public int? MaxPrice { get; set; }

        public string[]? LocationSlugs { get; set; }

        public string? Sort { get; set; }

        public bool? DeliveryOnly { get; set; }

        public string? Condition { get; set; }

        public string? SellerType { get; set; }

        public bool? FromCurrentDateTime { get; set; }

        public int? PollingIntervalMinutes { get; set; }

        public bool? SleepConfigured { get; set; }

        public int? SleepFromHour { get; set; }

        public int? SleepToHour { get; set; }
    }
}

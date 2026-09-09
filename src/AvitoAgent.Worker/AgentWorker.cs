using AvitoAgent.Core;
using AvitoAgent.Core.Interfaces;
using AvitoAgent.Core.Models;
using AvitoAgent.Shared;
using AvitoAgent.Shared.Configuration;
using AvitoAgent.Worker.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AvitoAgent.Worker;

public sealed class AgentWorker(
    IServiceScopeFactory scopeFactory,
    IParseSession session,
    IHostApplicationLifetime lifetime,
    IOptions<WorkerOptions> options,
    IOptions<LmStudioOptions> lmStudioOptions,
    ILogger<AgentWorker> logger
) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly IParseSession _session = session;
    private readonly IHostApplicationLifetime _lifetime = lifetime;
    private readonly WorkerOptions _options = options.Value;
    private readonly LmStudioOptions _lmStudioOptions = lmStudioOptions.Value;
    private readonly ILogger<AgentWorker> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            var pollingMinutes = Math.Max(1, _session.Snapshot().PollingIntervalMinutes);
            WorkerLog.Started(_logger, pollingMinutes);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var applyInterval = false;

            try
            {
                await _session.WaitUntilRunningAsync(stoppingToken);
                using var runCts = CancellationTokenSource.CreateLinkedTokenSource(
                    stoppingToken,
                    _session.RunningToken
                );
                var runToken = runCts.Token;

                await WaitOutOfSleepAsync(runToken);
                await RunCycleAsync(runToken);
                applyInterval = true;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                // The current Telegram-controlled run was stopped; wait for the next Start command.
                applyInterval = false;
            }
            catch (LmStudioUnavailableException ex)
            {
                WorkerLog.LmStudioUnavailable(_logger, ex.Message);
                applyInterval = _session.IsRunning;
            }
            catch (TelegramUnavailableException ex)
            {
                WorkerLog.TelegramUnavailable(_logger, ex.Message);
                applyInterval = _session.IsRunning;
            }
            catch (Exception ex)
            {
                if (BrowserErrorText.IsBrowserClosed(ex) || stoppingToken.IsCancellationRequested)
                {
                    _lifetime.StopApplication();
                    break;
                }

                WorkerLog.CycleFailed(_logger, BrowserErrorText.Describe(ex));
                WorkerLog.CycleFailedDetails(_logger, ex);
                applyInterval = _session.IsRunning;
            }

            if (!applyInterval || stoppingToken.IsCancellationRequested || !_session.IsRunning)
            {
                continue;
            }

            try
            {
                using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(
                    stoppingToken,
                    _session.RunningToken
                );
                await WaitPollingIntervalAsync(delayCts.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                // Stop during pause — wait for the next Start.
            }
        }
    }

    private async Task WaitPollingIntervalAsync(CancellationToken cancellationToken)
    {
        var minutes = Math.Max(1, _session.Snapshot().PollingIntervalMinutes);
        WorkerLog.WaitingNextCycle(_logger, minutes);
        await Task.Delay(TimeSpan.FromMinutes(minutes), cancellationToken);
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        var criteria = BuildSearchCriteria();

        if (criteria.Keywords.Count == 0)
        {
            WorkerLog.NoKeywords(_logger);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var marketplace = scope.ServiceProvider.GetRequiredService<IMarketplace>();
        var repository = scope.ServiceProvider.GetRequiredService<IListingRepository>();
        var analyzer = scope.ServiceProvider.GetRequiredService<IProductAnalyzer>();
        var notifier = scope.ServiceProvider.GetRequiredService<INotificationService>();

        if (_lmStudioOptions.Enabled)
        {
            await analyzer.EnsureAvailableAsync(cancellationToken);
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            var keywords = string.Join(", ", criteria.Keywords);
            WorkerLog.CycleStarted(_logger, keywords);
        }

        var analyzed = 0;
        var skipped = 0;
        var processedIds = new HashSet<string>(StringComparer.Ordinal);
        var totalFound = 0;
        var lmDisabledLogged = false;

        async Task HandleListingAsync(Listing listing, CancellationToken ct)
        {
            if (!processedIds.Add(listing.Id))
            {
                return;
            }

            // Без LM Studio анализ не нужен: шлём в Telegram, если ещё не отправляли.
            if (!_lmStudioOptions.Enabled)
            {
                if (!lmDisabledLogged)
                {
                    WorkerLog.LmStudioDisabled(_logger);
                    lmDisabledLogged = true;
                }

                if (await repository.HasTelegramNotificationAsync(listing.Id, ct))
                {
                    WorkerLog.ListingAlreadySentToTelegram(_logger, listing.Id);
                    skipped++;
                    return;
                }

                await repository.SaveAsync(listing, ct);
                if (listing.ImageUrls.Count > 0)
                {
                    await repository.SaveImagesAsync(listing.Id, listing.ImageUrls, ct);
                }

                try
                {
                    WorkerLog.NotifyingWithoutAnalysis(_logger, listing.Id);
                    await NotifyOnceAsync(listing, null, ct);
                }
                catch (TelegramUnavailableException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    WorkerLog.AnalysisFailed(_logger, listing.Id, BrowserErrorText.Describe(ex));
                }

                return;
            }

            if (await repository.HasAnalysisAsync(listing.Id, ct))
            {
                WorkerLog.ListingAlreadyAnalyzed(_logger, listing.Id);
                skipped++;
                return;
            }

            await repository.SaveAsync(listing, ct);

            if (listing.ImageUrls.Count > 0)
            {
                await repository.SaveImagesAsync(listing.Id, listing.ImageUrls, ct);
            }

            try
            {
                WorkerLog.AnalysisStarted(_logger, listing.Id);
                var analysis = await analyzer.AnalyzeAsync(listing, criteria, ct);
                await repository.SaveAnalysisAsync(listing.Id, analysis, ct);
                analyzed++;

                WorkerLog.AnalysisResult(
                    _logger,
                    listing.Id,
                    analysis.IsRelevant,
                    analysis.IsAuthentic,
                    analysis.Score,
                    analysis.Reason
                );

                if (!analysis.IsRelevant)
                {
                    WorkerLog.AnalysisSkipped(
                        _logger,
                        listing.Id,
                        "LM Studio: не соответствует строке поиска"
                    );
                    return;
                }

                if (analysis.Score < _options.MinAuthenticityScore)
                {
                    WorkerLog.AnalysisSkipped(
                        _logger,
                        listing.Id,
                        $"соответствие {analysis.Score} < {_options.MinAuthenticityScore}"
                    );
                    return;
                }

                await NotifyOnceAsync(listing, null, ct);
            }
            catch (LmStudioUnavailableException)
            {
                throw;
            }
            catch (TelegramUnavailableException)
            {
                throw;
            }
            catch (Exception ex)
            {
                WorkerLog.AnalysisFailed(_logger, listing.Id, BrowserErrorText.Describe(ex));
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(ex, "Стек AI-анализа для {ListingId}", listing.Id);
                }
            }
        }

        async Task NotifyOnceAsync(
            Listing listing,
            ProductAnalysis? analysis,
            CancellationToken ct
        )
        {
            if (await repository.HasTelegramNotificationAsync(listing.Id, ct))
            {
                WorkerLog.ListingAlreadySentToTelegram(_logger, listing.Id);
                return;
            }

            if (await notifier.NotifyListingFoundAsync(listing, analysis, ct))
            {
                await repository.SaveTelegramNotificationAsync(listing, ct);
                WorkerLog.Notified(_logger, listing.Id);
            }
        }

        var locations = criteria.LocationSlugs
            .Where(static slug => !string.IsNullOrWhiteSpace(slug))
            .Select(static slug => slug.Trim().Trim('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (locations.Count == 0)
        {
            locations.Add("rossiya");
        }

        foreach (var keyword in criteria.Keywords)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(keyword))
            {
                continue;
            }

            WorkerLog.KeywordStarted(_logger, keyword);

            foreach (var location in locations)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var passCriteria = criteria with
                {
                    Keywords = [keyword],
                    LocationSlugs = [location],
                };

                var listings = await marketplace.SearchAsync(
                    passCriteria,
                    onListingEnriched: HandleListingAsync,
                    cancellationToken
                );
                totalFound += listings.Count;

                // На случай превью-карточек без детального открытия — догоняем анализ.
                foreach (var listing in listings)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await HandleListingAsync(listing, cancellationToken);
                }
            }
        }

        if (totalFound <= 0)
        {
            var keywords = string.Join(
                ", ",
                criteria.Keywords.Where(static k => !string.IsNullOrWhiteSpace(k)).Select(static k => k.Trim())
            );
            WorkerLog.NothingFound(
                _logger,
                string.IsNullOrWhiteSpace(keywords) ? "без ключевых слов" : keywords
            );
        }
        else
        {
            WorkerLog.ListingsFound(_logger, totalFound);
        }
        WorkerLog.CycleStats(_logger, analyzed, skipped);
        WorkerLog.CycleCompleted(_logger);
    }

    private SearchCriteria BuildSearchCriteria()
    {
        var settings = _session.Snapshot();
        return new()
        {
            Keywords = settings.Keywords,
            ExcludedKeywords = settings.ExcludedKeywords,
            MinPrice = settings.MinPrice > 0 ? settings.MinPrice : null,
            MaxPrice = settings.MaxPrice > 0 ? settings.MaxPrice : null,
            LocationSlugs = settings.LocationSlugs,
            Sort = settings.Sort,
            DeliveryOnly = settings.DeliveryOnly,
            Condition = settings.Condition,
            SellerType = settings.SellerType,
            MaxResults = settings.MaxResults,
            PublishedAfterUtc = settings.FromCurrentDateTime ? _session.PublishedAfterUtc : null,
        };
    }

    private async Task WaitOutOfSleepAsync(CancellationToken cancellationToken)
    {
        var settings = _session.Snapshot();
        if (
            !SleepSchedule.TryGetActiveUntil(
                settings.SleepFromHour,
                settings.SleepToHour,
                ResolveTimeZone(_options.TimezoneId),
                DateTime.UtcNow,
                out var untilLocal
            )
        )
        {
            return;
        }

        WorkerLog.Sleeping(_logger, untilLocal, _options.TimezoneId);
        var delay =
            untilLocal
            - TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.UtcNow,
                ResolveTimeZone(_options.TimezoneId)
            );
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}

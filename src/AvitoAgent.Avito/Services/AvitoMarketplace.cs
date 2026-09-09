using AvitoAgent.Avito.Interfaces;
using AvitoAgent.Avito.Logging;
using AvitoAgent.Avito.Parsing;
using AvitoAgent.Core.Interfaces;
using AvitoAgent.Core.Models;
using AvitoAgent.Playwright.Extensions;
using AvitoAgent.Playwright.Interfaces;
using AvitoAgent.Shared;
using AvitoAgent.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace AvitoAgent.Avito.Services;

public sealed class AvitoMarketplace(
    IBrowserManager browserManager,
    IAvitoAuthService authService,
    IListingRepository listingRepository,
    IListingPhotoArchiver photoArchiver,
    BrowserWorkingPageHolder pageHolder,
    INotificationService notifications,
    IOptions<AvitoOptions> options,
    IOptions<LmStudioOptions> lmStudio,
    IOptions<TelegramOptions> telegram,
    ApplicationPaths paths,
    ILogger<AvitoMarketplace> logger
) : IMarketplace
{
    private readonly IBrowserManager _browserManager = browserManager;
    private readonly IAvitoAuthService _authService = authService;
    private readonly IListingRepository _listingRepository = listingRepository;
    private readonly IListingPhotoArchiver _photoArchiver = photoArchiver;
    private readonly BrowserWorkingPageHolder _pageHolder = pageHolder;
    private readonly INotificationService _notifications = notifications;
    private readonly AvitoOptions _options = options.Value;
    private readonly LmStudioOptions _lmStudio = lmStudio.Value;
    private readonly TelegramOptions _telegram = telegram.Value;
    private readonly ApplicationPaths _paths = paths;
    private readonly ILogger<AvitoMarketplace> _logger = logger;

    public async Task<IReadOnlyList<Listing>> SearchAsync(
        SearchCriteria criteria,
        Func<Listing, CancellationToken, Task>? onListingEnriched = null,
        CancellationToken cancellationToken = default)
    {
        if (criteria.Keywords.Count == 0)
        {
            AvitoLog.NoKeywords(_logger);
            return [];
        }

        var sessionRequest = new BrowserSessionRequest
        {
            StorageStatePath = _authService.StorageStatePath,
        };

        var session = await _browserManager.GetPersistentSessionAsync(sessionRequest, cancellationToken);

        if (_options.Auth.Enabled)
        {
            var authenticated = await _authService.EnsureAuthenticatedAsync(
                session,
                cancellationToken
            );
            if (!authenticated)
            {
                AvitoLog.SearchSkippedNotAuthenticated(_logger);
                return [];
            }
        }

        var page = _pageHolder.Page ?? await session.Pages.CreatePageAsync(cancellationToken);

        await AvitoNavigationDelays.SearchWarmupAsync(_options, cancellationToken);

        var results = new List<Listing>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var detailsOpened = 0;
        var stopped = false;

        var searchUrl = AvitoUrlBuilder.BuildSearchUrl(_options, criteria, 1);
        var searchQuery =
            criteria.Keywords.FirstOrDefault(static k => !string.IsNullOrWhiteSpace(k))?.Trim()
            ?? string.Empty;
        AvitoLog.SearchStarted(_logger, string.IsNullOrEmpty(searchQuery) ? searchUrl : $"{searchQuery} → {searchUrl}");
        var sortLabel = FormatSortLabel(criteria.Sort);
        if (_logger.IsEnabled(LogLevel.Information))
        {
            AvitoLog.SortSelected(_logger, sortLabel);
        }

        try
        {
            await AvitoHumanNavigator.NavigateToSearchAsync(
                page,
                _options,
                criteria,
                pageIndex: 1,
                cancellationToken);
        }
        catch (TimeoutException)
        {
            AvitoLog.SearchFallbackToUrl(_logger);
            await page.GotoAsync(
                searchUrl,
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = _options.NavigationTimeoutMs,
                });
            await AvitoHumanNavigator.NavigateToSearchAsync(
                page,
                _options,
                criteria,
                pageIndex: 1,
                cancellationToken);
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
        {
            AvitoLog.SearchFallbackToUrl(_logger);
            await page.GotoAsync(
                searchUrl,
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = _options.NavigationTimeoutMs,
                });
            await AvitoHumanNavigator.NavigateToSearchAsync(
                page,
                _options,
                criteria,
                pageIndex: 1,
                cancellationToken);
        }

        if (!await TryResolveBlockAsync(page, session, cancellationToken))
        {
            LogSearchFinished(criteria, results.Count);
            return results;
        }

        AvitoLog.WaitingForSearchResults(_logger);
        var resultsLoaded = await AvitoPageDiagnostics.WaitForSearchResultsAsync(
            page,
            _options,
            _logger,
            cancellationToken
        );

        if (
            !resultsLoaded
            && (
                string.IsNullOrWhiteSpace(searchQuery)
                || !AvitoUrlBuilder.UrlMatchesSearchQuery(page.Url, searchQuery)
            )
        )
        {
            AvitoLog.SearchFallbackToUrl(_logger);
            await page.GotoAsync(
                searchUrl,
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = _options.NavigationTimeoutMs,
                });
            await AvitoHumanNavigator.NavigateToSearchAsync(
                page,
                _options,
                criteria,
                pageIndex: 1,
                cancellationToken);

            if (!await TryResolveBlockAsync(page, session, cancellationToken))
            {
                LogSearchFinished(criteria, results.Count);
                return results;
            }

            resultsLoaded = await AvitoPageDiagnostics.WaitForSearchResultsAsync(
                page,
                _options,
                _logger,
                cancellationToken
            );
        }

        // Чаще всего выдача просто не догрузилась. Тревожим пользователя только если
        // и после перезагрузки страницы карточек нет.
        if (!resultsLoaded && await TryReloadSearchPageAsync(page, cancellationToken))
        {
            if (!await TryResolveBlockAsync(page, session, cancellationToken))
            {
                LogSearchFinished(criteria, results.Count);
                return results;
            }

            resultsLoaded = await AvitoPageDiagnostics.WaitForSearchResultsAsync(
                page,
                _options,
                _logger,
                cancellationToken
            );

            if (resultsLoaded)
            {
                AvitoLog.SearchResultsLoadedAfterReload(_logger);
            }
        }

        if (!resultsLoaded)
        {
            var screenshotPath = await AvitoPageDiagnostics.SaveScreenshotAsync(
                page,
                _paths,
                "search-timeout",
                _logger,
                cancellationToken
            );
            if (!string.IsNullOrEmpty(screenshotPath))
            {
                try
                {
                    await _notifications.NotifySearchTimeoutAsync(
                        screenshotPath,
                        page.Url,
                        cancellationToken
                    );
                }
                catch (Exception ex)
                {
                    AvitoLog.ActionFailed(
                        _logger,
                        "Не удалось отправить скриншот таймаута поиска в Telegram",
                        ex
                    );
                }
            }

            LogSearchFinished(criteria, results.Count);
            return results;
        }

        if (!await TryResolveBlockAsync(page, session, cancellationToken))
        {
            LogSearchFinished(criteria, results.Count);
            return results;
        }

        if (await AvitoHumanNavigator.ApplyCriteriaFiltersAsync(
                page,
                criteria,
                _options,
                cancellationToken))
        {
            AvitoLog.CriteriaFiltersApplied(_logger);
        }
        else
        {
            AvitoLog.CriteriaFiltersFailed(_logger);
        }

        await AvitoHumanNavigator.ApplyListingFiltersViaUiAsync(
            page,
            criteria,
            _options,
            cancellationToken);

        // Клики по меню (сортировка) часто сбрасывают condition= из URL - восстанавливаем.
        if (!AvitoUrlBuilder.HasCriteriaFilters(page.Url, criteria))
        {
            if (await AvitoHumanNavigator.ApplyCriteriaFiltersAsync(
                    page,
                    criteria,
                    _options,
                    cancellationToken))
            {
                AvitoLog.CriteriaFiltersApplied(_logger);
            }
            else
            {
                AvitoLog.CriteriaFiltersFailed(_logger);
            }
        }

        await LogListingFiltersStatus(page, criteria, cancellationToken);

        var sortMode = AvitoSort.TryResolve(criteria.Sort, out _, out var parsedSort)
            ? parsedSort
            : AvitoSortMode.Date;
        if (await AvitoHumanNavigator.IsSortAppliedAsync(page, sortMode, cancellationToken))
        {
            AvitoLog.SortUiApplied(_logger, sortLabel);
        }
        else
        {
            AvitoLog.SortUiFailed(_logger, sortLabel);
        }

        if (!await TryResolveBlockAsync(page, session, cancellationToken))
        {
            LogSearchFinished(criteria, results.Count);
            return results;
        }

        // От новых к старым: страница 1 сверху вниз, затем следующие страницы.
        var pagesProcessed = 0;

        while (!stopped && pagesProcessed < _options.MaxPages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (pagesProcessed > 0)
            {
                await AvitoNavigationDelays.DelayBetweenSearchPagesAsync(_options, cancellationToken);
                AvitoLog.GoingToNextSearchPage(_logger);

                var movedNext = await AvitoHumanNavigator.GoToNextSearchPageAsync(
                    page,
                    _options,
                    cancellationToken);

                if (!movedNext)
                {
                    break;
                }

                if (!await TryResolveBlockAsync(page, session, cancellationToken))
                {
                    break;
                }
            }

            await LoadSearchCardsAsync(page, cancellationToken);

            var cards = await AvitoSearchParser.ParseSearchResultsAsync(page, cancellationToken);
            AvitoLog.SearchCardsParsed(_logger, cards.Count);

            var otherCitiesBanner = await AvitoSearchParser.HasOtherCitiesBannerAsync(page);
            if (otherCitiesBanner)
            {
                AvitoLog.SearchCardsAboveOtherCitiesBanner(_logger, cards.Count);
            }

            if (cards.Count == 0)
            {
                break;
            }

            // Сверху вниз: сначала самые новые на странице.
            AvitoLog.ProcessingPageTopDown(_logger, cards.Count);

            var previewModeLogged = false;

            foreach (var rawCard in cards)
            {
                if (stopped)
                {
                    break;
                }

                if (results.Count >= criteria.MaxResults)
                {
                    stopped = true;
                    break;
                }

                var listingId = AvitoHumanNavigator.NormalizeListingId(rawCard.Id);

                if (!MatchesCriteria(rawCard, criteria))
                {
                    AvitoLog.ListingExcludedByFilters(
                        _logger,
                        string.IsNullOrWhiteSpace(listingId) ? "(без id)" : listingId,
                        string.IsNullOrWhiteSpace(rawCard.Title) ? "(без названия)" : rawCard.Title
                    );
                    continue;
                }

                if (IsOlderThanPublishedCutoff(rawCard, criteria.PublishedAfterUtc))
                {
                    AvitoLog.ListingExcludedByDate(
                        _logger,
                        string.IsNullOrWhiteSpace(listingId) ? rawCard.Title : listingId
                    );
                    continue;
                }

                if (IsOutsideSearchLocation(rawCard, criteria, out var cardWrongLocation))
                {
                    AvitoLog.ListingExcludedWrongLocation(
                        _logger,
                        string.IsNullOrWhiteSpace(listingId) ? rawCard.Title : listingId,
                        cardWrongLocation
                    );
                    continue;
                }

                if (string.IsNullOrWhiteSpace(listingId) || !seenIds.Add(listingId))
                {
                    continue;
                }

                var card = CloneWithId(rawCard, listingId);

                // Уже уведомили - детали не открываем.
                // При включённом LM также пропускаем уже проанализированные.
                // Без LM старый анализ не блокирует: нужны фото и отправка в Telegram.
                var alreadySent = await _listingRepository.HasTelegramNotificationAsync(
                    listingId,
                    cancellationToken
                );
                var alreadyAnalyzed =
                    _lmStudio.Enabled
                    && await _listingRepository.HasAnalysisAsync(listingId, cancellationToken);

                if (alreadySent || alreadyAnalyzed)
                {
                    AvitoLog.ListingAlreadyKnown(_logger, listingId);
                    results.Add(card);
                    continue;
                }

                var maxDetails = _options.MaxDetailsPerSearch;
                if (maxDetails > 0 && detailsOpened >= maxDetails)
                {
                    if (!previewModeLogged)
                    {
                        AvitoLog.SearchCardsUsingPreview(_logger, maxDetails);
                        previewModeLogged = true;
                    }

                    results.Add(card);
                    continue;
                }

                // Перед открытием - короткая пауза (бюджет времени - на самой странице объявления).
                var delayMs = CalculateDetailDelayMs();
                AvitoLog.DetailNavigationDelay(_logger, delayMs, listingId);
                await Task.Delay(delayMs, cancellationToken);

                var (detailed, canContinue, navigatedToDetail) = await EnrichListingAsync(
                    page,
                    session,
                    card,
                    criteria,
                    cancellationToken);

                // Рабочая вкладка могла смениться (объявление в новой вкладке).
                page = _pageHolder.Page ?? page;

                if (!navigatedToDetail)
                {
                    if (!canContinue)
                    {
                        stopped = true;
                        break;
                    }

                    await AvitoNavigationDelays.DelayAfterDetailAsync(_options, cancellationToken);
                    continue;
                }

                if (IsOutsideSearchLocation(detailed, criteria, out var detailWrongLocation))
                {
                    AvitoLog.ListingExcludedWrongLocation(_logger, listingId, detailWrongLocation);
                    await AvitoNavigationDelays.DelayOnListingBeforeReturnAsync(cancellationToken);
                    var searchAfterSkip = await AvitoHumanNavigator.EnsureBackOnSearchAsync(
                        page,
                        _options,
                        criteria,
                        Math.Min(_options.NavigationTimeoutMs, 20_000),
                        cancellationToken);
                    _pageHolder.SetPage(searchAfterSkip);
                    page = searchAfterSkip;
                    await AvitoNavigationDelays.DelayAfterDetailAsync(_options, cancellationToken);
                    continue;
                }

                if (IsOlderThanPublishedCutoff(detailed, criteria.PublishedAfterUtc))
                {
                    AvitoLog.ListingExcludedByDate(_logger, listingId);
                    await AvitoNavigationDelays.DelayOnListingBeforeReturnAsync(cancellationToken);
                    var searchAfterDate = await AvitoHumanNavigator.EnsureBackOnSearchAsync(
                        page,
                        _options,
                        criteria,
                        Math.Min(_options.NavigationTimeoutMs, 20_000),
                        cancellationToken);
                    _pageHolder.SetPage(searchAfterDate);
                    page = searchAfterDate;
                    await AvitoNavigationDelays.DelayAfterDetailAsync(_options, cancellationToken);
                    continue;
                }

                detailsOpened++;
                await _listingRepository.SaveAsync(detailed, cancellationToken);
                results.Add(detailed);

                // Старт отправки в LM сразу, но возврат к поиску не ждёт конца анализа (минуты).
                Task? analysisTask = null;
                if (onListingEnriched is not null)
                {
                    analysisTask = onListingEnriched(detailed, cancellationToken);
                }

                await AvitoNavigationDelays.DelayOnListingBeforeReturnAsync(cancellationToken);

                IPage searchPage;
                try
                {
                    searchPage = await AvitoHumanNavigator.EnsureBackOnSearchAsync(
                        page,
                        _options,
                        criteria,
                        Math.Min(_options.NavigationTimeoutMs, 20_000),
                        cancellationToken);
                }
                finally
                {
                    if (analysisTask is not null)
                    {
                        try
                        {
                            await analysisTask;
                        }
                        catch (Exception ex)
                        {
                            AvitoLog.ActionFailed(
                                _logger,
                                $"Не удалось обработать объявление {listingId} после открытия",
                                ex);
                        }
                    }
                }

                _pageHolder.SetPage(searchPage);
                page = searchPage;
                AvitoLog.ReturnedToSearch(_logger, listingId);

                if (!canContinue)
                {
                    stopped = true;
                    break;
                }

                await AvitoNavigationDelays.DelayAfterDetailAsync(_options, cancellationToken);
            }

            pagesProcessed++;

            if (otherCitiesBanner)
            {
                AvitoLog.SkipNextSearchPageDueToOtherCitiesBanner(_logger);
                break;
            }
        }

        LogSearchFinished(criteria, results.Count);
        return results;
    }

    /// <summary>
    /// Подгружает карточки прокруткой до лимита или до надписи «в других городах».
    /// </summary>
    private async Task LoadSearchCardsAsync(IPage page, CancellationToken cancellationToken)
    {
        var cap = Math.Max(1, _options.MaxCardsPerSearchPage);
        var lastCount = 0;
        var stagnant = 0;
        var scrolled = false;

        for (var round = 0; round < 4; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await AvitoSearchParser.HasOtherCitiesBannerAsync(page))
            {
                break;
            }

            var ids = await page.EvaluateAsync<string[]>(
                """
                () => [...document.querySelectorAll('[data-item-id]')]
                  .map(el => el.getAttribute('data-item-id'))
                  .filter(Boolean)
                """
            );
            var count = ids?.Length ?? 0;
            if (count >= cap || (round == 0 && count > 0 && count >= Math.Min(cap, 12)))
            {
                break;
            }

            if (count <= lastCount)
            {
                stagnant++;
                if (stagnant >= 2)
                {
                    break;
                }
            }
            else
            {
                stagnant = 0;
                lastCount = count;
            }

            try
            {
                await page.HumanScrollAsync(480, cancellationToken);
            }
            catch (PlaywrightException)
            {
                break;
            }

            scrolled = true;
            await page.HumanPauseRangeAsync(180, 320, cancellationToken);
        }

        if (scrolled)
        {
            try
            {
                await page.EvaluateAsync("() => window.scrollTo(0, 0)");
            }
            catch (PlaywrightException) { }
        }
    }

    /// <summary>
    /// Перезагружает страницу выдачи перед тем, как считать таймаут настоящим.
    /// </summary>
    private async Task<bool> TryReloadSearchPageAsync(IPage page, CancellationToken cancellationToken)
    {
        AvitoLog.SearchResultsReloading(_logger);

        try
        {
            await page.ReloadAsync(
                new PageReloadOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = _options.NavigationTimeoutMs,
                }
            );
        }
        catch (PlaywrightException ex)
        {
            AvitoLog.ActionFailed(_logger, "Не удалось перезагрузить страницу выдачи", ex);
            return false;
        }

        await page.HumanPauseRangeAsync(600, 1_200, cancellationToken);
        return true;
    }

    private async Task<bool> TryResolveBlockAsync(
        IPage page,
        IBrowserSession session,
        CancellationToken cancellationToken)
    {
        if (!await AvitoPageDiagnostics.IsAccessRestrictedAsync(page))
        {
            return true;
        }

        await AvitoPageDiagnostics.LogPageStateAsync(page, _logger);

        if (!_options.StopOnBlock)
        {
            await AvitoPageDiagnostics.SaveScreenshotAsync(
                page, _paths, "avito-blocked", _logger, cancellationToken);
            return false;
        }

        return await AvitoCaptchaWaiter.WaitUntilUnblockedAsync(
            page,
            session,
            _options,
            _authService,
            _notifications,
            _paths,
            _logger,
            cancellationToken);
    }

    private int CalculateDetailDelayMs() =>
        AvitoNavigationDelays.WithJitter(_options.DetailDelayMs, _options.DetailDelayJitterMs);

    private async Task<(Listing Listing, bool CanContinue, bool NavigatedToDetail)> EnrichListingAsync(
        IPage page,
        IBrowserSession session,
        Listing listing,
        SearchCriteria criteria,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(listing.Url))
        {
            return (listing, true, false);
        }

        if (page.IsClosed)
        {
            AvitoLog.DetailParseFailed(_logger, listing.Id, "окно браузера закрыто");
            return (listing, false, false);
        }

        const int maxAttempts = 2;
        Exception? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            page = _pageHolder.Page ?? page;

            if (page.IsClosed)
            {
                AvitoLog.DetailParseFailed(_logger, listing.Id, "окно браузера закрыто");
                return (listing, false, false);
            }

            try
            {
                var result = await EnrichListingOnceAsync(
                    page,
                    session,
                    listing,
                    cancellationToken
                );

                if (result.NavigatedToDetail)
                {
                    return result;
                }

                // Не открылось - один повтор, затем пропуск.
                if (attempt < maxAttempts)
                {
                    AvitoLog.ListingDetailRetry(
                        _logger,
                        listing.Id,
                        "не удалось открыть страницу"
                    );
                    page = await RecoverToSearchAfterDetailFailureAsync(
                        page,
                        criteria,
                        cancellationToken
                    );
                    await AvitoNavigationDelays.DelayAsync(800, 700, cancellationToken);
                    continue;
                }

                AvitoLog.ListingDetailSkippedAfterRetry(_logger, listing.Id);
                _ = await RecoverToSearchAfterDetailFailureAsync(page, criteria, cancellationToken);
                return (listing, true, false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (IsBrowserClosedError(ex))
            {
                AvitoLog.DetailParseFailed(_logger, listing.Id, ex);
                return (listing, false, false);
            }
            catch (Exception ex) when (BrowserErrorText.IsTimeout(ex) || IsRetryableDetailError(ex))
            {
                lastError = ex;
                AvitoLog.DetailParseFailed(_logger, listing.Id, ex);

                if (attempt < maxAttempts)
                {
                    AvitoLog.ListingDetailRetry(
                        _logger,
                        listing.Id,
                        BrowserErrorText.Describe(ex)
                    );
                    page = await RecoverToSearchAfterDetailFailureAsync(
                        page,
                        criteria,
                        cancellationToken
                    );
                    await AvitoNavigationDelays.DelayAsync(1_000, 800, cancellationToken);
                    continue;
                }

                AvitoLog.ListingDetailSkippedAfterRetry(_logger, listing.Id);
                _ = await RecoverToSearchAfterDetailFailureAsync(page, criteria, cancellationToken);
                return (listing, true, false);
            }
            catch (Exception ex)
            {
                AvitoLog.DetailParseFailed(_logger, listing.Id, ex);
                _ = await RecoverToSearchAfterDetailFailureAsync(page, criteria, cancellationToken);
                return (listing, true, false);
            }
        }

        if (lastError is not null)
        {
            AvitoLog.ListingDetailSkippedAfterRetry(_logger, listing.Id);
        }

        return (listing, true, false);
    }

    private async Task<(Listing Listing, bool CanContinue, bool NavigatedToDetail)> EnrichListingOnceAsync(
        IPage page,
        IBrowserSession session,
        Listing listing,
        CancellationToken cancellationToken
    )
    {
        var navigatedToDetail = false;

        if (_options.ClickListingLinks)
        {
            var detailPage = await AvitoHumanNavigator.OpenListingByClickAsync(
                page,
                listing.Id,
                _options.NavigationTimeoutMs,
                cancellationToken
            );

            if (detailPage is not null)
            {
                navigatedToDetail = true;
                _pageHolder.SetPage(detailPage);
                page = detailPage;
            }
        }

        if (!navigatedToDetail)
        {
            AvitoLog.ListingOpenFailed(_logger, listing.Id);
            return (listing, true, false);
        }

        AvitoLog.ListingOpened(_logger, listing.Id);

        if (!await TryResolveBlockAsync(page, session, cancellationToken))
        {
            return (listing, false, navigatedToDetail);
        }

        await AvitoNavigationDelays.DelayOnPageSettleAsync(_options, cancellationToken);
        await page.HumanScrollToBottomAsync(cancellationToken);
        await page.HumanPauseRangeAsync(250, 550, cancellationToken);

        var details = await AvitoDetailParser.ParseAsync(page, _options, cancellationToken);
        var galleryCount = details.ImageUrls.Count;
        var galleryTotal = Math.Max(details.GalleryTotalCount, galleryCount);
        AvitoLog.GalleryPhotosCollected(_logger, galleryCount, galleryTotal);
        var detailUrl = NormalizeListingUrl(page.Url);
        var imageUrls = details.ImageUrls.OrderByDescending(AvitoImageUrl.Quality).ToList();
        var downloadLimit = Math.Max(
            Math.Clamp(_telegram.MaxPhotos, 1, 10),
            _lmStudio.MaxImages > 0 ? _lmStudio.MaxImages : 1
        );
        var photos = await AvitoPageImageDownloader.DownloadAsync(
            page,
            imageUrls,
            _logger,
            cancellationToken,
            maxPhotos: Math.Min(imageUrls.Count, downloadLimit)
        );
        if (photos.Count > 0)
        {
            AvitoLog.ImagesDownloadedViaBrowser(_logger, photos.Count);
        }

        await _photoArchiver.ArchiveAsync(listing.Id, photos, cancellationToken);

        var rawLocation = string.IsNullOrWhiteSpace(details.Location)
            ? listing.Location
            : details.Location;

        return (
            new Listing
            {
                Id = listing.Id,
                Title = listing.Title,
                Description = string.IsNullOrWhiteSpace(details.Description)
                    ? listing.Description
                    : details.Description,
                Price = listing.Price,
                Currency = listing.Currency,
                Url = detailUrl,
                Location = AvitoGeo.FormatListingAddress(rawLocation, detailUrl),
                SellerName = string.IsNullOrWhiteSpace(details.SellerName)
                    ? listing.SellerName
                    : details.SellerName,
                PublishedAt = listing.PublishedAt,
                FirstSeenAt = listing.FirstSeenAt,
                LastSeenAt = listing.LastSeenAt,
                ImageUrls = imageUrls,
                Images = photos,
                Source = listing.Source,
            },
            true,
            navigatedToDetail
        );
    }

    private async Task<IPage> RecoverToSearchAfterDetailFailureAsync(
        IPage page,
        SearchCriteria criteria,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var searchPage = await AvitoHumanNavigator.EnsureBackOnSearchAsync(
                page,
                _options,
                criteria,
                Math.Min(_options.NavigationTimeoutMs, 20_000),
                cancellationToken
            );
            _pageHolder.SetPage(searchPage);
            return searchPage;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (!IsBrowserClosedError(ex))
        {
            AvitoLog.ActionFailed(_logger, "Не удалось вернуться к выдаче", ex);
            return _pageHolder.Page ?? page;
        }
    }

    private static bool IsRetryableDetailError(Exception exception)
    {
        var reason = BrowserErrorText.Describe(exception);
        return reason.Contains("не ответила вовремя", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("обновилась", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("сеть", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeListingUrl(string url)
    {
        var queryIndex = url.IndexOf('?', StringComparison.Ordinal);
        return queryIndex >= 0 ? url[..queryIndex] : url;
    }

    private static bool IsBrowserClosedError(Exception exception) =>
        BrowserErrorText.IsBrowserClosed(exception);

    private static bool MatchesCriteria(Listing listing, SearchCriteria criteria)
    {
        // Соответствие строке поиска (тип/бренд/модель) - задача LM Studio, здесь не фильтруем.
        var title = listing.Title ?? string.Empty;
        var text = $"{title} {listing.Description}";

        if (criteria.MinPrice.HasValue &&
            listing.Price.HasValue &&
            listing.Price.Value < criteria.MinPrice.Value)
        {
            return false;
        }

        if (criteria.MaxPrice.HasValue &&
            listing.Price.HasValue &&
            listing.Price.Value > criteria.MaxPrice.Value)
        {
            return false;
        }

        foreach (var excluded in criteria.ExcludedKeywords)
        {
            if (!string.IsNullOrWhiteSpace(excluded) &&
                text.Contains(excluded, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsOlderThanPublishedCutoff(Listing listing, DateTime? publishedAfterUtc)
    {
        if (publishedAfterUtc is null || listing.PublishedAt is null)
        {
            return false;
        }

        var published = listing.PublishedAt.Value;
        if (published.Kind == DateTimeKind.Unspecified)
        {
            published = DateTime.SpecifyKind(published, DateTimeKind.Utc);
        }
        else if (published.Kind == DateTimeKind.Local)
        {
            published = published.ToUniversalTime();
        }

        var cutoff = publishedAfterUtc.Value;
        if (cutoff.Kind == DateTimeKind.Unspecified)
        {
            cutoff = DateTime.SpecifyKind(cutoff, DateTimeKind.Utc);
        }
        else if (cutoff.Kind == DateTimeKind.Local)
        {
            cutoff = cutoff.ToUniversalTime();
        }

        return published < cutoff;
    }

    private static bool IsOutsideSearchLocation(
        Listing listing,
        SearchCriteria criteria,
        out string location
    )
    {
        location = listing.Location;
        var slugs = criteria
            .LocationSlugs.Where(slug => !string.IsNullOrWhiteSpace(slug))
            .Select(slug => slug.Trim().Trim('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (slugs.Length == 0)
        {
            return false;
        }

        var matches = slugs
            .Select(slug =>
                AvitoGeo.TryBelongsToSearchLocation(listing.Url, listing.Location, slug)
            )
            .ToArray();

        // Не исключаем объявления с нераспознанным адресом; достаточно совпадения с любым активным регионом.
        return matches.Any(match => match == false) && !matches.Any(match => match == true);
    }

    private async Task LogListingFiltersStatus(
        IPage page,
        SearchCriteria criteria,
        CancellationToken cancellationToken
    )
    {
        if (criteria.DeliveryOnly)
        {
            if (await AvitoHumanNavigator.IsDeliveryAppliedAsync(page, cancellationToken))
            {
                AvitoLog.DeliveryFilterApplied(_logger);
            }
            else
            {
                AvitoLog.DeliveryFilterFailed(_logger);
            }
        }

        var sellerMode = AvitoSellerType.TryResolve(criteria.SellerType, out _, out var parsedSeller)
            ? parsedSeller
            : AvitoSellerTypeMode.All;
        if (sellerMode is not AvitoSellerTypeMode.All)
        {
            var sellerLabel = AvitoSellerType.DisplayName(sellerMode);
            if (await AvitoHumanNavigator.IsSellerAppliedAsync(page, sellerMode, cancellationToken))
            {
                AvitoLog.SellerFilterApplied(_logger, sellerLabel);
            }
            else
            {
                AvitoLog.SellerFilterFailed(_logger, sellerLabel);
            }
        }

        var conditionMode = AvitoCondition.TryResolve(criteria.Condition, out _, out var parsedCondition)
            ? parsedCondition
            : AvitoConditionMode.All;
        if (conditionMode is not AvitoConditionMode.All)
        {
            var conditionLabel = AvitoCondition.DisplayName(conditionMode);
            if (AvitoUrlBuilder.IsConditionApplied(page.Url, conditionMode))
            {
                AvitoLog.ConditionFilterApplied(_logger, conditionLabel);
            }
            else
            {
                AvitoLog.ConditionFilterFailed(_logger, conditionLabel);
            }
        }
    }

    private void LogSearchFinished(SearchCriteria criteria, int count)
    {
        var query = FormatSearchQuery(criteria);
        var location = FormatSearchLocation(criteria);

        if (count <= 0)
        {
            AvitoLog.SearchNothingFound(_logger, query, location);
            return;
        }

        AvitoLog.SearchCompleted(_logger, query, location, count);
    }

    private static string FormatSearchQuery(SearchCriteria criteria)
    {
        var parts = criteria
            .Keywords.Where(static k => !string.IsNullOrWhiteSpace(k))
            .Select(static k => k.Trim())
            .ToArray();

        return parts.Length == 0 ? "без запроса" : string.Join(", ", parts);
    }

    private static string FormatSearchLocation(SearchCriteria criteria)
    {
        var slug =
            criteria.LocationSlugs.FirstOrDefault(static s => !string.IsNullOrWhiteSpace(s))
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(slug))
        {
            return "все регионы";
        }

        var key = slug.Trim();
        if (AvitoGeo.TryFromSearchSlug(key, out var region) && !string.IsNullOrWhiteSpace(region))
        {
            return region;
        }

        return AvitoGeo.DisplayName(key);
    }

    private static string FormatSortLabel(string? sort)
    {
        if (AvitoSort.TryResolve(sort, out var canonical, out _))
        {
            return canonical;
        }

        return string.IsNullOrWhiteSpace(sort) ? AvitoSort.DisplayName(AvitoSortMode.Date) : sort.Trim();
    }

    private static Listing CloneWithId(Listing source, string listingId) =>
        new()
        {
            Id = listingId,
            Title = source.Title,
            Description = source.Description,
            Price = source.Price,
            Currency = source.Currency,
            Url = source.Url,
            Location = source.Location,
            SellerName = source.SellerName,
            PublishedAt = source.PublishedAt,
            FirstSeenAt = source.FirstSeenAt,
            LastSeenAt = source.LastSeenAt,
            ImageUrls = source.ImageUrls,
            Images = source.Images,
            Source = source.Source
        };
}

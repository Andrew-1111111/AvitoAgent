using AvitoAgent.Shared;
using Microsoft.Extensions.Logging;

namespace AvitoAgent.Avito.Logging;

internal static partial class AvitoLog
{
    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Warning,
        Message = "Поиск Avito пропущен: ключевые слова не заданы"
    )]
    public static partial void NoKeywords(ILogger logger);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "Поиск Avito: {Url}")]
    public static partial void SearchStarted(ILogger logger, string url);

    [LoggerMessage(
        EventId = 3093,
        Level = LogLevel.Information,
        Message = "Сортировка: {Sort}"
    )]
    public static partial void SortSelected(ILogger logger, string sort);

    [LoggerMessage(
        EventId = 3004,
        Level = LogLevel.Information,
        Message = "Поиск по запросу «{Query}» ({Location}): найдено {Count} объявлений"
    )]
    public static partial void SearchCompleted(
        ILogger logger,
        string query,
        string location,
        int count
    );

    [LoggerMessage(
        EventId = 3090,
        Level = LogLevel.Information,
        Message = "По запросу «{Query}» в регионе «{Location}» ничего не найдено"
    )]
    public static partial void SearchNothingFound(ILogger logger, string query, string location);

    public static void DetailParseFailed(ILogger logger, string listingId, Exception exception)
    {
        if (BrowserErrorText.IsOperationCanceled(exception))
        {
            DetailParseCancelled(logger, listingId);
            return;
        }

        DetailParseFailed(logger, listingId, BrowserErrorText.Describe(exception));
        DetailParseFailedDetails(logger, listingId, exception);
    }

    [LoggerMessage(
        EventId = 3005,
        Level = LogLevel.Warning,
        Message = "Объявление {ListingId}: не удалось открыть карточку — {Reason}"
    )]
    public static partial void DetailParseFailed(ILogger logger, string listingId, string reason);

    [LoggerMessage(
        EventId = 3093,
        Level = LogLevel.Information,
        Message = "Объявление {ListingId}: загрузка прервана — поиск остановлен"
    )]
    public static partial void DetailParseCancelled(ILogger logger, string listingId);

    [LoggerMessage(
        EventId = 3070,
        Level = LogLevel.Debug,
        Message = "Подробности ошибки деталей объявления {ListingId}"
    )]
    public static partial void DetailParseFailedDetails(
        ILogger logger,
        string listingId,
        Exception exception
    );

    public static void ActionFailed(ILogger logger, string action, Exception exception)
    {
        if (BrowserErrorText.IsOperationCanceled(exception))
        {
            ActionCancelled(logger);
            return;
        }

        ActionFailed(logger, action, BrowserErrorText.Describe(exception));
        ActionFailedDetails(logger, action, exception);
    }

    [LoggerMessage(
        EventId = 3071,
        Level = LogLevel.Warning,
        Message = "{Action} — {Reason}"
    )]
    public static partial void ActionFailed(ILogger logger, string action, string reason);

    [LoggerMessage(
        EventId = 3094,
        Level = LogLevel.Debug,
        Message = "Возврат к выдаче не выполнен — поиск остановлен"
    )]
    public static partial void ActionCancelled(ILogger logger);

    [LoggerMessage(EventId = 3072, Level = LogLevel.Debug, Message = "Подробности: {Action}")]
    public static partial void ActionFailedDetails(
        ILogger logger,
        string action,
        Exception exception
    );

    [LoggerMessage(
        EventId = 3006,
        Level = LogLevel.Debug,
        Message = "Avito: сессия уже авторизована"
    )]
    public static partial void AlreadyAuthenticated(ILogger logger);

    [LoggerMessage(
        EventId = 3007,
        Level = LogLevel.Information,
        Message = "Avito: начало авторизации"
    )]
    public static partial void LoginStarted(ILogger logger);

    [LoggerMessage(
        EventId = 3008,
        Level = LogLevel.Information,
        Message = "Avito: авторизация успешна"
    )]
    public static partial void LoginSucceeded(ILogger logger);

    [LoggerMessage(
        EventId = 3011,
        Level = LogLevel.Debug,
        Message = "Avito: сессия сохранена в {Path}"
    )]
    public static partial void StorageStateSaved(ILogger logger, string path);

    [LoggerMessage(EventId = 3012, Level = LogLevel.Information, Message = "Avito auth: {Step}")]
    public static partial void LoginStep(ILogger logger, string step);

    [LoggerMessage(
        EventId = 3014,
        Level = LogLevel.Warning,
        Message = "Avito auth: форма не найдена, скриншот сохранён: {Path}"
    )]
    public static partial void LoginFailedScreenshot(ILogger logger, string path);

    [LoggerMessage(
        EventId = 3015,
        Level = LogLevel.Information,
        Message = "Avito auth: текущий URL — {Url}"
    )]
    public static partial void LoginCurrentUrl(ILogger logger, string url);

    [LoggerMessage(
        EventId = 3016,
        Level = LogLevel.Information,
        Message = "Avito auth: войдите вручную в открытом браузере. Ожидание отключено — после входа агент продолжит сам."
    )]
    public static partial void ManualLoginInstructions(ILogger logger);

    [LoggerMessage(
        EventId = 3062,
        Level = LogLevel.Warning,
        Message = "Avito: поиск пропущен — нет авторизации. Войдите в браузере агента."
    )]
    public static partial void SearchSkippedNotAuthenticated(ILogger logger);

    [LoggerMessage(
        EventId = 3017,
        Level = LogLevel.Information,
        Message = "Avito auth: вкладка закрыта, переключаемся на активную"
    )]
    public static partial void PageClosedSwitching(ILogger logger);

    [LoggerMessage(
        EventId = 3018,
        Level = LogLevel.Debug,
        Message = "Avito: доступ ограничён или требуется капча — {Url}"
    )]
    public static partial void AccessRestricted(ILogger logger, string url);

    [LoggerMessage(
        EventId = 3019,
        Level = LogLevel.Information,
        Message = "Avito: ожидание загрузки результатов поиска..."
    )]
    public static partial void WaitingForSearchResults(ILogger logger);

    [LoggerMessage(
        EventId = 3020,
        Level = LogLevel.Debug,
        Message = "Avito: страница поиска — title: {Title}, url: {Url}"
    )]
    public static partial void SearchPageState(ILogger logger, string title, string url);

    [LoggerMessage(
        EventId = 3021,
        Level = LogLevel.Warning,
        Message = "Avito: скриншот поиска сохранён: {Path}"
    )]
    public static partial void SearchScreenshotSaved(ILogger logger, string path);

    [LoggerMessage(
        EventId = 3091,
        Level = LogLevel.Warning,
        Message = "Avito: авторизация при старте не удалась — {Reason}. Приложение продолжит работу; войдите через Telegram или вручную."
    )]
    public static partial void AuthStartupFailed(ILogger logger, string reason);

    [LoggerMessage(
        EventId = 3092,
        Level = LogLevel.Warning,
        Message = "Avito: для входа нужен видимый браузер — Headless будет выключен, агент перезапускается"
    )]
    public static partial void RestartingForVisibleLogin(ILogger logger);

    [LoggerMessage(
        EventId = 3023,
        Level = LogLevel.Debug,
        Message = "Avito: пауза {DelayMs} мс перед открытием объявления {ListingId}"
    )]
    public static partial void DetailNavigationDelay(ILogger logger, int delayMs, string listingId);

    [LoggerMessage(
        EventId = 3024,
        Level = LogLevel.Information,
        Message = "Avito: запуск браузера..."
    )]
    public static partial void BrowserStarting(ILogger logger);

    [LoggerMessage(
        EventId = 3025,
        Level = LogLevel.Information,
        Message = "Avito: браузер готов, окно останется открытым"
    )]
    public static partial void BrowserReady(ILogger logger);

    [LoggerMessage(
        EventId = 3121,
        Level = LogLevel.Information,
        Message = "Браузер закрыт. Приложение останавливается."
    )]
    public static partial void BrowserClosedExternally(ILogger logger);

    [LoggerMessage(
        EventId = 3123,
        Level = LogLevel.Information,
        Message = "Приложение останавливается."
    )]
    public static partial void ApplicationStopping(ILogger logger);

    [LoggerMessage(
        EventId = 3124,
        Level = LogLevel.Error,
        Message = "Не удалось запустить браузер: {Reason}"
    )]
    public static partial void BrowserStartFailed(ILogger logger, string reason);

    [LoggerMessage(
        EventId = 3122,
        Level = LogLevel.Warning,
        Message = "Avito: не удалось отправить уведомление о закрытии: {Reason}"
    )]
    public static partial void ShutdownNotifyFailed(ILogger logger, string reason);

    [LoggerMessage(
        EventId = 3027,
        Level = LogLevel.Warning,
        Message = "Avito: нужна капча — пройдите её вручную в браузере агента ({Url})"
    )]
    public static partial void ManualCaptchaRequired(ILogger logger, string url);

    [LoggerMessage(
        EventId = 3028,
        Level = LogLevel.Information,
        Message = "Avito: капча пройдена, продолжаем"
    )]
    public static partial void ManualCaptchaResolved(ILogger logger);

    [LoggerMessage(
        EventId = 3029,
        Level = LogLevel.Information,
        Message = "Avito: всё ещё ждём капчу в браузере ({ElapsedSeconds} с)…"
    )]
    public static partial void ManualCaptchaWaiting(ILogger logger, int elapsedSeconds);

    [LoggerMessage(
        EventId = 3030,
        Level = LogLevel.Warning,
        Message = "Avito: время ожидания капчи истекло ({TimeoutMs} мс)"
    )]
    public static partial void ManualCaptchaTimeout(ILogger logger, int timeoutMs);

    [LoggerMessage(
        EventId = 3131,
        Level = LogLevel.Information,
        Message = "Avito: карточки не появились, перезагружаем страницу выдачи"
    )]
    public static partial void SearchResultsReloading(ILogger logger);

    [LoggerMessage(
        EventId = 3132,
        Level = LogLevel.Information,
        Message = "Avito: результаты загрузились после перезагрузки страницы"
    )]
    public static partial void SearchResultsLoadedAfterReload(ILogger logger);

    [LoggerMessage(
        EventId = 3130,
        Level = LogLevel.Warning,
        Message = "Avito: капча пройдена, но блокировка вернулась — ждём дальше "
            + "(нужен другой IP: мобильный/резидентный прокси)"
    )]
    public static partial void BlockPersistsAfterCaptcha(ILogger logger);

    [LoggerMessage(
        EventId = 3032,
        Level = LogLevel.Information,
        Message = "Avito: на странице найдено карточек: {Count}"
    )]
    public static partial void SearchCardsParsed(ILogger logger, int count);

    [LoggerMessage(
        EventId = 3034,
        Level = LogLevel.Warning,
        Message = "Avito: image download rejected: {Url} ({Status})"
    )]
    public static partial void ImageDownloadRejected(ILogger logger, string url, int status);

    public static void ImageDownloadViaBrowserFailed(ILogger logger, string url, Exception exception)
    {
        ImageDownloadViaBrowserFailed(logger, BrowserErrorText.Describe(exception), url);
        ImageDownloadViaBrowserFailedDetails(logger, url, exception);
    }

    [LoggerMessage(
        EventId = 3074,
        Level = LogLevel.Warning,
        Message = "Не удалось скачать фото ({Reason}): {Url}"
    )]
    public static partial void ImageDownloadViaBrowserFailed(
        ILogger logger,
        string reason,
        string url
    );

    [LoggerMessage(
        EventId = 3073,
        Level = LogLevel.Debug,
        Message = "Подробности ошибки скачивания фото: {Url}"
    )]
    public static partial void ImageDownloadViaBrowserFailedDetails(
        ILogger logger,
        string url,
        Exception exception
    );

    [LoggerMessage(
        EventId = 3033,
        Level = LogLevel.Information,
        Message = "Avito: лимит детальных переходов ({Limit}) — остальные берём с карточки поиска"
    )]
    public static partial void SearchCardsUsingPreview(ILogger logger, int limit);

    [LoggerMessage(
        EventId = 3034,
        Level = LogLevel.Warning,
        Message = "Avito: вкладка закрыта во время ожидания капчи"
    )]
    public static partial void ManualCaptchaPageClosed(ILogger logger);

    [LoggerMessage(
        EventId = 3035,
        Level = LogLevel.Information,
        Message = "Avito: капча пройдена, пауза {DelayMs} мс перед продолжением"
    )]
    public static partial void PostCaptchaPause(ILogger logger, int delayMs);

    [LoggerMessage(
        EventId = 3036,
        Level = LogLevel.Information,
        Message = "Avito: галерея — собрано {Count} из {Total} фото"
    )]
    public static partial void GalleryPhotosCollected(ILogger logger, int count, int total);

    [LoggerMessage(
        EventId = 3037,
        Level = LogLevel.Information,
        Message = "Avito: открыто объявление {ListingId}"
    )]
    public static partial void ListingOpened(ILogger logger, string listingId);

    [LoggerMessage(
        EventId = 3038,
        Level = LogLevel.Information,
        Message = "Avito: возврат к поиску после {ListingId}"
    )]
    public static partial void ReturnedToSearch(ILogger logger, string listingId);

    [LoggerMessage(
        EventId = 3039,
        Level = LogLevel.Information,
        Message = "Avito: стартовый URL — {Url}"
    )]
    public static partial void OpenedStartUrl(ILogger logger, string url);

    [LoggerMessage(
        EventId = 3042,
        Level = LogLevel.Warning,
        Message = "Не удалось открыть объявление {ListingId} кликом по карточке"
    )]
    public static partial void ListingOpenFailed(ILogger logger, string listingId);

    [LoggerMessage(
        EventId = 3088,
        Level = LogLevel.Warning,
        Message = "Объявление {ListingId}: повторное открытие после ошибки «{Reason}»"
    )]
    public static partial void ListingDetailRetry(ILogger logger, string listingId, string reason);

    [LoggerMessage(
        EventId = 3089,
        Level = LogLevel.Warning,
        Message = "Объявление {ListingId}: повторно не открылось — пропускаем"
    )]
    public static partial void ListingDetailSkippedAfterRetry(ILogger logger, string listingId);

    [LoggerMessage(
        EventId = 3044,
        Level = LogLevel.Information,
        Message = "Avito: обработка страницы сверху вниз (новые→старые), карточек: {Count}"
    )]
    public static partial void ProcessingPageTopDown(ILogger logger, int count);

    [LoggerMessage(
        EventId = 3046,
        Level = LogLevel.Information,
        Message = "Avito: переход на следующую страницу выдачи"
    )]
    public static partial void GoingToNextSearchPage(ILogger logger);

    [LoggerMessage(
        EventId = 3047,
        Level = LogLevel.Information,
        Message = "Avito: объявление {ListingId} уже обработано — детали не открываем"
    )]
    public static partial void ListingAlreadyKnown(ILogger logger, string listingId);

    [LoggerMessage(
        EventId = 3048,
        Level = LogLevel.Information,
        Message = "Avito: объявление {ListingId} старше заданной даты — пропускаем"
    )]
    public static partial void ListingExcludedByDate(ILogger logger, string listingId);

    [LoggerMessage(
        EventId = 3049,
        Level = LogLevel.Information,
        Message = "Avito: объявление {ListingId} вне региона поиска ({Location}) — пропускаем"
    )]
    public static partial void ListingExcludedWrongLocation(
        ILogger logger,
        string listingId,
        string location
    );

    [LoggerMessage(
        EventId = 3052,
        Level = LogLevel.Information,
        Message = "Avito: фото скачаны через браузер: {Count} шт."
    )]
    public static partial void ImagesDownloadedViaBrowser(ILogger logger, int count);

    [LoggerMessage(
        EventId = 3053,
        Level = LogLevel.Warning,
        Message = "Avito: выдача не загрузилась после формы — переход по URL поиска"
    )]
    public static partial void SearchFallbackToUrl(ILogger logger);

    [LoggerMessage(
        EventId = 3059,
        Level = LogLevel.Information,
        Message = "Avito: на странице блок «другие города/категории» — берём {Count} объявлений выше него"
    )]
    public static partial void SearchCardsAboveOtherCitiesBanner(ILogger logger, int count);

    [LoggerMessage(
        EventId = 3060,
        Level = LogLevel.Information,
        Message = "Avito: дальше страницы поиска не открываем — на текущей есть блок «другие города/категории»"
    )]
    public static partial void SkipNextSearchPageDueToOtherCitiesBanner(ILogger logger);

    [LoggerMessage(
        EventId = 3061,
        Level = LogLevel.Information,
        Message = "Avito: объявление {ListingId} отсеяно по цене/исключениям («{Title}») — пропускаем"
    )]
    public static partial void ListingExcludedByFilters(
        ILogger logger,
        string listingId,
        string title
    );

    [LoggerMessage(
        EventId = 3065,
        Level = LogLevel.Information,
        Message = "Avito: в меню выбрана сортировка «{Sort}»"
    )]
    public static partial void SortUiApplied(ILogger logger, string sort);

    [LoggerMessage(
        EventId = 3066,
        Level = LogLevel.Warning,
        Message = "Avito: не удалось выбрать сортировку «{Sort}» в меню"
    )]
    public static partial void SortUiFailed(ILogger logger, string sort);

    [LoggerMessage(
        EventId = 3068,
        Level = LogLevel.Information,
        Message = "Avito: фильтры состояние / доставка / продавец применены через URL"
    )]
    public static partial void CriteriaFiltersApplied(ILogger logger);

    [LoggerMessage(
        EventId = 3069,
        Level = LogLevel.Warning,
        Message = "Avito: не удалось применить фильтры состояние / доставка / продавец через URL"
    )]
    public static partial void CriteriaFiltersFailed(ILogger logger);

    [LoggerMessage(
        EventId = 3070,
        Level = LogLevel.Information,
        Message = "Avito: фильтр «С Авито Доставкой» активен"
    )]
    public static partial void DeliveryFilterApplied(ILogger logger);

    [LoggerMessage(
        EventId = 3071,
        Level = LogLevel.Warning,
        Message = "Avito: фильтр «С Авито Доставкой» не применился"
    )]
    public static partial void DeliveryFilterFailed(ILogger logger);

    [LoggerMessage(
        EventId = 3072,
        Level = LogLevel.Information,
        Message = "Avito: фильтр продавца «{Seller}» активен"
    )]
    public static partial void SellerFilterApplied(ILogger logger, string seller);

    [LoggerMessage(
        EventId = 3073,
        Level = LogLevel.Warning,
        Message = "Avito: фильтр продавца «{Seller}» не применился"
    )]
    public static partial void SellerFilterFailed(ILogger logger, string seller);

    [LoggerMessage(
        EventId = 3074,
        Level = LogLevel.Information,
        Message = "Avito: фильтр состояния «{Condition}» активен"
    )]
    public static partial void ConditionFilterApplied(ILogger logger, string condition);

    [LoggerMessage(
        EventId = 3075,
        Level = LogLevel.Warning,
        Message = "Avito: фильтр состояния «{Condition}» не применился"
    )]
    public static partial void ConditionFilterFailed(ILogger logger, string condition);
}

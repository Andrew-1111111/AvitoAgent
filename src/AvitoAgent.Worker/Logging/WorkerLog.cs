using Microsoft.Extensions.Logging;

namespace AvitoAgent.Worker.Logging;

internal static partial class WorkerLog
{
    [LoggerMessage(
        EventId = 6001,
        Level = LogLevel.Information,
        Message = "AgentWorker запущен, интервал опроса: {Minutes} мин"
    )]
    public static partial void Started(ILogger logger, int minutes);

    [LoggerMessage(
        EventId = 6022,
        Level = LogLevel.Information,
        Message = "Пауза {Minutes} мин до следующего цикла поиска"
    )]
    public static partial void WaitingNextCycle(ILogger logger, int minutes);

    [LoggerMessage(
        EventId = 6002,
        Level = LogLevel.Information,
        Message = "Начат цикл поиска: {Keywords}"
    )]
    public static partial void CycleStarted(ILogger logger, string keywords);

    [LoggerMessage(
        EventId = 6019,
        Level = LogLevel.Information,
        Message = "Поиск запроса: {Keyword}"
    )]
    public static partial void KeywordStarted(ILogger logger, string keyword);

    [LoggerMessage(
        EventId = 6003,
        Level = LogLevel.Information,
        Message = "Найдено объявлений: {Count}"
    )]
    public static partial void ListingsFound(ILogger logger, int count);

    [LoggerMessage(
        EventId = 6023,
        Level = LogLevel.Information,
        Message = "По запросам «{Keywords}» ничего не найдено"
    )]
    public static partial void NothingFound(ILogger logger, string keywords);

    [LoggerMessage(EventId = 6004, Level = LogLevel.Information, Message = "Цикл поиска завершён")]
    public static partial void CycleCompleted(ILogger logger);

    [LoggerMessage(
        EventId = 6005,
        Level = LogLevel.Warning,
        Message = "Цикл поиска завершился с ошибкой: {Reason}"
    )]
    public static partial void CycleFailed(ILogger logger, string reason);

    [LoggerMessage(
        EventId = 6021,
        Level = LogLevel.Debug,
        Message = "Подробности ошибки цикла поиска"
    )]
    public static partial void CycleFailedDetails(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6006, Level = LogLevel.Warning, Message = "Ключевые слова не заданы")]
    public static partial void NoKeywords(ILogger logger);

    [LoggerMessage(
        EventId = 6007,
        Level = LogLevel.Error,
        Message = "Ошибка AI-анализа объявления {ListingId}: {Error}"
    )]
    public static partial void AnalysisFailed(ILogger logger, string listingId, string error);

    [LoggerMessage(
        EventId = 6014,
        Level = LogLevel.Warning,
        Message = "Анализ {ListingId} пропущен: {Reason}"
    )]
    public static partial void AnalysisSkipped(ILogger logger, string listingId, string reason);

    [LoggerMessage(
        EventId = 6008,
        Level = LogLevel.Information,
        Message = "Отправлено уведомление для {ListingId}"
    )]
    public static partial void Notified(ILogger logger, string listingId);

    [LoggerMessage(
        EventId = 6009,
        Level = LogLevel.Information,
        Message = "Объявление уже проанализировано: {ListingId}"
    )]
    public static partial void ListingAlreadyAnalyzed(ILogger logger, string listingId);

    [LoggerMessage(
        EventId = 6016,
        Level = LogLevel.Information,
        Message = "Уведомление уже отправлено: {ListingId}"
    )]
    public static partial void ListingAlreadySentToTelegram(ILogger logger, string listingId);

    [LoggerMessage(
        EventId = 6010,
        Level = LogLevel.Information,
        Message = "Отправка в LM Studio: {ListingId}"
    )]
    public static partial void AnalysisStarted(ILogger logger, string listingId);

    [LoggerMessage(
        EventId = 6011,
        Level = LogLevel.Information,
        Message = "LM Studio ответ для {ListingId}: relevant={IsRelevant}, authentic={IsAuthentic}, score={Score}. {Reason}"
    )]
    public static partial void AnalysisResult(
        ILogger logger,
        string listingId,
        bool isRelevant,
        bool? isAuthentic,
        int score,
        string reason
    );

    [LoggerMessage(
        EventId = 6012,
        Level = LogLevel.Information,
        Message = "Цикл: проанализировано {Analyzed}, пропущено {Skipped}"
    )]
    public static partial void CycleStats(ILogger logger, int analyzed, int skipped);

    [LoggerMessage(EventId = 6013, Level = LogLevel.Error, Message = "{Message}")]
    public static partial void LmStudioUnavailable(ILogger logger, string message);

    [LoggerMessage(EventId = 6017, Level = LogLevel.Error, Message = "{Message}")]
    public static partial void TelegramUnavailable(ILogger logger, string message);

    [LoggerMessage(
        EventId = 6015,
        Level = LogLevel.Information,
        Message = "LM Studio отключена — объявления уходят в Telegram без AI-анализа"
    )]
    public static partial void LmStudioDisabled(ILogger logger);

    [LoggerMessage(
        EventId = 6024,
        Level = LogLevel.Information,
        Message = "Отправка в Telegram без анализа: {ListingId}"
    )]
    public static partial void NotifyingWithoutAnalysis(ILogger logger, string listingId);

    [LoggerMessage(
        EventId = 6022,
        Level = LogLevel.Information,
        Message = "Агент спит до {UntilLocal} ({TimeZone})"
    )]
    public static partial void Sleeping(ILogger logger, DateTime untilLocal, string timeZone);
}

using Microsoft.Extensions.Logging;

namespace AvitoAgent.AI.Logging;

internal static partial class AiLog
{
    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Information,
        Message = "Анализ объявления {ListingId}, модель: {ModelId}, изображений: {ImageCount}"
    )]
    public static partial void AnalyzingListing(
        ILogger logger,
        string listingId,
        string modelId,
        int imageCount
    );

    [LoggerMessage(
        EventId = 4002,
        Level = LogLevel.Information,
        Message = "Анализ {ListingId} завершён: authentic={IsAuthentic}, score={Score}"
    )]
    public static partial void AnalysisCompleted(
        ILogger logger,
        string listingId,
        bool? isAuthentic,
        int score
    );

    [LoggerMessage(
        EventId = 4003,
        Level = LogLevel.Error,
        Message = "LM Studio ошибка для {ListingId}: {StatusCode} {Body}"
    )]
    public static partial void RequestFailed(
        ILogger logger,
        string listingId,
        int statusCode,
        string body
    );

    [LoggerMessage(
        EventId = 4004,
        Level = LogLevel.Warning,
        Message = "Не удалось загрузить изображение {Url}"
    )]
    public static partial void ImageDownloadFailed(ILogger logger, string url, Exception exception);

    [LoggerMessage(
        EventId = 4005,
        Level = LogLevel.Information,
        Message = "LM Studio: используется модель {ModelId}"
    )]
    public static partial void ModelSelected(ILogger logger, string modelId);

    [LoggerMessage(
        EventId = 4006,
        Level = LogLevel.Warning,
        Message = "LM Studio: модель {RequestedModel} не найдена, используется {ResolvedModel}"
    )]
    public static partial void ModelNotFoundUsingFirst(
        ILogger logger,
        string requestedModel,
        string resolvedModel
    );

    [LoggerMessage(
        EventId = 4007,
        Level = LogLevel.Information,
        Message = "LM Studio: частичное совпадение {RequestedModel} → {ResolvedModel}"
    )]
    public static partial void ModelFallback(
        ILogger logger,
        string requestedModel,
        string resolvedModel
    );

    [LoggerMessage(
        EventId = 4008,
        Level = LogLevel.Information,
        Message = "LM Studio: фото {SentCount}/{TotalCount}, ~{PayloadKb} KB, {SizeLabel}, контекст {UsableContext}/{FullContext} ({Percent}%)"
    )]
    public static partial void ImagesPrepared(
        ILogger logger,
        int sentCount,
        int totalCount,
        int payloadKb,
        string sizeLabel,
        int usableContext,
        int fullContext,
        int percent
    );

    [LoggerMessage(
        EventId = 4009,
        Level = LogLevel.Information,
        Message = "LM Studio: prompt source {Path}"
    )]
    public static partial void PromptSource(ILogger logger, string path);

    [LoggerMessage(
        EventId = 4010,
        Level = LogLevel.Information,
        Message = "LM Studio: модель {ModelId} доступна, контекст {AvailableContext}, для фото берём {Percent}% = {UsableContext} токенов"
    )]
    public static partial void ModelContextBudget(
        ILogger logger,
        string modelId,
        int availableContext,
        int percent,
        int usableContext
    );

    [LoggerMessage(
        EventId = 4011,
        Level = LogLevel.Warning,
        Message = "LM Studio: у модели {ModelId} не удалось прочитать context_length - берём запасной бюджет {FallbackContext}"
    )]
    public static partial void ModelContextUnknown(ILogger logger, string modelId, int fallbackContext);

    [LoggerMessage(
        EventId = 4014,
        Level = LogLevel.Warning,
        Message = "LM Studio: модель {ModelId} ещё не загружена в память (max={MaxContext})"
    )]
    public static partial void ModelNotLoaded(ILogger logger, string modelId, int maxContext);

    [LoggerMessage(
        EventId = 4019,
        Level = LogLevel.Information,
        Message = "LM Studio: загружаем модель {ModelId} в память (context_length={ContextLength})…"
    )]
    public static partial void ModelLoading(ILogger logger, string modelId, int contextLength);

    [LoggerMessage(
        EventId = 4020,
        Level = LogLevel.Information,
        Message = "LM Studio: модель {ModelId} загружена за {Seconds:0.0} с, context_length={ContextLength}"
    )]
    public static partial void ModelLoadSucceeded(
        ILogger logger,
        string modelId,
        double seconds,
        int contextLength
    );

    [LoggerMessage(
        EventId = 4021,
        Level = LogLevel.Warning,
        Message = "LM Studio: не удалось загрузить {ModelId}: {Reason}"
    )]
    public static partial void ModelLoadFailed(ILogger logger, string modelId, string reason);

    [LoggerMessage(
        EventId = 4022,
        Level = LogLevel.Information,
        Message = "LM Studio: у {ModelId} контекст {LoadedContext} < {WantContext} - перезагружаем с нужным n_ctx"
    )]
    public static partial void ModelContextTooSmall(
        ILogger logger,
        string modelId,
        int loadedContext,
        int wantContext
    );

    [LoggerMessage(
        EventId = 4015,
        Level = LogLevel.Warning,
        Message = "LM Studio: у {ModelId} контекст {AvailableContext} токенов - для vision/скорости Processing prompt лучше 8k-16k в настройках загрузки модели"
    )]
    public static partial void ModelContextHuge(ILogger logger, string modelId, int availableContext);

    [LoggerMessage(
        EventId = 4012,
        Level = LogLevel.Debug,
        Message = "LM Studio: для {ListingId} thinking выключен (prefill + API flags)"
    )]
    public static partial void NoThinkEnabled(ILogger logger, string listingId);

    [LoggerMessage(
        EventId = 4017,
        Level = LogLevel.Warning,
        Message = "LM Studio: для {ListingId} thinking всё ещё активен (reasoning_content заполнен). В LM Studio выключите Thinking у модели и перезагрузите её."
    )]
    public static partial void ThinkingStillActive(ILogger logger, string listingId);

    [LoggerMessage(
        EventId = 4016,
        Level = LogLevel.Warning,
        Message = "LM Studio: для {ListingId} ответ без JSON. Фрагмент: {Preview}"
    )]
    public static partial void InvalidJsonResponse(ILogger logger, string listingId, string preview);
}

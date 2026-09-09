using Microsoft.Extensions.Logging;

namespace AvitoAgent.Telegram.Logging;

internal static partial class TelegramLog
{
    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Debug,
        Message = "Telegram не настроен, уведомление пропущено"
    )]
    public static partial void NotConfigured(ILogger logger);

    [LoggerMessage(
        EventId = 5002,
        Level = LogLevel.Information,
        Message = "Telegram-уведомление отправлено для {ListingId}"
    )]
    public static partial void Sent(ILogger logger, string listingId);

    [LoggerMessage(
        EventId = 5003,
        Level = LogLevel.Error,
        Message = "Ошибка отправки в Telegram: {StatusCode} {Body}"
    )]
    public static partial void SendFailed(ILogger logger, int statusCode, string body);

    [LoggerMessage(EventId = 5004, Level = LogLevel.Information, Message = "Telegram бот запущен")]
    public static partial void BotStarted(ILogger logger);

    [LoggerMessage(
        EventId = 5005,
        Level = LogLevel.Warning,
        Message = "Telegram polling failed: {Reason}"
    )]
    public static partial void BotPollFailed(ILogger logger, string reason);

    [LoggerMessage(
        EventId = 5006,
        Level = LogLevel.Information,
        Message = "Telegram: к объявлению {ListingId} прикрепляем {Count} фото файлами"
    )]
    public static partial void PhotosAttached(ILogger logger, string listingId, int count);

    [LoggerMessage(
        EventId = 5012,
        Level = LogLevel.Warning,
        Message = "Telegram: фото для {ListingId} не ушли ({Reason}) - отправляем текст без картинок"
    )]
    public static partial void PhotosFailedContinueText(ILogger logger, string listingId, string reason);

    [LoggerMessage(
        EventId = 5007,
        Level = LogLevel.Warning,
        Message = "Telegram: у {ListingId} нет скачанных фото (есть {UrlCount} URL) - отправка без картинок"
    )]
    public static partial void PhotosMissing(ILogger logger, string listingId, int urlCount);

    [LoggerMessage(
        EventId = 5009,
        Level = LogLevel.Information,
        Message = "Telegram: отправлено напоминание о входе в Avito"
    )]
    public static partial void AuthRequiredSent(ILogger logger);

    [LoggerMessage(
        EventId = 5010,
        Level = LogLevel.Information,
        Message = "Telegram: отправлено уведомление об успешном входе в Avito"
    )]
    public static partial void AuthSucceededSent(ILogger logger);

    [LoggerMessage(
        EventId = 5011,
        Level = LogLevel.Information,
        Message = "В Telegram отправлено: приложение закрыто."
    )]
    public static partial void ApplicationClosedSent(ILogger logger);

    [LoggerMessage(
        EventId = 5013,
        Level = LogLevel.Information,
        Message = "Telegram: отправлен скриншот таймаута поиска"
    )]
    public static partial void SearchTimeoutSent(ILogger logger);

    [LoggerMessage(
        EventId = 5014,
        Level = LogLevel.Information,
        Message = "Telegram: отправлен скриншот капчи Avito"
    )]
    public static partial void CaptchaRequiredSent(ILogger logger);
}

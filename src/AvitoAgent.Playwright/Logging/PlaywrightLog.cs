using AvitoAgent.Shared;
using Microsoft.Extensions.Logging;

namespace AvitoAgent.Playwright.Logging;

internal static partial class PlaywrightLog
{
    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Playwright инициализирован, движок: {Engine}, headless: {Headless}"
    )]
    public static partial void Initialized(ILogger logger, string engine, bool headless);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Debug, Message = "Создана браузерная сессия")]
    public static partial void SessionCreated(ILogger logger);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Debug, Message = "Браузерная сессия закрыта")]
    public static partial void SessionDisposed(ILogger logger);

    [LoggerMessage(
        EventId = 2006,
        Level = LogLevel.Information,
        Message = "Браузер запущен и будет работать до остановки приложения"
    )]
    public static partial void PersistentSessionStarted(ILogger logger);

    [LoggerMessage(
        EventId = 2007,
        Level = LogLevel.Warning,
        Message = "Браузер был закрыт, создаём новую сессию"
    )]
    public static partial void PersistentSessionRecreating(ILogger logger);

    [LoggerMessage(
        EventId = 2008,
        Level = LogLevel.Information,
        Message = "Chrome профиль: {ProfilePath}"
    )]
    public static partial void PersistentProfileUsed(ILogger logger, string profilePath);

    [LoggerMessage(
        EventId = 2010,
        Level = LogLevel.Information,
        Message = "Сессия браузера сохранена: {Path}"
    )]
    public static partial void StorageStateFlushed(ILogger logger, string path);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Warning, Message = "Playwright: {Message}")]
    public static partial void Warning(ILogger logger, string message);

    public static void OperationFailed(ILogger logger, Exception exception)
    {
        OperationFailed(logger, BrowserErrorText.Describe(exception));
        OperationFailedDetails(logger, exception);
    }

    [LoggerMessage(EventId = 2005, Level = LogLevel.Error, Message = "Ошибка браузера: {Reason}")]
    public static partial void OperationFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 2009, Level = LogLevel.Debug, Message = "Подробности ошибки браузера")]
    public static partial void OperationFailedDetails(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2011,
        Level = LogLevel.Information,
        Message = "Блокировка трекеров/рекламы включена"
    )]
    public static partial void TrackersBlocked(ILogger logger);
}

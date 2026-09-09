namespace AvitoAgent.Shared;

/// <summary>
/// Короткий текст ошибки браузера для логов: без стека Playwright и без английских дампов.
/// </summary>
public static class BrowserErrorText
{
    public static string Describe(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var mapped = Map(current);
            if (mapped is not null)
            {
                return mapped;
            }
        }

        var fallback = FirstUsefulLine(exception.Message);
        return string.IsNullOrWhiteSpace(fallback)
            ? "неизвестная ошибка браузера"
            : fallback;
    }

    public static bool IsTimeout(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (LooksTimeout(current, current.Message ?? string.Empty))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsOperationCanceled(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is OperationCanceledException)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsBrowserClosed(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (LooksClosed(current.Message) || LooksClosed(current.GetType().Name))
            {
                return true;
            }
        }

        return false;
    }

    private static string? Map(Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            return "поиск остановлен";
        }

        var message = exception.Message;
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        if (
            message.Contains("ProcessSingleton", StringComparison.OrdinalIgnoreCase)
            || message.Contains("profile is already in use", StringComparison.OrdinalIgnoreCase)
            || message.Contains("SingletonLock", StringComparison.OrdinalIgnoreCase)
            || message.Contains("user data directory is already in use", StringComparison.OrdinalIgnoreCase)
        )
        {
            return "профиль Chrome уже занят — закройте окна Chrome агента и запустите снова";
        }

        if (LooksClosed(message) || LooksClosed(exception.GetType().Name))
        {
            return "окно браузера закрыто";
        }

        if (message.Contains("Page crashed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Target crashed", StringComparison.OrdinalIgnoreCase))
        {
            return "браузер аварийно закрылся";
        }

        if (LooksTimeout(exception, message))
        {
            return "страница не ответила вовремя";
        }

        if (
            message.Contains("response ended prematurely", StringComparison.OrdinalIgnoreCase)
            || message.Contains("ResponseEnded", StringComparison.OrdinalIgnoreCase)
        )
        {
            return "LM Studio оборвала ответ во время анализа фото "
                + "(часто нехватка VRAM или слишком тяжёлый запрос)";
        }

        if (message.Contains("Execution context was destroyed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("most likely because of a navigation", StringComparison.OrdinalIgnoreCase)
            || message.Contains("frame was detached", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Frame was detached", StringComparison.OrdinalIgnoreCase)
            || message.Contains("not attached to the DOM", StringComparison.OrdinalIgnoreCase))
        {
            return "страница обновилась, пока агент её читал";
        }

        if (message.Contains("net::ERR_INTERNET_DISCONNECTED", StringComparison.OrdinalIgnoreCase)
            || message.Contains("net::ERR_NETWORK_CHANGED", StringComparison.OrdinalIgnoreCase))
        {
            return "пропал интернет";
        }

        if (message.Contains("net::ERR_NAME_NOT_RESOLVED", StringComparison.OrdinalIgnoreCase))
        {
            return "не удалось найти сайт Avito (DNS)";
        }

        if (message.Contains("net::ERR_", StringComparison.OrdinalIgnoreCase))
        {
            return "не удалось открыть страницу Avito (сеть)";
        }

        if (LooksRussian(message))
        {
            return FirstUsefulLine(message);
        }

        return null;
    }

    private static bool LooksClosed(string text) =>
        text.Contains("has been closed", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Target page, context or browser", StringComparison.OrdinalIgnoreCase)
        || text.Contains("TargetClosed", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Browser closed", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Connection closed", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Страница браузера закрыта", StringComparison.Ordinal);

    private static bool LooksTimeout(Exception exception, string message) =>
        exception is TimeoutException
        || exception.GetType().Name.Contains("Timeout", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Timeout", StringComparison.OrdinalIgnoreCase)
        || message.Contains("timed out", StringComparison.OrdinalIgnoreCase)
        || message.Contains("time out", StringComparison.OrdinalIgnoreCase);

    private static bool LooksRussian(string text)
    {
        foreach (var ch in text)
        {
            if (ch is >= 'А' and <= 'я' or 'ё' or 'Ё')
            {
                return true;
            }
        }

        return false;
    }

    private static string FirstUsefulLine(string message)
    {
        var text = message.Trim();
        var dump = text.IndexOf("====", StringComparison.Ordinal);
        if (dump > 0)
        {
            text = text[..dump].Trim();
        }

        var newline = text.IndexOfAny(['\r', '\n']);
        if (newline >= 0)
        {
            text = text[..newline].Trim();
        }

        return text.Length <= 180 ? text : text[..180] + "…";
    }
}

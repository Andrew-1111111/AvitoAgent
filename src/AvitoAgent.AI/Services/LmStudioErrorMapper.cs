using System.Net.Sockets;
using System.Text.RegularExpressions;
using AvitoAgent.Core;

namespace AvitoAgent.AI.Services;

internal static partial class LmStudioErrorMapper
{
    public static LmStudioUnavailableException NotRunning(
        string baseUrl,
        Exception? inner = null
    ) =>
        new(
            "LM Studio не запущена или локальный сервер недоступен "
                + $"({TrimUrl(baseUrl)}). Откройте LM Studio, загрузите модель и включите сервер "
                + "(вкладка Developer → Start server, обычно http://localhost:1234).",
            inner
        );

    public static LmStudioUnavailableException NoModelLoaded(Exception? inner = null) =>
        new("В LM Studio не загружена модель.", inner);

    public static LmStudioUnavailableException ModelNotFound(
        string requested,
        IReadOnlyList<string> available
    ) =>
        new(
            $"В LM Studio не найдена модель «{requested}». "
                + (
                    available.Count == 0
                        ? "Загруженных моделей нет - загрузите нужную в LM Studio."
                        : $"Сейчас загружено: {string.Join(", ", available)}. Укажите одну из них в настройке LmStudio:Model."
                )
        );

    public static LmStudioUnavailableException NotResponding(
        string baseUrl,
        Exception? inner = null
    ) =>
        new(
            "LM Studio не отвечает - возможно, модель ещё загружается в память. "
                + $"Дождитесь статуса Loaded и повторите ({TrimUrl(baseUrl)}).",
            inner
        );

    public static InvalidOperationException ResponseEnded(
        string baseUrl,
        Exception? inner = null
    ) =>
        new(
            "LM Studio оборвала ответ во время анализа фото "
                + "(часто нехватка VRAM, слишком большой контекст или падение модели). "
                + "Уменьшите MaxImages / MaxImageSidePx или контекст в LM Studio и повторите "
                + $"({TrimUrl(baseUrl)}).",
            inner
        );

    public static Exception FromHttpError(int statusCode, string? body, Exception? inner = null)
    {
        if (TryParseContextOverflow(body, out var overflow))
        {
            return new InvalidOperationException(
                $"Запрос к модели слишком большой: {overflow.PromptTokens} токенов при контексте {overflow.ContextSize}. "
                    + "Уменьшите число/размер фото или увеличьте контекст загруженной модели в LM Studio.",
                inner
            );
        }

        if (LooksLikeInvalidImageUrl(body))
        {
            return new InvalidOperationException(
                "LM Studio отклонила изображение. Проверьте доступность URL и формат фото.",
                inner
            );
        }

        if (LooksLikeEnginePredictFailure(body))
        {
            return new InvalidOperationException(
                "LM Studio не смогла выполнить запрос к модели. Перезагрузите модель и повторите попытку.",
                inner
            );
        }

        if (LooksLikeGrammarError(body))
        {
            return new InvalidOperationException(
                "LM Studio отклонила грамматику ответа. Отключите structured output или обновите модель.",
                inner
            );
        }

        if (statusCode == 400 && !LooksLikeMissingModel(body))
        {
            var details = string.IsNullOrWhiteSpace(body) ? string.Empty : " " + TrimBody(body);
            return new InvalidOperationException(
                "LM Studio отклонила запрос (HTTP 400)." + details,
                inner
            );
        }

        return RequestFailed(statusCode, body, inner);
    }

    public static bool LooksLikeInvalidImageUrl(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        return ContainsAny(
            body,
            "invalid image",
            "image url",
            "unsupported image",
            "failed to fetch image"
        );
    }

    public static bool LooksLikeEnginePredictFailure(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        return ContainsAny(
            body,
            "engine protocol predict request failed",
            "predict request failed",
            "fetch failed"
        );
    }

    public static bool LooksLikeGrammarError(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        return ContainsAny(
            body,
            "empty grammar stack",
            "failed to initialize samplers",
            "invalid_request_error",
            "<think>"
        );
    }

    public static LmStudioUnavailableException RequestFailed(
        int statusCode,
        string? body,
        Exception? inner = null
    )
    {
        var details = string.IsNullOrWhiteSpace(body)
            ? string.Empty
            : " Ответ сервера: " + TrimBody(body);

        if (statusCode is 404 or 400 && LooksLikeMissingModel(body))
        {
            return new LmStudioUnavailableException(
                "LM Studio не нашла запрошенную модель.",
                inner
            );
        }

        if (statusCode is 503 or 502 or 500 && LooksLikeLoadFailure(body))
        {
            return new LmStudioUnavailableException("LM Studio не смогла загрузить модель.", inner);
        }

        return new LmStudioUnavailableException(
            $"LM Studio вернула ошибку HTTP {statusCode}.{details}",
            inner
        );
    }

    public static LmStudioUnavailableException? TryMap(Exception exception, string baseUrl)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is LmStudioUnavailableException mapped)
            {
                return mapped;
            }
        }

        // ResponseEnded обрабатывается в TryMapOrDescribe - здесь только «сервер недоступен».
        if (IsPrematureResponse(exception))
        {
            return null;
        }

        if (IsConnectionFailure(exception))
        {
            return NotRunning(baseUrl, exception);
        }

        if (IsTimeout(exception))
        {
            return NotResponding(baseUrl, exception);
        }

        if (exception is System.Text.Json.JsonException)
        {
            return new LmStudioUnavailableException(
                "LM Studio вернула некорректный JSON.",
                exception
            );
        }

        if (exception is HttpRequestException { StatusCode: not null } http)
        {
            return RequestFailed((int)http.StatusCode.Value, http.Message, exception);
        }

        return null;
    }

    /// <summary>
    /// Маппинг для анализа: обрыв ответа - читаемая ошибка объявления, не останов цикла.
    /// </summary>
    public static Exception? TryMapAnalysisError(Exception exception, string baseUrl)
    {
        if (IsPrematureResponse(exception))
        {
            return ResponseEnded(baseUrl, exception);
        }

        return TryMap(exception, baseUrl);
    }

    public static bool IsConnectionFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException http)
            {
                if (http.StatusCode is null && http.InnerException is SocketException)
                {
                    return true;
                }

                if (http.StatusCode is null && LooksLikeConnectionMessage(http.Message))
                {
                    return true;
                }
            }

            if (current is SocketException)
            {
                return true;
            }

            if (LooksLikeConnectionMessage(current.Message))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsPrematureResponse(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (LooksLikePrematureMessage(current.Message))
            {
                return true;
            }

            // .NET 5+: System.Net.Http.HttpIOException с ResponseEnded.
            if (
                current.GetType().Name.Contains("HttpIOException", StringComparison.Ordinal)
                && LooksLikePrematureMessage(current.ToString())
            )
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsTimeout(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is TimeoutException)
            {
                return true;
            }

            if (current is TaskCanceledException && current.InnerException is TimeoutException)
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryParseContextOverflow(string? body, out ContextOverflowInfo overflow)
    {
        overflow = default;
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        if (
            !ContainsAny(
                body,
                "exceed_context_size",
                "exceeds the available context",
                "context length",
                "too many tokens"
            )
        )
        {
            return false;
        }

        var prompt = MatchInt(body, PromptTokensRegex()) ?? MatchInt(body, RequestTokensRegex());
        var context = MatchInt(body, ContextSizeRegex()) ?? MatchInt(body, AvailableContextRegex());

        overflow = new ContextOverflowInfo(prompt ?? 0, context ?? 0);
        return true;
    }

    public static bool LooksLikeMissingModel(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        return ContainsAny(body, "model not found", "no model", "model_not_found", "unknown model");
    }

    public static bool LooksLikeLoadFailure(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return true;
        }

        return ContainsAny(
            body,
            "failed to load",
            "out of memory",
            "oom",
            "loading",
            "not enough memory",
            "unable to load"
        );
    }

    private static int? MatchInt(string text, Regex regex)
    {
        var match = regex.Match(text);
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : null;
    }

    [GeneratedRegex(
        @"n_prompt_tokens""?\s*:\s*(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex PromptTokensRegex();

    [GeneratedRegex(
        @"n_ctx""?\s*:\s*(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex ContextSizeRegex();

    [GeneratedRegex(
        @"request \((\d+) tokens\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex RequestTokensRegex();

    [GeneratedRegex(
        @"context size \((\d+) tokens\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex AvailableContextRegex();

    private static bool LooksLikePrematureMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return ContainsAny(
            message,
            "response ended prematurely",
            "responseended",
            "connection reset",
            "connection was closed",
            "existing connection was forcibly closed",
            "error while copying content to a stream"
        );
    }

    private static bool LooksLikeConnectionMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return ContainsAny(
            message,
            "connection refused",
            "actively refused",
            "name or service not known",
            "no such host",
            "network is unreachable"
        );
    }

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static string TrimUrl(string baseUrl) =>
        string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost:1234/v1/" : baseUrl.Trim();

    private static string TrimBody(string body)
    {
        var trimmed = body.ReplaceLineEndings(" ").Trim();
        return trimmed.Length <= 280 ? trimmed : trimmed[..280] + "…";
    }
}

internal readonly record struct ContextOverflowInfo(int PromptTokens, int ContextSize);

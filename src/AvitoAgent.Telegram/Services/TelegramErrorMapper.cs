using System.Net.Sockets;
using System.Text.Json;
using AvitoAgent.Core;

namespace AvitoAgent.Telegram.Services;

internal static class TelegramErrorMapper
{
    public static TelegramUnavailableException Unavailable(
        string reason,
        Exception? inner = null
    ) => new($"Telegram недоступен. {reason}", inner);

    public static TelegramUnavailableException FromHttp(
        int statusCode,
        string? body,
        Exception? inner = null
    )
    {
        var description = ReadDescription(body);

        return statusCode switch
        {
            401 => Unavailable("Неверный BotToken в appsettings.json.", inner),
            403 => Unavailable(
                "Бот заблокирован пользователем или не имеет доступа к чату.",
                inner
            ),
            400 when LooksLikeMissingChat(description) => Unavailable(
                "Указанный ChatId не найден или недоступен боту.",
                inner
            ),
            _ => Unavailable(
                string.IsNullOrWhiteSpace(description)
                    ? $"api.telegram.org вернул HTTP {statusCode}."
                    : $"api.telegram.org: {description} (HTTP {statusCode}).",
                inner
            ),
        };
    }

    public static TelegramUnavailableException FromNetwork(Exception exception) =>
        Unavailable(DescribeNetwork(exception), exception);

    public static string DescribeNetwork(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("SOCKS5", StringComparison.OrdinalIgnoreCase))
            {
                return current.Message;
            }
        }

        if (exception is TaskCanceledException)
        {
            return "Истекло время ожидания ответа api.telegram.org. Проверьте интернет, VPN или Telegram:Socks5.";
        }

        if (exception is HttpRequestException http && http.InnerException is SocketException socket)
        {
            return socket.SocketErrorCode switch
            {
                SocketError.TimedOut =>
                    "Нет ответа от api.telegram.org (таймаут). Проверьте интернет, VPN или Telegram:Socks5.",
                SocketError.HostNotFound =>
                    "Не найден хост api.telegram.org. Проверьте DNS и интернет.",
                SocketError.ConnectionRefused =>
                    "api.telegram.org отклонил соединение. Проверьте интернет, VPN или Telegram:Socks5.",
                SocketError.NetworkUnreachable or SocketError.HostUnreachable =>
                    "Сеть недоступна. Проверьте интернет, VPN или Telegram:Socks5.",
                _ =>
                    "Не удалось подключиться к api.telegram.org. Проверьте интернет, VPN или Telegram:Socks5.",
            };
        }

        return "Не удалось подключиться к api.telegram.org. Проверьте интернет, VPN или Telegram:Socks5.";
    }

    public static bool IsOk(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean();
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ReadDescription(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("description", out var description)
                ? description.GetString()
                : null;
        }
        catch (JsonException)
        {
            return TrimBody(body);
        }
    }

    private static bool LooksLikeMissingChat(string? description) =>
        !string.IsNullOrWhiteSpace(description)
        && (
            description.Contains("chat not found", StringComparison.OrdinalIgnoreCase)
            || description.Contains(
                "bot was blocked by the user",
                StringComparison.OrdinalIgnoreCase
            )
            || description.Contains("user is deactivated", StringComparison.OrdinalIgnoreCase)
        );

    private static string TrimBody(string body)
    {
        var text = body.ReplaceLineEndings(" ").Trim();
        return text.Length <= 180 ? text : text[..180] + "…";
    }
}

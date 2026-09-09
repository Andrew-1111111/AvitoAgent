using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AvitoAgent.Core;
using AvitoAgent.Core.Interfaces;
using AvitoAgent.Core.Models;
using AvitoAgent.Shared.Configuration;
using AvitoAgent.Telegram.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AvitoAgent.Telegram.Services;

public sealed class TelegramNotificationService(
    IHttpClientFactory httpClientFactory,
    IOptions<TelegramOptions> options,
    ILogger<TelegramNotificationService> logger
) : INotificationService
{
    private const int CaptionLimit = 1024;
    private const int MessageLimit = 4096;
    /// <summary>Лимит sendDocument в Telegram Bot API (без сжатия, в отличие от sendPhoto).</summary>
    private const int MaxDocumentBytes = 50 * 1024 * 1024;
    private const int ChatGptPromptMaxLength = 20_000;

    private static readonly JsonSerializerOptions MediaJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly TelegramOptions _options = options.Value;
    private readonly ILogger<TelegramNotificationService> _logger = logger;
    private int _socks5Logged;
    private int _applicationClosedNotified;

    private int MaxPhotos => Math.Clamp(_options.MaxPhotos, 1, 10);

    public async Task<bool> NotifyListingFoundAsync(
        Listing listing,
        ProductAnalysis? analysis,
        CancellationToken cancellationToken = default
    )
    {
        // analysis не в тексте: отбор по соответствию - в AgentWorker до вызова.
        _ = analysis;

        if (!_options.Enabled)
        {
            return false;
        }

        if (!HasCredentials())
        {
            TelegramLog.NotConfigured(_logger);
            return false;
        }

        LogSocks5Once();

        var photos = await ResolvePhotosAsync(listing);
        if (photos.Count > MaxPhotos)
        {
            photos = [.. photos.Take(MaxPhotos)];
        }
        var text = BuildMessage(listing);
        var chatGptLink = _options.ChatGptPhotoCheckEnabled
            ? TryBuildChatGptPhotoCheckLink(listing.ImageUrls)
            : null;

        var client = _httpClientFactory.CreateClient("Telegram");

        try
        {
            var textSentWithPhotos = false;
            if (photos.Count > 0)
            {
                // Фото + текст в одном сообщении (caption ≤ 1024; длинный текст обрезается).
                TelegramLog.PhotosAttached(_logger, listing.Id, photos.Count);
                try
                {
                    await SendPhotosAsync(
                        client,
                        photos,
                        caption: TruncateForCaption(text, listing.Url),
                        cancellationToken
                    );
                    textSentWithPhotos = true;
                }
                catch (TelegramUnavailableException ex)
                {
                    // Не роняем всё уведомление: текст важнее альбома.
                    TelegramLog.PhotosFailedContinueText(_logger, listing.Id, ex.Message);
                }
            }

            if (!textSentWithPhotos)
            {
                foreach (var part in SplitText(text, MessageLimit))
                {
                    if (!await SendTextAsync(client, part, cancellationToken))
                    {
                        return false;
                    }
                }
            }

            if (chatGptLink is not null)
            {
                await SendTextAsync(client, chatGptLink, cancellationToken);
            }

            TelegramLog.Sent(_logger, listing.Id);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            throw TelegramErrorMapper.FromNetwork(ex);
        }
    }

    public async Task NotifyAvitoAuthRequiredAsync(
        string message,
        CancellationToken cancellationToken = default
    )
    {
        if (!_options.Enabled || !HasCredentials())
        {
            return;
        }

        LogSocks5Once();

        var client = _httpClientFactory.CreateClient("Telegram");
        try
        {
            await SendTextAsync(client, message.Trim(), cancellationToken);
            TelegramLog.AuthRequiredSent(_logger);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            TelegramLog.SendFailed(_logger, 0, "напоминание о входе в Avito: " + ex.Message);
        }
    }

    public async Task NotifyAvitoAuthSucceededAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || !HasCredentials())
        {
            return;
        }

        LogSocks5Once();

        var client = _httpClientFactory.CreateClient("Telegram");
        try
        {
            await SendTextAsync(client, "Вход в Avito выполнен.", cancellationToken);
            TelegramLog.AuthSucceededSent(_logger);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            TelegramLog.SendFailed(_logger, 0, "уведомление об успешном входе: " + ex.Message);
        }
    }

    public async Task NotifyApplicationClosedAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _applicationClosedNotified, 1) == 1)
        {
            return;
        }

        if (!_options.Enabled || !HasCredentials())
        {
            return;
        }

        LogSocks5Once();

        var client = _httpClientFactory.CreateClient("Telegram");
        try
        {
            await SendTextAsync(client, "Приложение закрыто.", cancellationToken);
            TelegramLog.ApplicationClosedSent(_logger);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            TelegramLog.SendFailed(_logger, 0, "уведомление о закрытии приложения: " + ex.Message);
        }
    }

    public async Task NotifySearchTimeoutAsync(
        string screenshotPath,
        string? pageUrl = null,
        CancellationToken cancellationToken = default
    )
    {
        await NotifyScreenshotAsync(
            screenshotPath,
            BuildScreenshotCaption(
                "Avito: таймаут загрузки результатов поиска",
                pageUrl
            ),
            "скриншот таймаута поиска",
            TelegramLog.SearchTimeoutSent,
            cancellationToken
        );
    }

    public async Task NotifyCaptchaRequiredAsync(
        string screenshotPath,
        string? pageUrl = null,
        CancellationToken cancellationToken = default
    )
    {
        await NotifyScreenshotAsync(
            screenshotPath,
            BuildScreenshotCaption(
                "Avito: нужна капча.\n"
                    + "Пройдите её вручную в окне браузера агента.\n"
                    + "После этого поиск продолжится сам.",
                pageUrl
            ),
            "скриншот капчи",
            TelegramLog.CaptchaRequiredSent,
            cancellationToken
        );
    }

    private async Task NotifyScreenshotAsync(
        string screenshotPath,
        string caption,
        string errorLabel,
        Action<ILogger> onSent,
        CancellationToken cancellationToken
    )
    {
        if (!_options.Enabled || !HasCredentials())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(screenshotPath) || !File.Exists(screenshotPath))
        {
            return;
        }

        LogSocks5Once();

        var client = _httpClientFactory.CreateClient("Telegram");
        try
        {
            await using var stream = File.OpenRead(screenshotPath);
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(_options.ChatId), "chat_id");
            form.Add(new StringContent(TruncateForCaption(caption)), "caption");

            var file = new StreamContent(stream);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, "photo", Path.GetFileName(screenshotPath));

            await PostFormAsync(client, $"{BotApiRoot()}/sendPhoto", form, cancellationToken);
            onSent(_logger);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TelegramUnavailableException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            throw TelegramErrorMapper.FromNetwork(ex);
        }
        catch (Exception ex)
        {
            TelegramLog.SendFailed(_logger, 0, errorLabel + ": " + ex.Message);
        }
    }

    private static string BuildScreenshotCaption(string head, string? pageUrl)
    {
        if (string.IsNullOrWhiteSpace(pageUrl))
        {
            return head;
        }

        return head + "\n" + pageUrl.Trim();
    }

    private Task<IReadOnlyList<ListingPhoto>> ResolvePhotosAsync(Listing listing)
    {
        // Исходные байты без сжатия - в Telegram уходят как document (не sendPhoto).
        var fromBytes = listing
            .Images.Where(photo =>
                photo.Content.Length > 0 && photo.Content.Length <= MaxDocumentBytes
            )
            .OrderBy(photo => photo.SortOrder)
            .Take(MaxPhotos)
            .ToList();

        if (fromBytes.Count == 0 && listing.ImageUrls.Count > 0)
        {
            TelegramLog.PhotosMissing(_logger, listing.Id, listing.ImageUrls.Count);
        }

        return Task.FromResult<IReadOnlyList<ListingPhoto>>(fromBytes);
    }

    private async Task<bool> SendTextAsync(
        HttpClient client,
        string message,
        CancellationToken cancellationToken,
        object? replyMarkup = null
    )
    {
        object payload =
            replyMarkup is null
                ? new
                {
                    chat_id = _options.ChatId,
                    text = message,
                    disable_web_page_preview = true,
                }
                : new
                {
                    chat_id = _options.ChatId,
                    text = message,
                    disable_web_page_preview = true,
                    reply_markup = replyMarkup,
                };

        using var response = await client.PostAsJsonAsync(
            $"{BotApiRoot()}/sendMessage",
            payload,
            cancellationToken
        );
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        TelegramLog.SendFailed(_logger, (int)response.StatusCode, TrimBody(body));
        throw TelegramErrorMapper.FromHttp((int)response.StatusCode, body);
    }

    private async Task SendPhotosAsync(
        HttpClient client,
        IReadOnlyList<ListingPhoto> photos,
        string? caption,
        CancellationToken cancellationToken
    )
    {
        // До MaxPhotos включительно. Альбом (photo) для 2+, иначе sendDocument по одному.
        // sendMediaGroup+document часто даёт «Wrong file identifier».
        IReadOnlyList<ListingPhoto> toSend =
            photos.Count <= MaxPhotos ? photos : [.. photos.Take(MaxPhotos)];

        if (toSend.Count >= 2 && await TrySendPhotoAlbumAsync(client, toSend, caption, cancellationToken))
        {
            return;
        }

        var sent = 0;
        TelegramUnavailableException? lastError = null;

        for (var i = 0; i < toSend.Count; i++)
        {
            try
            {
                using var form = new MultipartFormDataContent();
                form.Add(new StringContent(_options.ChatId), "chat_id");

                var fileName = ResolveFileName(toSend[i], $"photo_{i + 1}");
                AddPhotoPart(form, toSend[i], "document", fileName);

                if (i == 0 && !string.IsNullOrEmpty(caption))
                {
                    form.Add(new StringContent(TruncateForCaption(caption)), "caption");
                }

                await PostFormAsync(client, $"{BotApiRoot()}/sendDocument", form, cancellationToken);
                sent++;
            }
            catch (TelegramUnavailableException ex)
            {
                lastError = ex;
            }
        }

        if (sent == 0 && lastError is not null)
        {
            throw lastError;
        }
    }

    private async Task<bool> TrySendPhotoAlbumAsync(
        HttpClient client,
        IReadOnlyList<ListingPhoto> photos,
        string? caption,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(_options.ChatId), "chat_id");

            var captionForFile = string.IsNullOrEmpty(caption)
                ? null
                : TruncateForCaption(caption);
            var media = new List<InputMediaPhoto>(photos.Count);

            for (var i = 0; i < photos.Count; i++)
            {
                var attachName = $"photo_{i + 1}";
                var fileName = ResolveFileName(photos[i], attachName);
                AddPhotoPart(form, photos[i], attachName, fileName, asImage: true);
                media.Add(
                    new InputMediaPhoto
                    {
                        Type = "photo",
                        Media = $"attach://{attachName}",
                        Caption = i == 0 ? captionForFile : null,
                    }
                );
            }

            form.Add(new StringContent(JsonSerializer.Serialize(media, MediaJson)), "media");
            await PostFormAsync(client, $"{BotApiRoot()}/sendMediaGroup", form, cancellationToken);
            return true;
        }
        catch (TelegramUnavailableException)
        {
            return false;
        }
    }

    private async Task PostFormAsync(
        HttpClient client,
        string url,
        MultipartFormDataContent form,
        CancellationToken cancellationToken
    )
    {
        using var response = await client.PostAsync(url, form, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        TelegramLog.SendFailed(_logger, (int)response.StatusCode, TrimBody(body));
        throw TelegramErrorMapper.FromHttp((int)response.StatusCode, body);
    }

    private static void AddPhotoPart(
        MultipartFormDataContent form,
        ListingPhoto photo,
        string name,
        string fileName,
        bool asImage = false
    )
    {
        var content = new ByteArrayContent(photo.Content);
        content.Headers.ContentType = new MediaTypeHeaderValue(
            asImage ? ResolveContentType(photo.ContentType) : "application/octet-stream"
        );
        form.Add(content, name, fileName);
    }

    internal static string ResolveContentType(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return "image/jpeg";
        }

        var trimmed = contentType.Trim();
        return trimmed.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : "image/jpeg";
    }

    internal static string ResolveFileName(ListingPhoto photo, string stem)
    {
        var type = ResolveContentType(photo.ContentType);
        var ext = type switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            _ => ".jpg",
        };
        return stem + ext;
    }

    private string? TryBuildChatGptPhotoCheckLink(IReadOnlyList<string> imageUrls)
    {
        var urls = imageUrls
            .Where(static url =>
                !string.IsNullOrWhiteSpace(url)
                && (
                    url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                )
            )
            .Select(static url => url.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(MaxPhotos)
            .ToList();

        if (urls.Count == 0)
        {
            return null;
        }

        var promptBase = string.IsNullOrWhiteSpace(_options.ChatGptPhotoCheckPrompt)
            ? "Проверь подлинность товара по фотографиям"
            : _options.ChatGptPhotoCheckPrompt.Trim();

        for (var take = urls.Count; take >= 1; take--)
        {
            var prompt = promptBase + "\n\n" + string.Join("\n", urls.Take(take));
            if (prompt.Length > ChatGptPromptMaxLength)
            {
                continue;
            }

            var link =
                "https://chatgpt.com/?hints=search&q=" + Uri.EscapeDataString(prompt);
            if (link.Length <= MessageLimit)
            {
                return "Проверить в ChatGPT:\n" + link;
            }
        }

        return null;
    }

    internal static string BuildMessage(Listing listing)
    {
        var builder = new StringBuilder();
        builder.AppendLine(listing.Title.Trim());
        builder.AppendLine(FormatPrice(listing));
        builder.AppendLine(FormatAddress(listing));

        if (!string.IsNullOrWhiteSpace(listing.Description))
        {
            builder.AppendLine();
            builder.AppendLine(TrimDescription(listing.Description));
        }

        if (!string.IsNullOrWhiteSpace(listing.Url))
        {
            builder.AppendLine();
            builder.AppendLine(listing.Url.Trim());
        }

        return builder.ToString().Trim();
    }

    internal static string FormatAddress(Listing listing) =>
        string.IsNullOrWhiteSpace(listing.Location) ? "не указан" : listing.Location.Trim();

    internal static string FormatPrice(Listing listing)
    {
        if (!listing.Price.HasValue)
        {
            return "не указана";
        }

        var amount = listing.Price.Value.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"));
        var currency = string.Equals(listing.Currency, "RUB", StringComparison.OrdinalIgnoreCase)
            ? "руб."
            : listing.Currency;

        return $"{amount} {currency}";
    }

    internal static string TrimDescription(string description)
    {
        var text = description.ReplaceLineEndings("\n").Trim();
        const int max = 1500;
        return text.Length <= max ? text : text[..max].TrimEnd() + "…";
    }

    /// <summary>
    /// Лимит caption в Telegram Bot API - 1024 символа.
    /// Ссылка на объявление всегда в конце; обрезается только текст перед ней.
    /// </summary>
    internal static string TruncateForCaption(string text, string? listingUrl = null)
    {
        var trimmed = text.Trim();
        var url = listingUrl?.Trim();
        if (string.IsNullOrEmpty(url))
        {
            return TruncateToLimit(trimmed, CaptionLimit);
        }

        // Убираем URL из хвоста сообщения - добавим его после обрезки.
        var body = trimmed;
        if (body.EndsWith(url, StringComparison.Ordinal))
        {
            body = body[..^url.Length].TrimEnd();
        }

        var suffix = string.IsNullOrEmpty(body) ? url : "\n\n" + url;
        if (suffix.Length >= CaptionLimit)
        {
            return TruncateToLimit(url, CaptionLimit);
        }

        var budget = CaptionLimit - suffix.Length;
        if (body.Length <= budget)
        {
            return string.IsNullOrEmpty(body) ? url : body + suffix;
        }

        return TruncateToLimit(body, budget) + suffix;
    }

    internal static string TruncateToLimit(string text, int limit)
    {
        if (text.Length <= limit)
        {
            return text;
        }

        if (limit <= 1)
        {
            return text[..limit];
        }

        return text[..(limit - 1)].TrimEnd() + "…";
    }

    internal static string[] SplitText(string text, int limit)
    {
        if (text.Length <= limit)
        {
            return [text];
        }

        var parts = new List<string>();
        var offset = 0;
        while (offset < text.Length)
        {
            var length = Math.Min(limit, text.Length - offset);
            if (offset + length < text.Length)
            {
                var split = text.LastIndexOf('\n', offset + length - 1, length);
                if (split > offset)
                {
                    length = split - offset + 1;
                }
            }

            parts.Add(text.Substring(offset, length).Trim());
            offset += length;
        }

        return [.. parts.Where(static part => part.Length > 0)];
    }

    private void LogSocks5Once()
    {
        if (
            _options.Socks5 is { IsConfigured: true }
            && Interlocked.Exchange(ref _socks5Logged, 1) == 0
        )
        {
            // без отдельного лог-метода - избегаем шума при каждой отправке
        }
    }

    private string BotApiRoot() => $"https://api.telegram.org/bot{_options.BotToken}";

    private static string TrimBody(string body)
    {
        var text = body.ReplaceLineEndings(" ").Trim();
        return text.Length <= 300 ? text : text[..300] + "…";
    }

    private bool HasCredentials() =>
        !string.IsNullOrWhiteSpace(_options.BotToken)
        && !string.IsNullOrWhiteSpace(_options.ChatId);

    private sealed class InputMediaPhoto
    {
        public string Type { get; init; } = "photo";

        public string Media { get; init; } = string.Empty;

        public string? Caption { get; init; }
    }
}

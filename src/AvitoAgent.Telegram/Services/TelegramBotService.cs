using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AvitoAgent.Core;
using AvitoAgent.Core.Interfaces;
using AvitoAgent.Core.Models;
using AvitoAgent.Shared.Configuration;
using AvitoAgent.Telegram.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AvitoAgent.Telegram.Services;

public sealed partial class TelegramBotService(
    IHttpClientFactory httpClientFactory,
    IParseSession session,
    IAvitoAuthControl avitoAuth,
    ILocationCatalog locations,
    IOptions<TelegramOptions> options,
    ILogger<TelegramBotService> logger
) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IParseSession _session = session;
    private readonly IAvitoAuthControl _avitoAuth = avitoAuth;
    private readonly ILocationCatalog _locations = locations;
    private readonly TelegramOptions _options = options.Value;
    private readonly ILogger<TelegramBotService> _logger = logger;
    private ControlPrompt _prompt;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (
            !_options.Enabled
            || string.IsNullOrWhiteSpace(_options.BotToken)
            || string.IsNullOrWhiteSpace(_options.ChatId)
        )
        {
            return;
        }

        var client = _httpClientFactory.CreateClient("Telegram");
        long offset = 0;
        var started = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!started)
                {
                    offset = await BootstrapUpdatesAsync(client, stoppingToken);
                    await SendTextAsync(
                        client,
                        "Агент запущен, управляйте агентом через это меню.",
                        null,
                        stoppingToken
                    );
                    TelegramLog.BotStarted(_logger);
                    started = true;
                }

                var updates = await GetUpdatesAsync(
                    client,
                    offset,
                    timeoutSeconds: 50,
                    stoppingToken
                );
                foreach (var update in updates)
                {
                    offset = update.UpdateId + 1;
                    await HandleUpdateAsync(client, update, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                TelegramLog.BotPollFailed(_logger, TelegramErrorMapper.DescribeNetwork(ex));
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleUpdateAsync(
        HttpClient client,
        TelegramUpdate update,
        CancellationToken cancellationToken
    )
    {
        if (update.CallbackQuery is { } callback)
        {
            if (!IsAllowedChat(callback.Message?.Chat.Id))
            {
                return;
            }

            await AnswerCallbackAsync(client, callback.Id, cancellationToken);
            await HandleCallbackAsync(client, callback.Data, cancellationToken);
            return;
        }

        var message = update.Message;
        if (
            message is null
            || !IsAllowedChat(message.Chat.Id)
            || string.IsNullOrWhiteSpace(message.Text)
        )
        {
            return;
        }

        var (text, markup) = await HandleMessageAsync(message.Text.Trim(), cancellationToken);
        if (!string.IsNullOrWhiteSpace(text))
        {
            await SendTextAsync(client, text, markup, cancellationToken);
        }

        if (_prompt is ControlPrompt.Location)
        {
            await SendSlugCatalogAsync(client, cancellationToken);
        }
    }

    private async Task<(string Text, object? Markup)> HandleMessageAsync(
        string text,
        CancellationToken cancellationToken
    )
    {
        if (_prompt is not ControlPrompt.None && !IsMenuButton(text) && !text.StartsWith('/'))
        {
            return ApplyPrompt(text);
        }

        _prompt = ControlPrompt.None;
        var command = text.TrimStart('/').ToLowerInvariant();

        return command switch
        {
            "start" or "старт" => Start(),
            "stop" or "стоп" => Stop(),
            "status" or "статус" or "help" or "помощь" => (FormatStatus(), null),
            "запрос" => Ask(ControlPrompt.Keywords, "Пришлите поисковые запросы через запятую."),
            "присоединить к запросам" => Ask(
                ControlPrompt.AppendKeywords,
                "Пришлите запросы для добавления через запятую."
            ),
            "исключения" => Ask(
                ControlPrompt.Excluded,
                "Пришлите слова-исключения через запятую. «-» - очистить."
            ),
            "регион" => Ask(
                ControlPrompt.Location,
                "Пришлите регион или несколько регионов через запятую."
            ),
            "цена" => Ask(
                ControlPrompt.Price,
                "Пришлите цену: 1000-50000, от 1000, до 50000. 0 - без границ."
            ),
            "сортировка" => ("Выберите сортировку:", TelegramControlUi.SortKeyboard()),
            "доставка" => ("Фильтр доставки:", TelegramControlUi.DeliveryKeyboard()),
            "состояние" => ("Состояние товара:", TelegramControlUi.ConditionKeyboard()),
            "продавец" => ("Тип продавца:", TelegramControlUi.SellerKeyboard()),
            "дата" => ("Фильтр по дате публикации:", TelegramControlUi.FromDateKeyboard()),
            "интервал" => Ask(ControlPrompt.Interval, "Укажите интервал опроса в минутах."),
            "сон" => Ask(
                ControlPrompt.Sleep,
                "Укажите часы сна, например 23-7, или «-» для отключения."
            ),
            "авторизация" => await FormatAvitoAuthAsync(cancellationToken),
            _ => TrySetLocationFromFreeText(text),
        };
    }

    private async Task HandleCallbackAsync(
        HttpClient client,
        string? data,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return;
        }

        if (data is "auth:status")
        {
            var (text, markup) = await FormatAvitoAuthAsync(cancellationToken);
            await SendTextAsync(client, text, markup, cancellationToken);
            return;
        }

        var reply = HandleCallback(data);
        if (!string.IsNullOrWhiteSpace(reply))
        {
            await SendTextAsync(client, reply, null, cancellationToken);
        }
    }

    private string HandleCallback(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return string.Empty;
        }

        switch (data)
        {
            case "sort:default":
                _session.SetSort("По умолчанию");
                break;
            case "sort:cheap":
                _session.SetSort("Дешевле");
                break;
            case "sort:exp":
                _session.SetSort("Дороже");
                break;
            case "sort:date":
                _session.SetSort("По дате");
                break;
            case "sort:discount":
                _session.SetSort("По размеру скидки");
                break;
            case "del:1":
                _session.SetDeliveryOnly(true);
                break;
            case "del:0":
                _session.SetDeliveryOnly(false);
                break;
            case "cond:all":
                _session.SetCondition("Все");
                break;
            case "cond:new":
                _session.SetCondition("Новое");
                break;
            case "cond:used":
                _session.SetCondition("Б/у");
                break;
            case "seller:all":
                _session.SetSellerType("Все");
                break;
            case "seller:priv":
                _session.SetSellerType("Частные");
                break;
            case "seller:co":
                _session.SetSellerType("Компании");
                break;
            case "from:1":
                _session.SetFromCurrentDateTime(true);
                break;
            case "from:0":
                _session.SetFromCurrentDateTime(false);
                break;
            default:
                return "Неизвестная команда.";
        }

        return FormatStatus();
    }

    private async Task<(string Text, object? Markup)> FormatAvitoAuthAsync(
        CancellationToken cancellationToken
    )
    {
        var status = await _avitoAuth.GetStatusAsync(cancellationToken);
        var builder = new StringBuilder();
        builder.AppendLine("Авторизация Avito");
        builder.AppendLine();
        builder.AppendLine(status.Message);
        if (status.State is AvitoAuthState.NotAuthenticated or AvitoAuthState.LoginInProgress)
        {
            builder.AppendLine();
            builder.Append("Войдите вручную в открытом браузере агента.");
        }

        return (builder.ToString(), TelegramControlUi.AvitoAuthKeyboard());
    }

    private (string Text, object? Markup) Start()
    {
        if (!_session.TryStart(out var error))
        {
            return (error ?? "Не удалось запустить поиск.", null);
        }

        return ("Поиск запущен.\n\n" + FormatStatus(), null);
    }

    private (string Text, object? Markup) Stop()
    {
        _session.Stop();
        return ("Поиск остановлен.", null);
    }

    private (string Text, object? Markup) TrySetLocationFromFreeText(string text)
    {
        if (TryParseLocations(text, out var slugs, out _))
        {
            _session.SetLocations(slugs);
            return ($"Регионы сохранены ({slugs.Count}).\n\n" + FormatStatus(), null);
        }

        return (FormatStatus() + "\n\nКнопки ниже меняют параметры поиска.", null);
    }

    private (string Text, object? Markup) Ask(ControlPrompt prompt, string hint)
    {
        _prompt = prompt;
        return (hint, null);
    }

    private (string Text, object? Markup) ApplyPrompt(string text)
    {
        var prompt = _prompt;
        _prompt = ControlPrompt.None;

        switch (prompt)
        {
            case ControlPrompt.Keywords:
                var keywords = SplitPhrases(text);
                if (keywords.Length == 0)
                {
                    return ("Нужна хотя бы одна фраза.", null);
                }

                _session.SetKeywords(keywords);
                return ("Запрос сохранён.\n\n" + FormatStatus(), null);

            case ControlPrompt.AppendKeywords:
                var appended = SplitPhrases(text);
                if (appended.Length == 0)
                {
                    return ("Нужна хотя бы одна фраза.", null);
                }

                _session.AppendKeywords(appended);
                return ("Запросы добавлены.\n\n" + FormatStatus(), null);

            case ControlPrompt.Excluded:
                _session.SetExcludedKeywords(
                    IsClearListToken(text) ? [] : SplitPhrases(text)
                );
                return ("Исключения сохранены.\n\n" + FormatStatus(), null);

            case ControlPrompt.Location:
                if (!TryParseLocations(text, out var slugs, out var error))
                {
                    return (error, null);
                }

                _session.SetLocations(slugs);
                return ($"Регионы сохранены ({slugs.Count}).\n\n" + FormatStatus(), null);

            case ControlPrompt.Price:
                if (!TryParsePrice(text, out var min, out var max, out var priceError))
                {
                    return (priceError, null);
                }

                _session.SetPrice(min, max);
                return ("Цена сохранена.\n\n" + FormatStatus(), null);

            case ControlPrompt.Interval:
                if (!TryParseIntervalMinutes(text, out var minutes, out var intervalError))
                {
                    return (intervalError, null);
                }

                _session.SetPollingIntervalMinutes(minutes);
                return ($"Интервал сохранён: {minutes} мин.\n\n" + FormatStatus(), null);

            case ControlPrompt.Sleep:
                if (
                    !TryParseSleepHours(
                        text,
                        out var sleepFrom,
                        out var sleepTo,
                        out var sleepError
                    )
                )
                {
                    return (sleepError, null);
                }

                _session.SetSleepHours(sleepFrom, sleepTo);
                return (FormatSleepSaved(sleepFrom, sleepTo) + "\n\n" + FormatStatus(), null);

            default:
                return (FormatStatus(), null);
        }
    }

    private bool TryParseLocations(string text, out IReadOnlyList<string> slugs, out string error)
    {
        slugs = [];
        error = string.Empty;
        var parsed = new List<string>();
        foreach (
            var part in text.Split(
                [',', ';', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            if (!_locations.TryResolve(part, out var slug, out _))
            {
                error =
                    $"Не найден регион «{part}». Пример: Мытищи, tver, Московская область, rossiya.";
                return false;
            }

            if (!parsed.Contains(slug, StringComparer.OrdinalIgnoreCase))
            {
                parsed.Add(slug);
            }
        }

        if (parsed.Count == 0)
        {
            error = "Укажите хотя бы один регион.";
            return false;
        }

        slugs = parsed;
        return true;
    }

    internal static bool TryParsePrice(string text, out int min, out int max, out string error)
    {
        min = 0;
        max = 0;
        error = string.Empty;
        var raw = text.Trim().ToLowerInvariant().Replace('ё', 'е');
        if (IsClearListToken(text) || raw is "любая" or "без границ")
        {
            return true;
        }

        raw = raw.Replace("от", " ", StringComparison.Ordinal)
            .Replace("до", " ", StringComparison.Ordinal)
            .Replace("\u2014", "-")
            .Replace("\u2013", "-");

        var numbers = new List<int>();
        foreach (
            var token in raw.Split([' ', '-', '/', '\t'], StringSplitOptions.RemoveEmptyEntries)
        )
        {
            var digits = new string([.. token.Where(char.IsDigit)]);
            if (digits.Length == 0)
            {
                continue;
            }

            if (
                !int.TryParse(
                    digits,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var value
                )
            )
            {
                error = "Не удалось прочитать цену.";
                return false;
            }

            numbers.Add(value);
        }

        if (numbers.Count == 0)
        {
            error = "Укажите цену, например 1000-50000.";
            return false;
        }

        if (numbers.Count == 1)
        {
            if (text.Contains("до", StringComparison.OrdinalIgnoreCase))
            {
                max = numbers[0];
            }
            else
            {
                min = numbers[0];
            }

            return true;
        }

        min = Math.Min(numbers[0], numbers[1]);
        max = Math.Max(numbers[0], numbers[1]);
        return true;
    }

    internal static bool TryParseIntervalMinutes(string text, out int minutes, out string error)
    {
        minutes = 0;
        error = string.Empty;
        var raw = text.Trim().ToLowerInvariant().Replace('ё', 'е');

        // Сначала длинные формы, иначе «мин» съест начало «минут» → «5 ут».
        foreach (
            var token in new[]
            {
                "минуты",
                "минуту",
                "минут",
                "мин.",
                "мин",
                "m",
            }
        )
        {
            if (raw.EndsWith(token, StringComparison.Ordinal))
            {
                raw = raw[..^token.Length].Trim();
                break;
            }
        }

        raw = raw.Trim(' ', '.', ',', ':');

        if (
            !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes)
            || minutes < 1
        )
        {
            error = "Укажите целое число минут от 1. Пример: 10 или 10 мин";
            return false;
        }

        return true;
    }

    internal static bool TryParseSleepHours(
        string text,
        out int? fromHour,
        out int? toHour,
        out string error
    )
    {
        fromHour = null;
        toHour = null;
        error = string.Empty;
        var raw = text.Trim().ToLowerInvariant().Replace('ё', 'е');
        if (
            IsClearListToken(text)
            || raw is "выкл" or "выключить" or "off"
        )
        {
            return true;
        }

        var parts = SleepHoursRegex().Match(raw);

        if (
            !parts.Success
            || !TryParseHour(parts.Groups[1].Value, out var from)
            || !TryParseHour(parts.Groups[2].Value, out var to)
        )
        {
            error = "Укажите два часа 0-23: 23-7 или 1 8. «-» - выключить сон.";
            return false;
        }

        if (from == to)
        {
            error = "Часы «от» и «до» не должны совпадать. Для отключения пришлите «-».";
            return false;
        }

        fromHour = from;
        toHour = to;
        return true;
    }

    private static bool TryParseHour(string text, out int hour)
    {
        hour = 0;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out hour))
        {
            return false;
        }

        return hour is >= 0 and <= 23;
    }

    internal static string FormatSleepSaved(int? fromHour, int? toHour) =>
        SleepSchedule.IsConfigured(fromHour, toHour)
            ? $"Сон сохранён: {fromHour!.Value:00}:00-{toHour!.Value:00}:00"
            : "Сон выключен";

    internal static string FormatSleep(ParseSettings settings) =>
        SleepSchedule.IsConfigured(settings.SleepFromHour, settings.SleepToHour)
            ? $"{settings.SleepFromHour!.Value:00}:00-{settings.SleepToHour!.Value:00}:00"
            : "выкл";

    internal static string[] SplitPhrases(string text) =>
        [
            .. text.Split(
                    [',', ';', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
                .Where(static phrase => !IsClearListToken(phrase)),
        ];

    /// <summary>
    /// «-», «нет», «0» - очистить список (исключения и т.п.).
    /// </summary>
    internal static bool IsClearListToken(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var raw = text.Trim()
            .ToLowerInvariant()
            .Replace('ё', 'е')
            .Replace('\u2014', '-')
            .Replace('\u2013', '-')
            .Replace('\u2212', '-');

        return raw is "-" or "нет" or "0";
    }

    [GeneratedRegex(@"^\s*(\d{1,2})\s*(?:[-\u2013\u2014:]|\s+)\s*(\d{1,2})\s*$")]
    private static partial Regex SleepHoursRegex();

    private static bool IsMenuButton(string text) =>
        text
            is "Старт"
                or "Стоп"
                or "Статус"
                or "Запрос"
                or "Присоединить к запросам"
                or "Исключения"
                or "Регион"
                or "Цена"
                or "Сортировка"
                or "Доставка"
                or "Состояние"
                or "Продавец"
                or "Дата"
                or "Авторизация"
                or "Интервал"
                or "Сон";

    private string FormatStatus()
    {
        var settings = _session.Snapshot();
        var builder = new StringBuilder();
        builder.AppendLine(_session.IsRunning ? "Поиск: идёт" : "Поиск: остановлен");
        builder.AppendLine();
        builder.AppendLine("Запрос: " + JoinOrDash(settings.Keywords));
        builder.AppendLine("Исключения: " + JoinOrDash(settings.ExcludedKeywords));
        builder.AppendLine("Регионы: " + FormatLocations(settings.LocationSlugs));
        builder.AppendLine("Цена: " + FormatPrice(settings));
        builder.AppendLine("Сортировка: " + settings.Sort);
        builder.AppendLine(
            "Доставка: " + (settings.DeliveryOnly ? "только с Авито Доставкой" : "любые")
        );
        builder.AppendLine("Состояние: " + settings.Condition);
        builder.AppendLine("Продавец: " + settings.SellerType);
        builder.AppendLine("Дата: " + FormatFromDate(settings));
        builder.AppendLine(
            "Интервал между циклами: " + Math.Max(1, settings.PollingIntervalMinutes) + " мин"
        );
        builder.Append("Сон: " + FormatSleep(settings));
        return builder.ToString();
    }

    private string FormatFromDate(ParseSettings settings)
    {
        if (!settings.FromCurrentDateTime)
        {
            return "все";
        }

        var after = _session.PublishedAfterUtc;
        if (after is null)
        {
            return "с момента Старт";
        }

        var local = after.Value.ToLocalTime();
        return "с " + local.ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("ru-RU"));
    }

    private string FormatLocations(IReadOnlyList<string> slugs) =>
        slugs.Count == 0 ? "-" : string.Join(", ", slugs.Select(_locations.DisplayName));

    internal static string FormatPrice(ParseSettings settings)
    {
        if (settings.MinPrice <= 0 && settings.MaxPrice <= 0)
        {
            return "любая";
        }

        if (settings.MinPrice > 0 && settings.MaxPrice > 0)
        {
            return $"{settings.MinPrice}-{settings.MaxPrice} ₽";
        }

        return settings.MinPrice > 0 ? $"от {settings.MinPrice} ₽" : $"до {settings.MaxPrice} ₽";
    }

    internal static string JoinOrDash(IReadOnlyList<string> values) =>
        values.Count == 0 ? "-" : string.Join(", ", values);

    private bool IsAllowedChat(long? chatId)
    {
        if (chatId is null)
        {
            return false;
        }

        return chatId
            .Value.ToString(CultureInfo.InvariantCulture)
            .Equals(_options.ChatId.Trim(), StringComparison.Ordinal);
    }

    private async Task<long> BootstrapUpdatesAsync(
        HttpClient client,
        CancellationToken cancellationToken
    )
    {
        // Старые текстовые команды пропускаем, но callback «Войти» обрабатываем -
        // иначе нажатие до старта бота теряется, а кнопка в Telegram «висит».
        var pending = await GetUpdatesAsync(
            client,
            offset: 0,
            timeoutSeconds: 0,
            cancellationToken
        );
        long offset = 0;
        foreach (var update in pending)
        {
            offset = update.UpdateId + 1;
            if (update.CallbackQuery is null)
            {
                continue;
            }

            try
            {
                await HandleUpdateAsync(client, update, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                TelegramLog.BotPollFailed(_logger, "обработка отложенного callback: " + ex.Message);
            }
        }

        return offset;
    }

    private async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(
        HttpClient client,
        long offset,
        int timeoutSeconds,
        CancellationToken cancellationToken
    )
    {
        var url = $"{BotApiRoot()}/getUpdates";
        var payload = new
        {
            offset,
            timeout = timeoutSeconds,
            allowed_updates = new[] { "message", "callback_query" },
        };

        using var response = await client.PostAsJsonAsync(url, payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode || !TelegramErrorMapper.IsOk(body))
        {
            throw TelegramErrorMapper.FromHttp((int)response.StatusCode, body);
        }

        var parsed = JsonSerializer.Deserialize<TelegramUpdatesResponse>(body, Json);
        return parsed?.Result ?? [];
    }

    private async Task SendTextAsync(
        HttpClient client,
        string text,
        object? extraMarkup,
        CancellationToken cancellationToken
    )
    {
        var url = $"{BotApiRoot()}/sendMessage";
        var payload = new
        {
            chat_id = _options.ChatId,
            text,
            reply_markup = extraMarkup ?? TelegramControlUi.ReplyKeyboard,
        };

        using var response = await client.PostAsJsonAsync(url, payload, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        TelegramLog.SendFailed(_logger, (int)response.StatusCode, TrimBody(body));
    }

    private async Task SendSlugCatalogAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(_locations.FormatSlugCatalog());
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(_options.ChatId), "chat_id");
        form.Add(new StringContent("Полный список LocationSlug"), "caption");
        form.Add(new StringContent(TelegramControlUi.ReplyKeyboardJson), "reply_markup");

        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain")
        {
            CharSet = "utf-8",
        };
        form.Add(file, "document", "LocationSlug.txt");

        using var response = await client.PostAsync(
            $"{BotApiRoot()}/sendDocument",
            form,
            cancellationToken
        );
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        TelegramLog.SendFailed(_logger, (int)response.StatusCode, TrimBody(body));
    }

    private async Task AnswerCallbackAsync(
        HttpClient client,
        string callbackId,
        CancellationToken cancellationToken
    )
    {
        var url = $"{BotApiRoot()}/answerCallbackQuery";
        object payload = new { callback_query_id = callbackId };

        using var _ = await client.PostAsJsonAsync(url, payload, cancellationToken);
    }

    private string BotApiRoot() => $"https://api.telegram.org/bot{_options.BotToken}";

    private static string TrimBody(string body)
    {
        var text = body.ReplaceLineEndings(" ").Trim();
        return text.Length <= 300 ? text : text[..300] + "…";
    }

    private enum ControlPrompt
    {
        None,
        Keywords,
        AppendKeywords,
        Excluded,
        Location,
        Price,
        Interval,
        Sleep,
    }

    private sealed class TelegramUpdatesResponse
    {
        public List<TelegramUpdate> Result { get; set; } = [];
    }

    private sealed class TelegramUpdate
    {
        [JsonPropertyName("update_id")]
        public long UpdateId { get; set; }

        public TelegramMessage? Message { get; set; }

        [JsonPropertyName("callback_query")]
        public TelegramCallbackQuery? CallbackQuery { get; set; }
    }

    private sealed class TelegramCallbackQuery
    {
        public string Id { get; set; } = string.Empty;

        public string? Data { get; set; }

        public TelegramMessage? Message { get; set; }
    }

    private sealed class TelegramMessage
    {
        public string? Text { get; set; }

        public TelegramChat Chat { get; set; } = new();
    }

    private sealed class TelegramChat
    {
        public long Id { get; set; }
    }
}

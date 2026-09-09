namespace AvitoAgent.Shared.Configuration;

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    /// <summary>
    /// Включить рассылку. Нужны также заполненные BotToken и ChatId.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Управление поиском из Telegram (клавиатура, Старт/Стоп, фильтры).
    /// false — только уведомления, параметры берутся из appsettings.
    /// </summary>
    public bool ControlEnabled { get; set; } = true;

    public bool IsControlActive => Enabled && ControlEnabled;

    public string BotToken { get; set; } = string.Empty;

    public string ChatId { get; set; } = string.Empty;

    /// <summary>
    /// SOCKS5 только для Telegram API. Не связан с общим Socks5 приложения.
    /// </summary>
    public Socks5Options Socks5 { get; set; } = new();

    /// <summary>
    /// IP или имя адаптера для исходящих запросов Telegram API. Пусто — по умолчанию ОС.
    /// </summary>
    public string NetworkInterface { get; set; } = string.Empty;

    /// <summary>
    /// В уведомлении добавлять ссылку на ChatGPT с промптом и прямыми URL фото.
    /// </summary>
    public bool ChatGptPhotoCheckEnabled { get; set; }

    /// <summary>
    /// Текст промпта в ссылке ChatGPT (к нему дописываются URL фото).
    /// </summary>
    public string ChatGptPhotoCheckPrompt { get; set; } =
        "Проверь подлинность товара по фотографиям";

    /// <summary>
    /// Сколько фото прикреплять к уведомлению (1–10). Telegram album: максимум 10.
    /// </summary>
    public int MaxPhotos { get; set; } = 5;
}

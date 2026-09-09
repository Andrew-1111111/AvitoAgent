namespace AvitoAgent.Shared.Configuration;

/// <summary>
/// Исходящий сетевой интерфейс приложения (HTTP Avito/LM Studio и т.п., кроме Telegram).
/// </summary>
public sealed class NetworkOptions
{
    public const string SectionName = "Network";

    /// <summary>
    /// IP или имя адаптера (Ethernet, Wi-Fi, Ethernet 2). Пусто - интерфейс по умолчанию ОС.
    /// </summary>
    public string Interface { get; set; } = string.Empty;
}

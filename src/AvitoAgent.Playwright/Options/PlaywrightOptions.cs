namespace AvitoAgent.Playwright.Options;

/// <summary>
/// Root Playwright configuration.
/// </summary>
public sealed class PlaywrightOptions
{
    public const string SectionName = "Playwright";

    /// <summary>
    /// Browser launch options.
    /// </summary>
    public LaunchOptions Launch { get; init; } = new();

    /// <summary>
    /// Browser context options.
    /// </summary>
    public ContextOptions Context { get; init; } = new();

    /// <summary>
    /// Proxy settings.
    /// </summary>
    public ProxyOptions Proxy { get; init; } = new();

    /// <summary>
    /// Каталог профиля Chrome (cookies, localStorage между перезапусками).
    /// </summary>
    public string UserDataDir { get; set; } = string.Empty;

    /// <summary>
    /// Запускать Chrome с постоянным профилем вместо пустого контекста.
    /// </summary>
    public bool UsePersistentProfile { get; set; } = true;

    /// <summary>
    /// Блокировать рекламу, аналитику и трекеры (google/yandex ads, simbad и т.п.).
    /// </summary>
    public bool BlockTrackers { get; set; } = true;
}

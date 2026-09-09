namespace AvitoAgent.Shared.Configuration;

public sealed class DebugOptions
{
    public const string SectionName = "Debug";

    /// <summary>
    /// Выгружать фото объявлений на диск для отладки (по папке на объявление).
    /// </summary>
    public bool ExportListingPhotos { get; set; }

    /// <summary>
    /// Каталог относительно корня проекта или абсолютный путь.
    /// </summary>
    public string ListingPhotosDirectory { get; set; } = "debug/listing-photos";

    /// <summary>
    /// Писать отладочный лог браузера в logs/debug-*.log:
    /// ошибки Playwright, page crash, console, сбои сети/HTTP.
    /// </summary>
    public bool BrowserLog { get; set; }
}

using System.Collections.ObjectModel;

namespace AvitoAgent.Playwright.Options;

/// <summary>
/// Browser context configuration.
/// </summary>
public sealed class ContextOptions
{
    /// <summary>
    /// Browser locale.
    /// Example: en-US, ru-RU.
    /// </summary>
    public string Locale { get; init; } = "ru-RU";

    /// <summary>
    /// Browser timezone.
    /// Example: Europe/Moscow.
    /// </summary>
    public string? TimezoneId { get; init; }

    /// <summary>
    /// Browser user agent.
    /// </summary>
    public string? UserAgent { get; init; }

    /// <summary>
    /// Подменять размер окна через CDP. false - как у обычного Chrome (реальный размер окна).
    /// </summary>
    public bool EmulateViewport { get; init; }

    /// <summary>
    /// Browser viewport.
    /// </summary>
    public ViewportSize Viewport { get; init; } = ViewportSize.Default;

    /// <summary>
    /// Device scale factor.
    /// </summary>
    public double DeviceScaleFactor { get; init; } = 1.0;

    /// <summary>
    /// Enable touch support.
    /// </summary>
    public bool HasTouch { get; init; }

    /// <summary>
    /// Enable mobile emulation.
    /// </summary>
    public bool IsMobile { get; init; }

    /// <summary>
    /// Enable JavaScript.
    /// </summary>
    public bool JavaScriptEnabled { get; init; } = true;

    /// <summary>
    /// Work in offline mode.
    /// </summary>
    public bool Offline { get; init; }

    /// <summary>
    /// Ignore HTTPS certificate errors.
    /// </summary>
    public bool IgnoreHttpsErrors { get; init; }

    /// <summary>
    /// Automatically accept downloads.
    /// </summary>
    public bool AcceptDownloads { get; init; } = true;

    /// <summary>
    /// Browser color scheme.
    /// </summary>
    public string? ColorScheme { get; init; }

    /// <summary>
    /// Browser reduced motion.
    /// </summary>
    public string? ReducedMotion { get; init; }

    /// <summary>
    /// Geolocation settings.
    /// </summary>
    public GeolocationOptions? Geolocation { get; init; }

    /// <summary>
    /// Video recording settings.
    /// </summary>
    public VideoRecordingOptions? Video { get; init; }

    /// <summary>
    /// Browser permissions.
    /// </summary>
    public Collection<string> Permissions { get; init; } = [];

    /// <summary>
    /// Path to storage state json.
    /// </summary>
    public string? StorageStatePath { get; init; }
}

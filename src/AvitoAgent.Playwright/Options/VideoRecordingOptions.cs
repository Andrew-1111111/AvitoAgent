namespace AvitoAgent.Playwright.Options;

/// <summary>
/// Browser video recording settings.
/// </summary>
public sealed class VideoRecordingOptions
{
    /// <summary>
    /// Enable video recording.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Directory for recorded videos.
    /// </summary>
    public string? Directory { get; init; }

    /// <summary>
    /// Video width.
    /// </summary>
    public int Width { get; init; } = 1280;

    /// <summary>
    /// Video height.
    /// </summary>
    public int Height { get; init; } = 720;

    /// <summary>
    /// Gets whether recording is configured.
    /// </summary>
    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(Directory);
}

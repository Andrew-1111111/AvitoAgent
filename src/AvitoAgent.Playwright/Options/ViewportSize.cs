namespace AvitoAgent.Playwright.Options;

/// <summary>
/// Browser viewport size.
/// </summary>
public sealed class ViewportSize
{
    /// <summary>
    /// Default viewport.
    /// </summary>
    public static readonly ViewportSize Default = new(1920, 1080);

    public ViewportSize() { }

    public ViewportSize(int width, int height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Width in pixels.
    /// </summary>
    public int Width { get; init; } = 1920;

    /// <summary>
    /// Height in pixels.
    /// </summary>
    public int Height { get; init; } = 1080;

    public override string ToString() => $"{Width}x{Height}";
}

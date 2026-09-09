namespace AvitoAgent.Playwright.Options;

/// <summary>
/// Browser engine used by Microsoft Playwright.
/// </summary>
public enum BrowserEngine
{
    /// <summary>
    /// Chromium.
    /// </summary>
    Chromium = 0,

    /// <summary>
    /// Mozilla Firefox.
    /// </summary>
    Firefox = 1,

    /// <summary>
    /// Apple WebKit.
    /// </summary>
    WebKit = 2,
}

namespace AvitoAgent.Playwright.Options;

/// <summary>
/// Browser proxy settings.
/// </summary>
public sealed class ProxyOptions
{
    /// <summary>
    /// Enable proxy.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Proxy server.
    /// Example:
    /// http://127.0.0.1:8080
    /// socks5://127.0.0.1:9050
    /// </summary>
    public string? Server { get; set; }

    /// <summary>
    /// Proxy username.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Proxy password.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Proxy bypass list.
    /// Example:
    /// localhost;127.0.0.1
    /// </summary>
    public string? Bypass { get; set; }

    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(Server);
}

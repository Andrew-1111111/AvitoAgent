using System.Collections.ObjectModel;

namespace AvitoAgent.Playwright.Options;

/// <summary>
/// Browser launch options.
/// </summary>
public sealed class LaunchOptions
{
    /// <summary>
    /// Browser engine.
    /// </summary>
    public BrowserEngine Browser { get; init; } = BrowserEngine.Chromium;

    /// <summary>
    /// Launch browser in headless mode.
    /// </summary>
    public bool Headless { get; init; } = true;

    /// <summary>
    /// Open DevTools automatically.
    /// </summary>
    public bool DevTools { get; init; }

    /// <summary>
    /// Slow down Playwright operations.
    /// </summary>
    public int SlowMo { get; init; }

    /// <summary>
    /// Browser launch timeout (milliseconds).
    /// </summary>
    public int Timeout { get; init; } = 30000;

    /// <summary>
    /// Browser channel.
    /// Examples:
    /// chrome
    /// msedge
    /// chrome-beta
    /// </summary>
    public string? Channel { get; init; }

    /// <summary>
    /// Path to browser executable.
    /// </summary>
    public string? ExecutablePath { get; init; }

    /// <summary>
    /// Ignore HTTPS certificate errors.
    /// </summary>
    public bool IgnoreHttpsErrors { get; init; }

    /// <summary>
    /// Disable Chromium sandbox.
    /// </summary>
    public bool ChromiumSandbox { get; init; } = true;

    /// <summary>
    /// Additional browser arguments.
    /// </summary>
    public Collection<string> Arguments { get; init; } = [];

    /// <summary>
    /// Не передавать тестовые флаги Playwright. true ломает запуск: без --remote-debugging-pipe
    /// агент не подключается к Chrome и сразу завершается.
    /// </summary>
    public bool IgnoreAllDefaultArguments { get; init; }

    /// <summary>
    /// Ignore default browser arguments.
    /// </summary>
    public Collection<string> IgnoreDefaultArguments { get; init; } = [];
}

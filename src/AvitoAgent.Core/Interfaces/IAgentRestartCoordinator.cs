namespace AvitoAgent.Core.Interfaces;

/// <summary>
/// Управление перезапуском агента (например, выход из headless для входа в Avito).
/// </summary>
public interface IAgentRestartCoordinator
{
    /// <summary>
    /// true — браузер сейчас без окна (Headless).
    /// </summary>
    bool IsBrowserHeadless { get; }

    /// <summary>
    /// true — текущий процесс завершается ради перезапуска (например, выход из headless).
    /// </summary>
    bool IsRestarting { get; }

    /// <summary>
    /// Пишет Playwright:Launch:Headless=false в appsettings и перезапускает процесс.
    /// Возвращает true, если перезапуск инициирован (текущий процесс скоро завершится).
    /// </summary>
    Task<bool> DisableHeadlessAndRestartAsync(
        string reason,
        CancellationToken cancellationToken = default
    );
}

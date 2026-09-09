using AvitoAgent.Playwright.Interfaces;

namespace AvitoAgent.Avito.Interfaces;

public interface IAvitoAuthService
{
    string StorageStatePath { get; }

    /// <summary>
    /// true — сессия подтверждена. false — нужен ручной вход, ожидание не блокирует цикл.
    /// </summary>
    Task<bool> EnsureAuthenticatedAsync(
        IBrowserSession session,
        CancellationToken cancellationToken = default
    );

    Task SaveSessionAsync(IBrowserSession session, CancellationToken cancellationToken = default);
}

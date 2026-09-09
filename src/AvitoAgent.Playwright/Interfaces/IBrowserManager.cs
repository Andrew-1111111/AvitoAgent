namespace AvitoAgent.Playwright.Interfaces;

public interface IBrowserManager : IAsyncDisposable
{
    Task<IBrowserSession> CreateSessionAsync(
        BrowserSessionRequest? request = null,
        CancellationToken cancellationToken = default
    );

    Task<IBrowserSession> GetPersistentSessionAsync(
        BrowserSessionRequest? request = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Сохраняет cookies в storageState (если путь задан) и корректно закрывает Chrome,
    /// чтобы профиль на диске успел записаться.
    /// </summary>
    Task ClosePersistentSessionAsync(
        string? storageStatePath = null,
        CancellationToken cancellationToken = default
    );
}

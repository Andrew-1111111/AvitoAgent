using AvitoAgent.Core.Models;

namespace AvitoAgent.Core.Interfaces;

/// <summary>
/// Статус входа в Avito для Telegram (без полуавтоматического ввода).
/// </summary>
public interface IAvitoAuthControl
{
    Task<AvitoAuthStatusInfo> GetStatusAsync(CancellationToken cancellationToken = default);
}

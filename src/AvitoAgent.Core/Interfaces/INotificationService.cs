using AvitoAgent.Core.Models;

namespace AvitoAgent.Core.Interfaces;

public interface INotificationService
{
    Task<bool> NotifyListingFoundAsync(
        Listing listing,
        ProductAnalysis? analysis,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Нужен ручной вход в Avito в браузере агента.
    /// </summary>
    Task NotifyAvitoAuthRequiredAsync(
        string message,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Успешный вход в Avito.
    /// </summary>
    Task NotifyAvitoAuthSucceededAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Приложение или браузер закрыты. Вызов одноразовый: повторные не отправляют второе сообщение.
    /// </summary>
    Task NotifyApplicationClosedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Таймаут загрузки выдачи поиска: текст + скриншот страницы.
    /// </summary>
    Task NotifySearchTimeoutAsync(
        string screenshotPath,
        string? pageUrl = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Нужна ручная капча в браузере агента: текст + скриншот. Агент продолжает ждать.
    /// </summary>
    Task NotifyCaptchaRequiredAsync(
        string screenshotPath,
        string? pageUrl = null,
        CancellationToken cancellationToken = default
    );
}

using AvitoAgent.Core.Models;

namespace AvitoAgent.Core.Interfaces;

public interface IMarketplace
{
    /// <param name="onListingEnriched">
    /// Вызывается сразу после успешного открытия и парсинга объявления (до возврата к поиску).
    /// </param>
    Task<IReadOnlyList<Listing>> SearchAsync(
        SearchCriteria criteria,
        Func<Listing, CancellationToken, Task>? onListingEnriched = null,
        CancellationToken cancellationToken = default
    );
}

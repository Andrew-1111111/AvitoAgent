using AvitoAgent.Core.Models;

namespace AvitoAgent.Core.Interfaces;

public interface IProductAnalyzer
{
    Task EnsureAvailableAsync(CancellationToken cancellationToken = default);

    Task<ProductAnalysis> AnalyzeAsync(
        Listing listing,
        SearchCriteria criteria,
        CancellationToken cancellationToken = default
    );
}

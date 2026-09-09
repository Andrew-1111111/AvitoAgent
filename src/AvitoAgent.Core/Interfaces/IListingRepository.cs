using AvitoAgent.Core.Models;

namespace AvitoAgent.Core.Interfaces;

public interface IListingRepository
{
    Task<bool> HasAnalysisAsync(string listingId, CancellationToken cancellationToken = default);

    Task<bool> HasTelegramNotificationAsync(
        string listingId,
        CancellationToken cancellationToken = default
    );

    Task SaveAsync(Listing listing, CancellationToken cancellationToken = default);

    Task SaveAnalysisAsync(
        string listingId,
        ProductAnalysis analysis,
        CancellationToken cancellationToken = default
    );

    Task SaveTelegramNotificationAsync(
        Listing listing,
        CancellationToken cancellationToken = default
    );

    Task SaveImagesAsync(
        string listingId,
        IReadOnlyList<string> imageUrls,
        CancellationToken cancellationToken = default
    );
}

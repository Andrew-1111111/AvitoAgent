using AvitoAgent.Core.Models;

namespace AvitoAgent.Core.Interfaces;

public interface IListingPhotoArchiver
{
    /// <summary>
    /// Скачивает фото по URL и выгружает на диск, если включён Debug:ExportListingPhotos.
    /// </summary>
    Task<int> ArchiveAsync(
        string listingId,
        IReadOnlyList<string> imageUrls,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Выгружает уже скачанные фото на диск, если включён Debug:ExportListingPhotos.
    /// </summary>
    Task<int> ArchiveAsync(
        string listingId,
        IReadOnlyList<ListingPhoto> photos,
        CancellationToken cancellationToken = default
    );
}

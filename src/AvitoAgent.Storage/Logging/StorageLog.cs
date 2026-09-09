using Microsoft.Extensions.Logging;

namespace AvitoAgent.Storage.Logging;

internal static partial class StorageLog
{
    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Debug,
        Message = "Объявление сохранено: {ListingId}"
    )]
    public static partial void ListingSaved(ILogger logger, string listingId);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Debug,
        Message = "Анализ сохранён: {ListingId}"
    )]
    public static partial void AnalysisSaved(ILogger logger, string listingId);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Warning,
        Message = "Не удалось скачать фото {Url} для {ListingId}"
    )]
    public static partial void ImageDownloadFailed(
        ILogger logger,
        string listingId,
        string url,
        Exception exception
    );

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Information,
        Message = "Фото объявления {ListingId} сохранены на диск: {Count} шт. ({Directory})"
    )]
    public static partial void ImagesExported(
        ILogger logger,
        string listingId,
        string directory,
        int count
    );

    [LoggerMessage(
        EventId = 1007,
        Level = LogLevel.Information,
        Message = "ExportListingPhotos включён, но у объявления {ListingId} нет скачанных фото — папку не создаём"
    )]
    public static partial void ImagesExportSkipped(ILogger logger, string listingId);

    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Warning,
        Message = "Не удалось сохранить фото объявления {ListingId} в {Directory}: {Reason}"
    )]
    public static partial void ImageExportFailed(
        ILogger logger,
        string listingId,
        string directory,
        string reason
    );
}

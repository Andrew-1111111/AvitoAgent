using AvitoAgent.Core.Models;
using AvitoAgent.Shared.Configuration;
using AvitoAgent.Storage.Data;
using AvitoAgent.Storage.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace AvitoAgent.Tests.Storage;

public sealed class ListingRepositoryTests
{
    [Fact]
    public async Task Save_upsert_analysis_telegram_and_images()
    {
        var root = Path.Combine(Path.GetTempPath(), "avito-db-" + Guid.NewGuid());
        try
        {
            var paths = new ApplicationPaths(root);
            var factory = new AvitoDbConnectionFactory(paths);
            var repo = new ListingRepository(factory, NullLogger<ListingRepository>.Instance);

            var listing = new Listing
            {
                Id = "item-1",
                Title = "Title",
                Description = "Desc",
                Price = 1000,
                Url = "https://www.avito.ru/1",
                Location = "Москва",
                Source = "avito",
                FirstSeenAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow,
            };

            Assert.False(await repo.HasAnalysisAsync(listing.Id));
            Assert.False(await repo.HasTelegramNotificationAsync(listing.Id));

            await repo.SaveAsync(listing);
            await repo.SaveAsync(
                new Listing
                {
                    Id = listing.Id,
                    Title = "Updated",
                    Description = listing.Description,
                    Price = listing.Price,
                    Url = listing.Url,
                    Location = listing.Location,
                    Source = listing.Source,
                    FirstSeenAt = listing.FirstSeenAt,
                    LastSeenAt = DateTime.UtcNow,
                }
            );

            await repo.SaveAnalysisAsync(
                listing.Id,
                new ProductAnalysis
                {
                    Score = 80,
                    IsRelevant = true,
                    IsAuthentic = true,
                    AuthenticityScore = 70,
                    Brand = "Honda",
                    DetectedFeatures = ["a"],
                    CounterfeitIndicators = [],
                }
            );

            Assert.True(await repo.HasAnalysisAsync(listing.Id));

            await repo.SaveTelegramNotificationAsync(listing);
            Assert.True(await repo.HasTelegramNotificationAsync(listing.Id));

            await repo.SaveImagesAsync(listing.Id, ["https://cdn/a.jpg", "https://cdn/b.jpg"]);
            await repo.SaveImagesAsync(listing.Id, ["https://cdn/a.jpg"]);
        }
        finally
        {
            TryDeleteRoot(root);
        }
    }

    [Fact]
    public async Task SaveImages_empty_is_noop()
    {
        var root = Path.Combine(Path.GetTempPath(), "avito-db-" + Guid.NewGuid());
        try
        {
            var repo = new ListingRepository(
                new AvitoDbConnectionFactory(new ApplicationPaths(root)),
                NullLogger<ListingRepository>.Instance
            );
            await repo.SaveImagesAsync("x", []);
        }
        finally
        {
            TryDeleteRoot(root);
        }
    }

    private static void TryDeleteRoot(string root)
    {
        SqliteConnection.ClearAllPools();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Windows may keep a brief lock on sqlite files; temp cleanup is best-effort.
        }
    }
}

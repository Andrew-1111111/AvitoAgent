using System.Text.Json;
using AvitoAgent.Core.Interfaces;
using AvitoAgent.Core.Models;
using AvitoAgent.Storage.Data;
using AvitoAgent.Storage.Logging;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace AvitoAgent.Storage.Repositories;

public sealed class ListingRepository(
    AvitoDbConnectionFactory connectionFactory,
    ILogger<ListingRepository> logger
) : IListingRepository
{
    private readonly AvitoDbConnectionFactory _connectionFactory = connectionFactory;
    private readonly ILogger<ListingRepository> _logger = logger;

    public async Task<bool> HasAnalysisAsync(
        string listingId,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1
                FROM ListingAnalyses
                WHERE ListingId = $id
            );
            """;
        command.Parameters.AddWithValue("$id", listingId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result) == 1;
    }

    public async Task<bool> HasTelegramNotificationAsync(
        string listingId,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1
                FROM TelegramSentListings
                WHERE ListingId = $id
            );
            """;
        command.Parameters.AddWithValue("$id", listingId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result) == 1;
    }

    public async Task SaveAsync(Listing listing, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO Listings
            (
                Id,
                Title,
                Description,
                Price,
                Currency,
                Url,
                Location,
                SellerName,
                PublishedAt,
                FirstSeenAt,
                LastSeenAt,
                Source
            )
            VALUES
            (
                $id,
                $title,
                $description,
                $price,
                $currency,
                $url,
                $location,
                $sellerName,
                $publishedAt,
                $firstSeenAt,
                $lastSeenAt,
                $source
            )
            ON CONFLICT(Id) DO UPDATE SET
                Title = excluded.Title,
                Description = excluded.Description,
                Price = excluded.Price,
                Currency = excluded.Currency,
                Url = excluded.Url,
                Location = excluded.Location,
                SellerName = excluded.SellerName,
                PublishedAt = excluded.PublishedAt,
                LastSeenAt = excluded.LastSeenAt,
                Source = excluded.Source;
            """;

        command.Parameters.AddWithValue("$id", listing.Id);
        command.Parameters.AddWithValue("$title", listing.Title);
        command.Parameters.AddWithValue("$description", listing.Description);
        command.Parameters.AddWithValue("$price", listing.Price ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$currency", listing.Currency);
        command.Parameters.AddWithValue("$url", listing.Url);
        command.Parameters.AddWithValue("$location", listing.Location);
        command.Parameters.AddWithValue("$sellerName", listing.SellerName);
        command.Parameters.AddWithValue(
            "$publishedAt",
            listing.PublishedAt?.ToString("O") ?? (object)DBNull.Value
        );
        command.Parameters.AddWithValue("$firstSeenAt", listing.FirstSeenAt.ToString("O"));
        command.Parameters.AddWithValue("$lastSeenAt", listing.LastSeenAt.ToString("O"));
        command.Parameters.AddWithValue("$source", listing.Source);

        await command.ExecuteNonQueryAsync(cancellationToken);

        StorageLog.ListingSaved(_logger, listing.Id);
    }

    public async Task SaveAnalysisAsync(
        string listingId,
        ProductAnalysis analysis,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ListingAnalyses
            (
                ListingId,
                Score,
                IsRelevant,
                IsAuthentic,
                AuthenticityScore,
                Brand,
                Model,
                Category,
                Condition,
                Reason,
                DetectedFeaturesJson,
                CounterfeitIndicatorsJson,
                AnalyzedAt
            )
            VALUES
            (
                $listingId,
                $score,
                $isRelevant,
                $isAuthentic,
                $authenticityScore,
                $brand,
                $model,
                $category,
                $condition,
                $reason,
                $detectedFeatures,
                $counterfeitIndicators,
                $analyzedAt
            )
            ON CONFLICT(ListingId) DO UPDATE SET
                Score = excluded.Score,
                IsRelevant = excluded.IsRelevant,
                IsAuthentic = excluded.IsAuthentic,
                AuthenticityScore = excluded.AuthenticityScore,
                Brand = excluded.Brand,
                Model = excluded.Model,
                Category = excluded.Category,
                Condition = excluded.Condition,
                Reason = excluded.Reason,
                DetectedFeaturesJson = excluded.DetectedFeaturesJson,
                CounterfeitIndicatorsJson = excluded.CounterfeitIndicatorsJson,
                AnalyzedAt = excluded.AnalyzedAt;
            """;

        command.Parameters.AddWithValue("$listingId", listingId);
        command.Parameters.AddWithValue("$score", analysis.Score);
        command.Parameters.AddWithValue("$isRelevant", analysis.IsRelevant ? 1 : 0);
        command.Parameters.AddWithValue(
            "$isAuthentic",
            analysis.IsAuthentic.HasValue
                ? analysis.IsAuthentic.Value
                    ? 1
                    : 0
                : DBNull.Value
        );
        command.Parameters.AddWithValue("$authenticityScore", analysis.AuthenticityScore);
        command.Parameters.AddWithValue("$brand", analysis.Brand);
        command.Parameters.AddWithValue("$model", analysis.Model);
        command.Parameters.AddWithValue("$category", analysis.Category);
        command.Parameters.AddWithValue("$condition", analysis.Condition);
        command.Parameters.AddWithValue("$reason", analysis.Reason);
        command.Parameters.AddWithValue(
            "$detectedFeatures",
            JsonSerializer.Serialize(analysis.DetectedFeatures)
        );
        command.Parameters.AddWithValue(
            "$counterfeitIndicators",
            JsonSerializer.Serialize(analysis.CounterfeitIndicators)
        );
        command.Parameters.AddWithValue("$analyzedAt", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
        StorageLog.AnalysisSaved(_logger, listingId);
    }

    public async Task SaveTelegramNotificationAsync(
        Listing listing,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO TelegramSentListings
            (
                ListingId,
                Title,
                Url,
                SentAt
            )
            VALUES
            (
                $listingId,
                $title,
                $url,
                $sentAt
            )
            ON CONFLICT(ListingId) DO UPDATE SET
                Title = excluded.Title,
                Url = excluded.Url,
                SentAt = excluded.SentAt;
            """;

        command.Parameters.AddWithValue("$listingId", listing.Id);
        command.Parameters.AddWithValue("$title", listing.Title);
        command.Parameters.AddWithValue("$url", listing.Url);
        command.Parameters.AddWithValue("$sentAt", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveImagesAsync(
        string listingId,
        IReadOnlyList<string> imageUrls,
        CancellationToken cancellationToken = default
    )
    {
        if (imageUrls.Count == 0)
        {
            return;
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        for (var i = 0; i < imageUrls.Count; i++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO ListingImages (ListingId, Url, SortOrder)
                VALUES ($listingId, $url, $sortOrder)
                ON CONFLICT(ListingId, Url) DO NOTHING;
                """;

            command.Parameters.AddWithValue("$listingId", listingId);
            command.Parameters.AddWithValue("$url", imageUrls[i]);
            command.Parameters.AddWithValue("$sortOrder", i);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}

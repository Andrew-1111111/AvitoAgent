using AvitoAgent.Shared.Configuration;
using Microsoft.Data.Sqlite;

namespace AvitoAgent.Storage.Data;

public sealed class AvitoDbConnectionFactory(ApplicationPaths paths)
{
    private readonly ApplicationPaths _paths = paths;
    private readonly object _schemaGate = new();
    private bool _schemaReady;

    public SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection($"Data Source={_paths.DatabaseFile}");
        EnsureSchema(connection);
        return connection;
    }

    private void EnsureSchema(SqliteConnection connection)
    {
        if (_schemaReady)
        {
            return;
        }

        lock (_schemaGate)
        {
            if (_schemaReady)
            {
                return;
            }

            connection.Open();
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE IF NOT EXISTS Listings
                    (
                        Id TEXT PRIMARY KEY,
                        Title TEXT NOT NULL,
                        Description TEXT NOT NULL DEFAULT '',
                        Price REAL NULL,
                        Currency TEXT NOT NULL DEFAULT 'RUB',
                        Url TEXT NOT NULL DEFAULT '',
                        Location TEXT NOT NULL DEFAULT '',
                        SellerName TEXT NOT NULL DEFAULT '',
                        PublishedAt TEXT NULL,
                        FirstSeenAt TEXT NOT NULL,
                        LastSeenAt TEXT NOT NULL,
                        Source TEXT NOT NULL DEFAULT ''
                    );

                    CREATE TABLE IF NOT EXISTS ListingImages
                    (
                        ListingId TEXT NOT NULL,
                        Url TEXT NOT NULL,
                        SortOrder INTEGER NOT NULL DEFAULT 0,
                        PRIMARY KEY (ListingId, Url),
                        FOREIGN KEY (ListingId) REFERENCES Listings(Id) ON DELETE CASCADE
                    );

                    CREATE TABLE IF NOT EXISTS ListingAnalyses
                    (
                        ListingId TEXT PRIMARY KEY,
                        Score INTEGER NOT NULL,
                        IsRelevant INTEGER NOT NULL,
                        IsAuthentic INTEGER NULL,
                        AuthenticityScore INTEGER NOT NULL,
                        Brand TEXT NOT NULL DEFAULT '',
                        Model TEXT NOT NULL DEFAULT '',
                        Category TEXT NOT NULL DEFAULT '',
                        Condition TEXT NOT NULL DEFAULT '',
                        Reason TEXT NOT NULL DEFAULT '',
                        DetectedFeaturesJson TEXT NOT NULL DEFAULT '[]',
                        CounterfeitIndicatorsJson TEXT NOT NULL DEFAULT '[]',
                        AnalyzedAt TEXT NOT NULL,
                        FOREIGN KEY (ListingId) REFERENCES Listings(Id) ON DELETE CASCADE
                    );

                    CREATE TABLE IF NOT EXISTS TelegramSentListings
                    (
                        ListingId TEXT PRIMARY KEY,
                        Title TEXT NOT NULL DEFAULT '',
                        Url TEXT NOT NULL DEFAULT '',
                        SentAt TEXT NOT NULL,
                        FOREIGN KEY (ListingId) REFERENCES Listings(Id) ON DELETE CASCADE
                    );

                    CREATE INDEX IF NOT EXISTS IX_Listings_LastSeenAt
                        ON Listings(LastSeenAt);

                    CREATE INDEX IF NOT EXISTS IX_Listings_Source
                        ON Listings(Source);
                    """;
                command.ExecuteNonQuery();
                _schemaReady = true;
            }
            finally
            {
                connection.Close();
            }
        }
    }
}

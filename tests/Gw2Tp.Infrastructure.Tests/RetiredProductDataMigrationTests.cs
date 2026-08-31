using System.Data.Common;
using Gw2Tp.Infrastructure.Preferences;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Gw2Tp.Infrastructure.Tests;

public sealed class RetiredProductDataMigrationTests
{
    private const string PreM9MigrationId = "20260829120000_AddMarketScanPreferences";

    [Fact]
    public async Task Upgrade_deletes_retired_product_data_and_preserves_user_session_preferences()
    {
        var options = new DbContextOptionsBuilder<UserSessionPreferencesDbContext>()
            .UseSqlite($"Data Source={CreateDatabasePath()}")
            .Options;

        await using var dbContext = new UserSessionPreferencesDbContext(options);
        await dbContext.Database.MigrateAsync(PreM9MigrationId);
        await SeedPreM9DataAsync(dbContext);

        await dbContext.Database.MigrateAsync();

        await dbContext.Database.OpenConnectionAsync();
        try
        {
            var connection = dbContext.Database.GetDbConnection();
            foreach (var retiredTable in new[]
                     {
                         "Operations",
                         "OperationScenarios",
                         "MarketPriceSnapshots",
                         "MarketOrderBookSnapshots",
                         "MarketOrderBookLevels",
                         "MarketWatchlistItems",
                     })
            {
                Assert.Equal(0L, await ExecuteScalarAsync(
                    connection,
                    $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{retiredTable}'"));
            }

            Assert.Equal(1L, await ExecuteScalarAsync(
                connection,
                "SELECT COUNT(*) FROM UserSessionPreferences WHERE Id = 1"));
            Assert.Equal(120_000L, await ExecuteScalarAsync(
                connection,
                "SELECT CapitalLimitCopper FROM UserSessionPreferences WHERE Id = 1"));
            Assert.Equal(500L, await ExecuteScalarAsync(
                connection,
                "SELECT MinimumProfitCopper FROM UserSessionPreferences WHERE Id = 1"));
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    [Fact]
    public void Whole_market_discovery_adds_no_persisted_market_entity()
    {
        var options = new DbContextOptionsBuilder<UserSessionPreferencesDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var dbContext = new UserSessionPreferencesDbContext(options);

        var tables = dbContext.Model.GetEntityTypes()
            .Select(entityType => entityType.GetTableName() ?? throw new InvalidOperationException())
            .OrderBy(tableName => tableName, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["UserSessionPreferences"], tables);
    }

    private static async Task SeedPreM9DataAsync(UserSessionPreferencesDbContext dbContext)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO UserSessionPreferences
                (Id, CapitalLimitCopper, MinimumProfitCopper, RiskPreference, StrategyPreference, AllocationPercent, AnalysisQuantity, ListingFeeBasisPoints, ListingFeeRounding, ExchangeFeeBasisPoints, ExchangeFeeRounding)
            VALUES
                (1, 120000, 500, 'normal', 'market-flip', 65, 2, 500, 'down', 1000, 'up');
            """);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO Operations
                (Id, CreatedAtUtc, CreatedAtUtcTicks, LastModifiedAtUtc, Status, CalculationVersionId, ConfigurationVersionId, ActualOutcomeJson)
            VALUES
                ('11111111-1111-1111-1111-111111111111', '2026-08-31T12:00:00+00:00', 638922096000000000, '2026-08-31T12:00:00+00:00', 'planned', 'v1', 'v1', 'retired');
            """);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO OperationScenarios (OperationId, Kind, PayloadJson)
            VALUES ('11111111-1111-1111-1111-111111111111', 'market-flip', 'retired');
            """);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO MarketPriceSnapshots
                (Id, ItemId, CapturedAtUtc, CapturedAtUtcTicks, ExpiresAtUtc, FormatVersion, IsWhitelisted, BuyQuantity, BuyUnitPriceCopper, SellQuantity, SellUnitPriceCopper)
            VALUES
                ('22222222-2222-2222-2222-222222222222', 19721, '2026-08-31T12:00:00+00:00', 638922096000000000, '2026-08-31T12:05:00+00:00', 1, 1, 10, 100, 10, 110);
            """);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO MarketOrderBookSnapshots
                (Id, ItemId, CapturedAtUtc, CapturedAtUtcTicks, ExpiresAtUtc, FormatVersion)
            VALUES
                ('33333333-3333-3333-3333-333333333333', 19721, '2026-08-31T12:00:00+00:00', 638922096000000000, '2026-08-31T12:05:00+00:00', 1);
            """);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO MarketOrderBookLevels (SnapshotId, Side, Position, Listings, Quantity, UnitPriceCopper)
            VALUES ('33333333-3333-3333-3333-333333333333', 'buy', 0, 2, 10, 100);
            """);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO MarketWatchlistItems (ItemId, SamplingClass)
            VALUES (19721, 'watchlist');
            """);
    }

    private static async Task<long> ExecuteScalarAsync(DbConnection connection, string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static string CreateDatabasePath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TyrianLedger", "InfrastructureTests");
        Directory.CreateDirectory(directory);

        return Path.Combine(directory, $"retired-product-data-{Guid.NewGuid():N}.db");
    }
}

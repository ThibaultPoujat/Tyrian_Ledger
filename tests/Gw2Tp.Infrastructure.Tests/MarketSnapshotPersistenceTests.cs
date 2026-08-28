using System.Data.Common;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Domain.MarketData;
using Gw2Tp.Infrastructure.Preferences;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Gw2Tp.Infrastructure.Tests;

public sealed class MarketSnapshotPersistenceTests
{
    private const string PreMarketSnapshotMigrationId = "20260828120659_AddOperationActualOutcome";

    [Fact]
    public async Task Persists_append_only_price_and_order_book_snapshots_with_freshness_metadata()
    {
        var databasePath = CreateDatabasePath();
        using var serviceProvider = CreateServiceProvider(databasePath);
        await serviceProvider.MigrateTyrianLedgerUserSessionPreferencesAsync();
        var store = serviceProvider.GetRequiredService<IMarketSnapshotStore>();
        var watchlistStore = serviceProvider.GetRequiredService<IMarketWatchlistStore>();
        var capturedAtUtc = new DateTimeOffset(2026, 8, 28, 9, 0, 0, TimeSpan.Zero);
        var laterPrice = CreatePriceSnapshot(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            capturedAtUtc.AddHours(1));
        var earlierPrice = CreatePriceSnapshot(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            capturedAtUtc);
        var orderBook = new MarketOrderBookSnapshot(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            new MarketListing(
                19721,
                [
                    new MarketOrderLevel(2, 15, 99),
                    new MarketOrderLevel(1, 8, 95),
                ],
                [
                    new MarketOrderLevel(3, 10, 105),
                    new MarketOrderLevel(1, 4, 110),
                ]),
            new DataFreshness(capturedAtUtc, capturedAtUtc.AddMinutes(5)));

        await store.AppendAsync(laterPrice, CancellationToken.None);
        await store.AppendAsync(earlierPrice, CancellationToken.None);
        await store.AppendAsync(orderBook, CancellationToken.None);
        await watchlistStore.AddAsync(
            new MarketTrackedItem(19721, MarketSamplingClass.Watchlist),
            CancellationToken.None);
        await watchlistStore.AddAsync(
            new MarketTrackedItem(19722, MarketSamplingClass.Background),
            CancellationToken.None);
        await watchlistStore.UpdateSamplingClassAsync(
            19722,
            MarketSamplingClass.Watchlist,
            CancellationToken.None);

        var storedPrices = await store.ListPriceSnapshotsAsync(19721, CancellationToken.None);
        var storedOrderBooks = await store.ListOrderBookSnapshotsAsync(19721, CancellationToken.None);
        var collectionStates = await store.GetCollectionStatesAsync([19721, 19722], CancellationToken.None);

        Assert.Equal([earlierPrice.Id, laterPrice.Id], storedPrices.Select(snapshot => snapshot.Id));
        Assert.Equal(capturedAtUtc, storedPrices[0].Freshness.CapturedAtUtc);
        Assert.Equal(capturedAtUtc.AddMinutes(5), storedPrices[0].Freshness.ExpiresAtUtc);
        Assert.Equal(120, storedPrices[0].Price.Buys.UnitPriceInCopper);
        Assert.True(storedPrices[0].Price.IsWhitelisted);

        var storedOrderBook = Assert.Single(storedOrderBooks);
        Assert.Equal(orderBook.Id, storedOrderBook.Id);
        Assert.Equal(orderBook.Freshness, storedOrderBook.Freshness);
        Assert.Equal(orderBook.FormatVersion, storedOrderBook.FormatVersion);
        Assert.Equal([99, 95], storedOrderBook.Buys.Select(level => level.UnitPriceInCopper));
        Assert.Equal([105, 110], storedOrderBook.Sells.Select(level => level.UnitPriceInCopper));
        Assert.Equal(capturedAtUtc.AddHours(1), collectionStates[19721].LatestPriceCapturedAtUtc);
        Assert.Equal(capturedAtUtc, collectionStates[19721].LatestOrderBookCapturedAtUtc);
        Assert.Null(collectionStates[19722].LatestPriceCapturedAtUtc);
        Assert.Null(collectionStates[19722].LatestOrderBookCapturedAtUtc);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(earlierPrice, CancellationToken.None));
        Assert.Equal(
            [19721, 19722],
            (await watchlistStore.ListAsync(CancellationToken.None)).Select(item => item.ItemId));
        Assert.All(
            await watchlistStore.ListAsync(CancellationToken.None),
            item => Assert.Equal(MarketSamplingClass.Watchlist, item.SamplingClass));

        await watchlistStore.RemoveAsync(19722, CancellationToken.None);
        Assert.Single(await watchlistStore.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Migration_adds_snapshot_tables_without_losing_existing_preferences_data()
    {
        var databasePath = CreateDatabasePath();
        var options = new DbContextOptionsBuilder<UserSessionPreferencesDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        await using var dbContext = new UserSessionPreferencesDbContext(options);
        await dbContext.Database.MigrateAsync(PreMarketSnapshotMigrationId);
        await dbContext.Database.ExecuteSqlRawAsync(
            "INSERT INTO UserSessionPreferences (Id, CapitalLimitCopper, MinimumProfitCopper, RiskPreference, StrategyPreference, AllocationPercent) VALUES (1, 120000, 500, 'normal', 'market-flip', 65)");

        await dbContext.Database.MigrateAsync();

        await dbContext.Database.OpenConnectionAsync();
        try
        {
            var connection = dbContext.Database.GetDbConnection();
            Assert.Equal(65L, await ExecuteScalarAsync(
                connection,
                "SELECT AllocationPercent FROM UserSessionPreferences WHERE Id = 1"));
            Assert.Equal(1L, await ExecuteScalarAsync(
                connection,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'MarketPriceSnapshots'"));
            Assert.Equal(1L, await ExecuteScalarAsync(
                connection,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'MarketOrderBookSnapshots'"));
            Assert.Equal(1L, await ExecuteScalarAsync(
                connection,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'MarketOrderBookLevels'"));
            Assert.Equal(1L, await ExecuteScalarAsync(
                connection,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'MarketWatchlistItems'"));
            Assert.Equal(1L, await ExecuteScalarAsync(
                connection,
                "SELECT COUNT(*) FROM pragma_table_info('Operations') WHERE name = 'ActualOutcomeJson'"));
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    [Fact]
    public void Rejects_invalid_snapshot_content_before_it_reaches_sqlite()
    {
        var freshness = new DataFreshness(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.Throws<ArgumentException>(() => new MarketPriceSnapshot(
            Guid.Empty,
            new MarketPrice(19721, true, new MarketOrderSummary(1, 1), new MarketOrderSummary(1, 1)),
            freshness));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MarketOrderBookSnapshot(
            Guid.NewGuid(),
            new MarketListing(19721, [new MarketOrderLevel(1, -1, 10)], []),
            freshness));
    }

    private static MarketPriceSnapshot CreatePriceSnapshot(Guid id, DateTimeOffset capturedAtUtc) => new(
        id,
        new MarketPrice(
            19721,
            true,
            new MarketOrderSummary(30, 120),
            new MarketOrderSummary(25, 130)),
        new DataFreshness(capturedAtUtc, capturedAtUtc.AddMinutes(5)));

    private static ServiceProvider CreateServiceProvider(string databasePath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [UserSessionPreferencesServiceCollectionExtensions.DatabasePathConfigurationKey] = databasePath,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddTyrianLedgerUserSessionPreferences(configuration, new TestHostEnvironment("Testing"));
        return services.BuildServiceProvider();
    }

    private static async Task<long> ExecuteScalarAsync(DbConnection connection, string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static string CreateDatabasePath() => Path.Combine(
        Path.GetTempPath(),
        "TyrianLedger",
        "InfrastructureTests",
        $"market-snapshots-{Guid.NewGuid():N}.db");

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Gw2Tp.Infrastructure.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

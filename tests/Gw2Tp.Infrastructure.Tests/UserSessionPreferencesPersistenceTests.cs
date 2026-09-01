using Gw2Tp.Analytics.Finance;
using Gw2Tp.Application.Preferences;
using Gw2Tp.Infrastructure.Preferences;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Gw2Tp.Infrastructure.Tests;

public sealed class UserSessionPreferencesPersistenceTests
{
    [Fact]
    public async Task Incomplete_persisted_fee_rules_are_loaded_as_unconfigured()
    {
        var options = new DbContextOptionsBuilder<UserSessionPreferencesDbContext>()
            .UseSqlite($"Data Source={CreateDatabasePath()}")
            .Options;
        var store = new SqliteUserSessionPreferencesStore(new TestDbContextFactory(options));

        await using (var dbContext = new UserSessionPreferencesDbContext(options))
        {
            await dbContext.Database.MigrateAsync();
        }

        await store.SaveAsync(
            UserSessionPreferences.Create(
                capitalLimitCopper: null,
                minimumProfitCopper: null,
                riskPreference: OpportunityRiskPreference.All,
                strategyPreference: OpportunityStrategyPreference.All,
                allocationPercent: 100,
                listingFeeBasisPoints: 500,
                listingFeeRounding: FeeRounding.Down,
                exchangeFeeBasisPoints: 1_000,
                exchangeFeeRounding: FeeRounding.Up),
            CancellationToken.None);

        await using (var dbContext = new UserSessionPreferencesDbContext(options))
        {
            var entity = await dbContext.UserSessionPreferences.SingleAsync(CancellationToken.None);
            entity.ExchangeFeeRounding = null;
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        var preferences = await store.GetAsync(CancellationToken.None);

        Assert.Null(preferences.ListingFeeBasisPoints);
        Assert.Null(preferences.ListingFeeRounding);
        Assert.Null(preferences.ExchangeFeeBasisPoints);
        Assert.Null(preferences.ExchangeFeeRounding);
        Assert.False(preferences.TryCreateTransactionFeePolicy(out var feePolicy));
        Assert.Null(feePolicy);
    }

    private static string CreateDatabasePath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TyrianLedger", "InfrastructureTests");
        Directory.CreateDirectory(directory);

        return Path.Combine(directory, $"preferences-{Guid.NewGuid():N}.db");
    }

    private sealed class TestDbContextFactory(
        DbContextOptions<UserSessionPreferencesDbContext> options)
        : IDbContextFactory<UserSessionPreferencesDbContext>
    {
        public UserSessionPreferencesDbContext CreateDbContext() => new(options);

        public Task<UserSessionPreferencesDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}

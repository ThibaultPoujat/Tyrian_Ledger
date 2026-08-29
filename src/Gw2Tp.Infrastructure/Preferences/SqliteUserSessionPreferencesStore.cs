using Gw2Tp.Application.Preferences;
using Gw2Tp.Analytics.Finance;
using Microsoft.EntityFrameworkCore;

namespace Gw2Tp.Infrastructure.Preferences;

internal sealed class SqliteUserSessionPreferencesStore : IUserSessionPreferencesStore
{
    private readonly IDbContextFactory<UserSessionPreferencesDbContext> dbContextFactory;

    public SqliteUserSessionPreferencesStore(IDbContextFactory<UserSessionPreferencesDbContext> dbContextFactory)
    {
        this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    public async Task<UserSessionPreferences> GetAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.UserSessionPreferences
            .AsNoTracking()
            .SingleOrDefaultAsync(
                preferences => preferences.Id == UserSessionPreferencesEntity.SingletonId,
                cancellationToken);

        return entity is null ? UserSessionPreferences.Default : ToModel(entity);
    }

    public async Task SaveAsync(UserSessionPreferences preferences, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.UserSessionPreferences
            .SingleOrDefaultAsync(
                storedPreferences => storedPreferences.Id == UserSessionPreferencesEntity.SingletonId,
                cancellationToken);

        if (entity is null)
        {
            entity = new UserSessionPreferencesEntity { Id = UserSessionPreferencesEntity.SingletonId };
            dbContext.UserSessionPreferences.Add(entity);
        }

        entity.CapitalLimitCopper = preferences.CapitalLimitCopper;
        entity.MinimumProfitCopper = preferences.MinimumProfitCopper;
        entity.RiskPreference = ToStorageValue(preferences.RiskPreference);
        entity.StrategyPreference = ToStorageValue(preferences.StrategyPreference);
        entity.AllocationPercent = preferences.AllocationPercent;
        entity.AnalysisQuantity = preferences.AnalysisQuantity;
        entity.ListingFeeBasisPoints = preferences.ListingFeeBasisPoints;
        entity.ListingFeeRounding = preferences.ListingFeeRounding is { } listingRounding
            ? ToStorageValue(listingRounding)
            : null;
        entity.ExchangeFeeBasisPoints = preferences.ExchangeFeeBasisPoints;
        entity.ExchangeFeeRounding = preferences.ExchangeFeeRounding is { } exchangeRounding
            ? ToStorageValue(exchangeRounding)
            : null;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static UserSessionPreferences ToModel(UserSessionPreferencesEntity entity) => UserSessionPreferences.Create(
        entity.CapitalLimitCopper,
        entity.MinimumProfitCopper,
        ParseRiskPreference(entity.RiskPreference),
        ParseStrategyPreference(entity.StrategyPreference),
        entity.AllocationPercent,
        entity.AnalysisQuantity,
        entity.ListingFeeBasisPoints,
        ParseFeeRounding(entity.ListingFeeRounding),
        entity.ExchangeFeeBasisPoints,
        ParseFeeRounding(entity.ExchangeFeeRounding));

    private static OpportunityRiskPreference ParseRiskPreference(string value) => value switch
    {
        "all" => OpportunityRiskPreference.All,
        "normal" => OpportunityRiskPreference.Normal,
        "reduced" => OpportunityRiskPreference.Reduced,
        _ => throw new InvalidOperationException("The local user-session preference profile has an unsupported risk preference."),
    };

    private static OpportunityStrategyPreference ParseStrategyPreference(string value) => value switch
    {
        "all" => OpportunityStrategyPreference.All,
        "market-flip" => OpportunityStrategyPreference.MarketFlip,
        _ => throw new InvalidOperationException("The local user-session preference profile has an unsupported strategy preference."),
    };

    private static string ToStorageValue(OpportunityRiskPreference value) => value switch
    {
        OpportunityRiskPreference.All => "all",
        OpportunityRiskPreference.Normal => "normal",
        OpportunityRiskPreference.Reduced => "reduced",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "The risk preference is not supported."),
    };

    private static string ToStorageValue(OpportunityStrategyPreference value) => value switch
    {
        OpportunityStrategyPreference.All => "all",
        OpportunityStrategyPreference.MarketFlip => "market-flip",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "The strategy preference is not supported."),
    };

    private static FeeRounding? ParseFeeRounding(string? value) => value switch
    {
        null => null,
        "down" => FeeRounding.Down,
        "up" => FeeRounding.Up,
        _ => throw new InvalidOperationException("The local user-session preference profile has an unsupported fee rounding mode."),
    };

    private static string ToStorageValue(FeeRounding value) => value switch
    {
        FeeRounding.Down => "down",
        FeeRounding.Up => "up",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "The fee rounding mode is not supported."),
    };
}

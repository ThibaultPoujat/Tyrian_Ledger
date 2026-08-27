namespace Gw2Tp.Application.Preferences;

/// <summary>
/// The local, non-secret preferences that shape the opportunity dashboard.
/// Monetary values are exact copper amounts and remain within the browser-safe integer range.
/// </summary>
public sealed record UserSessionPreferences
{
    public const long MaximumSafeIntegerCopper = 9_007_199_254_740_991;
    public const int MinimumAllocationPercent = 1;
    public const int MaximumAllocationPercent = 100;

    public static UserSessionPreferences Default { get; } = Create(
        capitalLimitCopper: null,
        minimumProfitCopper: null,
        riskPreference: OpportunityRiskPreference.All,
        strategyPreference: OpportunityStrategyPreference.All,
        allocationPercent: MaximumAllocationPercent);

    private UserSessionPreferences(
        long? capitalLimitCopper,
        long? minimumProfitCopper,
        OpportunityRiskPreference riskPreference,
        OpportunityStrategyPreference strategyPreference,
        int allocationPercent)
    {
        CapitalLimitCopper = capitalLimitCopper;
        MinimumProfitCopper = minimumProfitCopper;
        RiskPreference = riskPreference;
        StrategyPreference = strategyPreference;
        AllocationPercent = allocationPercent;
    }

    public long? CapitalLimitCopper { get; }

    public long? MinimumProfitCopper { get; }

    public OpportunityRiskPreference RiskPreference { get; }

    public OpportunityStrategyPreference StrategyPreference { get; }

    public int AllocationPercent { get; }

    public static UserSessionPreferences Create(
        long? capitalLimitCopper,
        long? minimumProfitCopper,
        OpportunityRiskPreference riskPreference,
        OpportunityStrategyPreference strategyPreference,
        int allocationPercent)
    {
        ValidateCopper(capitalLimitCopper, nameof(capitalLimitCopper));
        ValidateCopper(minimumProfitCopper, nameof(minimumProfitCopper));

        if (!Enum.IsDefined(riskPreference))
        {
            throw new ArgumentOutOfRangeException(nameof(riskPreference));
        }

        if (!Enum.IsDefined(strategyPreference))
        {
            throw new ArgumentOutOfRangeException(nameof(strategyPreference));
        }

        if (allocationPercent is < MinimumAllocationPercent or > MaximumAllocationPercent)
        {
            throw new ArgumentOutOfRangeException(nameof(allocationPercent));
        }

        return new UserSessionPreferences(
            capitalLimitCopper,
            minimumProfitCopper,
            riskPreference,
            strategyPreference,
            allocationPercent);
    }

    public long? GetPerOpportunityCapitalLimitCopper() => CapitalLimitCopper is { } capitalLimit
        ? checked(capitalLimit * AllocationPercent / MaximumAllocationPercent)
        : null;

    private static void ValidateCopper(long? copper, string parameterName)
    {
        if (copper is < 0 or > MaximumSafeIntegerCopper)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

public enum OpportunityRiskPreference
{
    All,
    Normal,
    Reduced,
}

public enum OpportunityStrategyPreference
{
    All,
    MarketFlip,
}

public interface IUserSessionPreferencesStore
{
    Task<UserSessionPreferences> GetAsync(CancellationToken cancellationToken);

    Task SaveAsync(UserSessionPreferences preferences, CancellationToken cancellationToken);
}

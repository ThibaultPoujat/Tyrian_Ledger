using Gw2Tp.Analytics.Finance;

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
    public const int DefaultAnalysisQuantity = 1;

    public static UserSessionPreferences Default { get; } = Create(
        capitalLimitCopper: null,
        minimumProfitCopper: null,
        riskPreference: OpportunityRiskPreference.All,
        strategyPreference: OpportunityStrategyPreference.All,
        allocationPercent: MaximumAllocationPercent,
        analysisQuantity: DefaultAnalysisQuantity,
        listingFeeBasisPoints: null,
        listingFeeRounding: null,
        exchangeFeeBasisPoints: null,
        exchangeFeeRounding: null);

    private UserSessionPreferences(
        long? capitalLimitCopper,
        long? minimumProfitCopper,
        OpportunityRiskPreference riskPreference,
        OpportunityStrategyPreference strategyPreference,
        int allocationPercent,
        int analysisQuantity,
        int? listingFeeBasisPoints,
        FeeRounding? listingFeeRounding,
        int? exchangeFeeBasisPoints,
        FeeRounding? exchangeFeeRounding)
    {
        CapitalLimitCopper = capitalLimitCopper;
        MinimumProfitCopper = minimumProfitCopper;
        RiskPreference = riskPreference;
        StrategyPreference = strategyPreference;
        AllocationPercent = allocationPercent;
        AnalysisQuantity = analysisQuantity;
        ListingFeeBasisPoints = listingFeeBasisPoints;
        ListingFeeRounding = listingFeeRounding;
        ExchangeFeeBasisPoints = exchangeFeeBasisPoints;
        ExchangeFeeRounding = exchangeFeeRounding;
    }

    public long? CapitalLimitCopper { get; }

    public long? MinimumProfitCopper { get; }

    public OpportunityRiskPreference RiskPreference { get; }

    public OpportunityStrategyPreference StrategyPreference { get; }

    public int AllocationPercent { get; }

    public int AnalysisQuantity { get; }

    public int? ListingFeeBasisPoints { get; }

    public FeeRounding? ListingFeeRounding { get; }

    public int? ExchangeFeeBasisPoints { get; }

    public FeeRounding? ExchangeFeeRounding { get; }

    public static UserSessionPreferences Create(
        long? capitalLimitCopper,
        long? minimumProfitCopper,
        OpportunityRiskPreference riskPreference,
        OpportunityStrategyPreference strategyPreference,
        int allocationPercent,
        int analysisQuantity = DefaultAnalysisQuantity,
        int? listingFeeBasisPoints = null,
        FeeRounding? listingFeeRounding = null,
        int? exchangeFeeBasisPoints = null,
        FeeRounding? exchangeFeeRounding = null)
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

        if (analysisQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(analysisQuantity));
        }

        ValidateFeeConfiguration(
            listingFeeBasisPoints,
            listingFeeRounding,
            exchangeFeeBasisPoints,
            exchangeFeeRounding);

        return new UserSessionPreferences(
            capitalLimitCopper,
            minimumProfitCopper,
            riskPreference,
            strategyPreference,
            allocationPercent,
            analysisQuantity,
            listingFeeBasisPoints,
            listingFeeRounding,
            exchangeFeeBasisPoints,
            exchangeFeeRounding);
    }

    public long? GetPerOpportunityCapitalLimitCopper() => CapitalLimitCopper is { } capitalLimit
        ? checked(capitalLimit * AllocationPercent / MaximumAllocationPercent)
        : null;

    public bool TryCreateTransactionFeePolicy(out TransactionFeePolicy? feePolicy)
    {
        if (ListingFeeBasisPoints is not { } listingFeeBasisPoints ||
            ListingFeeRounding is not { } listingFeeRounding ||
            ExchangeFeeBasisPoints is not { } exchangeFeeBasisPoints ||
            ExchangeFeeRounding is not { } exchangeFeeRounding)
        {
            feePolicy = null;
            return false;
        }

        try
        {
            feePolicy = new TransactionFeePolicy(
                new FeeRule(listingFeeBasisPoints, listingFeeRounding),
                new FeeRule(exchangeFeeBasisPoints, exchangeFeeRounding));
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            feePolicy = null;
            return false;
        }
    }

    private static void ValidateCopper(long? copper, string parameterName)
    {
        if (copper is < 0 or > MaximumSafeIntegerCopper)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidateFeeConfiguration(
        int? listingFeeBasisPoints,
        FeeRounding? listingFeeRounding,
        int? exchangeFeeBasisPoints,
        FeeRounding? exchangeFeeRounding)
    {
        var values = new object?[]
        {
            listingFeeBasisPoints,
            listingFeeRounding,
            exchangeFeeBasisPoints,
            exchangeFeeRounding,
        };
        if (values.All(value => value is null))
        {
            return;
        }

        if (values.Any(value => value is null))
        {
            throw new ArgumentException("Listing and exchange fee rules must both be fully configured or both be absent.");
        }

        _ = new FeeRule(listingFeeBasisPoints!.Value, listingFeeRounding!.Value);
        _ = new FeeRule(exchangeFeeBasisPoints!.Value, exchangeFeeRounding!.Value);
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

using Gw2Tp.Analytics.Finance;
using Gw2Tp.Analytics.OrderBooks;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.MarketScanning;

/// <summary>
/// Configures one read-only current-market scan. Values remain in application
/// memory; durable user settings are outside the M17-01 scope.
/// </summary>
public sealed record LiveMarketScannerSettings(
    int MinimumRoiBasisPoints,
    Money MinimumNetProfit,
    int BidIncrementCopper,
    int ListUndercutCopper,
    int IntendedQuantity)
{
    public static LiveMarketScannerSettings Default { get; } = new(
        MinimumRoiBasisPoints: 0,
        MinimumNetProfit: new Money(1),
        BidIncrementCopper: 1,
        ListUndercutCopper: 1,
        IntendedQuantity: 1);

    public void Validate()
    {
        if (MinimumRoiBasisPoints < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumRoiBasisPoints));
        }

        if (MinimumNetProfit.Copper < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumNetProfit));
        }

        if (BidIncrementCopper <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(BidIncrementCopper));
        }

        if (ListUndercutCopper <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ListUndercutCopper));
        }

        if (IntendedQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(IntendedQuantity));
        }
    }
}

public enum LiveMarketScannerLiquidityReason
{
    InsufficientBuyListingDepth,
    InsufficientSellListingDepth,
    InsufficientBuyQuantityDepth,
    InsufficientSellQuantityDepth,
    BuyPriceCliff,
    SellPriceCliff,
    IntendedQuantityCannotFullyAcquire,
    IntendedQuantityCannotFullyLiquidate,
    ParticipationCapBelowIntendedQuantity,
}

/// <summary>
/// Visible current-book evidence. It is not a fill guarantee, historical-volume
/// estimate, or final portfolio-size recommendation.
/// </summary>
public sealed record LiveMarketScannerLiquidityEvidence(
    long TotalBuyQuantity,
    long TotalSellQuantity,
    long NearBestBuyQuantity,
    long NearBestSellQuantity,
    long NearBestBuyListings,
    long NearBestSellListings,
    Money? BuyNextLevelGap,
    Money? SellNextLevelGap,
    bool HasBuyPriceCliff,
    bool HasSellPriceCliff,
    OrderBookExecutionScenario Acquisition,
    OrderBookExecutionScenario Liquidation,
    int ParticipationCapQuantity,
    IReadOnlyList<LiveMarketScannerLiquidityReason> Reasons,
    IReadOnlyList<MarketOrderLevel> TopBuyLevels,
    IReadOnlyList<MarketOrderLevel> TopSellLevels);

public enum LiveMarketScannerState
{
    Ready,
    Unavailable,
}

public enum LiveMarketScannerInclusionReason
{
    PositiveModeledProfit,
    MeetsMinimumNetProfit,
    MeetsMinimumRoi,
    UsesConfiguredPricePolicy,
}

public enum LiveMarketScannerExclusionReason
{
    InvalidMarketData,
    PricePolicyInvalid,
    FeeLosing,
    MinimumNetProfitNotMet,
    MinimumRoiNotMet,
    ArithmeticOverflow,
}

public sealed record LiveMarketScannerExclusionCount(
    LiveMarketScannerExclusionReason Reason,
    int Count);

/// <summary>
/// Backend-authoritative one-unit economics and visible execution-depth evidence
/// for a current aggregate market.
/// </summary>
public sealed record LiveMarketScannerCandidate(
    MarketItemMetadata Item,
    MarketOrderSummary BestBuy,
    MarketOrderSummary LowestSell,
    Money PlannedBid,
    Money PlannedListPrice,
    FlipProfitScenario ProfitScenario,
    Money TotalCost,
    ExactRoi ModeledRoi,
    Money MaximumBid,
    IReadOnlyList<LiveMarketScannerInclusionReason> InclusionReasons,
    LiveMarketScannerLiquidityEvidence Liquidity);

public sealed record LiveMarketScannerResult(
    LiveMarketScannerState State,
    Gw2ApiErrorCategory? ErrorCategory,
    DateTimeOffset? ObservedAtUtc,
    LiveMarketScannerSettings Settings,
    bool IsFeeRoundingExternallyVerified,
    int QualifyingCandidateCount,
    bool IsTruncated,
    IReadOnlyList<LiveMarketScannerCandidate> Candidates,
    IReadOnlyList<LiveMarketScannerExclusionCount> Exclusions)
{
    public static LiveMarketScannerResult Unavailable(
        LiveMarketScannerSettings settings,
        Gw2ApiErrorCategory errorCategory) => new(
            LiveMarketScannerState.Unavailable,
            errorCategory,
            null,
            settings,
            IsFeeRoundingExternallyVerified: false,
            QualifyingCandidateCount: 0,
            IsTruncated: false,
            [],
            []);
}

public interface ILiveMarketScanner
{
    Task<LiveMarketScannerResult> ScanAsync(
        LiveMarketScannerSettings settings,
        CancellationToken cancellationToken = default);
}

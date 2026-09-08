using Gw2Tp.Analytics.Finance;
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
    int ListUndercutCopper)
{
    public static LiveMarketScannerSettings Default { get; } = new(
        MinimumRoiBasisPoints: 0,
        MinimumNetProfit: new Money(1),
        BidIncrementCopper: 1,
        ListUndercutCopper: 1);

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
    }
}

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
/// Backend-authoritative one-unit economics for a current aggregate market.
/// Detailed execution depth and suggested quantity are intentionally deferred.
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
    IReadOnlyList<LiveMarketScannerInclusionReason> InclusionReasons);

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

using Gw2Tp.Analytics.Finance;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.Accounting;

/// <summary>
/// Explicit UTC interval describing the transaction history retained for an
/// accounting calculation. It is evidence coverage, not a lifetime claim.
/// </summary>
public sealed record PerformanceHistoryCoverage(DateTimeOffset StartUtc, DateTimeOffset EndUtc);

/// <summary>
/// A public buy-order book observed for one account/item liquidation calculation.
/// The contained listing is application data, not an external DTO.
/// </summary>
public sealed record CurrentMarketLiquidationEvidence(
    long AccountProfileId,
    MarketListing Listing,
    DateTimeOffset ObservedAtUtc);

/// <summary>
/// All explicit evidence required for a deterministic personal-performance rebuild.
/// No persistence, gateway, or clock dependency is implied by this contract.
/// </summary>
public sealed record PersonalPerformanceRequest(
    DateTimeOffset AsOfUtc,
    PerformanceHistoryCoverage Coverage,
    IReadOnlyList<AccountScopedCompletedTransaction> CompletedTransactions,
    IReadOnlyList<CurrentMarketLiquidationEvidence> CurrentMarketEvidence);

public enum RealizedPerformanceWindow
{
    SevenDays = 7,
    ThirtyDays = 30,
    NinetyDays = 90,
}

public enum RealizedPerformanceWindowStatus
{
    Supported,
    InsufficientCoverage,
}

public enum CurrentLiquidationStatus
{
    FullyValued,
    EvidenceMissing,
    InsufficientBuyDepth,
}

/// <summary>
/// A known-basis FIFO allocation of one completed sale. Fees are allocated from
/// the canonical whole-sale fee and reconcile to that fee across all allocations.
/// </summary>
public sealed record KnownBasisRealizedSaleAllocation(
    FifoLotMatch Match,
    Money GrossSale,
    Money ListingFee,
    Money ExchangeFee,
    Money NetSaleProceeds,
    Money NetProfit,
    ExactRoi? Roi);

/// <summary>
/// The fee-bearing portion of a sale for which the acquisition history is absent.
/// It deliberately has no cost, profit, or ROI field.
/// </summary>
public sealed record UnknownBasisRealizedSaleAllocation(
    FifoUnknownBasisSale Sale,
    Money GrossSale,
    Money ListingFee,
    Money ExchangeFee,
    Money NetSaleProceeds);

/// <summary>
/// Aggregated known-basis realized results. These totals describe only retained
/// coverage and never imply lifetime performance.
/// </summary>
public sealed record RealizedPerformance(
    int KnownBasisQuantity,
    Money GrossSales,
    Money ListingFees,
    Money ExchangeFees,
    Money NetSaleProceeds,
    Money AcquisitionBasis,
    Money NetProfit,
    ExactRoi? Roi);

public sealed record RealizedPerformanceWindowResult(
    RealizedPerformanceWindow Window,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    RealizedPerformanceWindowStatus Status,
    RealizedPerformance? KnownBasisPerformance,
    int UnknownBasisQuantity);

/// <summary>
/// Current liquidation evidence and result for the aggregate remaining quantity
/// of one account/item. A partial book never produces a total-value claim.
/// </summary>
public sealed record OpenInventoryLiquidation(
    long AccountProfileId,
    int ItemId,
    int OpenQuantity,
    Money OpenAcquisitionBasis,
    CurrentLiquidationStatus Status,
    DateTimeOffset? MarketObservedAtUtc,
    int UnliquidatedQuantity,
    Money? GrossSaleValue,
    Money? ListingFee,
    Money? ExchangeFee,
    Money? NetLiquidationValue,
    Money? UnrealizedProfit,
    ExactRoi? UnrealizedRoi);

/// <summary>
/// Open known-basis inventory and its current modeled liquidation state.
/// Aggregate current value/profit is present only when every open item is fully
/// valued by explicit current market evidence.
/// </summary>
public sealed record OpenPerformance(
    int OpenQuantity,
    Money OpenAcquisitionBasis,
    bool IsFullyValued,
    Money? NetLiquidationValue,
    Money? UnrealizedProfit,
    ExactRoi? UnrealizedRoi,
    IReadOnlyList<OpenInventoryLiquidation> Items);

/// <summary>
/// Deterministic accounting result. Fee-derived fields remain provisional while
/// VERIFY-013's fractional-copper rounding question is open.
/// </summary>
public sealed record PersonalPerformanceRebuild(
    int FifoPolicyVersion,
    PerformanceHistoryCoverage Coverage,
    bool IsFeeRoundingExternallyVerified,
    RealizedPerformance CoverageRealizedPerformance,
    IReadOnlyList<KnownBasisRealizedSaleAllocation> KnownBasisSaleAllocations,
    IReadOnlyList<UnknownBasisRealizedSaleAllocation> UnknownBasisSaleAllocations,
    IReadOnlyList<RealizedPerformanceWindowResult> Windows,
    OpenPerformance OpenPerformance);

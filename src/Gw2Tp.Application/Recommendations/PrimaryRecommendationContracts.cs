using Gw2Tp.Application.MarketScanning;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.Recommendations;

public enum PrimaryRecommendationState { Ready = 1, NotSynchronized, AccountUnavailable, EvidenceUnavailable }
public enum PrimaryRecommendationAction { Buy = 1, BuySmall, Wait, KeepBid, UpdateBid, StopBidding, CancelBid, List, LeaveSellListing, Hold, Reduce, SellPartial, Sell, Skip, Review }
public enum PrimaryRecommendationSource { NewOpportunity = 1, BuyOrder, SellListing, Inventory }
public enum PrimaryRecommendationOrderState { NotApplicable = 1, Competitive, Outbid, AboveMaximumBid, UndercutOneCopper, UndercutMoreThanOneCopper, MarketUnavailable }

public enum PrimaryRecommendationReasonCode
{
    StrongEvidence = 1, PartialHistory, InsufficientHistory, HighLiquidity, LiquidityRisk,
    HardAnomaly, ZeroScore, NoSizingCapacity, EvidenceMissing, BidAboveMaximum,
    ReserveRestoration, BidCompetitive, BidOutbid, UpdateWithinMaximum,
    IncrementalCapitalAvailable, IncrementalCapitalUnavailable, SellCompetitive,
    OneCopperUndercutProtected, SellMateriallyUndercut, ItemExposureExceeded,
    SafeDepthLimited, PositiveImmediateExit, PositiveListingExit, NoPositiveExit,
    UnknownCostBasis, ReadOnlyManualAction, PenalizedEvidence, FullBuyEvidenceNotMet,
    StrategyExposureExceeded, CategoryExposureExceeded,
}

public sealed record PrimaryRecommendationReason(PrimaryRecommendationReasonCode Code, string Message);
public sealed record PrimaryRecommendationPolicies(
    int ActionPolicyVersion, int ScorePolicyVersion, int PositionSizingPolicyVersion,
    int FifoPolicyVersion, int FeePolicyVersion, int MinimumProfitInCopper,
    int MinimumRoiBasisPoints, int CashReserveBasisPoints, string Strategy, string Category);
public sealed record PrimaryRecommendationPortfolio(
    Money AvailableCash, Money TotalBankroll, Money CashReserve, CashReserveStatus ReserveStatus,
    Money CashReserveShortfall, Money RemainingCashAfterSizing);
public sealed record PrimaryRecommendationEconomics(
    Money AcquisitionCost, Money GrossSaleValue, Money ListingFee, Money ExchangeFee,
    Money NetSaleProceeds, Money NetProfit, Money TotalCost, string RoiDisplayPercent);
public sealed record PrimaryRecommendationHistoryWindow(
    int DurationDays, bool IsAvailable, int RawObservationCount, int EligibleObservationCount, decimal ObservedSpanPercent);
public sealed record PrimaryRecommendationHistory(
    OpportunityHistoricalConfidence Confidence, DateTimeOffset CommonCutoffUtc,
    IReadOnlyList<PrimaryRecommendationHistoryWindow> Windows);
public sealed record PrimaryRecommendationLiquidity(
    PositionSizingLiquidity Classification, long TotalBuyQuantity, long TotalSellQuantity,
    long NearBestBuyQuantity, long NearBestSellQuantity, int ParticipationCapQuantity,
    int SafeLiquidationQuantity, IReadOnlyList<LiveMarketScannerLiquidityReason> Reasons);
public sealed record PrimaryRecommendationPriceState(
    Money? CurrentOrderUnitPrice, Money? BestBuy, Money? LowestSell,
    Money? PlannedBid, Money? PlannedListPrice, Money? MaximumBid);
public sealed record PrimaryRecommendationScore(
    int Rank, decimal TotalPoints, decimal BasePoints, decimal AppliedPenaltyPoints,
    IReadOnlyList<OpportunityScoreComponent> Components, IReadOnlyList<OpportunityScoreAnomaly> Anomalies);

public sealed record PrimaryRecommendationRecord(
    PrimaryRecommendationAction Action, PrimaryRecommendationSource Source,
    PrimaryRecommendationOrderState OrderState, string? OrderId, int ItemId, string ItemName,
    int Quantity, Money Capital, PrimaryRecommendationPriceState Prices,
    PrimaryRecommendationEconomics? Economics, PrimaryRecommendationScore? Score,
    PrimaryRecommendationHistory? History, PrimaryRecommendationLiquidity? Liquidity,
    IReadOnlyList<PositionSizingConstraint> PortfolioConstraints,
    IReadOnlyList<PrimaryRecommendationReason> Reasons);

public sealed record PrimaryRecommendationResult(
    PrimaryRecommendationState State, string? EvidenceError, DateTimeOffset? GeneratedAtUtc,
    DateTimeOffset? LastSuccessfulSyncAtUtc, DateTimeOffset? CurrentOrdersObservedAtUtc,
    DateTimeOffset? ScannerObservedAtUtc, PrimaryRecommendationPolicies Policies,
    PrimaryRecommendationPortfolio? Portfolio, IReadOnlyList<PrimaryRecommendationRecord> Actions)
{
    public static PrimaryRecommendationResult Unavailable(
        PrimaryRecommendationState state, string? evidenceError, PrimaryRecommendationPolicies policies) =>
        new(state, evidenceError, null, null, null, null, policies, null, []);
}

public interface IPrimaryRecommendationService
{
    Task<PrimaryRecommendationResult> GetAsync(CancellationToken cancellationToken = default);
}

public sealed record PrimaryRecommendationEvidence(
    PrimaryRecommendationSource Source, PrimaryRecommendationOrderState OrderState,
    string? OrderId, int ItemId, string ItemName, int CurrentQuantity, int SuggestedQuantity,
    Money SuggestedCapital, PrimaryRecommendationPriceState Prices,
    PrimaryRecommendationEconomics? Economics, PrimaryRecommendationScore? Score,
    PrimaryRecommendationHistory? History, PrimaryRecommendationLiquidity? Liquidity,
    IReadOnlyList<PositionSizingConstraint> PortfolioConstraints, bool IsEvidenceComplete,
    bool IsSizingAvailable, bool IsReserveCancellation, bool IsUnknownBasis,
    bool IsExposureExceeded, bool IsImmediateFullExitPositive,
    bool IsImmediatePartialExitPositive, bool IsListingExitPositive,
    Money IncrementalCapitalRequired, Money IncrementalCapitalCapacity);

public interface IPrimaryRecommendationPolicy
{
    IReadOnlyList<PrimaryRecommendationRecord> Evaluate(IReadOnlyCollection<PrimaryRecommendationEvidence> evidence);
}

using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Analytics.Reconciliation;

/// <summary>
/// The amount of recorded outcome evidence available for an operation.
/// </summary>
public enum OperationReconciliationStatus
{
    NoRecordedActualOutcome = 0,
    UnrealizedOnly = 1,
    PartiallyRealized = 2,
    FullyRealized = 3,
}

/// <summary>
/// Deterministic realized and unrealized values kept deliberately separate.
/// A null realized profit means that no recorded sale has occurred.
/// </summary>
public sealed record OperationReconciliation(
    OperationReconciliationStatus Status,
    int AcquiredQuantity,
    int SoldQuantity,
    int RemainingQuantity,
    Money? RecognizedAcquisitionCost,
    Money? GrossSaleValue,
    Money? ListingFee,
    Money? ExchangeFee,
    Money? NetSaleProceeds,
    Money? RealizedProfit,
    Money? RemainingCostBasis,
    Money? CurrentModeledNetValue,
    Money? UnrealizedProfitLoss);

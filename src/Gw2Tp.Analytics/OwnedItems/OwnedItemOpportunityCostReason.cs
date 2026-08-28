namespace Gw2Tp.Analytics.OwnedItems;

/// <summary>
/// Stable explanation codes for why an owned-item strategy cannot be valued.
/// </summary>
public enum OwnedItemOpportunityCostReason
{
    MissingPurchaseMarketEvidence = 0,
    InsufficientPurchaseMarketDepth = 1,
    MissingImmediateLiquidationMarketEvidence = 2,
    InsufficientImmediateLiquidationMarketDepth = 3,
    MissingListingMarketEvidence = 4,
    InsufficientEligibleOwnedQuantity = 5,
    NoGenuineMixedAllocation = 6,
}

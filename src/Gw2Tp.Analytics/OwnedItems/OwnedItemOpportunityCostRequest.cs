namespace Gw2Tp.Analytics.OwnedItems;

/// <summary>
/// Immutable input for comparing purchase, owned, and mixed acquisition costs
/// for one item quantity.
/// </summary>
public sealed record OwnedItemOpportunityCostRequest
{
    public OwnedItemOpportunityCostRequest(
        int itemId,
        int requiredQuantity,
        IReadOnlyList<OwnedItemLot> ownedLots,
        OwnedItemMarketEvidence marketEvidence,
        OwnedItemValuationRoute valuationRoute)
    {
        if (itemId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemId), "An item ID must be positive.");
        }

        if (requiredQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredQuantity), "A required quantity must be positive.");
        }

        ArgumentNullException.ThrowIfNull(ownedLots);
        ArgumentNullException.ThrowIfNull(marketEvidence);

        if (ownedLots.Any(lot => lot is null))
        {
            throw new ArgumentException("Owned lots cannot contain null values.", nameof(ownedLots));
        }

        if (!Enum.IsDefined(valuationRoute))
        {
            throw new ArgumentOutOfRangeException(nameof(valuationRoute), valuationRoute, "The valuation route is unknown.");
        }

        ItemId = itemId;
        RequiredQuantity = requiredQuantity;
        OwnedLots = Array.AsReadOnly(ownedLots.ToArray());
        MarketEvidence = marketEvidence;
        ValuationRoute = valuationRoute;
    }

    public int ItemId { get; }

    public int RequiredQuantity { get; }

    public IReadOnlyList<OwnedItemLot> OwnedLots { get; }

    public OwnedItemMarketEvidence MarketEvidence { get; }

    public OwnedItemValuationRoute ValuationRoute { get; }
}

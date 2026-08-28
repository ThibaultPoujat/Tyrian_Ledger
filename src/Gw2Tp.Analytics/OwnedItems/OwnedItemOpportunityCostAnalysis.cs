namespace Gw2Tp.Analytics.OwnedItems;

/// <summary>
/// Explainable comparison of all supported acquisition allocations for one item.
/// </summary>
public sealed record OwnedItemOpportunityCostAnalysis
{
    public OwnedItemOpportunityCostAnalysis(
        int itemId,
        int requiredQuantity,
        OwnedItemValuationRoute valuationRoute,
        int eligibleOwnedQuantity,
        IReadOnlyList<OwnedItemRestrictionFlag> restrictionFlags,
        IReadOnlyList<OwnedItemStrategyAnalysis> strategies)
    {
        if (itemId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemId), "An item ID must be positive.");
        }

        if (requiredQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredQuantity), "A required quantity must be positive.");
        }

        if (!Enum.IsDefined(valuationRoute))
        {
            throw new ArgumentOutOfRangeException(nameof(valuationRoute), valuationRoute, "The valuation route is unknown.");
        }

        if (eligibleOwnedQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(eligibleOwnedQuantity), "An eligible owned quantity cannot be negative.");
        }

        ArgumentNullException.ThrowIfNull(restrictionFlags);
        ArgumentNullException.ThrowIfNull(strategies);

        if (restrictionFlags.Any(flag => flag is null))
        {
            throw new ArgumentException("Restriction flags cannot contain null values.", nameof(restrictionFlags));
        }

        if (strategies.Any(strategy => strategy is null))
        {
            throw new ArgumentException("Strategy analyses cannot contain null values.", nameof(strategies));
        }

        if (strategies.Count != Enum.GetValues<OwnedItemStrategy>().Length ||
            strategies.Select(strategy => strategy.Strategy).Distinct().Count() != strategies.Count)
        {
            throw new ArgumentException("Exactly one analysis is required for every owned-item strategy.", nameof(strategies));
        }

        ItemId = itemId;
        RequiredQuantity = requiredQuantity;
        ValuationRoute = valuationRoute;
        EligibleOwnedQuantity = eligibleOwnedQuantity;
        RestrictionFlags = Array.AsReadOnly(restrictionFlags.OrderBy(flag => flag.Restriction).ToArray());
        Strategies = Array.AsReadOnly(strategies.OrderBy(strategy => strategy.Strategy).ToArray());
    }

    public int ItemId { get; }

    public int RequiredQuantity { get; }

    public OwnedItemValuationRoute ValuationRoute { get; }

    public int EligibleOwnedQuantity { get; }

    public IReadOnlyList<OwnedItemRestrictionFlag> RestrictionFlags { get; }

    public IReadOnlyList<OwnedItemStrategyAnalysis> Strategies { get; }
}

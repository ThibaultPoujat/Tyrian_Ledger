namespace Gw2Tp.Analytics.OwnedItems;

/// <summary>
/// The aggregate quantity excluded from economic owned-stock allocation for one reason.
/// </summary>
public sealed record OwnedItemRestrictionFlag
{
    public OwnedItemRestrictionFlag(OwnedItemRestriction restriction, int quantity)
    {
        if (!Enum.IsDefined(restriction))
        {
            throw new ArgumentOutOfRangeException(nameof(restriction), restriction, "The ownership restriction is unknown.");
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "A restricted item quantity must be positive.");
        }

        Restriction = restriction;
        Quantity = quantity;
    }

    public OwnedItemRestriction Restriction { get; }

    public int Quantity { get; }
}

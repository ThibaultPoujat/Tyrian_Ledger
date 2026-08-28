namespace Gw2Tp.Analytics.OwnedItems;

/// <summary>
/// A normalized quantity from the account snapshot. Restricted lots are reported
/// but never assigned a zero-valued market cost.
/// </summary>
public sealed record OwnedItemLot
{
    public OwnedItemLot(int quantity, OwnedItemRestriction? restriction = null)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "An owned item quantity must be positive.");
        }

        if (restriction is { } value && !Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(restriction), value, "The ownership restriction is unknown.");
        }

        Quantity = quantity;
        Restriction = restriction;
    }

    public int Quantity { get; }

    public OwnedItemRestriction? Restriction { get; }
}

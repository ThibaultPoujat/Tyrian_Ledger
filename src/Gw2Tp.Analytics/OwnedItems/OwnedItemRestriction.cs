namespace Gw2Tp.Analytics.OwnedItems;

/// <summary>
/// A caller-known restriction that prevents an owned item lot from being assigned
/// a market opportunity cost.
/// </summary>
public enum OwnedItemRestriction
{
    Bound = 0,
    NonSellable = 1,
}

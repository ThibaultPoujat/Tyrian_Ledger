namespace Gw2Tp.Analytics.OwnedItems;

/// <summary>
/// Selects the market scenario used to value an owned item economically.
/// </summary>
public enum OwnedItemValuationRoute
{
    /// <summary>
    /// Models an immediate sale into currently available buy orders.
    /// </summary>
    ImmediateLiquidation = 0,

    /// <summary>
    /// Models a non-guaranteed listing at the current lowest sell price.
    /// </summary>
    ListingAtBestAsk = 1,
}

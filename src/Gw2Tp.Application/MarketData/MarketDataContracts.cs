namespace Gw2Tp.Application.MarketData;

/// <summary>
/// Top-of-book price data for one item. Monetary values are integer copper.
/// </summary>
public sealed record MarketPrice(
    int ItemId,
    bool IsWhitelisted,
    MarketOrderSummary Buys,
    MarketOrderSummary Sells);

/// <summary>
/// Aggregate quantity and unit price at one side of the order book.
/// </summary>
public sealed record MarketOrderSummary(int Quantity, int UnitPriceInCopper);

/// <summary>
/// Full public order book for one item.
/// </summary>
public sealed record MarketListing(
    int ItemId,
    IReadOnlyList<MarketOrderLevel> Buys,
    IReadOnlyList<MarketOrderLevel> Sells);

/// <summary>
/// One public order-book price level. Monetary values are integer copper.
/// </summary>
public sealed record MarketOrderLevel(
    int Listings,
    int Quantity,
    int UnitPriceInCopper);

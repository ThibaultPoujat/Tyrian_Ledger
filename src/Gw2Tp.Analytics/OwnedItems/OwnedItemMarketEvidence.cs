using Gw2Tp.Analytics.OrderBooks;

namespace Gw2Tp.Analytics.OwnedItems;

/// <summary>
/// The supplied market snapshot for one item. Buy levels support immediate
/// liquidation; sell levels support purchase and best-ask listing scenarios.
/// </summary>
public sealed record OwnedItemMarketEvidence
{
    public OwnedItemMarketEvidence(
        IReadOnlyList<OrderBookLevel> buyLevels,
        IReadOnlyList<OrderBookLevel> sellLevels)
    {
        ArgumentNullException.ThrowIfNull(buyLevels);
        ArgumentNullException.ThrowIfNull(sellLevels);

        if (buyLevels.Any(level => level is null))
        {
            throw new ArgumentException("Buy levels cannot contain null values.", nameof(buyLevels));
        }

        if (sellLevels.Any(level => level is null))
        {
            throw new ArgumentException("Sell levels cannot contain null values.", nameof(sellLevels));
        }

        BuyLevels = Array.AsReadOnly(buyLevels.ToArray());
        SellLevels = Array.AsReadOnly(sellLevels.ToArray());
    }

    public IReadOnlyList<OrderBookLevel> BuyLevels { get; }

    public IReadOnlyList<OrderBookLevel> SellLevels { get; }
}

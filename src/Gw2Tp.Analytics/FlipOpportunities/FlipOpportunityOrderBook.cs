using Gw2Tp.Analytics.OrderBooks;
using Gw2Tp.Domain.MarketData;

namespace Gw2Tp.Analytics.FlipOpportunities;

/// <summary>
/// An immutable order-book snapshot used to model one flip opportunity.
/// </summary>
public sealed record FlipOpportunityOrderBook
{
    public FlipOpportunityOrderBook(
        IReadOnlyList<OrderBookLevel> buyLevels,
        IReadOnlyList<OrderBookLevel> sellLevels,
        DataFreshness? freshness,
        bool isPartialData)
    {
        ArgumentNullException.ThrowIfNull(buyLevels);
        ArgumentNullException.ThrowIfNull(sellLevels);

        ValidateLevels(buyLevels, nameof(buyLevels));
        ValidateLevels(sellLevels, nameof(sellLevels));

        BuyLevels = Array.AsReadOnly(buyLevels.ToArray());
        SellLevels = Array.AsReadOnly(sellLevels.ToArray());
        Freshness = freshness;
        IsPartialData = isPartialData;
    }

    public IReadOnlyList<OrderBookLevel> BuyLevels { get; }

    public IReadOnlyList<OrderBookLevel> SellLevels { get; }

    public DataFreshness? Freshness { get; }

    public bool IsPartialData { get; }

    private static void ValidateLevels(IEnumerable<OrderBookLevel> levels, string parameterName)
    {
        foreach (var level in levels)
        {
            if (level is null)
            {
                throw new ArgumentException("An order-book level cannot be null.", parameterName);
            }
        }
    }
}

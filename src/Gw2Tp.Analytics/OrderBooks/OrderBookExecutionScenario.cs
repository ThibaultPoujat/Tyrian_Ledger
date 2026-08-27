using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Analytics.OrderBooks;

/// <summary>
/// A transparent immediate-execution scenario calculated from a supplied order-book snapshot.
/// </summary>
public sealed record OrderBookExecutionScenario(
    OrderBookExecutionKind Kind,
    int RequestedQuantity,
    int FilledQuantity,
    int RemainingQuantity,
    bool IsFullyFilled,
    IReadOnlyList<OrderBookExecutionFill> Fills,
    Money TotalValue,
    WeightedAverageExecutionPrice? WeightedAverageUnitPrice,
    Money PriceImpact);

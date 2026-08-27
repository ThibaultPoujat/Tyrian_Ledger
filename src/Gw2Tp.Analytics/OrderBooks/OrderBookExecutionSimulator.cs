using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Analytics.OrderBooks;

/// <summary>
/// Simulates immediate acquisition and liquidation against a supplied order-book snapshot.
/// Acquisition consumes the lowest-priced sell levels first; liquidation consumes the
/// highest-priced buy levels first. Results are scenarios, not execution guarantees.
/// </summary>
public sealed class OrderBookExecutionSimulator
{
    /// <summary>
    /// Models acquiring items by consuming available sell levels from the lowest unit price upward.
    /// </summary>
    public OrderBookExecutionScenario SimulateAcquisition(
        IReadOnlyList<OrderBookLevel> sellLevels,
        int requestedQuantity) =>
        Simulate(sellLevels, requestedQuantity, OrderBookExecutionKind.Acquisition);

    /// <summary>
    /// Models liquidating items by consuming available buy levels from the highest unit price downward.
    /// </summary>
    public OrderBookExecutionScenario SimulateLiquidation(
        IReadOnlyList<OrderBookLevel> buyLevels,
        int requestedQuantity) =>
        Simulate(buyLevels, requestedQuantity, OrderBookExecutionKind.Liquidation);

    private static OrderBookExecutionScenario Simulate(
        IReadOnlyList<OrderBookLevel> levels,
        int requestedQuantity,
        OrderBookExecutionKind kind)
    {
        ArgumentNullException.ThrowIfNull(levels);

        if (requestedQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedQuantity),
                "A requested order-book quantity must be positive.");
        }

        foreach (var level in levels)
        {
            ArgumentNullException.ThrowIfNull(level);
        }

        var sortedLevels = kind == OrderBookExecutionKind.Acquisition
            ? levels.OrderBy(level => level.UnitPrice.Copper)
            : levels.OrderByDescending(level => level.UnitPrice.Copper);
        var fills = new List<OrderBookExecutionFill>();
        var filledQuantity = 0;
        var remainingQuantity = requestedQuantity;
        var totalValue = Money.Zero;

        foreach (var level in sortedLevels)
        {
            if (remainingQuantity == 0)
            {
                break;
            }

            var fillQuantity = Math.Min(remainingQuantity, level.Quantity);
            var fillValue = CalculateTotalValue(level.UnitPrice, fillQuantity);
            fills.Add(new OrderBookExecutionFill(fillQuantity, level.UnitPrice, fillValue));
            totalValue += fillValue;
            filledQuantity += fillQuantity;
            remainingQuantity -= fillQuantity;
        }

        WeightedAverageExecutionPrice? weightedAverageUnitPrice = filledQuantity == 0
            ? null
            : new WeightedAverageExecutionPrice(totalValue, filledQuantity);
        var priceImpact = CalculatePriceImpact(kind, fills, filledQuantity, totalValue);

        return new OrderBookExecutionScenario(
            kind,
            requestedQuantity,
            filledQuantity,
            remainingQuantity,
            remainingQuantity == 0,
            fills.AsReadOnly(),
            totalValue,
            weightedAverageUnitPrice,
            priceImpact);
    }

    private static Money CalculatePriceImpact(
        OrderBookExecutionKind kind,
        IReadOnlyList<OrderBookExecutionFill> fills,
        int filledQuantity,
        Money totalValue)
    {
        if (filledQuantity == 0)
        {
            return Money.Zero;
        }

        var bestLevelValue = CalculateTotalValue(fills[0].UnitPrice, filledQuantity);
        return kind == OrderBookExecutionKind.Acquisition
            ? totalValue - bestLevelValue
            : bestLevelValue - totalValue;
    }

    private static Money CalculateTotalValue(Money unitPrice, int quantity) =>
        new(checked(unitPrice.Copper * quantity));
}

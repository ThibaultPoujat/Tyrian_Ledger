using Gw2Tp.Analytics.OrderBooks;
using Gw2Tp.Domain.Finance;
using Xunit;

namespace Gw2Tp.Analytics.Tests.OrderBooks;

public sealed class OrderBookExecutionSimulatorTests
{
    private readonly OrderBookExecutionSimulator _simulator = new();

    [Fact]
    public void Acquires_a_quantity_from_a_single_sell_level()
    {
        var scenario = _simulator.SimulateAcquisition([Level(10, 120)], requestedQuantity: 4);

        Assert.Equal(OrderBookExecutionKind.Acquisition, scenario.Kind);
        Assert.Equal(4, scenario.RequestedQuantity);
        Assert.Equal(4, scenario.FilledQuantity);
        Assert.Equal(0, scenario.RemainingQuantity);
        Assert.True(scenario.IsFullyFilled);
        var fill = Assert.Single(scenario.Fills);
        Assert.Equal(4, fill.Quantity);
        Assert.Equal(new Money(120), fill.UnitPrice);
        Assert.Equal(new Money(480), fill.TotalValue);
        Assert.Equal(new Money(480), scenario.TotalValue);
        Assert.Equal(
            new WeightedAverageExecutionPrice(new Money(480), 4),
            scenario.WeightedAverageUnitPrice!.Value);
        Assert.Equal(Money.Zero, scenario.PriceImpact);
    }

    [Fact]
    public void Acquires_across_unsorted_sell_levels_and_reports_exact_price_impact()
    {
        var scenario = _simulator.SimulateAcquisition(
            [Level(5, 120), Level(3, 100), Level(3, 110)],
            requestedQuantity: 7);

        Assert.Equal(7, scenario.FilledQuantity);
        Assert.True(scenario.IsFullyFilled);
        Assert.Equal(
            [new Money(100), new Money(110), new Money(120)],
            scenario.Fills.Select(fill => fill.UnitPrice));
        Assert.Equal([3, 3, 1], scenario.Fills.Select(fill => fill.Quantity));
        Assert.Equal(new Money(750), scenario.TotalValue);
        Assert.Equal(
            new WeightedAverageExecutionPrice(new Money(750), 7),
            scenario.WeightedAverageUnitPrice!.Value);
        Assert.Equal(new Money(50), scenario.PriceImpact);
    }

    [Fact]
    public void Liquidates_across_unsorted_buy_levels_and_reports_exact_price_impact()
    {
        var scenario = _simulator.SimulateLiquidation(
            [Level(3, 90), Level(3, 110), Level(3, 100)],
            requestedQuantity: 7);

        Assert.Equal(OrderBookExecutionKind.Liquidation, scenario.Kind);
        Assert.Equal([new Money(110), new Money(100), new Money(90)], scenario.Fills.Select(fill => fill.UnitPrice));
        Assert.Equal(new Money(720), scenario.TotalValue);
        Assert.Equal(
            new WeightedAverageExecutionPrice(new Money(720), 7),
            scenario.WeightedAverageUnitPrice!.Value);
        Assert.Equal(new Money(50), scenario.PriceImpact);
    }

    [Fact]
    public void Returns_a_partial_execution_with_the_remaining_quantity_when_depth_is_insufficient()
    {
        var scenario = _simulator.SimulateAcquisition(
            [Level(2, 10), Level(3, 20)],
            requestedQuantity: 10);

        Assert.Equal(10, scenario.RequestedQuantity);
        Assert.Equal(5, scenario.FilledQuantity);
        Assert.Equal(5, scenario.RemainingQuantity);
        Assert.False(scenario.IsFullyFilled);
        Assert.Equal(new Money(80), scenario.TotalValue);
        Assert.Equal(
            new WeightedAverageExecutionPrice(new Money(80), 5),
            scenario.WeightedAverageUnitPrice!.Value);
        Assert.Equal(new Money(30), scenario.PriceImpact);
    }

    [Fact]
    public void Returns_an_explicit_unfilled_scenario_for_an_empty_book()
    {
        var scenario = _simulator.SimulateLiquidation([], requestedQuantity: 5);

        Assert.Equal(0, scenario.FilledQuantity);
        Assert.Equal(5, scenario.RemainingQuantity);
        Assert.False(scenario.IsFullyFilled);
        Assert.Empty(scenario.Fills);
        Assert.Equal(Money.Zero, scenario.TotalValue);
        Assert.Null(scenario.WeightedAverageUnitPrice);
        Assert.Equal(Money.Zero, scenario.PriceImpact);
    }

    [Fact]
    public void Calculates_a_large_quantity_without_losing_copper_precision()
    {
        var scenario = _simulator.SimulateAcquisition(
            [Level(2_000_000_000, 2_000_000_000), Level(147_483_647, 2_100_000_000)],
            requestedQuantity: int.MaxValue);

        Assert.True(scenario.IsFullyFilled);
        Assert.Equal(int.MaxValue, scenario.FilledQuantity);
        Assert.Equal(new Money(4_309_715_658_700_000_000), scenario.TotalValue);
        Assert.Equal(
            new WeightedAverageExecutionPrice(new Money(4_309_715_658_700_000_000), int.MaxValue),
            scenario.WeightedAverageUnitPrice!.Value);
        Assert.Equal(new Money(14_748_364_700_000_000), scenario.PriceImpact);
    }

    [Fact]
    public void Rejects_invalid_input()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OrderBookLevel(0, new Money(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OrderBookLevel(1, new Money(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _simulator.SimulateAcquisition([Level(1, 1)], requestedQuantity: 0));
    }

    private static OrderBookLevel Level(int quantity, long copper) => new(quantity, new Money(copper));
}

using Gw2Tp.Analytics.Finance;
using Gw2Tp.Analytics.OrderBooks;
using Gw2Tp.Application.Finance;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketScanning;
using Gw2Tp.Application.Recommendations;
using Gw2Tp.Domain.Finance;
using Xunit;

namespace Gw2Tp.Application.Tests;

public sealed class PositionSizingServiceTests
{
    [Fact]
    public void High_liquidity_item_cap_scales_linearly_with_bankroll()
    {
        var candidate = Candidate(1, 1, PositionSizingLiquidity.High);
        var service = new PositionSizingService();

        var smaller = Assert.Single(service.Size(Snapshot(100_000), [candidate]).Allocations);
        var larger = Assert.Single(service.Size(Snapshot(200_000), [candidate]).Allocations);

        Assert.Equal(PercentageRoundDown(100_000, 500) / candidate.Market.TotalCost.Copper, smaller.SuggestedQuantity);
        Assert.Equal(PercentageRoundDown(200_000, 500) / candidate.Market.TotalCost.Copper, larger.SuggestedQuantity);
        Assert.Equal(smaller.SuggestedQuantity * 2, larger.SuggestedQuantity);
    }

    [Fact]
    public void Liquidity_class_and_visible_participation_independently_limit_size()
    {
        var service = new PositionSizingService();
        var high = Assert.Single(service.Size(Snapshot(1_000_000), [Candidate(1, 1, PositionSizingLiquidity.High, participationCap: 3)]).Allocations);
        var lowCandidate = Candidate(2, 1, PositionSizingLiquidity.Low, participationCap: 10_000);
        var low = Assert.Single(service.Size(Snapshot(1_000_000), [lowCandidate]).Allocations);

        Assert.Equal(3, high.SuggestedQuantity);
        Assert.Contains(high.Constraints, constraint => constraint.Name == PositionSizingConstraintName.LiquidityParticipation && constraint.IsBinding);
        Assert.Equal(PercentageRoundDown(1_000_000, 150) / lowCandidate.Market.TotalCost.Copper, low.SuggestedQuantity);
        Assert.True(low.SuggestedCapital.Copper < PercentageRoundDown(1_000_000, 500));
    }

    [Fact]
    public void Reserve_cannot_be_bypassed_by_multiple_candidates()
    {
        var candidates = Enumerable.Range(1, 30)
            .Select(itemId => Candidate(itemId, itemId, PositionSizingLiquidity.High, strategy: $"strategy-{itemId}", category: $"category-{itemId}"))
            .Reverse()
            .ToArray();
        var result = new PositionSizingService().Size(Snapshot(10_000), candidates);

        Assert.Equal(PositionSizingResultState.Sized, result.State);
        Assert.Equal(1_500, result.CashReserve!.Value.Copper);
        Assert.True(result.Allocations.Sum(allocation => allocation.SuggestedCapital.Copper) <= 8_500);
        Assert.True(result.RemainingCashAfterSizing!.Value.Copper >= 1_500);
        Assert.Equal(Enumerable.Range(1, 30), result.Allocations.Select(allocation => allocation.ScoreRank));
    }

    [Fact]
    public void Existing_orders_and_positions_reduce_their_respective_caps()
    {
        var candidate = Candidate(1, 1, PositionSizingLiquidity.High, strategy: "Flip", category: "Materials");
        var baseline = Assert.Single(new PositionSizingService().Size(Snapshot(100_000), [candidate]).Allocations);
        var itemLimited = Assert.Single(new PositionSizingService().Size(Snapshot(100_000, [
            Exposure("order", PortfolioExposureKind.CurrentBuyOrder, 1, "Flip", "Materials", 5_000),
        ]), [candidate]).Allocations);
        var strategyLimited = Assert.Single(new PositionSizingService().Size(Snapshot(100_000, [
            Exposure("position", PortfolioExposureKind.HeldPosition, 2, "Flip", "Other", 20_000),
        ]), [candidate]).Allocations);
        var categoryLimited = Assert.Single(new PositionSizingService().Size(Snapshot(100_000, [
            Exposure("listing", PortfolioExposureKind.CurrentSellListing, 2, "Other", "Materials", 30_000),
        ]), [candidate]).Allocations);

        Assert.True(itemLimited.SuggestedCapital.Copper < baseline.SuggestedCapital.Copper);
        Assert.Contains(itemLimited.Constraints, constraint => constraint.Name == PositionSizingConstraintName.ItemExposure && constraint.IsBinding);
        Assert.True(strategyLimited.SuggestedCapital.Copper < baseline.SuggestedCapital.Copper);
        Assert.Contains(strategyLimited.Constraints, constraint => constraint.Name == PositionSizingConstraintName.StrategyConcentration && constraint.IsBinding);
        Assert.True(categoryLimited.SuggestedCapital.Copper < baseline.SuggestedCapital.Copper);
        Assert.Contains(categoryLimited.Constraints, constraint => constraint.Name == PositionSizingConstraintName.CategoryConcentration && constraint.IsBinding);
    }

    [Fact]
    public void Binding_constraints_include_every_equal_minimum()
    {
        var candidate = Candidate(1, 1, PositionSizingLiquidity.High);
        var allocation = Assert.Single(new PositionSizingService().Size(Snapshot(20_000, [
            Exposure("other", PortfolioExposureKind.HeldPosition, 2, "Other", "Other", 80_000),
        ]), [candidate]).Allocations);

        Assert.Equal(PercentageRoundDown(100_000, 500) / candidate.Market.TotalCost.Copper, allocation.SuggestedQuantity);
        Assert.Equal(
            [PositionSizingConstraintName.CashAfterReserve, PositionSizingConstraintName.ItemExposure],
            allocation.Constraints.Where(constraint => constraint.IsBinding).Select(constraint => constraint.Name));
    }

    [Fact]
    public void Simultaneous_candidates_share_strategy_and_category_headroom()
    {
        var candidates = Enumerable.Range(1, 5).Select(itemId => Candidate(itemId, itemId, PositionSizingLiquidity.High, strategy: "Flip", category: "Materials"))
            .Append(Candidate(6, 6, PositionSizingLiquidity.High, strategy: "Other", category: "Other"))
            .ToArray();
        var result = new PositionSizingService().Size(Snapshot(1_000_000), candidates);

        var shared = result.Allocations.Where(allocation => string.Equals(allocation.Strategy, "Flip", StringComparison.OrdinalIgnoreCase));
        Assert.True(shared.Sum(allocation => allocation.SuggestedCapital.Copper) <= 200_000);
        Assert.True(result.Allocations.Single(allocation => allocation.ItemId == 6).SuggestedQuantity > 0);
        Assert.Contains(result.Allocations.Single(allocation => allocation.ItemId == 5).Constraints,
            constraint => constraint.Name == PositionSizingConstraintName.StrategyConcentration && constraint.IsBinding);
    }

    [Theory]
    [MemberData(nameof(UnavailableSnapshots))]
    public void Unknown_negative_or_incomplete_portfolio_state_returns_no_allocation(PortfolioSizingSnapshot snapshot)
    {
        var result = new PositionSizingService().Size(snapshot, [Candidate(1, 1, PositionSizingLiquidity.High)]);

        Assert.Equal(PositionSizingResultState.Unavailable, result.State);
        Assert.Equal(0, Assert.Single(result.Allocations).SuggestedQuantity);
        Assert.Equal(PositionSizingAllocationState.Unavailable, result.Allocations[0].State);
    }

    public static IEnumerable<object[]> UnavailableSnapshots()
    {
        yield return [new PortfolioSizingSnapshot(PortfolioSizingSnapshotState.Unknown, null, null)];
        yield return [Snapshot(-1)];
        yield return [new PortfolioSizingSnapshot(PortfolioSizingSnapshotState.Available, new Money(10_000), null)];
        yield return [Snapshot(10_000, [Exposure("duplicate", PortfolioExposureKind.CurrentBuyOrder, 1, "Flip", "Materials", 1), Exposure("DUPLICATE", PortfolioExposureKind.HeldPosition, 2, "Flip", "Materials", 1)])];
    }

    [Fact]
    public void Missing_group_label_fails_safe_instead_of_bypassing_concentration()
    {
        var invalid = Candidate(1, 1, PositionSizingLiquidity.High) with { Category = " " };
        var result = new PositionSizingService().Size(Snapshot(100_000), [invalid]);

        Assert.Equal(PositionSizingResultState.Unavailable, result.State);
        Assert.Equal(PositionSizingUnavailableReason.InvalidPortfolioSnapshot, result.UnavailableReason);
    }

    [Fact]
    public void Extreme_visible_depth_cannot_overflow_a_constraint_or_allocate_past_financial_caps()
    {
        var candidate = Candidate(1, 1, PositionSizingLiquidity.High, participationCap: int.MaxValue) with
        {
            Market = Candidate(1, 1, PositionSizingLiquidity.High, participationCap: int.MaxValue).Market with { TotalCost = new Money(long.MaxValue) },
        };

        var allocation = Assert.Single(new PositionSizingService().Size(Snapshot(long.MaxValue), [candidate]).Allocations);

        Assert.Equal(0, allocation.SuggestedQuantity);
        Assert.Equal(long.MaxValue, allocation.Constraints.Single(constraint => constraint.Name == PositionSizingConstraintName.LiquidityParticipation).CapitalCapacity.Copper);
    }

    private static PortfolioSizingSnapshot Snapshot(long availableCash, IReadOnlyList<PortfolioExposure>? exposures = null) => new(
        PortfolioSizingSnapshotState.Available,
        new Money(availableCash),
        exposures ?? []);

    private static PortfolioExposure Exposure(string id, PortfolioExposureKind kind, int itemId, string strategy, string category, long capital) =>
        new(id, kind, itemId, strategy, category, new Money(capital));

    private static PositionSizingCandidate Candidate(
        int itemId,
        int rank,
        PositionSizingLiquidity liquidity,
        int participationCap = 10_000,
        string strategy = "Flip",
        string category = "Materials")
    {
        const int bid = 100;
        const int list = 200;
        var levels = new[] { new MarketOrderLevel(5, 10_000, list + 1) };
        var simulator = new OrderBookExecutionSimulator();
        var acquisition = simulator.SimulateAcquisition([new OrderBookLevel(10_000, new Money(list + 1))], 1);
        var liquidation = simulator.SimulateLiquidation([new OrderBookLevel(10_000, new Money(bid - 1))], 1);
        var profit = new FlipProfitCalculator(Gw2TradingPostFeePolicy.Create()).Calculate(new Money(bid), new Money(list));
        var totalCost = Gw2TradingPostFeePolicy.CalculateFullUpFrontCost(new Money(bid), profit.ListingFee);
        var market = new LiveMarketScannerCandidate(
            new MarketItemMetadata(itemId, $"Item {itemId}", MarketItemStackPolicy.NormalStackLimit),
            new MarketOrderSummary(10_000, bid - 1),
            new MarketOrderSummary(10_000, list + 1),
            new Money(bid),
            new Money(list),
            profit,
            totalCost,
            new ExactRoi(profit.NetProfit, totalCost),
            new Money(bid),
            [],
            new LiveMarketScannerLiquidityEvidence(10_000, 10_000, 10_000, 10_000, 5, 5, null, null, false, false, acquisition, liquidation, participationCap, [], levels, levels));
        return new PositionSizingCandidate(new OpportunityScore(rank, itemId, 1, 0m, 0m, 0m, OpportunityHistoricalConfidence.Insufficient, OpportunityPersonalEvidenceState.NotYetAvailable, [], []), market, liquidity, strategy, category);
    }

    private static long PercentageRoundDown(long value, int basisPoints) => value / 10_000 * basisPoints + value % 10_000 * basisPoints / 10_000;
}

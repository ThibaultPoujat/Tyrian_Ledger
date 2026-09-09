using Gw2Tp.Analytics.Finance;
using Gw2Tp.Analytics.MarketHistory;
using Gw2Tp.Analytics.OrderBooks;
using Gw2Tp.Application.Finance;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Application.MarketScanning;
using Gw2Tp.Application.Recommendations;
using Gw2Tp.Domain.Finance;
using Xunit;

namespace Gw2Tp.Application.Tests;

public sealed class OpportunityScoreServiceTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Stable_liquid_moderate_roi_market_outranks_extreme_shallow_market()
    {
        var stable = Input(
            itemId: 1,
            plannedBid: 100,
            plannedListPrice: 150,
            aggregateQuantity: 100,
            nearBestQuantity: 20,
            nearBestListings: 5,
            intendedQuantity: 5,
            participationCap: 10,
            history: History(1, availableSevenDay: true, availableThirtyDay: true, StableSummary()));
        var extreme = Input(
            itemId: 2,
            plannedBid: 10,
            plannedListPrice: 30,
            aggregateQuantity: 1,
            nearBestQuantity: 1,
            nearBestListings: 1,
            intendedQuantity: 5,
            participationCap: 0,
            history: History(2, availableSevenDay: false, availableThirtyDay: false, StableSummary()));

        var scores = new OpportunityScoreService().Calculate([extreme, stable]);

        Assert.Equal([1, 2], scores.Select(score => score.ItemId));
        var stableScore = scores[0];
        var extremeScore = scores[1];
        Assert.Equal(2_500m, RoiBasisPoints(stable.Current.ModeledRoi));
        Assert.Equal(12_500m, RoiBasisPoints(extreme.Current.ModeledRoi));
        Assert.Equal(stable.Current.BestBuy.UnitPriceInCopper + 1, stable.Current.PlannedBid.Copper);
        Assert.Equal(stable.Current.LowestSell.UnitPriceInCopper - 1, stable.Current.PlannedListPrice.Copper);
        Assert.True(stable.Current.Liquidity.Acquisition.IsFullyFilled);
        Assert.True(stable.Current.Liquidity.Liquidation.IsFullyFilled);
        Assert.Equal(1, extreme.Current.Liquidity.Acquisition.FilledQuantity);
        Assert.Equal(4, extreme.Current.Liquidity.Acquisition.RemainingQuantity);
        Assert.False(extreme.Current.Liquidity.Acquisition.IsFullyFilled);
        Assert.Equal(1, extreme.Current.Liquidity.Liquidation.FilledQuantity);
        Assert.Equal(4, extreme.Current.Liquidity.Liquidation.RemainingQuantity);
        Assert.False(extreme.Current.Liquidity.Liquidation.IsFullyFilled);
        Assert.Equal(12.5270m, Component(stableScore, OpportunityScoreComponentName.ExpectedEconomics).AwardedPoints);
        Assert.Equal(25m, Component(stableScore, OpportunityScoreComponentName.CurrentLiquidity).AwardedPoints);
        Assert.Equal(20m, Component(stableScore, OpportunityScoreComponentName.HistoricalPersistence).AwardedPoints);
        Assert.Equal(15m, Component(stableScore, OpportunityScoreComponentName.HistoricalStability).AwardedPoints);
        Assert.Equal(15m, Component(stableScore, OpportunityScoreComponentName.HistoricalConfidence).AwardedPoints);
        Assert.Equal(87.5270m, stableScore.BasePoints);
        Assert.Equal(0m, stableScore.AppliedPenaltyPoints);
        Assert.Equal(87.5270m, stableScore.TotalPoints);
        Assert.Equal(3.8667m, Component(extremeScore, OpportunityScoreComponentName.CurrentLiquidity).AwardedPoints);
        Assert.Equal(18.8817m, extremeScore.BasePoints);
        Assert.Equal(25m, extremeScore.AppliedPenaltyPoints);
        Assert.Equal(0m, extremeScore.TotalPoints);
        Assert.Contains(extremeScore.Anomalies, anomaly => anomaly.Flag == OpportunityAnomalyFlag.ShallowBestLevels);
        Assert.Contains(extremeScore.Anomalies, anomaly => anomaly.Flag == OpportunityAnomalyFlag.IntendedPositionExceedsVisibleDepth);
    }

    [Fact]
    public void Score_exposes_named_bounded_components_policy_and_personal_placeholder()
    {
        var score = Assert.Single(new OpportunityScoreService().Calculate([
            Input(1, 1_000, 1_500, 100, 20, 5, 5, 10, History(1, true, true, StableSummary())),
        ]));

        Assert.Equal(OpportunityScorePolicy.CurrentVersion, score.PolicyVersion);
        Assert.Equal(Enum.GetValues<OpportunityScoreComponentName>(), score.Components.Select(component => component.Name));
        Assert.All(score.Components, component => Assert.InRange(component.NormalizedPercent, 0m, 100m));
        Assert.All(score.Components, component => Assert.InRange(component.AwardedPoints, 0m, component.MaximumPoints));
        Assert.Equal(score.BasePoints, score.Components.Sum(component => component.AwardedPoints));
        Assert.InRange(score.TotalPoints, 0m, 100m);
        Assert.Equal(OpportunityHistoricalConfidence.Strong, score.HistoricalConfidence);
        Assert.Equal(OpportunityPersonalEvidenceState.NotYetAvailable, score.PersonalEvidenceState);
        var personal = Assert.Single(score.Components, component => component.Name == OpportunityScoreComponentName.PersonalEvidence);
        Assert.Equal(OpportunityScoreComponentState.NotYetSupported, personal.State);
        Assert.Equal(0m, personal.MaximumPoints);
    }

    [Fact]
    public void Component_normalization_reproduces_independently_known_midrange_values()
    {
        var score = Assert.Single(new OpportunityScoreService().Calculate([
            Input(
                1,
                plannedBid: 100,
                plannedListPrice: 200,
                aggregateQuantity: 50,
                nearBestQuantity: 2,
                nearBestListings: 1,
                intendedQuantity: 5,
                participationCap: 5,
                History(1, true, true, StableSummary())),
        ]));

        var economics = Assert.Single(score.Components, component => component.Name == OpportunityScoreComponentName.ExpectedEconomics);
        var liquidity = Assert.Single(score.Components, component => component.Name == OpportunityScoreComponentName.CurrentLiquidity);
        Assert.Equal(60.28m, economics.NormalizedPercent);
        Assert.Equal(15.07m, economics.AwardedPoints);
        Assert.Equal(62.6667m, liquidity.NormalizedPercent);
        Assert.Equal(15.6667m, liquidity.AwardedPoints);
        Assert.Equal(20m, Assert.Single(score.Components, component => component.Name == OpportunityScoreComponentName.HistoricalPersistence).AwardedPoints);
        Assert.Equal(15m, Assert.Single(score.Components, component => component.Name == OpportunityScoreComponentName.HistoricalStability).AwardedPoints);
        Assert.Equal(15m, Assert.Single(score.Components, component => component.Name == OpportunityScoreComponentName.HistoricalConfidence).AwardedPoints);
    }

    [Fact]
    public void Insufficient_history_lowers_confidence_without_inventing_persistence_or_stability()
    {
        var score = Assert.Single(new OpportunityScoreService().Calculate([
            Input(1, 1_000, 1_500, 100, 20, 5, 5, 10, History(1, false, false, StableSummary())),
        ]));

        Assert.Equal(OpportunityHistoricalConfidence.Insufficient, score.HistoricalConfidence);
        foreach (var name in new[]
                 {
                     OpportunityScoreComponentName.HistoricalPersistence,
                     OpportunityScoreComponentName.HistoricalStability,
                     OpportunityScoreComponentName.HistoricalConfidence,
                 })
        {
            var component = Assert.Single(score.Components, value => value.Name == name);
            Assert.Equal(OpportunityScoreComponentState.InsufficientData, component.State);
            Assert.Equal(0m, component.AwardedPoints);
        }
        var flag = Assert.Single(score.Anomalies, anomaly => anomaly.Flag == OpportunityAnomalyFlag.InsufficientHistoricalCoverage);
        Assert.Equal(0m, flag.PenaltyPoints);
    }

    [Fact]
    public void Extreme_economics_and_combined_anomalies_remain_bounded()
    {
        var input = Input(
            1,
            plannedBid: 2,
            plannedListPrice: 1_000_000,
            aggregateQuantity: 1,
            nearBestQuantity: 1,
            nearBestListings: 1,
            intendedQuantity: 100,
            participationCap: 0,
            History(1, true, true, StableSummary()),
            hasPriceCliff: true);

        var score = Assert.Single(new OpportunityScoreService().Calculate([input]));

        Assert.Equal(25m, Assert.Single(score.Components, component => component.Name == OpportunityScoreComponentName.ExpectedEconomics).AwardedPoints);
        Assert.Equal(OpportunityScorePolicy.Default.MaximumAppliedPenaltyPoints, score.AppliedPenaltyPoints);
        Assert.InRange(score.TotalPoints, 0m, 100m);
        Assert.True(score.Anomalies.Sum(anomaly => anomaly.PenaltyPoints) > score.AppliedPenaltyPoints);
    }

    [Fact]
    public void Policy_rejects_component_weights_that_can_exceed_one_hundred_points()
    {
        var invalid = OpportunityScorePolicy.Default with { ExpectedEconomicsMaximumPoints = 26m };

        Assert.Throws<ArgumentOutOfRangeException>(() => new OpportunityScoreService(invalid));
    }

    [Fact]
    public void Repeated_and_reordered_inputs_produce_identical_rank_and_contributions()
    {
        var first = Input(1, 1_000, 1_500, 100, 20, 5, 5, 10, History(1, true, true, StableSummary()));
        var second = Input(2, 900, 1_350, 80, 10, 4, 5, 8, History(2, true, false, StableSummary()));
        var service = new OpportunityScoreService();

        var ordered = service.Calculate([first, second]);
        var reversed = service.Calculate([second, first]);

        AssertScoresEqual(ordered, reversed);
        AssertScoresEqual(ordered, service.Calculate([first, second]));
    }

    [Fact]
    public void Equal_score_uses_profit_then_item_id_as_stable_tie_breakers()
    {
        var lowProfit = Input(3, 1_000, 1_500, 100, 20, 5, 5, 10, History(3, true, true, StableSummary()));
        var highProfitHighId = Input(2, 2_000, 3_000, 100, 20, 5, 5, 10, History(2, true, true, StableSummary()));
        var highProfitLowId = Input(1, 2_000, 3_000, 100, 20, 5, 5, 10, History(1, true, true, StableSummary()));

        var scores = new OpportunityScoreService().Calculate([lowProfit, highProfitHighId, highProfitLowId]);

        Assert.Equal([1, 2, 3], scores.Select(score => score.ItemId));
    }

    [Fact]
    public void Available_thirty_day_window_is_the_baseline_and_partial_coverage_is_explicit()
    {
        var unstableThirtyDay = StableSummary() with
        {
            PositiveNetRoiPercent = 0m,
            RoiThresholdRates = [new(1_500, 0m), new(2_000, 0m)],
            BuyPricePopulationCoefficientOfVariation = 0.5,
            SellPricePopulationCoefficientOfVariation = 0.5,
            SpreadRatioPopulationCoefficientOfVariation = 0.5,
            MinimumSideDepthPopulationCoefficientOfVariation = 0.5,
        };
        var history = History(1, availableSevenDay: true, availableThirtyDay: true, StableSummary(), unstableThirtyDay);

        var score = Assert.Single(new OpportunityScoreService().Calculate([
            Input(1, 1_000, 1_500, 100, 20, 5, 5, 10, history),
        ]));

        Assert.Equal(0m, Assert.Single(score.Components, component => component.Name == OpportunityScoreComponentName.HistoricalPersistence).AwardedPoints);
        Assert.Equal(0m, Assert.Single(score.Components, component => component.Name == OpportunityScoreComponentName.HistoricalStability).AwardedPoints);
        Assert.Equal(OpportunityHistoricalConfidence.Strong, score.HistoricalConfidence);
    }

    [Fact]
    public void One_available_window_has_partial_confidence_and_flags_missing_coverage()
    {
        var score = Assert.Single(new OpportunityScoreService().Calculate([
            Input(1, 1_000, 1_500, 100, 20, 5, 5, 10, History(1, true, false, StableSummary())),
        ]));

        Assert.Equal(OpportunityHistoricalConfidence.Partial, score.HistoricalConfidence);
        Assert.Equal(5m, Assert.Single(score.Components, component => component.Name == OpportunityScoreComponentName.HistoricalConfidence).AwardedPoints);
        Assert.Contains(score.Anomalies, anomaly => anomaly.Flag == OpportunityAnomalyFlag.InsufficientHistoricalCoverage);
        Assert.Equal(20m, Assert.Single(score.Components, component => component.Name == OpportunityScoreComponentName.HistoricalPersistence).AwardedPoints);
    }

    [Fact]
    public void Required_anomaly_flags_are_emitted_from_actual_current_and_historical_evidence()
    {
        var input = Input(
            1,
            plannedBid: 131,
            plannedListPrice: 249,
            aggregateQuantity: 10,
            nearBestQuantity: 1,
            nearBestListings: 1,
            intendedQuantity: 5,
            participationCap: 1,
            History(1, true, true, PriceBaseline()),
            hasPriceCliff: true);

        var flags = Assert.Single(new OpportunityScoreService().Calculate([input])).Anomalies.Select(anomaly => anomaly.Flag).ToArray();

        Assert.Contains(OpportunityAnomalyFlag.ExtremeCurrentRoiVersusHistory, flags);
        Assert.Contains(OpportunityAnomalyFlag.ShallowBestLevels, flags);
        Assert.Contains(OpportunityAnomalyFlag.CurrentPriceCliff, flags);
        Assert.Contains(OpportunityAnomalyFlag.AbruptPriceSpike, flags);
        Assert.Contains(OpportunityAnomalyFlag.AbruptDepthChange, flags);
        Assert.Contains(OpportunityAnomalyFlag.IntendedPositionExceedsVisibleDepth, flags);
        Assert.DoesNotContain(OpportunityAnomalyFlag.InsufficientHistoricalCoverage, flags);
    }

    [Fact]
    public void Price_deviation_boundary_is_not_flagged_but_a_value_beyond_it_is()
    {
        var atBoundary = Input(1, 121, 239, 100, 20, 5, 5, 10, History(1, true, true, PriceBaseline()));
        var beyondDrop = Input(2, 80, 118, 100, 20, 5, 5, 10, History(2, true, true, PriceBaseline()));

        var results = new OpportunityScoreService().Calculate([atBoundary, beyondDrop]).ToDictionary(score => score.ItemId);

        Assert.DoesNotContain(results[1].Anomalies, anomaly => anomaly.Flag is OpportunityAnomalyFlag.AbruptPriceSpike or OpportunityAnomalyFlag.AbruptPriceDrop);
        Assert.Contains(results[2].Anomalies, anomaly => anomaly.Flag == OpportunityAnomalyFlag.AbruptPriceDrop);
    }

    [Fact]
    public void Duplicate_items_and_mismatched_history_are_rejected()
    {
        var input = Input(1, 1_000, 1_500, 100, 20, 5, 5, 10, History(1, true, true, StableSummary()));
        var mismatched = input with { History = History(2, true, true, StableSummary()) };
        var service = new OpportunityScoreService();

        Assert.Throws<ArgumentException>(() => service.Calculate([input, input]));
        Assert.Throws<ArgumentException>(() => service.Calculate([mismatched]));
    }

    [Fact]
    public void Execution_claims_that_exceed_visible_depth_are_rejected()
    {
        var shallow = Input(1, 10, 30, 1, 1, 1, 5, 0, History(1, false, false, StableSummary()));
        var falseFullFill = shallow with
        {
            Current = shallow.Current with
            {
                Liquidity = shallow.Current.Liquidity with
                {
                    Acquisition = shallow.Current.Liquidity.Acquisition with
                    {
                        FilledQuantity = 5,
                        RemainingQuantity = 0,
                        IsFullyFilled = true,
                    },
                },
            },
        };

        Assert.Throws<ArgumentException>(() => new OpportunityScoreService().Calculate([falseFullFill]));
    }

    private static OpportunityScoreCandidate Input(
        int itemId,
        long plannedBid,
        long plannedListPrice,
        int aggregateQuantity,
        int nearBestQuantity,
        int nearBestListings,
        int intendedQuantity,
        int participationCap,
        HistoricalMarketAnalytics history,
        bool hasPriceCliff = false)
    {
        if (plannedBid is <= 1 or > int.MaxValue || plannedListPrice is <= 0 or >= int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(plannedBid));
        }

        var bestBuyPrice = checked((int)plannedBid - 1);
        var lowestSellPrice = checked((int)plannedListPrice + 1);

        var buys = OrderLevels(aggregateQuantity, nearBestQuantity, nearBestListings, bestBuyPrice, isBuy: true);
        var sells = OrderLevels(aggregateQuantity, nearBestQuantity, nearBestListings, lowestSellPrice, isBuy: false);
        var simulator = new OrderBookExecutionSimulator();
        var acquisition = simulator.SimulateAcquisition(
            sells.Select(level => new OrderBookLevel(level.Quantity, new Money(level.UnitPriceInCopper))).ToArray(),
            intendedQuantity);
        var liquidation = simulator.SimulateLiquidation(
            buys.Select(level => new OrderBookLevel(level.Quantity, new Money(level.UnitPriceInCopper))).ToArray(),
            intendedQuantity);
        var profit = new FlipProfitCalculator(Gw2TradingPostFeePolicy.Create()).Calculate(new Money(plannedBid), new Money(plannedListPrice));
        var totalCost = Gw2TradingPostFeePolicy.CalculateFullUpFrontCost(new Money(plannedBid), profit.ListingFee);
        var liquidity = new LiveMarketScannerLiquidityEvidence(
            aggregateQuantity,
            aggregateQuantity,
            nearBestQuantity,
            nearBestQuantity,
            nearBestListings,
            nearBestListings,
            hasPriceCliff ? new Money(10) : null,
            hasPriceCliff ? new Money(10) : null,
            hasPriceCliff,
            hasPriceCliff,
            acquisition,
            liquidation,
            participationCap,
            [],
            buys,
            sells);
        var current = new LiveMarketScannerCandidate(
            new MarketItemMetadata(itemId, $"Item {itemId}", MarketItemStackPolicy.NormalStackLimit),
            new MarketOrderSummary(aggregateQuantity, bestBuyPrice),
            new MarketOrderSummary(aggregateQuantity, lowestSellPrice),
            new Money(plannedBid),
            new Money(plannedListPrice),
            profit,
            totalCost,
            new ExactRoi(profit.NetProfit, totalCost),
            new Money(bestBuyPrice),
            [],
            liquidity);
        return new OpportunityScoreCandidate(current, history);
    }

    private static IReadOnlyList<MarketOrderLevel> OrderLevels(
        int aggregateQuantity,
        int nearBestQuantity,
        int nearBestListings,
        int bestPrice,
        bool isBuy)
    {
        if (aggregateQuantity <= 0 || nearBestQuantity <= 0 || nearBestQuantity > aggregateQuantity || nearBestListings <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(aggregateQuantity));
        }

        var levels = new List<MarketOrderLevel> { new(nearBestListings, nearBestQuantity, bestPrice) };
        if (aggregateQuantity > nearBestQuantity)
        {
            levels.Add(new MarketOrderLevel(1, aggregateQuantity - nearBestQuantity, isBuy ? bestPrice - 1 : bestPrice + 1));
        }

        return levels;
    }

    private static HistoricalMarketAnalytics History(
        int itemId,
        bool availableSevenDay,
        bool availableThirtyDay,
        HistoricalMarketMetricSummary sevenDaySummary,
        HistoricalMarketMetricSummary? thirtyDaySummary = null) => new(
        itemId,
        AsOf,
        IsFeeRoundingExternallyVerified: false,
        HistoricalMarketAnalyticsSettings.Default,
        LatestObservedNetRoi: null,
        [
            Window(TimeSpan.FromDays(7), availableSevenDay, sevenDaySummary),
            Window(TimeSpan.FromDays(30), availableThirtyDay, thirtyDaySummary ?? sevenDaySummary),
        ]);

    private static HistoricalMarketWindowAnalytics Window(
        TimeSpan duration,
        bool available,
        HistoricalMarketMetricSummary summary) => new(
        available ? HistoricalMarketWindowState.Available : HistoricalMarketWindowState.InsufficientData,
        new HistoricalMarketWindowCoverage(
            AsOf - duration,
            AsOf,
            RawObservationCount: available ? 100 : 0,
            EligibleObservationCount: available ? 100 : 0,
            ExcludedObservationCount: 0,
            FirstEligibleObservedAtUtc: available ? AsOf - duration : null,
            LastEligibleObservedAtUtc: available ? AsOf : null,
            ObservedSpanPercent: available ? 100m : 0m,
            LargestEligibleObservationGap: available ? TimeSpan.FromHours(8) : null),
        available ? summary : null);

    private static HistoricalMarketMetricSummary StableSummary() => new(
        MedianNetRoiBasisPoints: 2_500m,
        RoiThresholdRates: [new(1_500, 100m), new(2_000, 100m)],
        PositiveNetRoiPercent: 100m,
        BuyPricePopulationCoefficientOfVariation: 0d,
        SellPricePopulationCoefficientOfVariation: 0d,
        SpreadRatioPopulationCoefficientOfVariation: 0d,
        MedianAggregateBuyQuantity: 100m,
        MedianAggregateSellQuantity: 100m,
        MinimumSideDepthPopulationCoefficientOfVariation: 0d,
        BuyPriceRange: new HistoricalPriceRange(99, 99),
        SellPriceRange: new HistoricalPriceRange(151, 151),
        MaximumSellPriceDrawdownPercent: 0d);

    private static HistoricalMarketMetricSummary PriceBaseline() => StableSummary() with
    {
        BuyPriceRange = new HistoricalPriceRange(100, 100),
        SellPriceRange = new HistoricalPriceRange(200, 200),
    };

    private static void AssertScoresEqual(IReadOnlyList<OpportunityScore> expected, IReadOnlyList<OpportunityScore> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index] with { Components = [], Anomalies = [] }, actual[index] with { Components = [], Anomalies = [] });
            Assert.Equal(expected[index].Components, actual[index].Components);
            Assert.Equal(expected[index].Anomalies, actual[index].Anomalies);
        }
    }

    private static OpportunityScoreComponent Component(OpportunityScore score, OpportunityScoreComponentName name) =>
        Assert.Single(score.Components, component => component.Name == name);

    private static decimal RoiBasisPoints(ExactRoi roi) => roi.Profit.Copper * 10_000m / roi.TotalCost.Copper;
}

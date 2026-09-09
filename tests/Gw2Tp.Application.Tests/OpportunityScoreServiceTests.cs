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
            acquisitionCost: 1_000,
            grossSale: 1_500,
            aggregateQuantity: 100,
            nearBestQuantity: 20,
            nearBestListings: 5,
            intendedQuantity: 5,
            participationCap: 10,
            history: History(1, availableSevenDay: true, availableThirtyDay: true, StableSummary()));
        var extreme = Input(
            itemId: 2,
            acquisitionCost: 10,
            grossSale: 30,
            aggregateQuantity: 1,
            nearBestQuantity: 1,
            nearBestListings: 1,
            intendedQuantity: 5,
            participationCap: 0,
            history: History(2, availableSevenDay: false, availableThirtyDay: false, StableSummary()));

        var scores = new OpportunityScoreService().Calculate([extreme, stable]);

        Assert.Equal([1, 2], scores.Select(score => score.ItemId));
        Assert.True(scores[0].TotalPoints > scores[1].TotalPoints);
        Assert.Contains(scores[1].Anomalies, anomaly => anomaly.Flag == OpportunityAnomalyFlag.ShallowBestLevels);
        Assert.Contains(scores[1].Anomalies, anomaly => anomaly.Flag == OpportunityAnomalyFlag.IntendedPositionExceedsVisibleDepth);
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
                acquisitionCost: 100,
                grossSale: 200,
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
            acquisitionCost: 1,
            grossSale: 1_000_000,
            aggregateQuantity: 1,
            nearBestQuantity: 0,
            nearBestListings: 0,
            intendedQuantity: 100,
            participationCap: 0,
            History(1, true, true, StableSummary()),
            hasPriceCliff: true,
            currentBuyPrice: 1_000,
            currentSellPrice: 2_000);

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
            acquisitionCost: 100,
            grossSale: 400,
            aggregateQuantity: 10,
            nearBestQuantity: 1,
            nearBestListings: 1,
            intendedQuantity: 5,
            participationCap: 1,
            History(1, true, true, StableSummary()),
            hasPriceCliff: true,
            currentBuyPrice: 130,
            currentSellPrice: 250);

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
        var atBoundary = Input(1, 1_000, 1_500, 100, 20, 5, 5, 10, History(1, true, true, StableSummary()), currentBuyPrice: 120, currentSellPrice: 240);
        var beyondDrop = Input(2, 1_000, 1_500, 100, 20, 5, 5, 10, History(2, true, true, StableSummary()), currentBuyPrice: 79, currentSellPrice: 119);

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

    private static OpportunityScoreCandidate Input(
        int itemId,
        long acquisitionCost,
        long grossSale,
        int aggregateQuantity,
        int nearBestQuantity,
        int nearBestListings,
        int intendedQuantity,
        int participationCap,
        HistoricalMarketAnalytics history,
        bool hasPriceCliff = false,
        int currentBuyPrice = 100,
        int currentSellPrice = 200)
    {
        var profit = new FlipProfitCalculator(Gw2TradingPostFeePolicy.Create()).Calculate(new Money(acquisitionCost), new Money(grossSale));
        var totalCost = Gw2TradingPostFeePolicy.CalculateFullUpFrontCost(new Money(acquisitionCost), profit.ListingFee);
        var acquisition = Execution(OrderBookExecutionKind.Acquisition, intendedQuantity);
        var liquidation = Execution(OrderBookExecutionKind.Liquidation, intendedQuantity);
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
            [],
            []);
        var current = new LiveMarketScannerCandidate(
            new MarketItemMetadata(itemId, $"Item {itemId}", MarketItemStackPolicy.NormalStackLimit),
            new MarketOrderSummary(aggregateQuantity, currentBuyPrice),
            new MarketOrderSummary(aggregateQuantity, currentSellPrice),
            new Money(acquisitionCost),
            new Money(grossSale),
            profit,
            totalCost,
            new ExactRoi(profit.NetProfit, totalCost),
            new Money(currentBuyPrice),
            [],
            liquidity);
        return new OpportunityScoreCandidate(current, history);
    }

    private static OrderBookExecutionScenario Execution(OrderBookExecutionKind kind, int requestedQuantity) => new(
        kind,
        requestedQuantity,
        requestedQuantity,
        0,
        true,
        [],
        Money.Zero,
        null,
        Money.Zero);

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
        MedianNetRoiBasisPoints: 2_000m,
        RoiThresholdRates: [new(1_500, 100m), new(2_000, 100m)],
        PositiveNetRoiPercent: 100m,
        BuyPricePopulationCoefficientOfVariation: 0d,
        SellPricePopulationCoefficientOfVariation: 0d,
        SpreadRatioPopulationCoefficientOfVariation: 0d,
        MedianAggregateBuyQuantity: 100m,
        MedianAggregateSellQuantity: 100m,
        MinimumSideDepthPopulationCoefficientOfVariation: 0d,
        BuyPriceRange: new HistoricalPriceRange(80, 100),
        SellPriceRange: new HistoricalPriceRange(150, 200),
        MaximumSellPriceDrawdownPercent: 0d);

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
}

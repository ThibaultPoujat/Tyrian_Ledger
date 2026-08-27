using Gw2Tp.Analytics.Finance;
using Gw2Tp.Analytics.FlipOpportunities;
using Gw2Tp.Analytics.OrderBooks;
using Gw2Tp.Domain.Finance;
using Gw2Tp.Domain.MarketData;
using Xunit;

namespace Gw2Tp.Analytics.Tests.FlipOpportunities;

public sealed class FlipOpportunityAnalyzerTests
{
    [Fact]
    public void Analyzes_a_known_good_fixture_with_exact_profit_roi_and_liquidity()
    {
        var analysis = CreateAnalyzer().Analyze(FlipOpportunityFixtures.KnownGood());

        Assert.Equal(FlipOpportunityUsability.Usable, analysis.Usability);
        Assert.Equal(FlipOpportunityConfidence.Normal, analysis.Confidence);
        Assert.True(analysis.MeetsFinancialConstraints);
        Assert.True(analysis.IsEligible);
        Assert.Empty(analysis.Reasons);

        Assert.Equal(900_001, analysis.Scenario.ItemId);
        Assert.Equal(5, analysis.Scenario.RequestedQuantity);
        Assert.Equal(FlipOpportunityFixtures.AnalysisAtUtc, analysis.Scenario.AnalyzedAtUtc);
        Assert.Equal(500, analysis.Scenario.ListingFeeRule.BasisPoints);
        Assert.Equal(1_000, analysis.Scenario.ExchangeFeeRule.BasisPoints);

        var acquisition = Assert.IsType<OrderBookExecutionScenario>(analysis.AcquisitionExecution);
        Assert.Equal([new Money(100), new Money(110)], acquisition.Fills.Select(fill => fill.UnitPrice));
        Assert.Equal([3, 2], acquisition.Fills.Select(fill => fill.Quantity));
        Assert.Equal(new Money(520), acquisition.TotalValue);
        Assert.Equal(new Money(20), acquisition.PriceImpact);

        var liquidation = Assert.IsType<OrderBookExecutionScenario>(analysis.LiquidationExecution);
        Assert.Equal(new Money(750), liquidation.TotalValue);
        Assert.Equal(Money.Zero, liquidation.PriceImpact);

        var liquidity = Assert.IsType<FlipLiquidityMetrics>(analysis.Liquidity);
        Assert.True(liquidity.IsFullyAcquirable);
        Assert.True(liquidity.IsFullyLiquidatable);
        Assert.Equal(5, liquidity.AcquisitionFilledQuantity);
        Assert.Equal(5, liquidity.LiquidationFilledQuantity);
        Assert.Equal(new Money(20), liquidity.TotalPriceImpact);

        var profit = Assert.IsType<FlipProfitScenario>(analysis.Profit);
        Assert.Equal(new Money(37), profit.ListingFee);
        Assert.Equal(new Money(75), profit.ExchangeFee);
        Assert.Equal(new Money(638), profit.NetSaleProceeds);
        Assert.Equal(new Money(118), profit.NetProfit);
        Assert.Equal(new Money(557), analysis.CapitalRequired!.Value);
        Assert.Equal(
            new ExactReturnOnInvestment(new Money(118), new Money(557)),
            analysis.ReturnOnInvestment!.Value);
    }

    [Fact]
    public void Represents_a_known_negative_profit_fixture_without_marking_data_unusable()
    {
        var analysis = CreateAnalyzer().Analyze(FlipOpportunityFixtures.NegativeProfit());

        Assert.Equal(FlipOpportunityUsability.Usable, analysis.Usability);
        Assert.Equal(FlipOpportunityConfidence.Normal, analysis.Confidence);
        Assert.False(analysis.MeetsFinancialConstraints);
        Assert.False(analysis.IsEligible);
        Assert.Equal([FlipAnalysisReason.BelowMinimumNetProfit], analysis.Reasons);
        Assert.Equal(new Money(-112), analysis.Profit!.NetProfit);
        Assert.Equal(new Money(787), analysis.CapitalRequired!.Value);
        Assert.Equal(
            new ExactReturnOnInvestment(new Money(-112), new Money(787)),
            analysis.ReturnOnInvestment!.Value);
    }

    [Fact]
    public void Marks_the_stale_fixture_as_lower_confidence_by_default_or_unusable_by_policy()
    {
        var staleFixture = FlipOpportunityFixtures.Stale();

        var lowerConfidenceAnalysis = CreateAnalyzer().Analyze(staleFixture);

        Assert.Equal(FlipOpportunityUsability.Usable, lowerConfidenceAnalysis.Usability);
        Assert.Equal(FlipOpportunityConfidence.Reduced, lowerConfidenceAnalysis.Confidence);
        Assert.True(lowerConfidenceAnalysis.IsEligible);
        Assert.Equal([FlipAnalysisReason.StaleMarketData], lowerConfidenceAnalysis.Reasons);

        var unusableAnalysis = CreateAnalyzer(
            new StaleDataPolicy(StaleDataHandling.Unusable)).Analyze(staleFixture);

        Assert.Equal(FlipOpportunityUsability.Unusable, unusableAnalysis.Usability);
        Assert.Equal(FlipOpportunityConfidence.Reduced, unusableAnalysis.Confidence);
        Assert.True(unusableAnalysis.MeetsFinancialConstraints);
        Assert.False(unusableAnalysis.IsEligible);
        Assert.Equal([FlipAnalysisReason.StaleMarketData], unusableAnalysis.Reasons);
        Assert.Equal(new Money(118), unusableAnalysis.Profit!.NetProfit);
    }

    [Fact]
    public void Marks_insufficient_depth_unusable_but_retains_execution_and_liquidity_explanations()
    {
        var analysis = CreateAnalyzer().Analyze(FlipOpportunityFixtures.InsufficientAcquisitionDepth());

        Assert.Equal(FlipOpportunityUsability.Unusable, analysis.Usability);
        Assert.Equal(FlipOpportunityConfidence.Normal, analysis.Confidence);
        Assert.False(analysis.MeetsFinancialConstraints);
        Assert.False(analysis.IsEligible);
        Assert.Equal([FlipAnalysisReason.InsufficientAcquisitionDepth], analysis.Reasons);

        var acquisition = Assert.IsType<OrderBookExecutionScenario>(analysis.AcquisitionExecution);
        Assert.Equal(3, acquisition.FilledQuantity);
        Assert.Equal(2, acquisition.RemainingQuantity);
        Assert.False(acquisition.IsFullyFilled);

        var liquidation = Assert.IsType<OrderBookExecutionScenario>(analysis.LiquidationExecution);
        Assert.True(liquidation.IsFullyFilled);

        var liquidity = Assert.IsType<FlipLiquidityMetrics>(analysis.Liquidity);
        Assert.False(liquidity.IsFullyAcquirable);
        Assert.True(liquidity.IsFullyLiquidatable);
        Assert.Equal(3, liquidity.AcquisitionFilledQuantity);
        Assert.Equal(5, liquidity.LiquidationFilledQuantity);
        Assert.Null(analysis.Profit);
        Assert.Null(analysis.CapitalRequired);
        Assert.Null(analysis.ReturnOnInvestment);
    }

    [Theory]
    [MemberData(nameof(UnusableDataRequests))]
    public void Marks_missing_partial_or_unqualified_market_data_unusable(
        FlipOpportunityRequest request,
        FlipAnalysisReason expectedReason)
    {
        var analysis = CreateAnalyzer().Analyze(request);

        Assert.Equal(FlipOpportunityUsability.Unusable, analysis.Usability);
        Assert.False(analysis.MeetsFinancialConstraints);
        Assert.False(analysis.IsEligible);
        Assert.Equal([expectedReason], analysis.Reasons);
        Assert.Null(analysis.AcquisitionExecution);
        Assert.Null(analysis.LiquidationExecution);
        Assert.Null(analysis.Liquidity);
        Assert.Null(analysis.Profit);
    }

    [Fact]
    public void Applies_capital_constraints_and_emits_reasons_in_declaration_order()
    {
        var constraints = new FlipOpportunityConstraints(new Money(119), new Money(556));
        var analysis = CreateAnalyzer().Analyze(FlipOpportunityFixtures.Stale(constraints));

        Assert.Equal(FlipOpportunityUsability.Usable, analysis.Usability);
        Assert.Equal(FlipOpportunityConfidence.Reduced, analysis.Confidence);
        Assert.False(analysis.MeetsFinancialConstraints);
        Assert.False(analysis.IsEligible);
        Assert.Equal(
            [
                FlipAnalysisReason.StaleMarketData,
                FlipAnalysisReason.BelowMinimumNetProfit,
                FlipAnalysisReason.ExceedsMaximumCapital,
            ],
            analysis.Reasons);
    }

    [Fact]
    public void Marks_a_zero_capital_scenario_unusable_without_calculating_roi()
    {
        var analysis = CreateAnalyzer().Analyze(FlipOpportunityFixtures.ZeroCapital());

        Assert.Equal(FlipOpportunityUsability.Unusable, analysis.Usability);
        Assert.False(analysis.MeetsFinancialConstraints);
        Assert.False(analysis.IsEligible);
        Assert.Equal([FlipAnalysisReason.UndefinedReturnOnInvestment], analysis.Reasons);
        Assert.NotNull(analysis.Profit);
        Assert.Equal(Money.Zero, analysis.CapitalRequired!.Value);
        Assert.Null(analysis.ReturnOnInvestment);
    }

    [Fact]
    public void Rejects_invalid_or_malformed_inputs()
    {
        var constraints = new FlipOpportunityConstraints(Money.Zero);

        Assert.Throws<ArgumentOutOfRangeException>(() => new FlipOpportunityRequest(
            itemId: 0,
            requestedQuantity: 1,
            orderBook: null,
            FlipOpportunityFixtures.AnalysisAtUtc,
            constraints));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FlipOpportunityRequest(
            itemId: 1,
            requestedQuantity: 0,
            orderBook: null,
            FlipOpportunityFixtures.AnalysisAtUtc,
            constraints));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FlipOpportunityConstraints(new Money(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FlipOpportunityConstraints(Money.Zero, new Money(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StaleDataPolicy((StaleDataHandling)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExactReturnOnInvestment(Money.Zero, Money.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DataFreshness(FlipOpportunityFixtures.AnalysisAtUtc, FlipOpportunityFixtures.AnalysisAtUtc.AddTicks(-1)));

        IReadOnlyList<OrderBookLevel> malformedLevels = [null!];
        Assert.Throws<ArgumentException>(() => new FlipOpportunityOrderBook(
            malformedLevels,
            [],
            freshness: null,
            isPartialData: false));
    }

    public static IEnumerable<object[]> UnusableDataRequests()
    {
        yield return [FlipOpportunityFixtures.MissingOrderBook(), FlipAnalysisReason.MissingOrderBook];
        yield return [FlipOpportunityFixtures.PartialOrderBook(), FlipAnalysisReason.PartialMarketData];
        yield return [
            FlipOpportunityFixtures.MissingFreshnessMetadata(),
            FlipAnalysisReason.MissingFreshnessMetadata,
        ];
    }

    private static FlipOpportunityAnalyzer CreateAnalyzer(StaleDataPolicy? staleDataPolicy = null) =>
        new(
            new TransactionFeePolicy(
                new FeeRule(500, FeeRounding.Down),
                new FeeRule(1_000, FeeRounding.Down)),
            staleDataPolicy);
}

using Gw2Tp.Analytics.Finance;
using Gw2Tp.Analytics.FlipOpportunities;
using Gw2Tp.Analytics.OrderBooks;
using Gw2Tp.Domain.Finance;
using Gw2Tp.Domain.MarketData;
using Xunit;

namespace Gw2Tp.Analytics.Tests.FlipOpportunities;

public sealed class FlipOpportunityScorerTests
{
    private static readonly DateTimeOffset AnalysisAtUtc = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Scores_identical_analysis_and_configuration_deterministically_with_explanation_metadata()
    {
        var scorer = new FlipOpportunityScorer(CreateConfiguration());
        var analysis = CreateAnalysis(itemId: 900_001, acquisitionUnitPrice: 100, liquidationUnitPrice: 150);

        var first = scorer.Score(analysis);
        var second = scorer.Score(analysis);

        Assert.Equal(first.ItemId, second.ItemId);
        Assert.Equal(first.RequestedQuantity, second.RequestedQuantity);
        Assert.Equal(first.ScoreBasisPoints, second.ScoreBasisPoints);
        Assert.Equal(first.Explanation.Configuration, second.Explanation.Configuration);
        Assert.Equal(first.Explanation.NetProfit, second.Explanation.NetProfit);
        Assert.Equal(first.Explanation.CapitalRequired, second.Explanation.CapitalRequired);
        Assert.Equal(first.Explanation.ReturnOnInvestment, second.Explanation.ReturnOnInvestment);
        Assert.Equal(first.Explanation.TotalPriceImpact, second.Explanation.TotalPriceImpact);
        Assert.Equal(first.Explanation.IsStale, second.Explanation.IsStale);
        Assert.Equal(first.Explanation.Confidence, second.Explanation.Confidence);
        Assert.Equal(first.Explanation.TransactionLegCount, second.Explanation.TransactionLegCount);
        Assert.Equal(first.Explanation.Contributions, second.Explanation.Contributions);
        Assert.InRange(first.ScoreBasisPoints, 0, FlipOpportunityScoringConfiguration.MaximumScoreBasisPoints);
        Assert.Equal(first.ScoreBasisPoints, first.Explanation.Contributions.Sum(
            contribution => contribution.WeightedContributionBasisPoints));
        Assert.Equal(
            [
                OpportunityScoreFactor.NetProfit,
                OpportunityScoreFactor.CapitalEfficiency,
                OpportunityScoreFactor.Liquidity,
                OpportunityScoreFactor.Freshness,
                OpportunityScoreFactor.Risk,
                OpportunityScoreFactor.Complexity,
            ],
            first.Explanation.Contributions.Select(contribution => contribution.Factor));
        Assert.Equal(new Money(138), first.Explanation.NetProfit);
        Assert.Equal(new Money(537), first.Explanation.CapitalRequired);
        Assert.Equal(Money.Zero, first.Explanation.TotalPriceImpact);
        Assert.False(first.Explanation.IsStale);
        Assert.Equal(FlipOpportunityConfidence.Normal, first.Explanation.Confidence);
        Assert.Equal(2, first.Explanation.TransactionLegCount);
    }

    [Fact]
    public void Ranks_higher_profit_before_lower_profit_when_profit_weight_dominates()
    {
        var scorer = new FlipOpportunityScorer(CreateConfiguration(
            targetNetProfit: new Money(1_000),
            weights: new OpportunityScoringWeights(100, 0, 0, 0, 0, 0)));
        var higherProfit = CreateAnalysis(itemId: 900_001, acquisitionUnitPrice: 10_000, liquidationUnitPrice: 13_000);
        var lowerProfit = CreateAnalysis(itemId: 900_002, acquisitionUnitPrice: 100, liquidationUnitPrice: 300);

        var ranked = scorer.Rank([lowerProfit, higherProfit]);

        Assert.Equal([900_001, 900_002], ranked.Select(score => score.ItemId));
        Assert.True(ranked[0].Explanation.NetProfit.Copper > ranked[1].Explanation.NetProfit.Copper);
    }

    [Fact]
    public void Breaks_score_ties_by_item_id_ascending()
    {
        var scorer = new FlipOpportunityScorer(CreateConfiguration());
        var higherItemId = CreateAnalysis(itemId: 900_002, acquisitionUnitPrice: 100, liquidationUnitPrice: 150);
        var lowerItemId = CreateAnalysis(itemId: 900_001, acquisitionUnitPrice: 100, liquidationUnitPrice: 150);

        var ranked = scorer.Rank([higherItemId, lowerItemId]);

        Assert.Equal([900_001, 900_002], ranked.Select(score => score.ItemId));
        Assert.Equal(ranked[0].ScoreBasisPoints, ranked[1].ScoreBasisPoints);
    }

    [Fact]
    public void Changes_ordering_when_profit_and_capital_efficiency_weights_change()
    {
        var higherProfit = CreateAnalysis(itemId: 900_001, acquisitionUnitPrice: 10_000, liquidationUnitPrice: 13_000);
        var higherReturnOnInvestment = CreateAnalysis(itemId: 900_002, acquisitionUnitPrice: 100, liquidationUnitPrice: 300);

        var profitFirst = new FlipOpportunityScorer(CreateConfiguration(
            targetNetProfit: new Money(1_000),
            weights: new OpportunityScoringWeights(100, 0, 0, 0, 0, 0)));
        var capitalEfficiencyFirst = new FlipOpportunityScorer(CreateConfiguration(
            targetReturnOnInvestmentBasisPoints: 2_000,
            weights: new OpportunityScoringWeights(0, 100, 0, 0, 0, 0)));

        var profitFirstRanking = profitFirst.Rank([higherProfit, higherReturnOnInvestment]);
        var capitalEfficiencyFirstRanking = capitalEfficiencyFirst.Rank([higherProfit, higherReturnOnInvestment]);

        Assert.Equal([900_001, 900_002], profitFirstRanking.Select(score => score.ItemId));
        Assert.Equal([900_002, 900_001], capitalEfficiencyFirstRanking.Select(score => score.ItemId));
    }

    [Fact]
    public void Lowers_the_liquidity_factor_for_order_book_price_impact()
    {
        var scorer = new FlipOpportunityScorer(CreateConfiguration(
            weights: new OpportunityScoringWeights(0, 0, 1, 0, 0, 0)));
        var noImpact = CreateAnalysis(itemId: 900_001, acquisitionUnitPrice: 100, liquidationUnitPrice: 150);
        var priceImpact = CreateAnalysisWithAcquisitionPriceImpact(itemId: 900_002);

        var noImpactScore = scorer.Score(noImpact);
        var priceImpactScore = scorer.Score(priceImpact);

        Assert.Equal(
            FlipOpportunityScoringConfiguration.MaximumScoreBasisPoints,
            GetContribution(noImpactScore, OpportunityScoreFactor.Liquidity).FactorScoreBasisPoints);
        Assert.True(
            GetContribution(priceImpactScore, OpportunityScoreFactor.Liquidity).FactorScoreBasisPoints <
            GetContribution(noImpactScore, OpportunityScoreFactor.Liquidity).FactorScoreBasisPoints);
        Assert.True(priceImpactScore.Explanation.TotalPriceImpact.Copper > Money.Zero.Copper);
    }

    [Fact]
    public void Applies_configured_stale_data_and_reduced_confidence_penalties()
    {
        var scorer = new FlipOpportunityScorer(CreateConfiguration(
            weights: new OpportunityScoringWeights(0, 0, 0, 1, 1, 0),
            freshDataScoreBasisPoints: 10_000,
            staleDataScoreBasisPoints: 2_000,
            normalConfidenceRiskScoreBasisPoints: 9_000,
            reducedConfidenceRiskScoreBasisPoints: 3_000));
        var fresh = CreateAnalysis(itemId: 900_001, acquisitionUnitPrice: 100, liquidationUnitPrice: 150);
        var stale = CreateAnalysis(
            itemId: 900_002,
            acquisitionUnitPrice: 100,
            liquidationUnitPrice: 150,
            freshness: new DataFreshness(AnalysisAtUtc.AddMinutes(-2), AnalysisAtUtc));

        var freshScore = scorer.Score(fresh);
        var staleScore = scorer.Score(stale);

        Assert.True(freshScore.ScoreBasisPoints > staleScore.ScoreBasisPoints);
        Assert.False(freshScore.Explanation.IsStale);
        Assert.True(staleScore.Explanation.IsStale);
        Assert.Equal(FlipOpportunityConfidence.Reduced, staleScore.Explanation.Confidence);
        Assert.Equal(
            2_000,
            GetContribution(staleScore, OpportunityScoreFactor.Freshness).FactorScoreBasisPoints);
        Assert.Equal(
            3_000,
            GetContribution(staleScore, OpportunityScoreFactor.Risk).FactorScoreBasisPoints);
    }

    [Fact]
    public void Omits_ineligible_analyses_from_rankings_and_rejects_direct_scoring()
    {
        var scorer = new FlipOpportunityScorer(CreateConfiguration());
        var eligible = CreateAnalysis(itemId: 900_001, acquisitionUnitPrice: 100, liquidationUnitPrice: 150);
        var ineligible = CreateAnalysis(itemId: 900_002, acquisitionUnitPrice: 150, liquidationUnitPrice: 150);

        var ranked = scorer.Rank([ineligible, eligible]);

        Assert.Equal([900_001], ranked.Select(score => score.ItemId));
        Assert.Throws<ArgumentException>(() => scorer.Score(ineligible));
    }

    [Fact]
    public void Rejects_invalid_scoring_configuration()
    {
        var weights = new OpportunityScoringWeights(1, 1, 1, 1, 1, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => new OpportunityScoringWeights(-1, 0, 0, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpportunityScoringWeights(0, 0, 0, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateConfiguration(targetNetProfit: Money.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateConfiguration(targetReturnOnInvestmentBasisPoints: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateConfiguration(acceptablePriceImpactBasisPoints: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateConfiguration(freshDataScoreBasisPoints: 10_001));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateConfiguration(staleDataScoreBasisPoints: -1));
        Assert.Throws<ArgumentNullException>(() => new FlipOpportunityScoringConfiguration(
            new Money(100),
            1_000,
            1_000,
            weights: null!,
            freshDataScoreBasisPoints: 10_000,
            staleDataScoreBasisPoints: 5_000,
            normalConfidenceRiskScoreBasisPoints: 10_000,
            reducedConfidenceRiskScoreBasisPoints: 5_000,
            twoLegFlipComplexityScoreBasisPoints: 8_000));
    }

    private static OpportunityScoreContribution GetContribution(
        FlipOpportunityScore score,
        OpportunityScoreFactor factor) =>
        Assert.Single(score.Explanation.Contributions, contribution => contribution.Factor == factor);

    private static FlipOpportunityScoringConfiguration CreateConfiguration(
        Money? targetNetProfit = null,
        int targetReturnOnInvestmentBasisPoints = 1_000,
        int acceptablePriceImpactBasisPoints = 1_000,
        OpportunityScoringWeights? weights = null,
        int freshDataScoreBasisPoints = 10_000,
        int staleDataScoreBasisPoints = 4_000,
        int normalConfidenceRiskScoreBasisPoints = 10_000,
        int reducedConfidenceRiskScoreBasisPoints = 5_000,
        int twoLegFlipComplexityScoreBasisPoints = 8_000) =>
        new(
            targetNetProfit ?? new Money(100),
            targetReturnOnInvestmentBasisPoints,
            acceptablePriceImpactBasisPoints,
            weights ?? new OpportunityScoringWeights(3, 2, 2, 1, 1, 1),
            freshDataScoreBasisPoints,
            staleDataScoreBasisPoints,
            normalConfidenceRiskScoreBasisPoints,
            reducedConfidenceRiskScoreBasisPoints,
            twoLegFlipComplexityScoreBasisPoints);

    private static FlipOpportunityAnalysis CreateAnalysis(
        int itemId,
        long acquisitionUnitPrice,
        long liquidationUnitPrice,
        DataFreshness? freshness = null)
    {
        var analyzer = new FlipOpportunityAnalyzer(
            new TransactionFeePolicy(
                new FeeRule(500, FeeRounding.Down),
                new FeeRule(1_000, FeeRounding.Down)));
        var request = new FlipOpportunityRequest(
            itemId,
            requestedQuantity: 5,
            new FlipOpportunityOrderBook(
                buyLevels: [new OrderBookLevel(5, new Money(liquidationUnitPrice))],
                sellLevels: [new OrderBookLevel(5, new Money(acquisitionUnitPrice))],
                freshness ?? new DataFreshness(AnalysisAtUtc.AddMinutes(-1), AnalysisAtUtc.AddMinutes(1)),
                isPartialData: false),
            AnalysisAtUtc,
            new FlipOpportunityConstraints(Money.Zero));

        return analyzer.Analyze(request);
    }

    private static FlipOpportunityAnalysis CreateAnalysisWithAcquisitionPriceImpact(int itemId)
    {
        var analyzer = new FlipOpportunityAnalyzer(
            new TransactionFeePolicy(
                new FeeRule(500, FeeRounding.Down),
                new FeeRule(1_000, FeeRounding.Down)));
        var request = new FlipOpportunityRequest(
            itemId,
            requestedQuantity: 5,
            new FlipOpportunityOrderBook(
                buyLevels: [new OrderBookLevel(5, new Money(150))],
                sellLevels: [new OrderBookLevel(3, new Money(100)), new OrderBookLevel(2, new Money(110))],
                new DataFreshness(AnalysisAtUtc.AddMinutes(-1), AnalysisAtUtc.AddMinutes(1)),
                isPartialData: false),
            AnalysisAtUtc,
            new FlipOpportunityConstraints(Money.Zero));

        return analyzer.Analyze(request);
    }
}

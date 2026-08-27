using System.Numerics;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Analytics.FlipOpportunities;

/// <summary>
/// Deterministically scores and ranks eligible two-leg flip analyses using caller-supplied policy.
/// </summary>
public sealed class FlipOpportunityScorer
{
    private const int TwoLegFlipTransactionLegCount = 2;

    private static readonly OpportunityScoreFactor[] Factors =
    [
        OpportunityScoreFactor.NetProfit,
        OpportunityScoreFactor.CapitalEfficiency,
        OpportunityScoreFactor.Liquidity,
        OpportunityScoreFactor.Freshness,
        OpportunityScoreFactor.Risk,
        OpportunityScoreFactor.Complexity,
    ];

    private readonly FlipOpportunityScoringConfiguration configuration;

    public FlipOpportunityScorer(FlipOpportunityScoringConfiguration configuration)
    {
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <summary>
    /// Scores one eligible analysis. Ineligible analyses are intentionally not rankable.
    /// </summary>
    public FlipOpportunityScore Score(FlipOpportunityAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        if (!analysis.IsEligible)
        {
            throw new ArgumentException("Only eligible flip analyses can be scored.", nameof(analysis));
        }

        var profit = analysis.Profit ?? throw new ArgumentException(
            "An eligible analysis must provide profit details.",
            nameof(analysis));
        var capitalRequired = analysis.CapitalRequired ?? throw new ArgumentException(
            "An eligible analysis must provide required capital.",
            nameof(analysis));
        var returnOnInvestment = analysis.ReturnOnInvestment ?? throw new ArgumentException(
            "An eligible analysis must provide return on investment.",
            nameof(analysis));
        var liquidity = analysis.Liquidity ?? throw new ArgumentException(
            "An eligible analysis must provide liquidity details.",
            nameof(analysis));

        var isStale = analysis.Reasons.Contains(FlipAnalysisReason.StaleMarketData);
        var factorScores = new Dictionary<OpportunityScoreFactor, int>
        {
            [OpportunityScoreFactor.NetProfit] = NormalizeRatio(
                profit.NetProfit,
                configuration.TargetNetProfit),
            [OpportunityScoreFactor.CapitalEfficiency] = NormalizeReturnOnInvestment(
                profit.NetProfit,
                capitalRequired),
            [OpportunityScoreFactor.Liquidity] = NormalizeLiquidity(
                liquidity.TotalPriceImpact,
                capitalRequired),
            [OpportunityScoreFactor.Freshness] = isStale
                ? configuration.StaleDataScoreBasisPoints
                : configuration.FreshDataScoreBasisPoints,
            [OpportunityScoreFactor.Risk] = GetRiskScore(analysis.Confidence),
            [OpportunityScoreFactor.Complexity] = configuration.TwoLegFlipComplexityScoreBasisPoints,
        };

        var contributions = CalculateContributions(factorScores);
        var scoreBasisPoints = contributions.Sum(contribution => contribution.WeightedContributionBasisPoints);
        var explanation = new OpportunityScoreExplanation(
            configuration,
            profit.NetProfit,
            capitalRequired,
            returnOnInvestment,
            liquidity.TotalPriceImpact,
            isStale,
            analysis.Confidence,
            TwoLegFlipTransactionLegCount,
            contributions);

        return new FlipOpportunityScore(
            analysis.Scenario.ItemId,
            analysis.Scenario.RequestedQuantity,
            scoreBasisPoints,
            explanation);
    }

    /// <summary>
    /// Scores eligible analyses and returns them in deterministic rank order.
    /// </summary>
    public IReadOnlyList<FlipOpportunityScore> Rank(IEnumerable<FlipOpportunityAnalysis> analyses)
    {
        ArgumentNullException.ThrowIfNull(analyses);

        var scores = new List<FlipOpportunityScore>();
        foreach (var analysis in analyses)
        {
            if (analysis is null)
            {
                throw new ArgumentException("Analyses cannot contain null values.", nameof(analyses));
            }

            if (analysis.IsEligible)
            {
                scores.Add(Score(analysis));
            }
        }

        return Array.AsReadOnly(
            scores
                .OrderByDescending(score => score.ScoreBasisPoints)
                .ThenBy(score => score.ItemId)
                .ThenBy(score => score.RequestedQuantity)
                .ThenBy(score => score.Explanation.CapitalRequired.Copper)
                .ThenBy(score => score.Explanation.NetProfit.Copper)
                .ToArray());
    }

    private int NormalizeReturnOnInvestment(Money netProfit, Money capitalRequired)
    {
        if (netProfit.Copper <= 0)
        {
            return 0;
        }

        return Normalize(
            new BigInteger(netProfit.Copper) * FlipOpportunityScoringConfiguration.MaximumScoreBasisPoints *
            FlipOpportunityScoringConfiguration.MaximumScoreBasisPoints,
            new BigInteger(capitalRequired.Copper) * configuration.TargetReturnOnInvestmentBasisPoints);
    }

    private int NormalizeLiquidity(Money totalPriceImpact, Money capitalRequired)
    {
        var penalty = Normalize(
            new BigInteger(totalPriceImpact.Copper) * FlipOpportunityScoringConfiguration.MaximumScoreBasisPoints *
            FlipOpportunityScoringConfiguration.MaximumScoreBasisPoints,
            new BigInteger(capitalRequired.Copper) * configuration.AcceptablePriceImpactBasisPoints);

        return FlipOpportunityScoringConfiguration.MaximumScoreBasisPoints - penalty;
    }

    private int GetRiskScore(FlipOpportunityConfidence confidence) => confidence switch
    {
        FlipOpportunityConfidence.Normal => configuration.NormalConfidenceRiskScoreBasisPoints,
        FlipOpportunityConfidence.Reduced => configuration.ReducedConfidenceRiskScoreBasisPoints,
        _ => throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "The opportunity confidence is not supported."),
    };

    private IReadOnlyList<OpportunityScoreContribution> CalculateContributions(
        IReadOnlyDictionary<OpportunityScoreFactor, int> factorScores)
    {
        var calculationRows = Factors
            .Select(factor => new ContributionCalculation(
                factor,
                factorScores[factor],
                configuration.Weights.GetWeight(factor),
                (long)factorScores[factor] * configuration.Weights.GetWeight(factor),
                0))
            .ToArray();

        var baseContributionTotal = 0;
        for (var index = 0; index < calculationRows.Length; index++)
        {
            var weightedContribution = calculationRows[index].WeightedScore / configuration.Weights.TotalWeight;
            calculationRows[index] = calculationRows[index] with
            {
                WeightedContributionBasisPoints = checked((int)weightedContribution),
            };
            baseContributionTotal = checked(
                baseContributionTotal + calculationRows[index].WeightedContributionBasisPoints);
        }

        var totalScore = checked((int)(
            calculationRows.Sum(row => row.WeightedScore) / configuration.Weights.TotalWeight));
        var remainingPoints = totalScore - baseContributionTotal;

        foreach (var index in Enumerable.Range(0, calculationRows.Length)
                     .OrderByDescending(index => calculationRows[index].WeightedScore % configuration.Weights.TotalWeight)
                     .ThenBy(index => calculationRows[index].Factor)
                     .Take(remainingPoints))
        {
            calculationRows[index] = calculationRows[index] with
            {
                WeightedContributionBasisPoints = calculationRows[index].WeightedContributionBasisPoints + 1,
            };
        }

        return Array.AsReadOnly(
            calculationRows
                .Select(row => new OpportunityScoreContribution(
                    row.Factor,
                    row.FactorScoreBasisPoints,
                    row.Weight,
                    row.WeightedContributionBasisPoints))
                .ToArray());
    }

    private static int NormalizeRatio(Money value, Money target)
    {
        if (value.Copper <= 0)
        {
            return 0;
        }

        return Normalize(
            new BigInteger(value.Copper) * FlipOpportunityScoringConfiguration.MaximumScoreBasisPoints,
            target.Copper);
    }

    private static int Normalize(BigInteger numerator, BigInteger denominator)
    {
        if (numerator <= BigInteger.Zero)
        {
            return 0;
        }

        var score = numerator / denominator;
        return score >= FlipOpportunityScoringConfiguration.MaximumScoreBasisPoints
            ? FlipOpportunityScoringConfiguration.MaximumScoreBasisPoints
            : (int)score;
    }

    private sealed record ContributionCalculation(
        OpportunityScoreFactor Factor,
        int FactorScoreBasisPoints,
        int Weight,
        long WeightedScore,
        int WeightedContributionBasisPoints);
}

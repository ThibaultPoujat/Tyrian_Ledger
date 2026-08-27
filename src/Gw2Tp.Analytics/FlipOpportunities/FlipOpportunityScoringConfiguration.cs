using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Analytics.FlipOpportunities;

/// <summary>
/// The complete caller-supplied policy used to score an eligible two-leg flip.
/// Values are intentionally not defaulted so that product ranking policy is explicit.
/// </summary>
public sealed record FlipOpportunityScoringConfiguration
{
    public const int MaximumScoreBasisPoints = 10_000;

    public FlipOpportunityScoringConfiguration(
        Money targetNetProfit,
        int targetReturnOnInvestmentBasisPoints,
        int acceptablePriceImpactBasisPoints,
        OpportunityScoringWeights weights,
        int freshDataScoreBasisPoints,
        int staleDataScoreBasisPoints,
        int normalConfidenceRiskScoreBasisPoints,
        int reducedConfidenceRiskScoreBasisPoints,
        int twoLegFlipComplexityScoreBasisPoints)
    {
        if (targetNetProfit.Copper <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetNetProfit),
                "The target net profit must be positive.");
        }

        if (targetReturnOnInvestmentBasisPoints <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetReturnOnInvestmentBasisPoints),
                "The target return on investment must be positive.");
        }

        if (acceptablePriceImpactBasisPoints <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(acceptablePriceImpactBasisPoints),
                "The acceptable price impact must be positive.");
        }

        ArgumentNullException.ThrowIfNull(weights);
        ValidateScore(freshDataScoreBasisPoints, nameof(freshDataScoreBasisPoints));
        ValidateScore(staleDataScoreBasisPoints, nameof(staleDataScoreBasisPoints));
        ValidateScore(normalConfidenceRiskScoreBasisPoints, nameof(normalConfidenceRiskScoreBasisPoints));
        ValidateScore(reducedConfidenceRiskScoreBasisPoints, nameof(reducedConfidenceRiskScoreBasisPoints));
        ValidateScore(twoLegFlipComplexityScoreBasisPoints, nameof(twoLegFlipComplexityScoreBasisPoints));

        TargetNetProfit = targetNetProfit;
        TargetReturnOnInvestmentBasisPoints = targetReturnOnInvestmentBasisPoints;
        AcceptablePriceImpactBasisPoints = acceptablePriceImpactBasisPoints;
        Weights = weights;
        FreshDataScoreBasisPoints = freshDataScoreBasisPoints;
        StaleDataScoreBasisPoints = staleDataScoreBasisPoints;
        NormalConfidenceRiskScoreBasisPoints = normalConfidenceRiskScoreBasisPoints;
        ReducedConfidenceRiskScoreBasisPoints = reducedConfidenceRiskScoreBasisPoints;
        TwoLegFlipComplexityScoreBasisPoints = twoLegFlipComplexityScoreBasisPoints;
    }

    public Money TargetNetProfit { get; }

    public int TargetReturnOnInvestmentBasisPoints { get; }

    public int AcceptablePriceImpactBasisPoints { get; }

    public OpportunityScoringWeights Weights { get; }

    public int FreshDataScoreBasisPoints { get; }

    public int StaleDataScoreBasisPoints { get; }

    public int NormalConfidenceRiskScoreBasisPoints { get; }

    public int ReducedConfidenceRiskScoreBasisPoints { get; }

    public int TwoLegFlipComplexityScoreBasisPoints { get; }

    private static void ValidateScore(int scoreBasisPoints, string parameterName)
    {
        if (scoreBasisPoints < 0 || scoreBasisPoints > MaximumScoreBasisPoints)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"An opportunity score must be between 0 and {MaximumScoreBasisPoints} basis points.");
        }
    }
}

namespace Gw2Tp.Analytics.FlipOpportunities;

/// <summary>
/// One transparent component of a weighted opportunity score.
/// </summary>
public sealed record OpportunityScoreContribution(
    OpportunityScoreFactor Factor,
    int FactorScoreBasisPoints,
    int Weight,
    int WeightedContributionBasisPoints);

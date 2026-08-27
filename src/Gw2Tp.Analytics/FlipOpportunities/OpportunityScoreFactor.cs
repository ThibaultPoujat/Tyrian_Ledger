namespace Gw2Tp.Analytics.FlipOpportunities;

/// <summary>
/// The stable factor order used to calculate and explain an opportunity score.
/// </summary>
public enum OpportunityScoreFactor
{
    NetProfit = 0,
    CapitalEfficiency = 1,
    Liquidity = 2,
    Freshness = 3,
    Risk = 4,
    Complexity = 5,
}

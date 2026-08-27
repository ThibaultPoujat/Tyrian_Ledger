namespace Gw2Tp.Analytics.FlipOpportunities;

/// <summary>
/// Caller-supplied relative weights for the deterministic opportunity score.
/// </summary>
public sealed record OpportunityScoringWeights
{
    public OpportunityScoringWeights(
        int netProfit,
        int capitalEfficiency,
        int liquidity,
        int freshness,
        int risk,
        int complexity)
    {
        ValidateWeight(netProfit, nameof(netProfit));
        ValidateWeight(capitalEfficiency, nameof(capitalEfficiency));
        ValidateWeight(liquidity, nameof(liquidity));
        ValidateWeight(freshness, nameof(freshness));
        ValidateWeight(risk, nameof(risk));
        ValidateWeight(complexity, nameof(complexity));

        NetProfit = netProfit;
        CapitalEfficiency = capitalEfficiency;
        Liquidity = liquidity;
        Freshness = freshness;
        Risk = risk;
        Complexity = complexity;
        TotalWeight = checked(
            (long)netProfit + capitalEfficiency + liquidity + freshness + risk + complexity);

        if (TotalWeight == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(netProfit),
                "At least one opportunity-scoring weight must be positive.");
        }
    }

    public int NetProfit { get; }

    public int CapitalEfficiency { get; }

    public int Liquidity { get; }

    public int Freshness { get; }

    public int Risk { get; }

    public int Complexity { get; }

    public long TotalWeight { get; }

    public int GetWeight(OpportunityScoreFactor factor) => factor switch
    {
        OpportunityScoreFactor.NetProfit => NetProfit,
        OpportunityScoreFactor.CapitalEfficiency => CapitalEfficiency,
        OpportunityScoreFactor.Liquidity => Liquidity,
        OpportunityScoreFactor.Freshness => Freshness,
        OpportunityScoreFactor.Risk => Risk,
        OpportunityScoreFactor.Complexity => Complexity,
        _ => throw new ArgumentOutOfRangeException(nameof(factor), factor, "The score factor is not supported."),
    };

    private static void ValidateWeight(int weight, string parameterName)
    {
        if (weight < 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "An opportunity-scoring weight cannot be negative.");
        }
    }
}

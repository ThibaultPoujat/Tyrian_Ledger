using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Analytics.FlipOpportunities;

/// <summary>
/// Source metrics and weighted components retained for UI-facing score explanations.
/// </summary>
public sealed record OpportunityScoreExplanation
{
    public OpportunityScoreExplanation(
        FlipOpportunityScoringConfiguration configuration,
        Money netProfit,
        Money capitalRequired,
        ExactReturnOnInvestment returnOnInvestment,
        Money totalPriceImpact,
        bool isStale,
        FlipOpportunityConfidence confidence,
        int transactionLegCount,
        IReadOnlyList<OpportunityScoreContribution> contributions)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (capitalRequired.Copper <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capitalRequired), "Capital required must be positive.");
        }

        if (totalPriceImpact.Copper < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalPriceImpact), "Price impact cannot be negative.");
        }

        if (transactionLegCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(transactionLegCount),
                "A scored opportunity must have at least one transaction leg.");
        }

        ArgumentNullException.ThrowIfNull(contributions);
        if (contributions.Any(contribution => contribution is null))
        {
            throw new ArgumentException("Score contributions cannot contain null values.", nameof(contributions));
        }

        Configuration = configuration;
        NetProfit = netProfit;
        CapitalRequired = capitalRequired;
        ReturnOnInvestment = returnOnInvestment;
        TotalPriceImpact = totalPriceImpact;
        IsStale = isStale;
        Confidence = confidence;
        TransactionLegCount = transactionLegCount;
        Contributions = Array.AsReadOnly(contributions.ToArray());
    }

    public FlipOpportunityScoringConfiguration Configuration { get; }

    public Money NetProfit { get; }

    public Money CapitalRequired { get; }

    public ExactReturnOnInvestment ReturnOnInvestment { get; }

    public Money TotalPriceImpact { get; }

    public bool IsStale { get; }

    public FlipOpportunityConfidence Confidence { get; }

    public int TransactionLegCount { get; }

    public IReadOnlyList<OpportunityScoreContribution> Contributions { get; }
}

using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Analytics.FlipOpportunities;

/// <summary>
/// Caller-supplied financial constraints for one modeled flip opportunity.
/// </summary>
public sealed record FlipOpportunityConstraints
{
    public FlipOpportunityConstraints(Money minimumNetProfit, Money? maximumCapitalRequired = null)
    {
        if (minimumNetProfit.Copper < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumNetProfit),
                "A minimum net profit cannot be negative.");
        }

        if (maximumCapitalRequired is { Copper: < 0 })
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumCapitalRequired),
                "A maximum capital requirement cannot be negative.");
        }

        MinimumNetProfit = minimumNetProfit;
        MaximumCapitalRequired = maximumCapitalRequired;
    }

    public Money MinimumNetProfit { get; }

    /// <summary>
    /// The optional maximum copper that may be committed up front to this scenario.
    /// </summary>
    public Money? MaximumCapitalRequired { get; }
}

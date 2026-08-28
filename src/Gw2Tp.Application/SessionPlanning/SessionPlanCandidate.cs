using Gw2Tp.Analytics.FlipOpportunities;
using Gw2Tp.Application.Preferences;

namespace Gw2Tp.Application.SessionPlanning;

/// <summary>
/// A scored opportunity paired with source-supplied session-planning metadata.
/// </summary>
public sealed record SessionPlanCandidate
{
    public SessionPlanCandidate(
        FlipOpportunityScore score,
        OpportunityStrategyPreference strategy,
        SessionEffortCategory effortCategory)
    {
        ArgumentNullException.ThrowIfNull(score);

        if (!Enum.IsDefined(strategy) || strategy == OpportunityStrategyPreference.All)
        {
            throw new ArgumentOutOfRangeException(
                nameof(strategy),
                strategy,
                "A session-plan candidate must identify one concrete strategy.");
        }

        if (!Enum.IsDefined(effortCategory))
        {
            throw new ArgumentOutOfRangeException(nameof(effortCategory));
        }

        Score = score;
        Strategy = strategy;
        EffortCategory = effortCategory;
    }

    public FlipOpportunityScore Score { get; }

    public OpportunityStrategyPreference Strategy { get; }

    public SessionEffortCategory EffortCategory { get; }
}

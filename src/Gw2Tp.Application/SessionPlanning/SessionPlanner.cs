using Gw2Tp.Analytics.FlipOpportunities;
using Gw2Tp.Application.Preferences;

namespace Gw2Tp.Application.SessionPlanning;

/// <summary>
/// Produces a deterministic, constraint-respecting session shortlist without predicting time or execution.
/// </summary>
public sealed class SessionPlanner
{
    public IReadOnlyList<SessionPlanEntry> CreateShortlist(
        IEnumerable<SessionPlanCandidate> candidates,
        UserSessionPreferences preferences,
        SessionEffortCategory? selectedEffortCategory = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(preferences);

        if (selectedEffortCategory is { } effortCategory && !Enum.IsDefined(effortCategory))
        {
            throw new ArgumentOutOfRangeException(nameof(selectedEffortCategory));
        }

        var candidateList = candidates.ToArray();
        if (candidateList.Any(candidate => candidate is null))
        {
            throw new ArgumentException("Candidates cannot contain null values.", nameof(candidates));
        }

        var shortlisted = candidateList
            .Where(candidate => MatchesPreferences(candidate, preferences, selectedEffortCategory))
            .OrderByDescending(candidate => candidate.Score.ScoreBasisPoints)
            .ThenBy(candidate => candidate.Score.ItemId)
            .ThenBy(candidate => candidate.Score.RequestedQuantity)
            .ThenBy(candidate => candidate.Score.Explanation.CapitalRequired.Copper)
            .ThenBy(candidate => candidate.Score.Explanation.NetProfit.Copper)
            .Select((candidate, index) => new SessionPlanEntry(candidate, index + 1))
            .ToArray();

        return Array.AsReadOnly(shortlisted);
    }

    private static bool MatchesPreferences(
        SessionPlanCandidate candidate,
        UserSessionPreferences preferences,
        SessionEffortCategory? selectedEffortCategory)
    {
        var explanation = candidate.Score.Explanation;
        var perOpportunityCapitalLimit = preferences.GetPerOpportunityCapitalLimitCopper();

        return (perOpportunityCapitalLimit is null ||
                explanation.CapitalRequired.Copper <= perOpportunityCapitalLimit)
            && (preferences.MinimumProfitCopper is null ||
                explanation.NetProfit.Copper >= preferences.MinimumProfitCopper)
            && MatchesRiskPreference(explanation.Confidence, preferences.RiskPreference)
            && MatchesStrategyPreference(candidate.Strategy, preferences.StrategyPreference)
            && (selectedEffortCategory is null || candidate.EffortCategory == selectedEffortCategory);
    }

    private static bool MatchesRiskPreference(
        FlipOpportunityConfidence confidence,
        OpportunityRiskPreference preference) => preference switch
    {
        OpportunityRiskPreference.All => true,
        OpportunityRiskPreference.Normal => confidence == FlipOpportunityConfidence.Normal,
        OpportunityRiskPreference.Reduced => confidence == FlipOpportunityConfidence.Reduced,
        _ => throw new ArgumentOutOfRangeException(nameof(preference), preference, "The risk preference is not supported."),
    };

    private static bool MatchesStrategyPreference(
        OpportunityStrategyPreference strategy,
        OpportunityStrategyPreference preference) => preference switch
    {
        OpportunityStrategyPreference.All => true,
        OpportunityStrategyPreference.MarketFlip => strategy == OpportunityStrategyPreference.MarketFlip,
        _ => throw new ArgumentOutOfRangeException(nameof(preference), preference, "The strategy preference is not supported."),
    };
}

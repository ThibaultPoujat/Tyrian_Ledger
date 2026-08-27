namespace Gw2Tp.Analytics.FlipOpportunities;

/// <summary>
/// The deterministic 0-to-10,000 score and metadata for one eligible flip opportunity.
/// </summary>
public sealed record FlipOpportunityScore
{
    public FlipOpportunityScore(
        int itemId,
        int requestedQuantity,
        int scoreBasisPoints,
        OpportunityScoreExplanation explanation)
    {
        if (itemId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemId), "An item identifier must be positive.");
        }

        if (requestedQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedQuantity),
                "A requested quantity must be positive.");
        }

        if (scoreBasisPoints < 0 || scoreBasisPoints > FlipOpportunityScoringConfiguration.MaximumScoreBasisPoints)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scoreBasisPoints),
                $"A score must be between 0 and {FlipOpportunityScoringConfiguration.MaximumScoreBasisPoints} basis points.");
        }

        ArgumentNullException.ThrowIfNull(explanation);

        ItemId = itemId;
        RequestedQuantity = requestedQuantity;
        ScoreBasisPoints = scoreBasisPoints;
        Explanation = explanation;
    }

    public int ItemId { get; }

    public int RequestedQuantity { get; }

    public int ScoreBasisPoints { get; }

    public OpportunityScoreExplanation Explanation { get; }
}

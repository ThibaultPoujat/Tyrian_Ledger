namespace Gw2Tp.Analytics.FlipOpportunities;

/// <summary>
/// Specifies whether expired market data remains visible with reduced confidence
/// or makes a flip opportunity unusable.
/// </summary>
public sealed record StaleDataPolicy
{
    public StaleDataPolicy(StaleDataHandling staleDataHandling = StaleDataHandling.LowerConfidence)
    {
        if (staleDataHandling is not StaleDataHandling.LowerConfidence and not StaleDataHandling.Unusable)
        {
            throw new ArgumentOutOfRangeException(
                nameof(staleDataHandling),
                staleDataHandling,
                "The stale-data handling mode is not supported.");
        }

        StaleDataHandling = staleDataHandling;
    }

    public StaleDataHandling StaleDataHandling { get; }
}

public enum StaleDataHandling
{
    LowerConfidence = 0,
    Unusable = 1,
}

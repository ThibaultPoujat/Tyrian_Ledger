namespace Gw2Tp.Application.SessionPlanning;

/// <summary>
/// One eligible session-plan candidate in deterministic shortlist order.
/// </summary>
public sealed record SessionPlanEntry
{
    public SessionPlanEntry(SessionPlanCandidate candidate, int rank)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (rank <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rank), "A shortlist rank must be positive.");
        }

        Candidate = candidate;
        Rank = rank;
    }

    public SessionPlanCandidate Candidate { get; }

    public int Rank { get; }
}

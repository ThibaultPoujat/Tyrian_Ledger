namespace Gw2Tp.Application.MarketHistory;

/// <summary>
/// A capability that contributes public-market items to the next collection
/// plan. Future features can register another source without changing the
/// scheduler or persistence model.
/// </summary>
public interface IMarketSamplingSource
{
    string Name { get; }

    Task<IReadOnlyList<MarketSamplingCandidate>> GetCandidatesAsync(
        CancellationToken cancellationToken = default);
}

public sealed record MarketSamplingCandidate(
    int ItemId,
    MarketSamplingTier Tier,
    bool IncludeOrderBook);

public sealed record MarketSamplingTarget(
    int ItemId,
    MarketSamplingTier Tier,
    TimeSpan Interval,
    bool IncludeOrderBook,
    IReadOnlyList<string> SourceNames);

public sealed record MarketSamplingPlan(
    int PolicyVersion,
    IReadOnlyList<MarketSamplingTarget> Targets);

/// <summary>
/// Non-secret policy settings. These defaults are collection intent only; M18-02
/// owns scheduling, request budgeting, retries, and operational health.
/// </summary>
public sealed record MarketSamplingSettings(
    int PolicyVersion,
    TimeSpan CurrentPersonalOrderInterval,
    TimeSpan WatchlistInterval,
    TimeSpan BroadMarketInterval)
{
    public static MarketSamplingSettings Default { get; } = new(
        PolicyVersion: 1,
        CurrentPersonalOrderInterval: TimeSpan.FromMinutes(15),
        WatchlistInterval: TimeSpan.FromMinutes(30),
        BroadMarketInterval: TimeSpan.FromHours(6));

    public TimeSpan GetInterval(MarketSamplingTier tier) => tier switch
    {
        MarketSamplingTier.CurrentPersonalOrder => CurrentPersonalOrderInterval,
        MarketSamplingTier.Watchlist => WatchlistInterval,
        MarketSamplingTier.BroadMarket => BroadMarketInterval,
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };

    public void Validate()
    {
        if (PolicyVersion <= 0 || CurrentPersonalOrderInterval <= TimeSpan.Zero ||
            WatchlistInterval <= TimeSpan.Zero || BroadMarketInterval <= TimeSpan.Zero ||
            CurrentPersonalOrderInterval > WatchlistInterval || WatchlistInterval > BroadMarketInterval)
        {
            throw new ArgumentOutOfRangeException(nameof(MarketSamplingSettings));
        }
    }
}

public interface IAdaptiveMarketSamplingPolicy
{
    Task<MarketSamplingPlan> BuildPlanAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Deterministically composes independently registered sources. An item keeps
/// its highest-priority tier; detailed capture is allowed only when a source at
/// that winning high-interest tier explicitly requests it.
/// </summary>
public sealed class AdaptiveMarketSamplingPolicy : IAdaptiveMarketSamplingPolicy
{
    private readonly IReadOnlyList<IMarketSamplingSource> sources;
    private readonly MarketSamplingSettings settings;

    public AdaptiveMarketSamplingPolicy(
        IEnumerable<IMarketSamplingSource> sources,
        MarketSamplingSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        this.sources = sources.ToArray();
        this.settings = settings ?? MarketSamplingSettings.Default;
        this.settings.Validate();
        if (this.sources.Any(source => source is null || string.IsNullOrWhiteSpace(source.Name)))
        {
            throw new ArgumentException("Sampling sources must have names.", nameof(sources));
        }
    }

    public async Task<MarketSamplingPlan> BuildPlanAsync(CancellationToken cancellationToken = default)
    {
        var contributions = new List<(string SourceName, MarketSamplingCandidate Candidate)>();
        foreach (var source in sources)
        {
            var candidates = await source.GetCandidatesAsync(cancellationToken).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(candidates);
            foreach (var candidate in candidates)
            {
                ValidateCandidate(candidate);
                contributions.Add((source.Name, candidate));
            }
        }

        var targets = contributions
            .GroupBy(contribution => contribution.Candidate.ItemId)
            .Select(group =>
            {
                var winningTier = group.Min(contribution => contribution.Candidate.Tier);
                var winners = group.Where(contribution => contribution.Candidate.Tier == winningTier).ToArray();
                var includeOrderBook = winningTier != MarketSamplingTier.BroadMarket &&
                    winners.Any(contribution => contribution.Candidate.IncludeOrderBook);
                return new MarketSamplingTarget(
                    group.Key,
                    winningTier,
                    settings.GetInterval(winningTier),
                    includeOrderBook,
                    winners.Select(contribution => contribution.SourceName).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray());
            })
            .OrderBy(target => target.Tier)
            .ThenBy(target => target.ItemId)
            .ToArray();

        return new MarketSamplingPlan(settings.PolicyVersion, targets);
    }

    private static void ValidateCandidate(MarketSamplingCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.ItemId <= 0 || !Enum.IsDefined(candidate.Tier))
        {
            throw new ArgumentOutOfRangeException(nameof(candidate));
        }
    }
}

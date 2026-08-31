using Gw2Tp.Application.Recommendations;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.Scans;

/// <summary>
/// The observable state of the one player-triggered local market scan.
/// </summary>
public enum PlayerMarketScanState
{
    Idle,
    Running,
    Complete,
    Cancelled,
    RateLimited,
    Failed,
}

/// <summary>
/// A factual scan stage. Stages intentionally make no duration or completion
/// percentage promises because upstream request timing is not under product control.
/// </summary>
public enum PlayerMarketScanStage
{
    DiscoveringPriceItemIds,
    DiscoveringAggregatePrices,
    ScreeningCandidates,
    ReadingFinalistListings,
    ReadingFinalistMetadata,
    CalculatingRecommendations,
}

/// <summary>
/// Player-selected input for one current-market recommendation scan.
/// </summary>
public sealed record PlayerMarketScanRequest(Money Capital, BeginnerRiskProfile RiskProfile);

/// <summary>
/// Meaningful current progress for a running scan. The finalist count is known
/// only after aggregate-price screening has completed.
/// </summary>
public sealed record PlayerMarketScanProgress(
    PlayerMarketScanStage Stage,
    int? FinalistCount);

/// <summary>
/// The in-memory state visible to the local player. A recommendation result is
/// present only after a complete scan has atomically published it.
/// </summary>
public sealed record PlayerMarketScanSnapshot(
    PlayerMarketScanState State,
    PlayerMarketScanProgress? Progress,
    BeginnerRecommendationResult? Result)
{
    public bool IsRetryable => State is PlayerMarketScanState.Cancelled or
        PlayerMarketScanState.RateLimited or
        PlayerMarketScanState.Failed;

    public static PlayerMarketScanSnapshot Idle { get; } = new(
        PlayerMarketScanState.Idle,
        Progress: null,
        Result: null);
}

/// <summary>
/// Result of requesting explicit cancellation for the active scan.
/// </summary>
public sealed record PlayerMarketScanCancellationResult(
    bool HadActiveScan,
    PlayerMarketScanSnapshot Snapshot);

/// <summary>
/// Coordinates the single local player scan without persisting market inputs,
/// recommendation results, or history.
/// </summary>
public interface IPlayerMarketScanLifecycle
{
    PlayerMarketScanSnapshot GetSnapshot();

    bool TryStart(PlayerMarketScanRequest request, out PlayerMarketScanSnapshot startedSnapshot);

    Task<PlayerMarketScanCancellationResult> CancelAsync();
}

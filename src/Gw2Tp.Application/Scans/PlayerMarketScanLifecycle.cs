using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketSnapshots;
using Gw2Tp.Application.Recommendations;

namespace Gw2Tp.Application.Scans;

/// <summary>
/// Runs exactly one player-requested scan at a time. All current market input
/// remains local to the active worker until a complete result can be published.
/// </summary>
public sealed class PlayerMarketScanLifecycle : IPlayerMarketScanLifecycle
{
    public const int MaximumFinalistCount = PublicMarketSnapshotCollector.MaximumFinalistCount;

    private readonly object gate = new();
    private readonly PublicMarketSnapshotCollector marketSnapshotCollector;
    private readonly BeginnerRecommendationEngine recommendationEngine;

    private PlayerMarketScanSnapshot snapshot = PlayerMarketScanSnapshot.Idle;
    private CancellationTokenSource? activeCancellation;
    private Task? activeWorker;

    public PlayerMarketScanLifecycle(
        PublicMarketSnapshotCollector marketSnapshotCollector,
        BeginnerRecommendationEngine recommendationEngine)
    {
        this.marketSnapshotCollector = marketSnapshotCollector ?? throw new ArgumentNullException(nameof(marketSnapshotCollector));
        this.recommendationEngine = recommendationEngine ?? throw new ArgumentNullException(nameof(recommendationEngine));
    }

    public PlayerMarketScanSnapshot GetSnapshot()
    {
        lock (gate)
        {
            return snapshot;
        }
    }

    public bool TryStart(PlayerMarketScanRequest request, out PlayerMarketScanSnapshot startedSnapshot)
    {
        ValidateRequest(request);

        lock (gate)
        {
            if (activeWorker is not null)
            {
                startedSnapshot = snapshot;
                return false;
            }

            var cancellation = new CancellationTokenSource();
            activeCancellation = cancellation;
            snapshot = Running(PlayerMarketScanStage.DiscoveringPriceItemIds);
            activeWorker = Task.Run(() => ExecuteAsync(request, cancellation));
            startedSnapshot = snapshot;
            return true;
        }
    }

    public async Task<PlayerMarketScanCancellationResult> CancelAsync()
    {
        Task? worker;
        lock (gate)
        {
            if (activeWorker is null || activeCancellation is null)
            {
                return new PlayerMarketScanCancellationResult(false, snapshot);
            }

            activeCancellation.Cancel();
            worker = activeWorker;
        }

        await worker.ConfigureAwait(false);
        return new PlayerMarketScanCancellationResult(true, GetSnapshot());
    }

    private async Task ExecuteAsync(
        PlayerMarketScanRequest request,
        CancellationTokenSource cancellation)
    {
        var cancellationToken = cancellation.Token;
        try
        {
            var collection = await marketSnapshotCollector.CollectAsync(
                progress => SetRunningProgress(
                    cancellation,
                    ToPlayerMarketScanStage(progress.Stage),
                    progress.FinalistCount),
                cancellationToken).ConfigureAwait(false);

            SetRunningProgress(
                cancellation,
                PlayerMarketScanStage.CalculatingRecommendations,
                collection.Candidates.Count);
            cancellationToken.ThrowIfCancellationRequested();
            var result = recommendationEngine.Calculate(new BeginnerRecommendationRequest(
                request.Capital,
                request.RiskProfile,
                collection.GeneratedAtUtc,
                collection.Candidates.Select(ToRecommendationCandidate).ToArray()));
            PublishComplete(cancellation, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetTerminal(cancellation, PlayerMarketScanState.Cancelled);
        }
        catch (PublicMarketSnapshotCollectionException exception)
        {
            SetTerminal(
                cancellation,
                cancellation.IsCancellationRequested
                    ? PlayerMarketScanState.Cancelled
                    : exception.ErrorCategory == Gw2ApiErrorCategory.RateLimited
                    ? PlayerMarketScanState.RateLimited
                    : PlayerMarketScanState.Failed);
        }
        catch (Exception)
        {
            SetTerminal(
                cancellation,
                cancellation.IsCancellationRequested
                    ? PlayerMarketScanState.Cancelled
                    : PlayerMarketScanState.Failed);
        }
        finally
        {
            lock (gate)
            {
                if (ReferenceEquals(activeCancellation, cancellation))
                {
                    activeCancellation = null;
                    activeWorker = null;
                    cancellation.Dispose();
                }
            }
        }
    }

    private static BeginnerRecommendationCandidate ToRecommendationCandidate(
        MarketSnapshotCandidate candidate) => new(
        new MarketItemMetadata(
            candidate.ItemId,
            candidate.ItemName,
            MarketItemStackPolicy.NormalStackLimit),
        new MarketListing(
            candidate.ItemId,
            candidate.Buys.Select(ToMarketOrderLevel).ToArray(),
            candidate.Sells.Select(ToMarketOrderLevel).ToArray()));

    private static MarketOrderLevel ToMarketOrderLevel(MarketSnapshotOrderLevel level) => new(
        level.ListingCount,
        level.Quantity,
        level.UnitPriceInCopper);

    private static PlayerMarketScanStage ToPlayerMarketScanStage(
        PublicMarketSnapshotCollectionStage stage) => stage switch
    {
        PublicMarketSnapshotCollectionStage.DiscoveringPriceItemIds => PlayerMarketScanStage.DiscoveringPriceItemIds,
        PublicMarketSnapshotCollectionStage.DiscoveringAggregatePrices => PlayerMarketScanStage.DiscoveringAggregatePrices,
        PublicMarketSnapshotCollectionStage.ScreeningCandidates => PlayerMarketScanStage.ScreeningCandidates,
        PublicMarketSnapshotCollectionStage.ReadingFinalistListings => PlayerMarketScanStage.ReadingFinalistListings,
        PublicMarketSnapshotCollectionStage.ReadingFinalistMetadata => PlayerMarketScanStage.ReadingFinalistMetadata,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown public market collection stage."),
    };

    private static void ValidateRequest(PlayerMarketScanRequest? request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Capital.Copper < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Scan capital cannot be negative.");
        }

        if (!Enum.IsDefined(request.RiskProfile))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "The scan risk profile is not supported.");
        }
    }

    private void SetRunningProgress(
        CancellationTokenSource cancellation,
        PlayerMarketScanStage stage,
        int? finalistCount)
    {
        cancellation.Token.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!ReferenceEquals(activeCancellation, cancellation))
            {
                throw new OperationCanceledException(cancellation.Token);
            }

            snapshot = Running(stage, finalistCount);
        }
    }

    private void PublishComplete(
        CancellationTokenSource cancellation,
        BeginnerRecommendationResult result)
    {
        cancellation.Token.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!ReferenceEquals(activeCancellation, cancellation) || cancellation.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellation.Token);
            }

            snapshot = new PlayerMarketScanSnapshot(
                PlayerMarketScanState.Complete,
                Progress: null,
                result);
        }
    }

    private void SetTerminal(CancellationTokenSource cancellation, PlayerMarketScanState state)
    {
        lock (gate)
        {
            if (ReferenceEquals(activeCancellation, cancellation))
            {
                snapshot = new PlayerMarketScanSnapshot(state, Progress: null, Result: null);
            }
        }
    }

    private static PlayerMarketScanSnapshot Running(
        PlayerMarketScanStage stage,
        int? finalistCount = null) => new(
        PlayerMarketScanState.Running,
        new PlayerMarketScanProgress(stage, finalistCount),
        Result: null);
}

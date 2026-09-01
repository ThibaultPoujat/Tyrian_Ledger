using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Recommendations;
using Gw2Tp.Application.Time;

namespace Gw2Tp.Application.Scans;

/// <summary>
/// Runs exactly one player-requested scan at a time. All current market input
/// remains local to the active worker until a complete result can be published.
/// </summary>
public sealed class PlayerMarketScanLifecycle : IPlayerMarketScanLifecycle
{
    public const int MaximumFinalistCount = 200;

    private readonly object gate = new();
    private readonly IGw2ApiClient marketDataClient;
    private readonly BeginnerRecommendationEngine recommendationEngine;
    private readonly IClock clock;

    private PlayerMarketScanSnapshot snapshot = PlayerMarketScanSnapshot.Idle;
    private CancellationTokenSource? activeCancellation;
    private Task? activeWorker;

    public PlayerMarketScanLifecycle(
        IGw2ApiClient marketDataClient,
        BeginnerRecommendationEngine recommendationEngine,
        IClock clock)
    {
        this.marketDataClient = marketDataClient ?? throw new ArgumentNullException(nameof(marketDataClient));
        this.recommendationEngine = recommendationEngine ?? throw new ArgumentNullException(nameof(recommendationEngine));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
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
            var itemIds = await GetRequiredValueAsync(
                marketDataClient.GetPriceItemIdsAsync(cancellationToken),
                cancellationToken).ConfigureAwait(false);
            ValidatePriceItemIds(itemIds);

            SetRunningProgress(cancellation, PlayerMarketScanStage.DiscoveringAggregatePrices, finalistCount: null);
            var prices = await GetRequiredValueAsync(
                marketDataClient.GetPricesAsync(itemIds, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            SetRunningProgress(cancellation, PlayerMarketScanStage.ScreeningCandidates, finalistCount: null);
            var finalists = SelectFinalists(itemIds, prices);

            if (finalists.Length == 0)
            {
                PublishComplete(cancellation, recommendationEngine.Calculate(new BeginnerRecommendationRequest(
                    request.Capital,
                    request.RiskProfile,
                    clock.UtcNow,
                    [])));
                return;
            }

            SetRunningProgress(
                cancellation,
                PlayerMarketScanStage.ReadingFinalistListings,
                finalists.Length);
            var listings = await GetRequiredValueAsync(
                marketDataClient.GetListingsAsync(finalists, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            ValidateListings(finalists, listings);

            SetRunningProgress(
                cancellation,
                PlayerMarketScanStage.ReadingFinalistMetadata,
                finalists.Length);
            var metadata = await GetRequiredValueAsync(
                marketDataClient.GetItemMetadataAsync(finalists, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            ValidateMetadata(finalists, metadata);

            SetRunningProgress(
                cancellation,
                PlayerMarketScanStage.CalculatingRecommendations,
                finalists.Length);
            cancellationToken.ThrowIfCancellationRequested();
            var metadataByItemId = metadata.ToDictionary(item => item.ItemId);
            var candidates = listings
                .OrderBy(listing => listing.ItemId)
                .Select(listing => new BeginnerRecommendationCandidate(
                    metadataByItemId[listing.ItemId],
                    listing))
                .ToArray();
            var result = recommendationEngine.Calculate(new BeginnerRecommendationRequest(
                request.Capital,
                request.RiskProfile,
                clock.UtcNow,
                candidates));
            PublishComplete(cancellation, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetTerminal(cancellation, PlayerMarketScanState.Cancelled);
        }
        catch (PlayerMarketScanGatewayException exception)
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

    private static async Task<T> GetRequiredValueAsync<T>(
        Task<Gw2ApiResult<T>> responseTask,
        CancellationToken cancellationToken)
    {
        var response = await responseTask.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!response.IsSuccess || response.IsPartialData || response.Value is null)
        {
            throw new PlayerMarketScanGatewayException(
                response.ErrorCategory ?? Gw2ApiErrorCategory.IncompleteData);
        }

        return response.Value;
    }

    private static int[] SelectFinalists(
        IReadOnlyList<int> itemIds,
        IReadOnlyList<MarketPrice> prices)
    {
        ValidatePrices(itemIds, prices);
        return prices
            .Where(HasPotentialAggregateSpread)
            .Select(price => new Finalist(
                price.ItemId,
                Math.Min(price.Buys.Quantity, price.Sells.Quantity),
                (long)price.Sells.UnitPriceInCopper - price.Buys.UnitPriceInCopper))
            .OrderByDescending(finalist => finalist.MinimumAggregateSideQuantity)
            .ThenByDescending(finalist => finalist.AggregatePriceGap)
            .ThenBy(finalist => finalist.ItemId)
            .Take(MaximumFinalistCount)
            .Select(finalist => finalist.ItemId)
            .ToArray();
    }

    private static bool HasPotentialAggregateSpread(MarketPrice price)
    {
        if (price.Buys.Quantity < BeginnerRecommendationPolicy.MinimumAggregateSideQuantity ||
            price.Sells.Quantity < BeginnerRecommendationPolicy.MinimumAggregateSideQuantity ||
            price.Buys.UnitPriceInCopper <= 0 || price.Sells.UnitPriceInCopper <= 1)
        {
            return false;
        }

        var plannedBuyPrice = checked((long)price.Buys.UnitPriceInCopper + 1);
        var plannedSalePrice = checked((long)price.Sells.UnitPriceInCopper - 1);
        return plannedSalePrice >= plannedBuyPrice &&
            plannedSalePrice <= checked(plannedBuyPrice * BeginnerRecommendationPolicy.MaximumPlannedPriceSpreadMultiple);
    }

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

    private static void ValidatePriceItemIds(IReadOnlyList<int>? itemIds)
    {
        if (itemIds is null || itemIds.Count == 0 || itemIds.Any(itemId => itemId <= 0) ||
            itemIds.Distinct().Count() != itemIds.Count)
        {
            throw new PlayerMarketScanGatewayException(Gw2ApiErrorCategory.IncompleteData);
        }
    }

    private static void ValidatePrices(
        IReadOnlyList<int> expectedItemIds,
        IReadOnlyList<MarketPrice>? prices)
    {
        ValidateExactItemSet(expectedItemIds, prices, price => price.ItemId);
        if (prices!.Any(price => price is null || price.Buys is null || price.Sells is null ||
            price.Buys.Quantity < 0 || price.Sells.Quantity < 0 ||
            price.Buys.UnitPriceInCopper < 0 || price.Sells.UnitPriceInCopper < 0))
        {
            throw new PlayerMarketScanGatewayException(Gw2ApiErrorCategory.InvalidPayload);
        }
    }

    private static void ValidateListings(
        IReadOnlyList<int> expectedItemIds,
        IReadOnlyList<MarketListing>? listings)
    {
        ValidateExactItemSet(expectedItemIds, listings, listing => listing.ItemId);
        if (listings!.Any(listing => listing is null || listing.Buys is null || listing.Sells is null ||
            listing.Buys.Any(level => level is null || level.Listings <= 0 || level.Quantity <= 0 || level.UnitPriceInCopper <= 0) ||
            listing.Sells.Any(level => level is null || level.Listings <= 0 || level.Quantity <= 0 || level.UnitPriceInCopper <= 0)))
        {
            throw new PlayerMarketScanGatewayException(Gw2ApiErrorCategory.InvalidPayload);
        }
    }

    private static void ValidateMetadata(
        IReadOnlyList<int> expectedItemIds,
        IReadOnlyList<MarketItemMetadata>? metadata)
    {
        ValidateExactItemSet(expectedItemIds, metadata, item => item.ItemId);
        if (metadata!.Any(item => item is null || string.IsNullOrWhiteSpace(item.Name) ||
            item.NormalStackLimit != MarketItemStackPolicy.NormalStackLimit))
        {
            throw new PlayerMarketScanGatewayException(Gw2ApiErrorCategory.InvalidPayload);
        }
    }

    private static void ValidateExactItemSet<T>(
        IReadOnlyList<int> expectedItemIds,
        IReadOnlyList<T>? values,
        Func<T, int> getItemId)
    {
        if (values is null || values.Count != expectedItemIds.Count)
        {
            throw new PlayerMarketScanGatewayException(Gw2ApiErrorCategory.IncompleteData);
        }

        var expected = expectedItemIds.ToHashSet();
        var received = new HashSet<int>();
        foreach (var value in values)
        {
            if (value is null || !received.Add(getItemId(value)) || !expected.Contains(getItemId(value)))
            {
                throw new PlayerMarketScanGatewayException(Gw2ApiErrorCategory.IncompleteData);
            }
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

    private sealed record Finalist(int ItemId, int MinimumAggregateSideQuantity, long AggregatePriceGap);

    private sealed class PlayerMarketScanGatewayException : Exception
    {
        public PlayerMarketScanGatewayException(Gw2ApiErrorCategory errorCategory)
        {
            ErrorCategory = errorCategory;
        }

        public Gw2ApiErrorCategory ErrorCategory { get; }
    }
}

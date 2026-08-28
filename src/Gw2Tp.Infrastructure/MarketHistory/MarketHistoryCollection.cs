using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Application.Time;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Gw2Tp.Infrastructure.MarketHistory;

/// <summary>
/// Local policy for the opt-in historical collector. These limits bound this
/// feature's work; the shared GW2 gateway remains responsible for all
/// outbound rate limiting and retries.
/// </summary>
internal sealed class MarketHistoryCollectionOptions
{
    public const string ConfigurationSectionName = "MarketHistory:Collection";

    public int MaximumRequestsPerCycle { get; set; } = 2;

    public int IdlePollSeconds { get; set; } = 60;

    public int RateLimitCooldownSeconds { get; set; } = 300;

    public bool TryValidate(out string validationError)
    {
        if (MaximumRequestsPerCycle is < 1 or > 2)
        {
            validationError = "MarketHistory:Collection:MaximumRequestsPerCycle must be between one and two.";
            return false;
        }

        if (IdlePollSeconds <= 0)
        {
            validationError = "MarketHistory:Collection:IdlePollSeconds must be greater than zero.";
            return false;
        }

        if (RateLimitCooldownSeconds <= 0)
        {
            validationError = "MarketHistory:Collection:RateLimitCooldownSeconds must be greater than zero.";
            return false;
        }

        validationError = string.Empty;
        return true;
    }
}

internal sealed class MarketHistoryCollectionOptionsValidator : IValidateOptions<MarketHistoryCollectionOptions>
{
    public ValidateOptionsResult Validate(string? name, MarketHistoryCollectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.TryValidate(out var validationError)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(validationError);
    }
}

/// <summary>
/// Performs at most one batched prices request and one batched listings
/// request in a cycle, using only explicitly tracked item IDs.
/// </summary>
internal sealed class MarketHistoryCollector
{
    private readonly IMarketWatchlistStore watchlistStore;
    private readonly IMarketSnapshotStore snapshotStore;
    private readonly IGw2ApiClient? gw2ApiClient;
    private readonly IClock clock;
    private readonly MarketHistoryCollectionOptions options;

    public MarketHistoryCollector(
        IMarketWatchlistStore watchlistStore,
        IMarketSnapshotStore snapshotStore,
        IClock clock,
        IOptions<MarketHistoryCollectionOptions> options,
        IGw2ApiClient? gw2ApiClient = null)
        : this(
            watchlistStore,
            snapshotStore,
            gw2ApiClient,
            clock,
            options?.Value ?? throw new ArgumentNullException(nameof(options)))
    {
    }

    internal MarketHistoryCollector(
        IMarketWatchlistStore watchlistStore,
        IMarketSnapshotStore snapshotStore,
        IGw2ApiClient? gw2ApiClient,
        IClock clock,
        MarketHistoryCollectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(watchlistStore);
        ArgumentNullException.ThrowIfNull(snapshotStore);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.TryValidate(out var validationError))
        {
            throw new OptionsValidationException(
                MarketHistoryCollectionOptions.ConfigurationSectionName,
                typeof(MarketHistoryCollectionOptions),
                [validationError]);
        }

        this.watchlistStore = watchlistStore;
        this.snapshotStore = snapshotStore;
        this.gw2ApiClient = gw2ApiClient;
        this.clock = clock;
        this.options = options;
    }

    public async Task<MarketHistoryCollectionCycleOutcome> CollectDueAsync(CancellationToken cancellationToken)
    {
        var trackedItems = await watchlistStore.ListAsync(cancellationToken).ConfigureAwait(false);
        if (trackedItems.Count == 0)
        {
            return MarketHistoryCollectionCycleOutcome.Empty;
        }

        var configuredGw2ApiClient = gw2ApiClient ?? throw new InvalidOperationException(
            "Historical collection requires the typed public-market gateway when tracked items exist.");

        var collectionStates = await snapshotStore
            .GetCollectionStatesAsync(trackedItems.Select(item => item.ItemId).ToArray(), cancellationToken)
            .ConfigureAwait(false);
        var plan = MarketCollectionPlanner.CreatePlan(trackedItems, collectionStates, clock.UtcNow);
        var remainingRequestBudget = options.MaximumRequestsPerCycle;
        var requestCount = 0;
        var priceSnapshotCount = 0;
        var orderBookSnapshotCount = 0;

        if (plan.PriceItemIds.Count > 0 && remainingRequestBudget > 0)
        {
            var priceBatch = await CollectPricesAsync(
                    configuredGw2ApiClient,
                    plan.PriceItemIds,
                    collectionStates,
                    cancellationToken)
                .ConfigureAwait(false);
            requestCount++;
            remainingRequestBudget--;
            priceSnapshotCount += priceBatch.SnapshotCount;
            if (priceBatch.IsRateLimited)
            {
                return new MarketHistoryCollectionCycleOutcome(
                    IsRateLimited: true,
                    requestCount,
                    priceSnapshotCount,
                    orderBookSnapshotCount);
            }
        }

        if (plan.OrderBookItemIds.Count > 0 && remainingRequestBudget > 0)
        {
            var orderBookBatch = await CollectOrderBooksAsync(
                    configuredGw2ApiClient,
                    plan.OrderBookItemIds,
                    collectionStates,
                    cancellationToken)
                .ConfigureAwait(false);
            requestCount++;
            orderBookSnapshotCount += orderBookBatch.SnapshotCount;
            if (orderBookBatch.IsRateLimited)
            {
                return new MarketHistoryCollectionCycleOutcome(
                    IsRateLimited: true,
                    requestCount,
                    priceSnapshotCount,
                    orderBookSnapshotCount);
            }
        }

        return new MarketHistoryCollectionCycleOutcome(
            IsRateLimited: false,
            requestCount,
            priceSnapshotCount,
            orderBookSnapshotCount);
    }

    private async Task<CollectionBatchOutcome> CollectPricesAsync(
        IGw2ApiClient configuredGw2ApiClient,
        IReadOnlyList<int> dueItemIds,
        IReadOnlyDictionary<int, MarketSnapshotCollectionState> collectionStates,
        CancellationToken cancellationToken)
    {
        var result = await configuredGw2ApiClient
            .GetPricesAsync(dueItemIds, cancellationToken)
            .ConfigureAwait(false);
        if (result.ErrorCategory == Gw2ApiErrorCategory.RateLimited)
        {
            return CollectionBatchOutcome.RateLimited;
        }

        if (!result.IsSuccess || result.Value is null || result.Freshness is null)
        {
            return CollectionBatchOutcome.Empty;
        }

        var dueItemIdSet = dueItemIds.ToHashSet();
        var snapshotCount = 0;
        foreach (var price in result.Value
                     .Where(price => dueItemIdSet.Contains(price.ItemId))
                     .GroupBy(price => price.ItemId)
                     .Select(group => group.First()))
        {
            if (collectionStates.TryGetValue(price.ItemId, out var state) &&
                state.LatestPriceCapturedAtUtc is { } latestCapturedAtUtc &&
                result.Freshness.CapturedAtUtc <= latestCapturedAtUtc)
            {
                continue;
            }

            await snapshotStore.AppendAsync(
                new MarketPriceSnapshot(Guid.NewGuid(), price, result.Freshness),
                cancellationToken).ConfigureAwait(false);
            snapshotCount++;
        }

        return new CollectionBatchOutcome(IsRateLimited: false, SnapshotCount: snapshotCount);
    }

    private async Task<CollectionBatchOutcome> CollectOrderBooksAsync(
        IGw2ApiClient configuredGw2ApiClient,
        IReadOnlyList<int> dueItemIds,
        IReadOnlyDictionary<int, MarketSnapshotCollectionState> collectionStates,
        CancellationToken cancellationToken)
    {
        var result = await configuredGw2ApiClient
            .GetListingsAsync(dueItemIds, cancellationToken)
            .ConfigureAwait(false);
        if (result.ErrorCategory == Gw2ApiErrorCategory.RateLimited)
        {
            return CollectionBatchOutcome.RateLimited;
        }

        if (!result.IsSuccess || result.Value is null || result.Freshness is null)
        {
            return CollectionBatchOutcome.Empty;
        }

        var dueItemIdSet = dueItemIds.ToHashSet();
        var snapshotCount = 0;
        foreach (var orderBook in result.Value
                     .Where(orderBook => dueItemIdSet.Contains(orderBook.ItemId))
                     .GroupBy(orderBook => orderBook.ItemId)
                     .Select(group => group.First()))
        {
            if (collectionStates.TryGetValue(orderBook.ItemId, out var state) &&
                state.LatestOrderBookCapturedAtUtc is { } latestCapturedAtUtc &&
                result.Freshness.CapturedAtUtc <= latestCapturedAtUtc)
            {
                continue;
            }

            await snapshotStore.AppendAsync(
                new MarketOrderBookSnapshot(Guid.NewGuid(), orderBook, result.Freshness),
                cancellationToken).ConfigureAwait(false);
            snapshotCount++;
        }

        return new CollectionBatchOutcome(IsRateLimited: false, SnapshotCount: snapshotCount);
    }

    private sealed record CollectionBatchOutcome(bool IsRateLimited, int SnapshotCount)
    {
        public static readonly CollectionBatchOutcome Empty = new(IsRateLimited: false, SnapshotCount: 0);
        public static readonly CollectionBatchOutcome RateLimited = new(IsRateLimited: true, SnapshotCount: 0);
    }
}

internal sealed record MarketHistoryCollectionCycleOutcome(
    bool IsRateLimited,
    int RequestCount,
    int PriceSnapshotCount,
    int OrderBookSnapshotCount)
{
    public static readonly MarketHistoryCollectionCycleOutcome Empty = new(
        IsRateLimited: false,
        RequestCount: 0,
        PriceSnapshotCount: 0,
        OrderBookSnapshotCount: 0);
}

internal interface IMarketHistoryCollectionDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

internal sealed class SystemMarketHistoryCollectionDelay : IMarketHistoryCollectionDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}

/// <summary>
/// Re-evaluates the local, opt-in watchlist on a bounded cadence. A terminal
/// rate-limit result pauses collection before any further due work is sent.
/// </summary>
internal sealed class MarketHistoryCollectionHostedService : BackgroundService
{
    private readonly MarketHistoryCollector collector;
    private readonly MarketHistoryCollectionOptions options;
    private readonly IMarketHistoryCollectionDelay delay;

    public MarketHistoryCollectionHostedService(
        MarketHistoryCollector collector,
        IOptions<MarketHistoryCollectionOptions> options,
        IMarketHistoryCollectionDelay delay)
        : this(
            collector,
            options?.Value ?? throw new ArgumentNullException(nameof(options)),
            delay)
    {
    }

    internal MarketHistoryCollectionHostedService(
        MarketHistoryCollector collector,
        MarketHistoryCollectionOptions options,
        IMarketHistoryCollectionDelay delay)
    {
        ArgumentNullException.ThrowIfNull(collector);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(delay);

        if (!options.TryValidate(out var validationError))
        {
            throw new OptionsValidationException(
                MarketHistoryCollectionOptions.ConfigurationSectionName,
                typeof(MarketHistoryCollectionOptions),
                [validationError]);
        }

        this.collector = collector;
        this.options = options;
        this.delay = delay;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        RunCollectionLoopAsync(stoppingToken);

    internal async Task RunCollectionLoopAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var outcome = await collector.CollectDueAsync(stoppingToken).ConfigureAwait(false);
                var nextDelay = outcome.IsRateLimited
                    ? TimeSpan.FromSeconds(options.RateLimitCooldownSeconds)
                    : TimeSpan.FromSeconds(options.IdlePollSeconds);
                await delay.DelayAsync(nextDelay, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown: do not begin another collection cycle.
        }
    }
}

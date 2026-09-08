using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Time;

namespace Gw2Tp.Application.MarketHistory;

public enum MarketHistoryCollectorState
{
    Idle,
    Collecting,
}

public enum MarketHistoryCollectionOutcome
{
    Succeeded,
    Failed,
}

/// <summary>
/// Safe operational state for the local market-history collector. It contains
/// no upstream payloads, credentials, or request details.
/// </summary>
public sealed record MarketHistoryCollectorHealth(
    MarketHistoryCollectorState State,
    DateTimeOffset? LastSuccessfulCaptureAtUtc,
    Gw2ApiErrorCategory? LastFailure,
    int FailureCount,
    int TrackedItemCount,
    DateTimeOffset? NextRunAtUtc);

public sealed record MarketHistoryCollectionRun(
    MarketHistoryCollectionOutcome Outcome,
    int TrackedItemCount,
    int DueItemCount,
    int AppendedPriceObservationCount,
    int AppendedOrderBookSnapshotCount,
    Gw2ApiErrorCategory? ErrorCategory,
    DateTimeOffset? NextDueAtUtc);

public interface IMarketHistoryCollector
{
    Task<MarketHistoryCollectionRun> CollectDueAsync(CancellationToken cancellationToken = default);

    Task<MarketHistoryCollectionRun> CollectNowAsync(CancellationToken cancellationToken = default);

    MarketHistoryCollectorHealth GetHealth();

    void SetNextRunAtUtc(DateTimeOffset? nextRunAtUtc);
}

/// <summary>
/// Collects raw evidence for a policy-selected set of local markets. Gateway
/// rate limiting, request batching, retries, and cancellation remain inside
/// <see cref="IGw2ApiClient"/>; this service never constructs upstream URLs.
/// </summary>
public sealed class MarketHistoryCollector(
    IAdaptiveMarketSamplingPolicy samplingPolicy,
    IMarketHistoryRepository marketHistoryRepository,
    IGw2ApiClient marketDataClient,
    IClock clock) : IMarketHistoryCollector
{
    private readonly SemaphoreSlim collectionGate = new(1, 1);
    private readonly object healthGate = new();
    private MarketHistoryCollectorHealth health = new(
        MarketHistoryCollectorState.Idle, null, null, 0, 0, null);

    public async Task<MarketHistoryCollectionRun> CollectDueAsync(CancellationToken cancellationToken = default) =>
        await CollectAsync(forceAllTargets: false, cancellationToken).ConfigureAwait(false);

    public async Task<MarketHistoryCollectionRun> CollectNowAsync(CancellationToken cancellationToken = default) =>
        await CollectAsync(forceAllTargets: true, cancellationToken).ConfigureAwait(false);

    public MarketHistoryCollectorHealth GetHealth()
    {
        lock (healthGate)
        {
            return health;
        }
    }

    public void SetNextRunAtUtc(DateTimeOffset? nextRunAtUtc)
    {
        if (nextRunAtUtc is { } nextRun && nextRun.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Collector schedule timestamps must be UTC.", nameof(nextRunAtUtc));
        }

        lock (healthGate)
        {
            health = health with { NextRunAtUtc = nextRunAtUtc };
        }
    }

    private async Task<MarketHistoryCollectionRun> CollectAsync(bool forceAllTargets, CancellationToken cancellationToken)
    {
        await collectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SetCollecting(true);
            var plan = await samplingPolicy.BuildPlanAsync(cancellationToken).ConfigureAwait(false);
            var requestedAtUtc = RequireUtc(clock.UtcNow);
            var dueTargets = await GetDueTargetsAsync(plan.Targets, requestedAtUtc, forceAllTargets, cancellationToken).ConfigureAwait(false);
            var observedAtUtc = await GetUniqueObservedAtUtcAsync(dueTargets, requestedAtUtc, cancellationToken).ConfigureAwait(false);
            var nextDueAtUtc = await GetNextDueAtUtcAsync(plan.Targets, observedAtUtc, cancellationToken).ConfigureAwait(false);
            SetTrackedItemCount(plan.Targets.Count);

            if (dueTargets.Count == 0)
            {
                return new MarketHistoryCollectionRun(
                    MarketHistoryCollectionOutcome.Succeeded, plan.Targets.Count, 0, 0, 0, null, nextDueAtUtc);
            }

            var prices = await GetCompletePricesAsync(dueTargets, cancellationToken).ConfigureAwait(false);
            if (prices.ErrorCategory is { } priceError)
            {
                return RecordFailure(plan.Targets.Count, dueTargets.Count, priceError, nextDueAtUtc);
            }

            var listings = await GetCompleteListingsAsync(dueTargets.Where(target => target.IncludeOrderBook).ToArray(), cancellationToken).ConfigureAwait(false);
            var priceCount = 0;
            foreach (var target in dueTargets)
            {
                var price = prices.Value![target.ItemId];
                await marketHistoryRepository.AppendPriceObservationAsync(new MarketPriceObservation(
                    observedAtUtc,
                    target.ItemId,
                    price.Buys.UnitPriceInCopper,
                    price.Sells.UnitPriceInCopper,
                    price.Buys.Quantity,
                    price.Sells.Quantity,
                    MarketObservationSourceStatus.Complete,
                    target.Tier,
                    plan.PolicyVersion), cancellationToken).ConfigureAwait(false);
                priceCount++;
            }

            var orderBookCount = 0;
            if (listings.ErrorCategory is null)
            {
                foreach (var target in dueTargets.Where(target => target.IncludeOrderBook))
                {
                    var listing = listings.Value![target.ItemId];
                    await marketHistoryRepository.AppendOrderBookSnapshotAsync(new MarketOrderBookSnapshot(
                        observedAtUtc,
                        target.ItemId,
                        MarketObservationSourceStatus.Complete,
                        target.Tier,
                        plan.PolicyVersion,
                        ToOrderBookLevels(listing)), cancellationToken).ConfigureAwait(false);
                    orderBookCount++;
                }
            }

            RecordSuccessfulCapture(observedAtUtc);
            return listings.ErrorCategory is { } listingError
                ? RecordFailure(plan.Targets.Count, dueTargets.Count, listingError, nextDueAtUtc, priceCount, orderBookCount)
                : new MarketHistoryCollectionRun(
                    MarketHistoryCollectionOutcome.Succeeded,
                    plan.Targets.Count,
                    dueTargets.Count,
                    priceCount,
                    orderBookCount,
                    null,
                    nextDueAtUtc);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return RecordFailure(GetHealth().TrackedItemCount, 0, Gw2ApiErrorCategory.UnexpectedResponse, null);
        }
        finally
        {
            SetCollecting(false);
            collectionGate.Release();
        }
    }

    private async Task<IReadOnlyList<MarketSamplingTarget>> GetDueTargetsAsync(
        IReadOnlyList<MarketSamplingTarget> targets,
        DateTimeOffset observedAtUtc,
        bool forceAllTargets,
        CancellationToken cancellationToken)
    {
        var dueTargets = new List<MarketSamplingTarget>();
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var latest = await marketHistoryRepository.GetLatestPriceObservationAsync(target.ItemId, cancellationToken).ConfigureAwait(false);
            if (forceAllTargets || latest is null || latest.ObservedAtUtc + target.Interval <= observedAtUtc)
            {
                dueTargets.Add(target);
            }
        }

        return dueTargets;
    }

    private async Task<DateTimeOffset?> GetNextDueAtUtcAsync(
        IReadOnlyList<MarketSamplingTarget> targets,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        DateTimeOffset? nextDueAtUtc = null;
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var latest = await marketHistoryRepository.GetLatestPriceObservationAsync(target.ItemId, cancellationToken).ConfigureAwait(false);
            var candidate = latest is null ? observedAtUtc : latest.ObservedAtUtc + target.Interval;
            if (nextDueAtUtc is null || candidate < nextDueAtUtc)
            {
                nextDueAtUtc = candidate;
            }
        }

        return nextDueAtUtc;
    }

    private async Task<DateTimeOffset> GetUniqueObservedAtUtcAsync(
        IReadOnlyList<MarketSamplingTarget> dueTargets,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken)
    {
        var observedAtUtc = requestedAtUtc;
        foreach (var target in dueTargets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var latest = await marketHistoryRepository.GetLatestPriceObservationAsync(target.ItemId, cancellationToken).ConfigureAwait(false);
            if (latest is not null && latest.ObservedAtUtc >= observedAtUtc)
            {
                observedAtUtc = latest.ObservedAtUtc.AddTicks(1);
            }
        }

        return observedAtUtc;
    }

    private async Task<CollectionPayload<MarketPrice>> GetCompletePricesAsync(
        IReadOnlyList<MarketSamplingTarget> targets,
        CancellationToken cancellationToken)
    {
        var response = await marketDataClient.GetPricesAsync(targets.Select(target => target.ItemId).ToArray(), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess || response.IsPartialData || response.Value is null)
        {
            return CollectionPayload<MarketPrice>.Failure(response.ErrorCategory ?? Gw2ApiErrorCategory.IncompleteData);
        }

        return TryIndexExact(targets, response.Value, price => price.ItemId,
            price => price.Buys is not null && price.Sells is not null &&
                     price.Buys.Quantity >= 0 && price.Sells.Quantity >= 0 &&
                     price.Buys.UnitPriceInCopper >= 0 && price.Sells.UnitPriceInCopper >= 0);
    }

    private async Task<CollectionPayload<MarketListing>> GetCompleteListingsAsync(
        IReadOnlyList<MarketSamplingTarget> targets,
        CancellationToken cancellationToken)
    {
        if (targets.Count == 0)
        {
            return CollectionPayload<MarketListing>.Success(new Dictionary<int, MarketListing>());
        }

        var response = await marketDataClient.GetListingsAsync(targets.Select(target => target.ItemId).ToArray(), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess || response.IsPartialData || response.Value is null)
        {
            return CollectionPayload<MarketListing>.Failure(response.ErrorCategory ?? Gw2ApiErrorCategory.IncompleteData);
        }

        return TryIndexExact(targets, response.Value, listing => listing.ItemId,
            listing => listing.Buys is not null && listing.Sells is not null &&
                       listing.Buys.All(IsValidLevel) && listing.Sells.All(IsValidLevel));
    }

    private static CollectionPayload<T> TryIndexExact<T>(
        IReadOnlyList<MarketSamplingTarget> targets,
        IReadOnlyList<T> values,
        Func<T, int> getItemId,
        Func<T, bool> isValid)
    {
        if (values.Count != targets.Count)
        {
            return CollectionPayload<T>.Failure(Gw2ApiErrorCategory.IncompleteData);
        }

        var expected = targets.Select(target => target.ItemId).ToHashSet();
        var indexed = new Dictionary<int, T>();
        foreach (var value in values)
        {
            if (value is null || !isValid(value) || !indexed.TryAdd(getItemId(value), value) || !expected.Contains(getItemId(value)))
            {
                return CollectionPayload<T>.Failure(Gw2ApiErrorCategory.InvalidPayload);
            }
        }

        return indexed.Count == expected.Count
            ? CollectionPayload<T>.Success(indexed)
            : CollectionPayload<T>.Failure(Gw2ApiErrorCategory.IncompleteData);
    }

    private static bool IsValidLevel(MarketOrderLevel level) =>
        level is not null && level.Listings > 0 && level.Quantity > 0 && level.UnitPriceInCopper > 0;

    private static IReadOnlyList<MarketOrderBookLevel> ToOrderBookLevels(MarketListing listing) =>
        listing.Buys.Select((level, ordinal) => new MarketOrderBookLevel(MarketOrderBookSide.Buy, ordinal, level.UnitPriceInCopper, level.Quantity, level.Listings))
            .Concat(listing.Sells.Select((level, ordinal) => new MarketOrderBookLevel(MarketOrderBookSide.Sell, ordinal, level.UnitPriceInCopper, level.Quantity, level.Listings)))
            .ToArray();

    private MarketHistoryCollectionRun RecordFailure(
        int trackedItemCount,
        int dueItemCount,
        Gw2ApiErrorCategory errorCategory,
        DateTimeOffset? nextDueAtUtc,
        int appendedPriceObservationCount = 0,
        int appendedOrderBookSnapshotCount = 0)
    {
        lock (healthGate)
        {
            health = health with
            {
                LastFailure = errorCategory,
                FailureCount = checked(health.FailureCount + 1),
                TrackedItemCount = trackedItemCount,
            };
        }

        return new MarketHistoryCollectionRun(
            MarketHistoryCollectionOutcome.Failed,
            trackedItemCount,
            dueItemCount,
            appendedPriceObservationCount,
            appendedOrderBookSnapshotCount,
            errorCategory,
            nextDueAtUtc);
    }

    private void RecordSuccessfulCapture(DateTimeOffset observedAtUtc)
    {
        lock (healthGate)
        {
            health = health with { LastSuccessfulCaptureAtUtc = observedAtUtc };
        }
    }

    private void SetCollecting(bool isCollecting)
    {
        lock (healthGate)
        {
            health = health with { State = isCollecting ? MarketHistoryCollectorState.Collecting : MarketHistoryCollectorState.Idle };
        }
    }

    private void SetTrackedItemCount(int trackedItemCount)
    {
        lock (healthGate)
        {
            health = health with { TrackedItemCount = trackedItemCount };
        }
    }

    private static DateTimeOffset RequireUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero
        ? value
        : throw new ArgumentException("Collection timestamps must be UTC.", nameof(value));

    private sealed record CollectionPayload<T>(IReadOnlyDictionary<int, T>? Value, Gw2ApiErrorCategory? ErrorCategory)
    {
        public static CollectionPayload<T> Success(IReadOnlyDictionary<int, T> value) => new(value, null);

        public static CollectionPayload<T> Failure(Gw2ApiErrorCategory errorCategory) => new(null, errorCategory);
    }
}

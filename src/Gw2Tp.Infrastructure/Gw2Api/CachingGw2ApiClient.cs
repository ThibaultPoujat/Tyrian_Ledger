using System.Collections.Concurrent;
using System.Globalization;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Time;
using Microsoft.Extensions.Options;

namespace Gw2Tp.Infrastructure.Gw2Api;

internal interface IGw2ApiTransport
{
    Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken = default);

    Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Application-facing public-market gateway that caches successful responses
/// and exposes their capture/expiry metadata.
/// </summary>
internal sealed class CachingGw2ApiClient : IGw2ApiClient
{
    private readonly IGw2ApiTransport _transport;
    private readonly IClock _clock;
    private readonly IMarketDataDiagnosticsRecorder _diagnostics;
    private readonly TimeSpan _timeToLive;
    private readonly ExpiringResponseCache<IReadOnlyList<MarketPrice>> _prices;
    private readonly ExpiringResponseCache<IReadOnlyList<MarketListing>> _listings;

    public CachingGw2ApiClient(
        IGw2ApiTransport transport,
        IOptions<Gw2MarketCacheOptions> options,
        IClock clock,
        IMarketDataDiagnosticsRecorder diagnostics)
        : this(
            transport,
            options?.Value ?? throw new ArgumentNullException(nameof(options)),
            clock,
            diagnostics)
    {
    }

    internal CachingGw2ApiClient(
        IGw2ApiTransport transport,
        Gw2MarketCacheOptions options,
        IClock clock,
        IMarketDataDiagnosticsRecorder diagnostics)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (!options.TryValidate(out var validationError))
        {
            throw new OptionsValidationException(
                "Gw2Api:MarketCache",
                typeof(Gw2MarketCacheOptions),
                [validationError]);
        }

        _transport = transport;
        _clock = clock;
        _diagnostics = diagnostics;
        _timeToLive = TimeSpan.FromSeconds(options.TimeToLiveSeconds);
        _prices = new ExpiringResponseCache<IReadOnlyList<MarketPrice>>(
            _clock,
            _diagnostics,
            Gw2MarketDataEndpoint.Prices);
        _listings = new ExpiringResponseCache<IReadOnlyList<MarketListing>>(
            _clock,
            _diagnostics,
            Gw2MarketDataEndpoint.Listings);
    }

    internal CachingGw2ApiClient(
        IGw2ApiTransport transport,
        Gw2MarketCacheOptions options,
        IClock clock)
        : this(transport, options, clock, NullMarketDataDiagnostics.Instance)
    {
    }

    public Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken = default)
    {
        var request = CreateCacheRequest("commerce/prices", itemIds);
        return _prices.GetOrCreateAsync(
            request.Key,
            cancellationToken,
            operationCancellationToken => _transport.GetPricesAsync(request.ItemIds, operationCancellationToken),
            CreateFreshness);
    }

    public Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken = default)
    {
        var request = CreateCacheRequest("commerce/listings", itemIds);
        return _listings.GetOrCreateAsync(
            request.Key,
            cancellationToken,
            operationCancellationToken => _transport.GetListingsAsync(request.ItemIds, operationCancellationToken),
            CreateFreshness);
    }

    private DataFreshness CreateFreshness()
    {
        var capturedAtUtc = _clock.UtcNow;
        return new DataFreshness(capturedAtUtc, capturedAtUtc + _timeToLive);
    }

    private static Gw2MarketCacheRequest CreateCacheRequest(
        string resourcePath,
        IReadOnlyCollection<int> itemIds)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        if (itemIds.Count == 0)
        {
            throw new ArgumentException("At least one item ID is required.", nameof(itemIds));
        }

        var itemIdSnapshot = itemIds.OrderBy(itemId => itemId).ToArray();
        foreach (var itemId in itemIdSnapshot)
        {
            if (itemId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(itemIds),
                    itemId,
                    "Item IDs must be positive.");
            }
        }

        return new Gw2MarketCacheRequest(
            new Gw2MarketCacheKey(
                resourcePath,
                string.Join(
                    ',',
                    itemIdSnapshot.Select(itemId => itemId.ToString(CultureInfo.InvariantCulture)))),
            itemIdSnapshot);
    }

    private sealed class ExpiringResponseCache<T>
    {
        private readonly IClock _clock;
        private readonly IMarketDataDiagnosticsRecorder _diagnostics;
        private readonly Gw2MarketDataEndpoint _endpoint;
        private readonly ConcurrentDictionary<Gw2MarketCacheKey, CacheEntry> _entries = new();
        private readonly ConcurrentDictionary<Gw2MarketCacheKey, Lazy<Task<Gw2ApiResult<T>>>> _inFlight = new();

        public ExpiringResponseCache(
            IClock clock,
            IMarketDataDiagnosticsRecorder diagnostics,
            Gw2MarketDataEndpoint endpoint)
        {
            ArgumentNullException.ThrowIfNull(clock);
            ArgumentNullException.ThrowIfNull(diagnostics);
            _clock = clock;
            _diagnostics = diagnostics;
            _endpoint = endpoint;
        }

        public async Task<Gw2ApiResult<T>> GetOrCreateAsync(
            Gw2MarketCacheKey key,
            CancellationToken cancellationToken,
            Func<CancellationToken, Task<Gw2ApiResult<T>>> getFromTransportAsync,
            Func<DataFreshness> createFreshness)
        {
            ArgumentNullException.ThrowIfNull(getFromTransportAsync);
            ArgumentNullException.ThrowIfNull(createFreshness);
            cancellationToken.ThrowIfCancellationRequested();

            if (_entries.TryGetValue(key, out var cached) &&
                _clock.UtcNow < cached.Result.Freshness!.ExpiresAtUtc)
            {
                _diagnostics.RecordCacheHit(_endpoint);
                return cached.Result;
            }

            _diagnostics.RecordCacheMiss(_endpoint);
            _entries.TryRemove(key, out _);
            var candidate = new Lazy<Task<Gw2ApiResult<T>>>(
                () => FillAsync(key, getFromTransportAsync, createFreshness),
                LazyThreadSafetyMode.ExecutionAndPublication);
            var inFlight = _inFlight.GetOrAdd(key, candidate);

            return await inFlight.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task<Gw2ApiResult<T>> FillAsync(
            Gw2MarketCacheKey key,
            Func<CancellationToken, Task<Gw2ApiResult<T>>> getFromTransportAsync,
            Func<DataFreshness> createFreshness)
        {
            try
            {
                // A cache fill is shared by all callers with this request
                // key. Like the request scheduler, one caller cancelling its
                // wait must not cancel the shared outbound operation.
                var result = await getFromTransportAsync(CancellationToken.None).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    return result;
                }

                var cachedResult = Gw2ApiResult<T>.Success(
                    result.Value!,
                    result.IsPartialData,
                    createFreshness());
                _entries[key] = new CacheEntry(cachedResult);
                return cachedResult;
            }
            finally
            {
                _inFlight.TryRemove(key, out _);
            }
        }

        private sealed record CacheEntry(Gw2ApiResult<T> Result);
    }
}

internal sealed record Gw2MarketCacheKey(string ResourcePath, string ItemIds);

internal sealed record Gw2MarketCacheRequest(
    Gw2MarketCacheKey Key,
    IReadOnlyCollection<int> ItemIds);

using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Time;
using Gw2Tp.Infrastructure.Gw2Api;
using Xunit;

namespace Gw2Tp.Infrastructure.Tests;

public sealed class CachingGw2ApiClientTests
{
    [Fact]
    public async Task Successful_market_data_is_cached_with_capture_and_expiry_metadata()
    {
        var capturedAtUtc = new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);
        var clock = new MutableClock(capturedAtUtc);
        var transport = new StubMarketTransport((_, _) => Task.FromResult(SuccessfulPrices()));
        var client = CreateClient(transport, clock);

        var first = await client.GetPricesAsync([900001]);
        var second = await client.GetPricesAsync([900001]);

        Assert.All([first, second], result => Assert.True(result.IsSuccess));
        Assert.Equal(1, transport.PriceRequestCount);
        Assert.Equal(capturedAtUtc, first.Freshness!.CapturedAtUtc);
        Assert.Equal(capturedAtUtc.AddMinutes(2), first.Freshness.ExpiresAtUtc);
        Assert.Equal(first.Freshness, second.Freshness);
    }

    [Fact]
    public async Task Entry_at_its_expiry_boundary_is_refilled_with_new_freshness()
    {
        var capturedAtUtc = new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);
        var clock = new MutableClock(capturedAtUtc);
        var transport = new StubMarketTransport((_, _) => Task.FromResult(SuccessfulPrices()));
        var client = CreateClient(transport, clock);

        var first = await client.GetPricesAsync([900001]);
        clock.Advance(TimeSpan.FromMinutes(2));
        var refreshed = await client.GetPricesAsync([900001]);

        Assert.Equal(2, transport.PriceRequestCount);
        Assert.Equal(capturedAtUtc, first.Freshness!.CapturedAtUtc);
        Assert.Equal(capturedAtUtc.AddMinutes(2), refreshed.Freshness!.CapturedAtUtc);
        Assert.Equal(capturedAtUtc.AddMinutes(4), refreshed.Freshness.ExpiresAtUtc);
    }

    [Fact]
    public async Task Concurrent_cache_misses_share_one_transport_fill()
    {
        var responseSource = new TaskCompletionSource<Gw2ApiResult<IReadOnlyList<MarketPrice>>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new StubMarketTransport((_, _) => responseSource.Task);
        var client = CreateClient(
            transport,
            new MutableClock(new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero)));

        var first = client.GetPricesAsync([900001]);
        await transport.WaitForPriceRequestAsync();

        var second = client.GetPricesAsync([900001]);
        await Task.Yield();
        Assert.Equal(1, transport.PriceRequestCount);

        responseSource.SetResult(SuccessfulPrices());
        var results = await Task.WhenAll(first, second);

        Assert.All(results, result =>
        {
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Freshness);
        });
        Assert.Equal(1, transport.PriceRequestCount);
    }

    [Fact]
    public async Task Failed_transport_results_are_not_cached()
    {
        var responses = new Queue<Gw2ApiResult<IReadOnlyList<MarketPrice>>>(
        [
            Gw2ApiResult<IReadOnlyList<MarketPrice>>.Failure(Gw2ApiErrorCategory.UpstreamUnavailable),
            SuccessfulPrices(),
        ]);
        var transport = new StubMarketTransport((_, _) => Task.FromResult(responses.Dequeue()));
        var client = CreateClient(
            transport,
            new MutableClock(new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero)));

        var failed = await client.GetPricesAsync([900001]);
        var recovered = await client.GetPricesAsync([900001]);

        Assert.False(failed.IsSuccess);
        Assert.Null(failed.Freshness);
        Assert.True(recovered.IsSuccess);
        Assert.NotNull(recovered.Freshness);
        Assert.Equal(2, transport.PriceRequestCount);
    }

    [Fact]
    public async Task Partial_successes_are_cached_with_freshness_metadata()
    {
        var transport = new StubMarketTransport((_, _) => Task.FromResult(
            Gw2ApiResult<IReadOnlyList<MarketPrice>>.Success(
                SuccessfulPrices().Value!,
                isPartialData: true)));
        var client = CreateClient(
            transport,
            new MutableClock(new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero)));

        var first = await client.GetPricesAsync([900001, 999999]);
        var second = await client.GetPricesAsync([900001, 999999]);

        Assert.True(first.IsPartialData);
        Assert.True(second.IsPartialData);
        Assert.NotNull(first.Freshness);
        Assert.Equal(first.Freshness, second.Freshness);
        Assert.Equal(1, transport.PriceRequestCount);
    }

    [Fact]
    public void Cache_options_require_a_positive_time_to_live()
    {
        var options = new Gw2MarketCacheOptions { TimeToLiveSeconds = 0 };

        Assert.False(options.TryValidate(out var error));
        Assert.Equal(
            "Gw2Api:MarketCache:TimeToLiveSeconds must be greater than zero.",
            error);
    }

    private static CachingGw2ApiClient CreateClient(
        IGw2ApiTransport transport,
        IClock clock) =>
        new(
            transport,
            new Gw2MarketCacheOptions { TimeToLiveSeconds = 120 },
            clock);

    private static Gw2ApiResult<IReadOnlyList<MarketPrice>> SuccessfulPrices() =>
        Gw2ApiResult<IReadOnlyList<MarketPrice>>.Success(
        [
            new MarketPrice(
                900001,
                false,
                new MarketOrderSummary(1200, 850),
                new MarketOrderSummary(40, 905)),
        ]);

    private sealed class MutableClock : IClock
    {
        public MutableClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow.ToUniversalTime();
        }

        public DateTimeOffset UtcNow { get; private set; }

        public void Advance(TimeSpan elapsed) => UtcNow = UtcNow.Add(elapsed);
    }

    private sealed class StubMarketTransport : IGw2ApiTransport
    {
        private readonly Func<IReadOnlyCollection<int>, CancellationToken, Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>>>
            _getPricesAsync;
        private readonly TaskCompletionSource _priceRequestStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _priceRequestCount;

        public StubMarketTransport(
            Func<IReadOnlyCollection<int>, CancellationToken, Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>>>
                getPricesAsync)
        {
            _getPricesAsync = getPricesAsync;
        }

        public int PriceRequestCount => Volatile.Read(ref _priceRequestCount);

        public Task WaitForPriceRequestAsync() => _priceRequestStarted.Task;

        public Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(
            IReadOnlyCollection<int> itemIds,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _priceRequestCount);
            _priceRequestStarted.TrySetResult();
            return _getPricesAsync(itemIds, cancellationToken);
        }

        public Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(
            IReadOnlyCollection<int> itemIds,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Listings are not used by this test transport.");
    }
}

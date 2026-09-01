using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketSnapshots;
using Gw2Tp.Testing;
using Xunit;

namespace Gw2Tp.Application.Tests;

public sealed class PublicMarketSnapshotCollectorTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Collection_returns_only_the_bounded_finalist_set_with_canonical_candidate_data()
    {
        var itemIds = Enumerable.Range(1, 205).Reverse().ToArray();
        var client = new StubMarketDataClient(
            itemIds: itemIds,
            prices: ids => Success(ids.Select(itemId => new MarketPrice(
                itemId,
                IsWhitelisted: false,
                new MarketOrderSummary(10, 2_000),
                new MarketOrderSummary(10, 3_000 + itemId)))),
            listings: ids => Success(ids.Select(itemId => new MarketListing(
                itemId,
                [
                    new MarketOrderLevel(2, 10, 1_900),
                    new MarketOrderLevel(1, 5, 1_900),
                ],
                [new MarketOrderLevel(3, 12, 3_100)]))),
            metadata: ids => Success(ids.Select(itemId => new MarketItemMetadata(
                itemId,
                $"Synthetic {itemId}",
                MarketItemStackPolicy.NormalStackLimit))));
        var collector = CreateCollector(client);

        var collection = await collector.CollectAsync();

        Assert.Equal(GeneratedAt, collection.GeneratedAtUtc);
        Assert.Equal(PublicMarketSnapshotCollector.MaximumFinalistCount, collection.Candidates.Count);
        Assert.Equal(6, collection.Candidates[0].ItemId);
        Assert.Equal(205, collection.Candidates[^1].ItemId);
        Assert.Equal([205, 204, 203], Assert.Single(client.ListingRequests).Take(3));
        Assert.Equal(Assert.Single(client.ListingRequests), Assert.Single(client.MetadataRequests));
        Assert.Equal([5, 10], collection.Candidates[0].Buys.Select(level => level.Quantity));
    }

    [Fact]
    public async Task Incomplete_finalist_data_is_rejected_without_returning_a_partial_collection()
    {
        var client = new StubMarketDataClient(
            metadata: _ => Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>.Success([]));
        var collector = CreateCollector(client);

        var exception = await Assert.ThrowsAsync<PublicMarketSnapshotCollectionException>(
            () => collector.CollectAsync());

        Assert.Equal(Gw2ApiErrorCategory.IncompleteData, exception.ErrorCategory);
        Assert.Single(client.ListingRequests);
        Assert.Single(client.MetadataRequests);
    }

    [Fact]
    public async Task Empty_eligible_set_is_a_complete_snapshot_without_detailed_reads()
    {
        var client = new StubMarketDataClient(
            prices: ids => Success(ids.Select(itemId => new MarketPrice(
                itemId,
                IsWhitelisted: false,
                new MarketOrderSummary(0, 0),
                new MarketOrderSummary(0, 0)))));
        var collector = CreateCollector(client);

        var collection = await collector.CollectAsync();

        Assert.Empty(collection.Candidates);
        Assert.Equal(["price-item-ids", "prices"], client.Calls);
        Assert.Empty(client.ListingRequests);
        Assert.Empty(client.MetadataRequests);
    }

    private static PublicMarketSnapshotCollector CreateCollector(StubMarketDataClient client) => new(
        client,
        new FrozenClock(GeneratedAt));

    private static Gw2ApiResult<IReadOnlyList<T>> Success<T>(IEnumerable<T> values) =>
        Gw2ApiResult<IReadOnlyList<T>>.Success(values.ToArray());

    private sealed class StubMarketDataClient : IGw2ApiClient
    {
        private readonly IReadOnlyList<int> itemIds;
        private readonly Func<IReadOnlyCollection<int>, Gw2ApiResult<IReadOnlyList<MarketPrice>>> prices;
        private readonly Func<IReadOnlyCollection<int>, Gw2ApiResult<IReadOnlyList<MarketListing>>> listings;
        private readonly Func<IReadOnlyCollection<int>, Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>> metadata;

        public StubMarketDataClient(
            IReadOnlyList<int>? itemIds = null,
            Func<IReadOnlyCollection<int>, Gw2ApiResult<IReadOnlyList<MarketPrice>>>? prices = null,
            Func<IReadOnlyCollection<int>, Gw2ApiResult<IReadOnlyList<MarketListing>>>? listings = null,
            Func<IReadOnlyCollection<int>, Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>>? metadata = null)
        {
            this.itemIds = itemIds ?? [1];
            this.prices = prices ?? (ids => Success(ids.Select(StandardPrice)));
            this.listings = listings ?? (ids => Success(ids.Select(StandardListing)));
            this.metadata = metadata ?? (ids => Success(ids.Select(StandardMetadata)));
        }

        public List<string> Calls { get; } = [];

        public List<IReadOnlyList<int>> ListingRequests { get; } = [];

        public List<IReadOnlyList<int>> MetadataRequests { get; } = [];

        public Task<Gw2ApiResult<IReadOnlyList<int>>> GetPriceItemIdsAsync(
            CancellationToken cancellationToken = default)
        {
            Calls.Add("price-item-ids");
            return Task.FromResult(Success(itemIds));
        }

        public Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(
            IReadOnlyCollection<int> requestedItemIds,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("prices");
            return Task.FromResult(prices(requestedItemIds));
        }

        public Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(
            IReadOnlyCollection<int> requestedItemIds,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("listings");
            ListingRequests.Add(requestedItemIds.ToArray());
            return Task.FromResult(listings(requestedItemIds));
        }

        public Task<Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>> GetItemMetadataAsync(
            IReadOnlyCollection<int> requestedItemIds,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("metadata");
            MetadataRequests.Add(requestedItemIds.ToArray());
            return Task.FromResult(metadata(requestedItemIds));
        }

        private static MarketPrice StandardPrice(int itemId) => new(
            itemId,
            IsWhitelisted: false,
            new MarketOrderSummary(100, 1_000),
            new MarketOrderSummary(100, 1_500));

        private static MarketListing StandardListing(int itemId) => new(
            itemId,
            [new MarketOrderLevel(3, 100, 1_000)],
            [new MarketOrderLevel(3, 100, 1_500)]);

        private static MarketItemMetadata StandardMetadata(int itemId) => new(
            itemId,
            $"Synthetic {itemId}",
            MarketItemStackPolicy.NormalStackLimit);
    }
}

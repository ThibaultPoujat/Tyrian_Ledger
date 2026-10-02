using System.Net;
using System.Text.Json;
using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Time;
using Gw2Tp.Infrastructure.Crafting;
using Gw2Tp.Infrastructure.Gw2Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Gw2Tp.Infrastructure.Tests;

public sealed class PublicReferenceCacheTests
{
    [Fact]
    public async Task Sequential_overlap_reordered_and_duplicate_ids_reuse_only_valid_references()
    {
        using var handler = new ReferenceHandler();
        using var http = Client(handler);
        using var scheduler = Scheduler();
        var cache = Cache(new Clock());
        var market = Market(http, scheduler, cache);
        Assert.Equal(new[] { 1, 2 }, (await market.GetItemMetadataAsync([2, 1, 2])).Value!.Select(x => x.ItemId));
        Assert.Equal(new[] { 2, 3 }, (await market.GetItemMetadataAsync([3, 2])).Value!.Select(x => x.ItemId));
        Assert.True((await market.GetItemMetadataAsync([1, 2, 3])).IsSuccess);
        Assert.Equal(new[] { "/v2/items:1,2", "/v2/items:3" }, handler.Reads);
        var stats = cache.GetDiagnostics();
        Assert.Equal(4, stats.Hits);
        Assert.Equal(3, stats.Misses);
        Assert.Equal(3, stats.Entries);
        await market.GetPricesAsync([1]);
        await market.GetPricesAsync([1]);
        await market.GetListingsAsync([1]);
        await market.GetListingsAsync([1]);
        await market.GetPriceItemIdsAsync();
        await market.GetPriceItemIdsAsync();
        Assert.Equal(2, handler.Reads.Count(x => x.StartsWith("/v2/commerce/prices:1", StringComparison.Ordinal)));
        Assert.Equal(2, handler.Reads.Count(x => x.StartsWith("/v2/commerce/listings:", StringComparison.Ordinal)));
        Assert.Equal(2, handler.Reads.Count(x => x == "/v2/commerce/prices:"));
    }

    [Fact]
    public async Task Key_separates_schema_language_and_endpoint_and_results_are_deeply_detached()
    {
        var cache = Cache(new Clock());
        var reads = 0;
        Task<Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>> Fetch(IReadOnlyCollection<int> ids, CancellationToken _) {
            reads++;
            return Task.FromResult(Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>.Success(ids.Select(id => new MarketItemMetadata(id, "Reference", 250)).ToArray()));
        }
        var first = await cache.GetItemsAsync("schema-1", "en", [1], Fetch, default);
        ((MarketItemMetadata[])first.Value!)[0] = new(99, "Corrupted", 1);
        Assert.Equal(1, (await cache.GetItemsAsync("schema-1", "en", [1], Fetch, default)).Value![0].ItemId);
        await cache.GetItemsAsync("schema-2", "en", [1], Fetch, default);
        await cache.GetItemsAsync("schema-1", "fr", [1], Fetch, default);
        Assert.Equal(3, reads);
        using var handler = new ReferenceHandler();
        using var http = Client(handler);
        using var scheduler = Scheduler();
        var recipes = new CraftingReferenceGateway(http, scheduler, cache);
        var recipe = (await recipes.GetRecipesAsync([1])).Value![0];
        ((string[])recipe.Disciplines)[0] = "Corrupted";
        ((string[])recipe.Flags)[0] = "Corrupted";
        ((CraftingRecipeIngredient[])recipe.Ingredients)[0] = new("Item", 99, 99);
        var later = (await recipes.GetRecipesAsync([1, 1])).Value![0];
        Assert.Equal("Artificer", later.Disciplines[0]);
        Assert.Equal("AutoLearned", later.Flags[0]);
        Assert.Equal(10, later.Ingredients[0].Id);
        Assert.Single(handler.Reads);
        Assert.Equal(4, cache.GetDiagnostics().Entries);
    }

    [Fact]
    public async Task Exact_expiry_backward_clock_and_fifo_capacity_are_deterministic()
    {
        var clock = new Clock();
        var cache = Cache(clock, capacity: 2);
        using var handler = new ReferenceHandler();
        using var http = Client(handler);
        using var scheduler = Scheduler();
        var market = Market(http, scheduler, cache);
        await market.GetItemMetadataAsync([1, 2]);
        clock.Now += TimeSpan.FromSeconds(9);
        await market.GetItemMetadataAsync([1]);
        Assert.Single(handler.Reads);
        await market.GetItemMetadataAsync([3]);
        Assert.Equal(2, cache.GetDiagnostics().Entries);
        Assert.Equal(1, cache.GetDiagnostics().Evictions);
        await market.GetItemMetadataAsync([1]); // FIFO: a hit did not extend insertion order or TTL.
        Assert.Equal(3, handler.Reads.Count);
        clock.Now += TimeSpan.FromSeconds(10);
        await market.GetItemMetadataAsync([1]);
        Assert.Equal(4, handler.Reads.Count);
        clock.Now -= TimeSpan.FromSeconds(1);
        await market.GetItemMetadataAsync([1]);
        Assert.Equal(5, handler.Reads.Count);
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("unexpected")]
    [InlineData("malformed")]
    [InlineData("not-found")]
    public async Task Bad_item_fill_never_fabricates_a_complete_cached_set_and_recovery_works(string shape)
    {
        using var handler = new ReferenceHandler();
        using var http = Client(handler);
        using var scheduler = Scheduler();
        var cache = Cache(new Clock());
        var market = Market(http, scheduler, cache);
        await market.GetItemMetadataAsync([1]);
        handler.Shape = shape;
        Assert.False((await market.GetItemMetadataAsync([1, 2, 3])).IsSuccess);
        Assert.Equal(1, cache.GetDiagnostics().Entries);
        handler.Shape = "valid";
        Assert.True((await market.GetItemMetadataAsync([1, 2, 3])).IsSuccess);
        Assert.Equal(new[] { "/v2/items:1", "/v2/items:2,3", "/v2/items:2,3" }, handler.Reads);
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("unexpected")]
    [InlineData("malformed")]
    [InlineData("not-found")]
    public async Task Bad_recipe_fill_is_not_retained(string shape)
    {
        using var handler = new ReferenceHandler { Shape = shape };
        using var http = Client(handler);
        using var scheduler = Scheduler();
        var cache = Cache(new Clock());
        var recipes = new CraftingReferenceGateway(http, scheduler, cache);
        Assert.False((await recipes.GetRecipesAsync([1, 2])).IsSuccess);
        Assert.Equal(0, cache.GetDiagnostics().Entries);
        handler.Shape = "valid";
        Assert.True((await recipes.GetRecipesAsync([1, 2])).IsSuccess);
        Assert.Equal(2, handler.Reads.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task In_flight_scheduler_preserves_waiter_cancellation_and_cancelled_fill_can_retry(bool recipes)
    {
        using var handler = new ReferenceHandler { Release = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var http = Client(handler);
        using var scheduler = Scheduler();
        var cache = Cache(new Clock());
        var market = Market(http, scheduler, cache);
        var recipeGateway = new CraftingReferenceGateway(http, scheduler, cache);
        async Task<bool> Read(CancellationToken token) => recipes
            ? (await recipeGateway.GetRecipesAsync([1], token)).IsSuccess
            : (await market.GetItemMetadataAsync([1], token)).IsSuccess;
        using var cancel = new CancellationTokenSource();
        var first = Read(cancel.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = Read(default);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        handler.Release.SetResult();
        Assert.True(await second.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await Read(default));
        Assert.Single(handler.Reads);
        Assert.False(handler.OutboundCancelled);

        using var failedHandler = new ReferenceHandler { Release = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var failedHttp = Client(failedHandler);
        var failedMarket = Market(failedHttp, scheduler, Cache(new Clock()));
        using var last = new CancellationTokenSource();
        var cancelled = failedMarket.GetItemMetadataAsync([2], last.Token);
        await failedHandler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        last.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        failedHandler.Release.SetResult();
        failedHandler.Release = null;
        Assert.True((await failedMarket.GetItemMetadataAsync([2])).IsSuccess);
        Assert.Equal(2, failedHandler.Reads.Count);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(-1)]
    public async Task Expiry_or_clock_rollback_during_missing_fill_cannot_be_returned_as_fresh(int seconds)
    {
        var clock = new Clock();
        var cache = Cache(clock);
        using var handler = new ReferenceHandler();
        using var http = Client(handler);
        using var scheduler = Scheduler();
        var market = Market(http, scheduler, cache);
        await market.GetItemMetadataAsync([1]);
        handler.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = market.GetItemMetadataAsync([1, 2]);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Now += TimeSpan.FromSeconds(seconds);
        handler.Release.SetResult();
        Assert.Equal(Gw2ApiErrorCategory.IncompleteData, (await pending).ErrorCategory);
        Assert.Equal(0, cache.GetDiagnostics().Entries);
        handler.Release = null;
        Assert.True((await market.GetItemMetadataAsync([1, 2])).IsSuccess);
        Assert.Equal(3, handler.Reads.Count);
    }

    [Fact]
    public async Task Large_reads_retain_bounded_outbound_batches_even_when_capacity_is_smaller()
    {
        using var handler = new ReferenceHandler();
        using var http = Client(handler);
        using var scheduler = Scheduler();
        var cache = Cache(new Clock(), 2);
        var ids = Enumerable.Range(1, 401).ToArray();
        Assert.Equal(401, (await Market(http, scheduler, cache).GetItemMetadataAsync(ids)).Value!.Count);
        Assert.Equal(new[] { 200, 200, 1 }, handler.Reads.Select(x => x.Split(':')[1].Split(',').Length));
        var recipes = new CraftingReferenceGateway(http, scheduler, cache);
        Assert.Equal(401, (await recipes.GetRecipesAsync(ids)).Value!.Count);
        Assert.Equal(new[] { 200, 200, 1 }, handler.Reads.Skip(3).Select(x => x.Split(':')[1].Split(',').Length));
        Assert.Equal(2, cache.GetDiagnostics().Entries);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    public void Invalid_options_fail_before_reads(int ttl, int capacity)
    {
        var options = new PublicReferenceCacheOptions { TimeToLiveSeconds = ttl, MaximumEntries = capacity };
        Assert.Throws<ArgumentOutOfRangeException>(() => new PublicReferenceCache(options, new Clock()));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Gw2Api:PublicReferences:TimeToLiveSeconds"] = ttl.ToString(),
            ["Gw2Api:PublicReferences:MaximumEntries"] = capacity.ToString() }).Build();
        using var provider = new ServiceCollection().AddLogging().AddTyrianLedgerGw2ApiClient(configuration).BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IGw2ApiClient>());
    }

    internal static PublicReferenceCache Cache(IClock clock, int capacity = 100) => new(
        new PublicReferenceCacheOptions { TimeToLiveSeconds = 10, MaximumEntries = capacity }, clock);
    internal static BatchingGw2ApiClient Market(HttpClient http, IGw2RequestScheduler scheduler, PublicReferenceCache cache) =>
        new(new Gw2ApiClient(http, scheduler), references: cache);
    internal static HttpClient Client(HttpMessageHandler handler) => new(handler) { BaseAddress = new("https://api.guildwars2.com/v2/") };
    internal static Gw2RequestScheduler Scheduler() => new(
        Options.Create(new Gw2ApiSchedulerOptions { RateLimit = new() { BurstSize = 1000, MaxConcurrentRequests = 5 } }),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<Gw2RequestScheduler>.Instance);
    internal sealed class Clock : IClock
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UtcNow => Now;
    }
    internal sealed class ReferenceHandler : HttpMessageHandler
    {
        public List<string> Reads { get; } = [];
        public string Shape { get; set; } = "valid";
        public TaskCompletionSource? Release { get; set; }
        public TaskCompletionSource Started { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool OutboundCancelled { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var uri = request.RequestUri!;
            var ids = uri.Query.TrimStart('?').Split('&').Select(x => x.Split('=', 2))
                .Where(x => x[0] == "ids").SelectMany(x => x[1].Split(',').Select(int.Parse)).ToArray();
            Reads.Add(uri.AbsolutePath + ":" + string.Join(',', ids));
            Assert.Null(request.Headers.Authorization);
            Started.TrySetResult();
            if (Release is { } release)
            {
                try { await release.Task.WaitAsync(token); }
                catch (OperationCanceledException) { OutboundCancelled = true; throw; }
            }
            if (Shape == "not-found") return new(HttpStatusCode.NotFound);
            if (Shape == "malformed") return new(HttpStatusCode.OK) { Content = new StringContent("[{}]") };
            if (Shape == "missing") ids = ids.Skip(1).ToArray();
            if (Shape == "duplicate") ids = [ids[0], ids[0]];
            if (Shape == "unexpected") ids = [99999];
            object payload = uri.AbsolutePath switch {
                "/v2/items" => ids.Select(id => new { id, name = $"Reference {id}", type = "CraftingMaterial" }),
                "/v2/recipes" => ids.Select(id => new { id, output_item_id = 100, output_item_count = 1,
                    disciplines = new[] { "Artificer" }, min_rating = 400, flags = new[] { "AutoLearned" },
                    ingredients = new[] { new { type = "Item", id = 10, count = 1 } } }),
                "/v2/commerce/listings" => ids.Select(id => new { id, buys = new[] { new { listings = 1, quantity = 100, unit_price = 100 } },
                    sells = new[] { new { listings = 1, quantity = 100, unit_price = 200 } } }),
                "/v2/commerce/prices" when ids.Length == 0 => new[] { 1 },
                _ => ids.Select(id => new { id, whitelisted = true, buys = new { quantity = 100, unit_price = 100 }, sells = new { quantity = 100, unit_price = 200 } }) };
            return new(Shape == "partial" ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
                { Content = new StringContent(JsonSerializer.Serialize(payload)) };
        }
    }
}

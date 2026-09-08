using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Application.Persistence;
using Gw2Tp.Application.PersonalTradingPost;
using Xunit;

namespace Gw2Tp.Application.Tests;

public sealed class AdaptiveMarketSamplingPolicyTests
{
    private static readonly DateTimeOffset ObservedAtUtc = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Plan_deduplicates_overlapping_sources_with_highest_priority_and_explicit_detail_opt_in()
    {
        var policy = new AdaptiveMarketSamplingPolicy(
        [
            new StaticSource("broad", [new MarketSamplingCandidate(42, MarketSamplingTier.BroadMarket, IncludeOrderBook: true), new MarketSamplingCandidate(84, MarketSamplingTier.BroadMarket, IncludeOrderBook: false)]),
            new StaticSource("watchlist", [new MarketSamplingCandidate(42, MarketSamplingTier.Watchlist, IncludeOrderBook: true), new MarketSamplingCandidate(126, MarketSamplingTier.Watchlist, IncludeOrderBook: false)]),
            new StaticSource("orders", [new MarketSamplingCandidate(42, MarketSamplingTier.CurrentPersonalOrder, IncludeOrderBook: false)]),
        ]);

        var plan = await policy.BuildPlanAsync();

        Assert.Equal(1, plan.PolicyVersion);
        Assert.Collection(plan.Targets,
            target =>
            {
                Assert.Equal(42, target.ItemId);
                Assert.Equal(MarketSamplingTier.CurrentPersonalOrder, target.Tier);
                Assert.Equal(TimeSpan.FromMinutes(15), target.Interval);
                Assert.False(target.IncludeOrderBook);
                Assert.Equal(["orders"], target.SourceNames);
            },
            target => Assert.Equal((126, MarketSamplingTier.Watchlist, TimeSpan.FromMinutes(30), false), (target.ItemId, target.Tier, target.Interval, target.IncludeOrderBook)),
            target => Assert.Equal((84, MarketSamplingTier.BroadMarket, TimeSpan.FromHours(6), false), (target.ItemId, target.Tier, target.Interval, target.IncludeOrderBook)));
    }

    [Fact]
    public async Task Plan_uses_configured_intervals_and_allows_detail_only_for_winning_high_interest_sources()
    {
        var policy = new AdaptiveMarketSamplingPolicy(
            [new StaticSource("shortlist", [new MarketSamplingCandidate(42, MarketSamplingTier.Watchlist, IncludeOrderBook: true)])],
            new MarketSamplingSettings(9, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(20), TimeSpan.FromHours(4)));

        var target = Assert.Single((await policy.BuildPlanAsync()).Targets);

        Assert.Equal(9, (await policy.BuildPlanAsync()).PolicyVersion);
        Assert.Equal(TimeSpan.FromMinutes(20), target.Interval);
        Assert.True(target.IncludeOrderBook);
    }

    [Fact]
    public void Policy_rejects_intervals_that_invert_tier_priority()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AdaptiveMarketSamplingPolicy(
            Array.Empty<IMarketSamplingSource>(),
            new MarketSamplingSettings(1, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(15), TimeSpan.FromHours(6))));

        Assert.Throws<ArgumentOutOfRangeException>(() => new AdaptiveMarketSamplingPolicy(
            Array.Empty<IMarketSamplingSource>(),
            new MarketSamplingSettings(1, TimeSpan.FromMinutes(15), TimeSpan.FromHours(7), TimeSpan.FromHours(6))));
    }

    [Fact]
    public async Task Current_personal_order_source_returns_empty_without_a_synced_profile()
    {
        var source = new CurrentPersonalOrderMarketSamplingSource(
            new StubGateway(Gw2ApiResult<AccountScope>.Success(new AccountScope("opaque-account"))),
            new StubPersonalRepository(profile: null, snapshot: null));

        Assert.Empty(await source.GetCandidatesAsync());
    }

    [Fact]
    public async Task Current_orders_and_watchlist_compose_without_duplicate_items()
    {
        var profile = new AccountProfile(1, "opaque-account", ObservedAtUtc, ObservedAtUtc);
        var currentSource = new CurrentPersonalOrderMarketSamplingSource(
            new StubGateway(Gw2ApiResult<AccountScope>.Success(new AccountScope("opaque-account"))),
            new StubPersonalRepository(profile, new CurrentPersonalTradingPostOrderSnapshot(ObservedAtUtc,
            [
                new CurrentPersonalTradingPostOrder(1, PersonalTradingPostSide.Buy, 42, 100, 1, ObservedAtUtc),
                new CurrentPersonalTradingPostOrder(2, PersonalTradingPostSide.Sell, 42, 200, 1, ObservedAtUtc),
                new CurrentPersonalTradingPostOrder(3, PersonalTradingPostSide.Sell, 84, 200, 1, ObservedAtUtc),
            ])));
        var watchlistSource = new WatchlistMarketSamplingSource(new StubWatchlistRepository([new WatchlistEntry(42, ObservedAtUtc), new WatchlistEntry(126, ObservedAtUtc)]));
        var policy = new AdaptiveMarketSamplingPolicy([currentSource, watchlistSource]);

        var targets = (await policy.BuildPlanAsync()).Targets;

        Assert.Equal([42, 84, 126], targets.Select(target => target.ItemId));
        Assert.Equal(MarketSamplingTier.CurrentPersonalOrder, targets[0].Tier);
        Assert.Equal(MarketSamplingTier.CurrentPersonalOrder, targets[1].Tier);
        Assert.Equal(MarketSamplingTier.Watchlist, targets[2].Tier);
        Assert.All(targets, target => Assert.False(target.IncludeOrderBook));
    }

    private sealed class StaticSource(string name, IReadOnlyList<MarketSamplingCandidate> candidates) : IMarketSamplingSource
    {
        public string Name => name;
        public Task<IReadOnlyList<MarketSamplingCandidate>> GetCandidatesAsync(CancellationToken cancellationToken = default) => Task.FromResult(candidates);
    }

    private sealed class StubGateway(Gw2ApiResult<AccountScope> accountResult) : IPersonalTradingPostGateway
    {
        public Task<Gw2ApiResult<AccountScope>> GetAccountScopeAsync(CancellationToken cancellationToken = default) => Task.FromResult(accountResult);
        public Task<Gw2ApiResult<PersonalTransactionPage>> GetCurrentBuyOrdersAsync(int page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<PersonalTransactionPage>> GetCurrentSellListingsAsync(int page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<PersonalTransactionPage>> GetCompletedBuyHistoryAsync(int page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<PersonalTransactionPage>> GetCompletedSellHistoryAsync(int page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubPersonalRepository(AccountProfile? profile, CurrentPersonalTradingPostOrderSnapshot? snapshot) : IPersonalTradingPostRepository
    {
        public Task<AccountProfile?> FindAccountProfileAsync(string accountScopeId, CancellationToken cancellationToken = default) => Task.FromResult(profile);
        public Task<AccountProfile> GetOrCreateAccountProfileAsync(string accountScopeId, DateTimeOffset observedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RecordSuccessfulSyncAsync(AccountProfile accountProfile, DateTimeOffset completedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpsertCompletedTransactionsAsync(AccountProfile accountProfile, IReadOnlyCollection<CompletedPersonalTradingPostTransaction> transactions, DateTimeOffset observedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<StoredCompletedPersonalTradingPostTransaction>> GetCompletedTransactionsAsync(AccountProfile accountProfile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PersonalTradingPostHistoryCoverage> GetHistoryCoverageAsync(AccountProfile accountProfile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReplaceCurrentOrderSnapshotAsync(AccountProfile accountProfile, CurrentPersonalTradingPostOrderSnapshot snapshot, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CurrentPersonalTradingPostOrder>> GetCurrentOrdersAsync(AccountProfile accountProfile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CurrentPersonalTradingPostOrderSnapshot?> GetLatestCurrentOrderSnapshotAsync(AccountProfile accountProfile, CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
        public Task<IReadOnlyList<CurrentPersonalTradingPostOrderSnapshot>> GetCurrentOrderObservationsAsync(AccountProfile accountProfile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubWatchlistRepository(IReadOnlyList<WatchlistEntry> entries) : IWatchlistRepository
    {
        public Task<IReadOnlyList<WatchlistEntry>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(entries);
        public Task AddAsync(WatchlistEntry entry, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemoveAsync(int itemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

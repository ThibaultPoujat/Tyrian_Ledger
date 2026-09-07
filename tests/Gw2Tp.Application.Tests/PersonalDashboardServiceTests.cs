using Gw2Tp.Application.Dashboard;
using Gw2Tp.Application.Accounting;
using Gw2Tp.Application.LocalData;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Persistence;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Testing;
using Xunit;

namespace Gw2Tp.Application.Tests;

public sealed class PersonalDashboardServiceTests
{
    private static readonly DateTimeOffset AsOfUtc = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Returns_not_synchronized_without_creating_a_profile()
    {
        var repository = new FakeRepository { Profile = null };
        var result = await Service(repository).GetAsync();

        Assert.Equal(PersonalDashboardState.NotSynchronized, result.State);
        Assert.Empty(result.CurrentOrders);
        Assert.Null(result.HistoryCoverage);
    }

    [Fact]
    public async Task Returns_not_synchronized_when_only_a_failed_sync_profile_exists()
    {
        var repository = new FakeRepository
        {
            Profile = new AccountProfile(1, "account", AsOfUtc.AddHours(-1), null),
        };

        var result = await Service(repository).GetAsync();

        Assert.Equal(PersonalDashboardState.NotSynchronized, result.State);
    }

    [Fact]
    public async Task Returns_safe_account_unavailable_state_when_scope_cannot_be_read()
    {
        var service = new PersonalDashboardService(
            new FakeGateway(Gw2ApiResult<AccountScope>.Failure(Gw2ApiErrorCategory.CredentialNotConfigured)),
            new FakeRepository(),
            new FakeMetadataRepository(),
            new FakeMarketClient(),
            new FrozenClock(AsOfUtc));

        var result = await service.GetAsync();

        Assert.Equal(PersonalDashboardState.AccountUnavailable, result.State);
        Assert.Equal(Gw2ApiErrorCategory.CredentialNotConfigured, result.AccountError);
    }

    [Fact]
    public async Task Projects_authoritative_performance_orders_and_market_comparisons()
    {
        var profile = new AccountProfile(1, "account", AsOfUtc.AddDays(-100), AsOfUtc.AddHours(-1));
        var repository = new FakeRepository
        {
            Profile = profile,
            Coverage = new PersonalTradingPostHistoryCoverage(AsOfUtc.AddDays(-100), AsOfUtc),
            Transactions =
            [
                Stored(Buy(101, 42, 100, 2, AsOfUtc.AddDays(-3))),
                Stored(Sell(102, 42, 200, 1, AsOfUtc.AddDays(-2))),
            ],
            Snapshot = new CurrentPersonalTradingPostOrderSnapshot(AsOfUtc.AddHours(-1),
            [
                new CurrentPersonalTradingPostOrder(201, PersonalTradingPostSide.Buy, 42, 50, 2, AsOfUtc.AddDays(-1)),
                new CurrentPersonalTradingPostOrder(202, PersonalTradingPostSide.Sell, 42, 300, 2, AsOfUtc.AddDays(-1)),
            ]),
        };
        var service = Service(repository, new FakeMarketClient(
            new MarketListing(42, [new MarketOrderLevel(1, 5, 200)], [new MarketOrderLevel(1, 5, 250)])));

        var result = await service.GetAsync();

        Assert.Equal(PersonalDashboardState.Ready, result.State);
        Assert.Equal("70", Assert.Single(result.RealizedWindows, window => window.Days == 7).NetProfit!.Copper);
        Assert.Equal("100", result.OpenAcquisitionBasis!.Copper);
        Assert.Equal("70", result.UnrealizedProfit!.Copper);
        Assert.Equal("100", result.CurrentBuyCapital.Copper);
        Assert.Equal("600", result.CurrentSellGrossValue.Copper);
        Assert.Equal("510", result.CurrentSellNetValue.Copper);
        Assert.All(result.CurrentOrders, order => Assert.Equal(DashboardOrderMarketComparisonStatus.Available, order.MarketComparisonStatus));
        Assert.Equal("200", result.CurrentOrders.Single(order => order.Side == PersonalTradingPostSide.Buy).CurrentMarketUnitPrice!.Copper);
        Assert.Equal("250", result.CurrentOrders.Single(order => order.Side == PersonalTradingPostSide.Sell).CurrentMarketUnitPrice!.Copper);
        Assert.All(result.RecentTrades, trade => Assert.Equal("Test item", trade.ItemName));
        Assert.Equal("70", Assert.Single(result.BestRealizedItems).NetProfit.Copper);
    }

    [Fact]
    public async Task Preserves_orders_but_labels_market_and_history_limitations()
    {
        var profile = new AccountProfile(1, "account", AsOfUtc.AddDays(-1), AsOfUtc.AddHours(-1));
        var repository = new FakeRepository
        {
            Profile = profile,
            Coverage = new PersonalTradingPostHistoryCoverage(null, null),
            Snapshot = new CurrentPersonalTradingPostOrderSnapshot(AsOfUtc,
                [new CurrentPersonalTradingPostOrder(201, PersonalTradingPostSide.Buy, 42, 50, 1, AsOfUtc)]),
        };

        var result = await Service(repository, new FakeMarketClient(Gw2ApiResult<IReadOnlyList<MarketListing>>.Failure(Gw2ApiErrorCategory.UpstreamUnavailable))).GetAsync();

        Assert.Empty(result.RealizedWindows);
        Assert.Equal(DashboardMarketState.Unavailable, result.MarketState);
        Assert.Equal(DashboardOrderMarketComparisonStatus.Unavailable, Assert.Single(result.CurrentOrders).MarketComparisonStatus);
    }

    [Fact]
    public async Task Bases_realized_windows_on_the_retained_coverage_end_not_the_later_market_read()
    {
        var coverageEndUtc = AsOfUtc.AddMinutes(-5);
        var profile = new AccountProfile(1, "account", AsOfUtc.AddDays(-100), coverageEndUtc);
        var repository = new FakeRepository
        {
            Profile = profile,
            Coverage = new PersonalTradingPostHistoryCoverage(coverageEndUtc.AddDays(-100), coverageEndUtc),
            Transactions =
            [
                Stored(Buy(101, 42, 100, 1, coverageEndUtc.AddDays(-3))),
                Stored(Sell(102, 42, 200, 1, coverageEndUtc.AddDays(-2))),
            ],
        };

        var result = await Service(repository, new FakeMarketClient(
            new MarketListing(42, [new MarketOrderLevel(1, 1, 200)], []))).GetAsync();

        Assert.Equal(RealizedPerformanceWindowStatus.Supported,
            Assert.Single(result.RealizedWindows, window => window.Days == 7).Status);
    }

    [Fact]
    public async Task Excludes_retained_transactions_outside_the_continuous_coverage_interval()
    {
        var profile = new AccountProfile(1, "account", AsOfUtc.AddDays(-30), AsOfUtc);
        var repository = new FakeRepository
        {
            Profile = profile,
            Coverage = new PersonalTradingPostHistoryCoverage(AsOfUtc.AddDays(-10), AsOfUtc),
            Transactions =
            [
                Stored(Buy(101, 42, 100, 1, AsOfUtc.AddDays(-20))),
                Stored(Sell(102, 42, 200, 1, AsOfUtc.AddDays(-2))),
            ],
        };

        var result = await Service(repository).GetAsync();

        var sevenDays = Assert.Single(result.RealizedWindows, window => window.Days == 7);
        Assert.Equal("0", sevenDays.NetProfit!.Copper);
        Assert.Equal(1, sevenDays.UnknownBasisQuantity);
        Assert.Empty(result.OpenInventory);
    }

    [Fact]
    public async Task Encodes_large_copper_values_losslessly_and_coordinates_the_local_read()
    {
        var repository = new FakeRepository
        {
            Snapshot = new CurrentPersonalTradingPostOrderSnapshot(AsOfUtc,
                [new CurrentPersonalTradingPostOrder(201, PersonalTradingPostSide.Buy, 42, int.MaxValue, int.MaxValue, AsOfUtc)]),
        };
        var gate = new RecordingOperationGate();
        var service = new PersonalDashboardService(
            new FakeGateway(Gw2ApiResult<AccountScope>.Success(new AccountScope("account"))),
            repository,
            new FakeMetadataRepository(),
            new FakeMarketClient(),
            new FrozenClock(AsOfUtc),
            gate);

        var result = await service.GetAsync();

        Assert.Equal("4611686014132420609", result.CurrentBuyCapital.Copper);
        Assert.Equal("201", Assert.Single(result.CurrentOrders).OrderId);
        Assert.Equal(1, gate.Acquisitions);
    }

    private static PersonalDashboardService Service(FakeRepository repository, FakeMarketClient? market = null) => new(
        new FakeGateway(Gw2ApiResult<AccountScope>.Success(new AccountScope("account"))),
        repository,
        new FakeMetadataRepository(),
        market ?? new FakeMarketClient(),
        new FrozenClock(AsOfUtc));

    private static CompletedPersonalTradingPostTransaction Buy(long id, int itemId, int price, int quantity, DateTimeOffset completedAt) =>
        new(id, PersonalTradingPostSide.Buy, itemId, price, quantity, completedAt, completedAt);

    private static CompletedPersonalTradingPostTransaction Sell(long id, int itemId, int price, int quantity, DateTimeOffset completedAt) =>
        new(id, PersonalTradingPostSide.Sell, itemId, price, quantity, completedAt, completedAt);

    private static StoredCompletedPersonalTradingPostTransaction Stored(CompletedPersonalTradingPostTransaction transaction) =>
        new(transaction, AsOfUtc, AsOfUtc);

    private sealed class FakeGateway(Gw2ApiResult<AccountScope> result) : IPersonalTradingPostGateway
    {
        public Task<Gw2ApiResult<AccountScope>> GetAccountScopeAsync(CancellationToken cancellationToken = default) => Task.FromResult(result);
        public Task<Gw2ApiResult<PersonalTransactionPage>> GetCurrentBuyOrdersAsync(int page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<PersonalTransactionPage>> GetCurrentSellListingsAsync(int page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<PersonalTransactionPage>> GetCompletedBuyHistoryAsync(int page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<PersonalTransactionPage>> GetCompletedSellHistoryAsync(int page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeRepository : IPersonalTradingPostRepository
    {
        public AccountProfile? Profile { get; init; } = new(1, "account", AsOfUtc.AddDays(-100), AsOfUtc);
        public PersonalTradingPostHistoryCoverage Coverage { get; init; } = new(AsOfUtc.AddDays(-100), AsOfUtc);
        public IReadOnlyList<StoredCompletedPersonalTradingPostTransaction> Transactions { get; init; } = [];
        public CurrentPersonalTradingPostOrderSnapshot? Snapshot { get; init; }
        public Task<AccountProfile?> FindAccountProfileAsync(string accountScopeId, CancellationToken cancellationToken = default) => Task.FromResult(Profile);
        public Task<AccountProfile> GetOrCreateAccountProfileAsync(string accountScopeId, DateTimeOffset observedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RecordSuccessfulSyncAsync(AccountProfile accountProfile, DateTimeOffset completedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpsertCompletedTransactionsAsync(AccountProfile accountProfile, IReadOnlyCollection<CompletedPersonalTradingPostTransaction> transactions, DateTimeOffset observedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<StoredCompletedPersonalTradingPostTransaction>> GetCompletedTransactionsAsync(AccountProfile accountProfile, CancellationToken cancellationToken = default) => Task.FromResult(Transactions);
        public Task<PersonalTradingPostHistoryCoverage> GetHistoryCoverageAsync(AccountProfile accountProfile, CancellationToken cancellationToken = default) => Task.FromResult(Coverage);
        public Task ReplaceCurrentOrderSnapshotAsync(AccountProfile accountProfile, CurrentPersonalTradingPostOrderSnapshot snapshot, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CurrentPersonalTradingPostOrder>> GetCurrentOrdersAsync(AccountProfile accountProfile, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CurrentPersonalTradingPostOrder>>([]);
        public Task<CurrentPersonalTradingPostOrderSnapshot?> GetLatestCurrentOrderSnapshotAsync(AccountProfile accountProfile, CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
        public Task<IReadOnlyList<CurrentPersonalTradingPostOrderSnapshot>> GetCurrentOrderObservationsAsync(AccountProfile accountProfile, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CurrentPersonalTradingPostOrderSnapshot>>([]);
    }

    private sealed class FakeMetadataRepository : IItemMetadataRepository
    {
        public Task UpsertAsync(IReadOnlyCollection<StoredItemMetadata> items, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<StoredItemMetadata?> GetAsync(int itemId, CancellationToken cancellationToken = default) => Task.FromResult<StoredItemMetadata?>(new(itemId, "Test item", AsOfUtc));
        public Task<IReadOnlyList<StoredItemMetadata>> GetManyAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoredItemMetadata>>(itemIds.Select(itemId => new StoredItemMetadata(itemId, "Test item", AsOfUtc)).ToArray());
    }

    private sealed class RecordingOperationGate : IPersonalDataOperationGate
    {
        public int Acquisitions { get; private set; }

        public ValueTask<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default)
        {
            Acquisitions++;
            return ValueTask.FromResult<IAsyncDisposable>(Lease.Instance);
        }

        private sealed class Lease : IAsyncDisposable
        {
            public static readonly Lease Instance = new();
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FakeMarketClient : IGw2ApiClient
    {
        private readonly Gw2ApiResult<IReadOnlyList<MarketListing>> listings;
        public FakeMarketClient(params MarketListing[] listings) : this(Gw2ApiResult<IReadOnlyList<MarketListing>>.Success(listings)) { }
        public FakeMarketClient(Gw2ApiResult<IReadOnlyList<MarketListing>> listings) => this.listings = listings;
        public Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) => Task.FromResult(listings);
        public Task<Gw2ApiResult<IReadOnlyList<int>>> GetPriceItemIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>> GetItemMetadataAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

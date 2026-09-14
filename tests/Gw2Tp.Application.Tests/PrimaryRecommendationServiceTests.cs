using Gw2Tp.Application.LocalData;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Application.MarketScanning;
using Gw2Tp.Application.Persistence;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Application.Recommendations;
using Gw2Tp.Application.Time;
using Gw2Tp.Domain.Finance;
using Xunit;

namespace Gw2Tp.Application.Tests;

public sealed class PrimaryRecommendationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);
    private static readonly AccountProfile Profile = new(1, "opaque-account-scope", Now.AddDays(-40), Now.AddMinutes(-5));

    [Fact]
    public async Task Account_failure_is_safe_and_does_not_open_the_personal_data_gate()
    {
        var gate = new RecordingGate();
        var repository = new FakeRepository();
        var service = CreateService(
            new FakePortfolioGateway(Gw2ApiResult<AccountPortfolioSnapshot>.Failure(Gw2ApiErrorCategory.Unauthorized)),
            repository,
            gate: gate);

        var result = await service.GetAsync();

        Assert.Equal(PrimaryRecommendationState.AccountUnavailable, result.State);
        Assert.Equal(nameof(Gw2ApiErrorCategory.Unauthorized), result.EvidenceError);
        Assert.Null(result.Portfolio);
        Assert.Empty(result.Actions);
        Assert.Equal(0, gate.AcquisitionCount);
        Assert.Equal(0, repository.ReadCount);
        Assert.Equal(0, repository.MutationCount);
    }

    [Fact]
    public async Task Empty_synchronized_portfolio_uses_wallet_cash_defaults_and_one_coherent_read_gate()
    {
        var gate = new RecordingGate();
        var repository = new FakeRepository
        {
            Profile = Profile,
            CurrentOrders = new CurrentPersonalTradingPostOrderSnapshot(Now.AddMinutes(-1), []),
        };
        var service = CreateService(SuccessfulPortfolio(100_000), repository, gate: gate);

        var result = await service.GetAsync();

        Assert.Equal(PrimaryRecommendationState.Ready, result.State);
        Assert.Equal(100_000, result.Portfolio!.AvailableCash.Copper);
        Assert.Equal(100_000, result.Portfolio.TotalBankroll.Copper);
        Assert.Equal(15_000, result.Portfolio.CashReserve.Copper);
        Assert.Equal(100_000, result.Portfolio.RemainingCashAfterSizing.Copper);
        Assert.Equal(1_500, result.Policies.CashReserveBasisPoints);
        Assert.Equal("FastFlip", result.Policies.Strategy);
        Assert.Equal("TradingPost", result.Policies.Category);
        Assert.Equal(1, gate.AcquisitionCount);
        Assert.Equal(1, gate.DisposalCount);
        Assert.Equal(0, repository.MutationCount);
    }

    [Fact]
    public async Task Sell_quantity_beyond_fifo_inventory_keeps_basis_unknown_and_disables_buy_sizing()
    {
        var repository = PersonalRepository(
            buyQuantity: 2,
            sellQuantity: 3,
            sellUnitPrice: 201);
        var service = CreateService(SuccessfulPortfolio(100_000), repository, MarketListingFor(42));

        var result = await service.GetAsync();

        Assert.Equal(PrimaryRecommendationState.Ready, result.State);
        Assert.Equal("buy_sizing_unavailable", result.EvidenceError);
        Assert.Null(result.Portfolio);
        var listing = Assert.Single(result.Actions, action => action.Source == PrimaryRecommendationSource.SellListing);
        Assert.Equal(PrimaryRecommendationAction.Review, listing.Action);
        Assert.Null(listing.Economics);
        Assert.Contains(listing.Reasons, reason => reason.Code == PrimaryRecommendationReasonCode.UnknownCostBasis);
        Assert.Equal(0, repository.MutationCount);
    }

    [Fact]
    public async Task Known_sell_listing_uses_fifo_basis_and_recalculates_fees_for_the_full_quantity()
    {
        var repository = PersonalRepository(
            buyQuantity: 3,
            sellQuantity: 3,
            sellUnitPrice: 201);
        var service = CreateService(SuccessfulPortfolio(100_000), repository, MarketListingFor(42));

        var result = await service.GetAsync();

        var listing = Assert.Single(result.Actions, action => action.Source == PrimaryRecommendationSource.SellListing);
        Assert.Equal(PrimaryRecommendationAction.LeaveSellListing, listing.Action);
        Assert.Equal(300, listing.Capital.Copper);
        Assert.Equal(603, listing.Economics!.GrossSaleValue.Copper);
        Assert.Equal(31, listing.Economics.ListingFee.Copper);
        Assert.Equal(61, listing.Economics.ExchangeFee.Copper);
        Assert.Equal(511, listing.Economics.NetSaleProceeds.Copper);
        Assert.Equal(211, listing.Economics.NetProfit.Copper);
        Assert.Equal(331, listing.Economics.TotalCost.Copper);
        Assert.Equal("63.75%", listing.Economics.RoiDisplayPercent);
        Assert.Equal(300, result.Portfolio!.TotalBankroll.Copper - result.Portfolio.AvailableCash.Copper);
        Assert.Equal(0, repository.MutationCount);
    }

    private static PrimaryRecommendationService CreateService(
        IAccountPortfolioGateway portfolioGateway,
        FakeRepository repository,
        MarketListing? listing = null,
        RecordingGate? gate = null) => new(
            portfolioGateway,
            repository,
            new FakeMetadataRepository(),
            new FakeSettingsRepository(),
            gate ?? new RecordingGate(),
            new EmptyScanner(),
            new FakeMarketClient(listing),
            new FakeHistoryService(),
            new FixedClock(Now),
            new OpportunityScoreService(),
            new PrimaryRecommendationPolicy());

    private static IAccountPortfolioGateway SuccessfulPortfolio(long cash) =>
        new FakePortfolioGateway(Gw2ApiResult<AccountPortfolioSnapshot>.Success(
            new AccountPortfolioSnapshot(new AccountScope(Profile.AccountScopeId), new Money(cash))));

    private static FakeRepository PersonalRepository(int buyQuantity, int sellQuantity, int sellUnitPrice)
    {
        var buy = new CompletedPersonalTradingPostTransaction(
            10, PersonalTradingPostSide.Buy, 42, 100, buyQuantity, Now.AddDays(-2), Now.AddDays(-2));
        return new FakeRepository
        {
            Profile = Profile,
            Coverage = new PersonalTradingPostHistoryCoverage(Now.AddDays(-30), Now),
            Completed = [new StoredCompletedPersonalTradingPostTransaction(buy, Now.AddDays(-2), Now.AddDays(-2))],
            CurrentOrders = new CurrentPersonalTradingPostOrderSnapshot(Now.AddMinutes(-1), [
                new CurrentPersonalTradingPostOrder(99, PersonalTradingPostSide.Sell, 42, sellUnitPrice, sellQuantity, Now.AddHours(-1)),
            ]),
        };
    }

    private static MarketListing MarketListingFor(int itemId) => new(
        itemId,
        [new MarketOrderLevel(3, 100, 100)],
        [new MarketOrderLevel(3, 100, 200)]);

    private sealed class FakePortfolioGateway(Gw2ApiResult<AccountPortfolioSnapshot> result) : IAccountPortfolioGateway
    {
        public Task<Gw2ApiResult<AccountPortfolioSnapshot>> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class FakeRepository : IPersonalTradingPostRepository
    {
        public AccountProfile? Profile { get; init; }
        public PersonalTradingPostHistoryCoverage Coverage { get; init; } = new(null, null);
        public IReadOnlyList<StoredCompletedPersonalTradingPostTransaction> Completed { get; init; } = [];
        public CurrentPersonalTradingPostOrderSnapshot? CurrentOrders { get; init; }
        public int ReadCount { get; private set; }
        public int MutationCount { get; private set; }

        public Task<AccountProfile?> FindAccountProfileAsync(string accountScopeId, CancellationToken cancellationToken = default)
        { ReadCount++; return Task.FromResult(Profile); }
        public Task<IReadOnlyList<StoredCompletedPersonalTradingPostTransaction>> GetCompletedTransactionsAsync(AccountProfile accountProfile, CancellationToken cancellationToken = default)
        { ReadCount++; return Task.FromResult(Completed); }
        public Task<PersonalTradingPostHistoryCoverage> GetHistoryCoverageAsync(AccountProfile accountProfile, CancellationToken cancellationToken = default)
        { ReadCount++; return Task.FromResult(Coverage); }
        public Task<CurrentPersonalTradingPostOrderSnapshot?> GetLatestCurrentOrderSnapshotAsync(AccountProfile accountProfile, CancellationToken cancellationToken = default)
        { ReadCount++; return Task.FromResult(CurrentOrders); }

        public Task<AccountProfile> GetOrCreateAccountProfileAsync(string accountScopeId, DateTimeOffset observedAtUtc, CancellationToken cancellationToken = default)
        { MutationCount++; throw new NotSupportedException(); }
        public Task RecordSuccessfulSyncAsync(AccountProfile accountProfile, DateTimeOffset completedAtUtc, CancellationToken cancellationToken = default)
        { MutationCount++; throw new NotSupportedException(); }
        public Task UpsertCompletedTransactionsAsync(AccountProfile accountProfile, IReadOnlyCollection<CompletedPersonalTradingPostTransaction> transactions, DateTimeOffset observedAtUtc, CancellationToken cancellationToken = default)
        { MutationCount++; throw new NotSupportedException(); }
        public Task ReplaceCurrentOrderSnapshotAsync(AccountProfile accountProfile, CurrentPersonalTradingPostOrderSnapshot snapshot, CancellationToken cancellationToken = default)
        { MutationCount++; throw new NotSupportedException(); }
        public Task<IReadOnlyList<CurrentPersonalTradingPostOrder>> GetCurrentOrdersAsync(AccountProfile accountProfile, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CurrentPersonalTradingPostOrder>>(CurrentOrders?.Orders ?? []);
        public Task<IReadOnlyList<CurrentPersonalTradingPostOrderSnapshot>> GetCurrentOrderObservationsAsync(AccountProfile accountProfile, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CurrentPersonalTradingPostOrderSnapshot>>([]);
    }

    private sealed class FakeMetadataRepository : IItemMetadataRepository
    {
        public Task<IReadOnlyList<StoredItemMetadata>> GetManyAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoredItemMetadata>>(itemIds.Select(itemId => new StoredItemMetadata(itemId, $"Item {itemId}", Now)).ToArray());
        public Task<StoredItemMetadata?> GetAsync(int itemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpsertAsync(IReadOnlyCollection<StoredItemMetadata> items, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeSettingsRepository : IUserSettingsRepository
    {
        public Task<UserSettings?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult<UserSettings?>(null);
        public Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingGate : IPersonalDataOperationGate
    {
        public int AcquisitionCount { get; private set; }
        public int DisposalCount { get; private set; }
        public ValueTask<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default)
        {
            AcquisitionCount++;
            return ValueTask.FromResult<IAsyncDisposable>(new Releaser(() => DisposalCount++));
        }
        private sealed class Releaser(Action release) : IAsyncDisposable
        {
            public ValueTask DisposeAsync() { release(); return ValueTask.CompletedTask; }
        }
    }

    private sealed class EmptyScanner : ILiveMarketScanner
    {
        public Task<LiveMarketScannerResult> ScanAsync(LiveMarketScannerSettings settings, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LiveMarketScannerResult(
                LiveMarketScannerState.Ready, null, Now, settings, false, 0, false, [], []));
    }

    private sealed class FakeMarketClient(MarketListing? listing) : IGw2ApiClient
    {
        public Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(Gw2ApiResult<IReadOnlyList<MarketListing>>.Success(
                listing is null || !itemIds.Contains(listing.ItemId) ? [] : [listing]));
        public Task<Gw2ApiResult<IReadOnlyList<int>>> GetPriceItemIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>> GetItemMetadataAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeHistoryService : IHistoricalMarketAnalyticsService
    {
        public Task<HistoricalMarketAnalytics> GetAsync(int itemId, CancellationToken cancellationToken = default) =>
            GetAtAsync(itemId, Now, cancellationToken);
        public Task<HistoricalMarketAnalytics> GetAtAsync(int itemId, DateTimeOffset asOfUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(new HistoricalMarketAnalytics(
                itemId, asOfUtc, false, HistoricalMarketAnalyticsSettings.Default, null, []));
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}

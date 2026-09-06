using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Persistence;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Testing;
using Xunit;

namespace Gw2Tp.Application.Tests;

public sealed class PersonalTradingPostSynchronizationServiceTests
{
    private static readonly DateTimeOffset ObservedAtUtc = new(2026, 9, 6, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Complete_multi_page_sync_commits_one_normalized_snapshot_with_metadata_and_coverage()
    {
        var gateway = new StubGateway();
        gateway.CurrentBuys[0] = SuccessPage(0, 2, 2, Transaction(1001, 11, purchasedAtUtc: null));
        gateway.CurrentBuys[1] = SuccessPage(1, 2, 2, Transaction(1002, 12, purchasedAtUtc: null));
        gateway.CurrentSells[0] = EmptyPage();
        gateway.CompletedBuys[0] = SuccessPage(0, 1, 1, Transaction(2001, 11, ObservedAtUtc.AddHours(-2)));
        gateway.CompletedSells[0] = SuccessPage(0, 1, 1, Transaction(2002, 12, ObservedAtUtc.AddHours(-1)));
        var marketData = new StubMarketDataClient(
            Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>.Success(
                [new MarketItemMetadata(11, "Eleven", 250), new MarketItemMetadata(12, "Twelve", 250)]));
        var store = new RecordingStore();
        var service = CreateService(gateway, marketData, store);

        var result = await service.SynchronizeAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.CompletedTransactionCount);
        Assert.Equal(2, result.CurrentOrderCount);
        Assert.Equal(ObservedAtUtc.AddHours(-2), result.HistoryCoverageStartUtc);
        Assert.Equal(ObservedAtUtc.AddHours(-1), result.HistoryCoverageEndUtc);
        var persisted = Assert.Single(store.SuccessfulSyncs);
        Assert.Equal("opaque-account-a", persisted.AccountScopeId);
        Assert.Equal([2001L, 2002L], persisted.CompletedTransactions.Select(transaction => transaction.ExternalTransactionId));
        Assert.Equal([1001L, 1002L], persisted.CurrentOrders.Orders.Select(order => order.ExternalOrderId));
        Assert.Equal([11, 12], persisted.ItemMetadata.Select(item => item.ItemId));
        Assert.Equal([0, 1], gateway.CurrentBuyPagesRead);
    }

    [Fact]
    public async Task Failed_or_inconsistent_remote_reads_do_not_commit_and_record_a_safe_failure_after_scope_resolution()
    {
        var gateway = new StubGateway();
        gateway.CurrentBuys[0] = SuccessPage(0, 1, 1, Transaction(1001, 11, purchasedAtUtc: null));
        gateway.CurrentSells[0] = EmptyPage();
        gateway.CompletedBuys[0] = SuccessPage(0, 2, 2, Transaction(2001, 11, ObservedAtUtc));
        gateway.CompletedBuys[1] = SuccessPage(1, 3, 2, Transaction(2002, 12, ObservedAtUtc));
        var store = new RecordingStore();
        var service = CreateService(gateway, new StubMarketDataClient(), store);

        var result = await service.SynchronizeAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(Gw2ApiErrorCategory.IncompleteData, result.ErrorCategory);
        Assert.Empty(store.SuccessfulSyncs);
        var failure = Assert.Single(store.Failures);
        Assert.Equal("opaque-account-a", failure.AccountScopeId);
        Assert.Equal(Gw2ApiErrorCategory.IncompleteData, failure.ErrorCategory);
    }

    [Fact]
    public async Task Duplicate_external_ids_or_incomplete_metadata_do_not_commit_current_or_completed_state()
    {
        var gateway = new StubGateway();
        gateway.CurrentBuys[0] = SuccessPage(0, 1, 1, Transaction(1001, 11, purchasedAtUtc: null));
        gateway.CurrentSells[0] = SuccessPage(0, 1, 1, Transaction(1001, 12, purchasedAtUtc: null));
        gateway.CompletedBuys[0] = EmptyPage();
        gateway.CompletedSells[0] = EmptyPage();
        var store = new RecordingStore();
        var service = CreateService(gateway, new StubMarketDataClient(), store);

        var result = await service.SynchronizeAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(Gw2ApiErrorCategory.IncompleteData, result.ErrorCategory);
        Assert.Empty(store.SuccessfulSyncs);
        Assert.Single(store.Failures);
    }

    private static PersonalTradingPostSynchronizationService CreateService(
        IPersonalTradingPostGateway gateway,
        IGw2ApiClient marketDataClient,
        IPersonalTradingPostSynchronizationStore store) => new(
        gateway,
        marketDataClient,
        store,
        new FrozenClock(ObservedAtUtc));

    private static Gw2ApiResult<PersonalTransactionPage> EmptyPage() =>
        SuccessPage(0, 0, 0);

    private static Gw2ApiResult<PersonalTransactionPage> SuccessPage(
        int pageNumber,
        int pageCount,
        int resultTotal,
        params PersonalTradingPostTransaction[] transactions) =>
        Gw2ApiResult<PersonalTransactionPage>.Success(new(
            transactions,
            pageNumber,
            pageCount == 0 ? 50 : 1,
            pageCount,
            transactions.Length,
            resultTotal));

    private static PersonalTradingPostTransaction Transaction(
        long transactionId,
        int itemId,
        DateTimeOffset? purchasedAtUtc) => new(
        transactionId,
        itemId,
        100,
        1,
        ObservedAtUtc.AddHours(-3),
        purchasedAtUtc);

    private sealed class StubGateway : IPersonalTradingPostGateway
    {
        public Dictionary<int, Gw2ApiResult<PersonalTransactionPage>> CurrentBuys { get; } = [];
        public Dictionary<int, Gw2ApiResult<PersonalTransactionPage>> CurrentSells { get; } = [];
        public Dictionary<int, Gw2ApiResult<PersonalTransactionPage>> CompletedBuys { get; } = [];
        public Dictionary<int, Gw2ApiResult<PersonalTransactionPage>> CompletedSells { get; } = [];
        public List<int> CurrentBuyPagesRead { get; } = [];

        public Task<Gw2ApiResult<AccountScope>> GetAccountScopeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Gw2ApiResult<AccountScope>.Success(new AccountScope("opaque-account-a")));

        public Task<Gw2ApiResult<PersonalTransactionPage>> GetCurrentBuyOrdersAsync(int page, CancellationToken cancellationToken = default)
        {
            CurrentBuyPagesRead.Add(page);
            return Task.FromResult(CurrentBuys[page]);
        }

        public Task<Gw2ApiResult<PersonalTransactionPage>> GetCurrentSellListingsAsync(int page, CancellationToken cancellationToken = default) =>
            Task.FromResult(CurrentSells[page]);

        public Task<Gw2ApiResult<PersonalTransactionPage>> GetCompletedBuyHistoryAsync(int page, CancellationToken cancellationToken = default) =>
            Task.FromResult(CompletedBuys[page]);

        public Task<Gw2ApiResult<PersonalTransactionPage>> GetCompletedSellHistoryAsync(int page, CancellationToken cancellationToken = default) =>
            Task.FromResult(CompletedSells[page]);
    }

    private sealed class StubMarketDataClient(Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>? metadataResult = null) : IGw2ApiClient
    {
        public Task<Gw2ApiResult<IReadOnlyList<int>>> GetPriceItemIdsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>> GetItemMetadataAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(metadataResult ?? Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>.Failure(Gw2ApiErrorCategory.IncompleteData));
    }

    private sealed class RecordingStore : IPersonalTradingPostSynchronizationStore
    {
        public List<PersonalTradingPostSuccessfulSync> SuccessfulSyncs { get; } = [];
        public List<(string AccountScopeId, DateTimeOffset AttemptedAtUtc, Gw2ApiErrorCategory ErrorCategory)> Failures { get; } = [];

        public Task CommitSuccessfulSyncAsync(PersonalTradingPostSuccessfulSync sync, CancellationToken cancellationToken = default)
        {
            SuccessfulSyncs.Add(sync);
            return Task.CompletedTask;
        }

        public Task RecordFailedSyncAsync(string accountScopeId, DateTimeOffset attemptedAtUtc, Gw2ApiErrorCategory errorCategory, CancellationToken cancellationToken = default)
        {
            Failures.Add((accountScopeId, attemptedAtUtc, errorCategory));
            return Task.CompletedTask;
        }
    }
}

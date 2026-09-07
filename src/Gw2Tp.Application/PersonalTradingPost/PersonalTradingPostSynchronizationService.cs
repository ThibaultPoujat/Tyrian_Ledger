using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.LocalData;
using Gw2Tp.Application.Persistence;
using Gw2Tp.Application.Time;

namespace Gw2Tp.Application.PersonalTradingPost;

/// <summary>
/// Reads every required personal Trading Post page before committing one
/// normalized snapshot. A failed or incomplete remote read never reaches the
/// successful persistence path.
/// </summary>
public sealed class PersonalTradingPostSynchronizationService : IPersonalTradingPostSynchronizationService
{
    private readonly IPersonalTradingPostGateway personalTradingPostGateway;
    private readonly IGw2ApiClient marketDataClient;
    private readonly IPersonalTradingPostSynchronizationStore synchronizationStore;
    private readonly IClock clock;
    private readonly IPersonalDataOperationGate operationGate;

    public PersonalTradingPostSynchronizationService(
        IPersonalTradingPostGateway personalTradingPostGateway,
        IGw2ApiClient marketDataClient,
        IPersonalTradingPostSynchronizationStore synchronizationStore,
        IClock clock,
        IPersonalDataOperationGate? operationGate = null)
    {
        this.personalTradingPostGateway = personalTradingPostGateway ?? throw new ArgumentNullException(nameof(personalTradingPostGateway));
        this.marketDataClient = marketDataClient ?? throw new ArgumentNullException(nameof(marketDataClient));
        this.synchronizationStore = synchronizationStore ?? throw new ArgumentNullException(nameof(synchronizationStore));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.operationGate = operationGate ?? NoopPersonalDataOperationGate.Instance;
    }

    public async Task<PersonalTradingPostSynchronizationResult> SynchronizeAsync(
        CancellationToken cancellationToken = default)
    {
        await using var operationLease = await operationGate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        var attemptedAtUtc = RequireUtc(clock.UtcNow, "clock.UtcNow");
        var accountResult = await personalTradingPostGateway.GetAccountScopeAsync(cancellationToken).ConfigureAwait(false);
        if (!accountResult.IsSuccess || accountResult.Value is null || string.IsNullOrWhiteSpace(accountResult.Value.AccountId))
        {
            return PersonalTradingPostSynchronizationResult.GatewayFailed(
                attemptedAtUtc,
                accountResult.ErrorCategory ?? Gw2ApiErrorCategory.InvalidPayload);
        }

        var accountScopeId = accountResult.Value.AccountId;
        try
        {
            var currentBuys = await ReadAllPagesAsync(personalTradingPostGateway.GetCurrentBuyOrdersAsync, cancellationToken).ConfigureAwait(false);
            var currentSells = await ReadAllPagesAsync(personalTradingPostGateway.GetCurrentSellListingsAsync, cancellationToken).ConfigureAwait(false);
            var completedBuys = await ReadAllPagesAsync(personalTradingPostGateway.GetCompletedBuyHistoryAsync, cancellationToken).ConfigureAwait(false);
            var completedSells = await ReadAllPagesAsync(personalTradingPostGateway.GetCompletedSellHistoryAsync, cancellationToken).ConfigureAwait(false);

            var currentOrders = CombineCurrentOrders(currentBuys, currentSells);
            var completedTransactions = CombineCompletedTransactions(completedBuys, completedSells);
            var itemIds = currentOrders.Select(order => order.ItemId)
                .Concat(completedTransactions.Select(transaction => transaction.ItemId))
                .Distinct()
                .OrderBy(itemId => itemId)
                .ToArray();
            var observedAtUtc = RequireUtc(clock.UtcNow, "clock.UtcNow");
            var itemMetadata = await ReadItemMetadataAsync(itemIds, observedAtUtc, cancellationToken).ConfigureAwait(false);
            var historyCoverage = GetHistoryCoverage(completedTransactions, observedAtUtc);

            var effectiveHistoryCoverage = await synchronizationStore.CommitSuccessfulSyncAsync(
                new PersonalTradingPostSuccessfulSync(
                    accountScopeId,
                    completedTransactions,
                    new CurrentPersonalTradingPostOrderSnapshot(observedAtUtc, currentOrders),
                    itemMetadata,
                    observedAtUtc,
                    historyCoverage.StartUtc,
                    historyCoverage.EndUtc),
                cancellationToken).ConfigureAwait(false);

            return PersonalTradingPostSynchronizationResult.Succeeded(
                attemptedAtUtc,
                completedTransactions.Count,
                currentOrders.Count,
                effectiveHistoryCoverage.StartUtc,
                effectiveHistoryCoverage.EndUtc);
        }
        catch (PersonalTradingPostSynchronizationException exception)
        {
            try
            {
                await synchronizationStore.RecordFailedSyncAsync(
                    accountScopeId,
                    attemptedAtUtc,
                    exception.ErrorCategory,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return PersonalTradingPostSynchronizationResult.PersistenceFailed(attemptedAtUtc);
            }

            return PersonalTradingPostSynchronizationResult.GatewayFailed(attemptedAtUtc, exception.ErrorCategory);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return PersonalTradingPostSynchronizationResult.PersistenceFailed(attemptedAtUtc);
        }
    }

    private sealed class NoopPersonalDataOperationGate : IPersonalDataOperationGate
    {
        public static readonly NoopPersonalDataOperationGate Instance = new();

        public ValueTask<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IAsyncDisposable>(NoopLease.Instance);
        }

        private sealed class NoopLease : IAsyncDisposable
        {
            public static readonly NoopLease Instance = new();

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private async Task<IReadOnlyList<PersonalTradingPostTransaction>> ReadAllPagesAsync(
        Func<int, CancellationToken, Task<Gw2ApiResult<PersonalTransactionPage>>> readPageAsync,
        CancellationToken cancellationToken)
    {
        var firstResult = await readPageAsync(0, cancellationToken).ConfigureAwait(false);
        if (!firstResult.IsSuccess || firstResult.Value is null)
        {
            throw new PersonalTradingPostSynchronizationException(firstResult.ErrorCategory ?? Gw2ApiErrorCategory.InvalidPayload);
        }

        var firstPage = firstResult.Value;
        ValidatePage(firstPage, expectedPage: 0, expectedPageCount: firstPage.PageCount, expectedResultTotal: firstPage.ResultTotal, expectedPageSize: firstPage.PageSize);
        var transactions = new List<PersonalTradingPostTransaction>(firstPage.ResultTotal);
        transactions.AddRange(firstPage.Transactions);
        for (var pageNumber = 1; pageNumber < firstPage.PageCount; pageNumber++)
        {
            var pageResult = await readPageAsync(pageNumber, cancellationToken).ConfigureAwait(false);
            if (!pageResult.IsSuccess || pageResult.Value is null)
            {
                throw new PersonalTradingPostSynchronizationException(pageResult.ErrorCategory ?? Gw2ApiErrorCategory.InvalidPayload);
            }

            ValidatePage(pageResult.Value, pageNumber, firstPage.PageCount, firstPage.ResultTotal, firstPage.PageSize);
            transactions.AddRange(pageResult.Value.Transactions);
        }

        if (transactions.Count != firstPage.ResultTotal ||
            transactions.Any(transaction => transaction is null) ||
            transactions.Select(transaction => transaction.TransactionId).Distinct().Count() != transactions.Count)
        {
            throw new PersonalTradingPostSynchronizationException(Gw2ApiErrorCategory.IncompleteData);
        }

        return transactions;
    }

    private static void ValidatePage(
        PersonalTransactionPage page,
        int expectedPage,
        int expectedPageCount,
        int expectedResultTotal,
        int expectedPageSize)
    {
        if (page is null || page.Transactions is null || page.PageNumber != expectedPage ||
            page.PageCount != expectedPageCount || page.ResultTotal != expectedResultTotal ||
            page.PageSize != expectedPageSize || page.PageCount < 0 || page.ResultTotal < 0 ||
            page.PageSize <= 0 || page.ResultCount != page.Transactions.Count)
        {
            throw new PersonalTradingPostSynchronizationException(Gw2ApiErrorCategory.IncompleteData);
        }
    }

    private async Task<IReadOnlyList<StoredItemMetadata>> ReadItemMetadataAsync(
        IReadOnlyCollection<int> itemIds,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        if (itemIds.Count == 0)
        {
            return [];
        }

        var metadataResult = await marketDataClient.GetItemMetadataAsync(itemIds, cancellationToken).ConfigureAwait(false);
        if (!metadataResult.IsSuccess || metadataResult.IsPartialData || metadataResult.Value is null ||
            metadataResult.Value.Count != itemIds.Count ||
            metadataResult.Value.Any(item => item is null || item.ItemId <= 0 || string.IsNullOrWhiteSpace(item.Name)) ||
            !metadataResult.Value.Select(item => item.ItemId).ToHashSet().SetEquals(itemIds))
        {
            throw new PersonalTradingPostSynchronizationException(metadataResult.ErrorCategory ?? Gw2ApiErrorCategory.IncompleteData);
        }

        return metadataResult.Value
            .OrderBy(item => item.ItemId)
            .Select(item => new StoredItemMetadata(item.ItemId, item.Name, observedAtUtc))
            .ToArray();
    }

    private static IReadOnlyList<CurrentPersonalTradingPostOrder> CombineCurrentOrders(
        IReadOnlyList<PersonalTradingPostTransaction> buys,
        IReadOnlyList<PersonalTradingPostTransaction> sells)
    {
        var orders = buys.Select(transaction => ToCurrentOrder(transaction, PersonalTradingPostSide.Buy))
            .Concat(sells.Select(transaction => ToCurrentOrder(transaction, PersonalTradingPostSide.Sell)))
            .OrderBy(order => order.ExternalOrderId)
            .ToArray();
        if (orders.Select(order => order.ExternalOrderId).Distinct().Count() != orders.Length)
        {
            throw new PersonalTradingPostSynchronizationException(Gw2ApiErrorCategory.IncompleteData);
        }

        return orders;
    }

    private static IReadOnlyList<CompletedPersonalTradingPostTransaction> CombineCompletedTransactions(
        IReadOnlyList<PersonalTradingPostTransaction> buys,
        IReadOnlyList<PersonalTradingPostTransaction> sells)
    {
        var transactions = buys.Select(transaction => ToCompletedTransaction(transaction, PersonalTradingPostSide.Buy))
            .Concat(sells.Select(transaction => ToCompletedTransaction(transaction, PersonalTradingPostSide.Sell)))
            .OrderBy(transaction => transaction.ExternalTransactionId)
            .ToArray();
        if (transactions.Select(transaction => transaction.ExternalTransactionId).Distinct().Count() != transactions.Length)
        {
            throw new PersonalTradingPostSynchronizationException(Gw2ApiErrorCategory.IncompleteData);
        }

        return transactions;
    }

    private static CurrentPersonalTradingPostOrder ToCurrentOrder(
        PersonalTradingPostTransaction transaction,
        PersonalTradingPostSide side)
    {
        ValidateRemoteTransaction(transaction, requiresCompletedTimestamp: false);
        return new CurrentPersonalTradingPostOrder(
            transaction.TransactionId,
            side,
            transaction.ItemId,
            transaction.PriceInCopper,
            transaction.Quantity,
            RequireUtc(transaction.CreatedAtUtc, nameof(transaction.CreatedAtUtc)));
    }

    private static CompletedPersonalTradingPostTransaction ToCompletedTransaction(
        PersonalTradingPostTransaction transaction,
        PersonalTradingPostSide side)
    {
        ValidateRemoteTransaction(transaction, requiresCompletedTimestamp: true);
        return new CompletedPersonalTradingPostTransaction(
            transaction.TransactionId,
            side,
            transaction.ItemId,
            transaction.PriceInCopper,
            transaction.Quantity,
            RequireUtc(transaction.CreatedAtUtc, nameof(transaction.CreatedAtUtc)),
            RequireUtc(transaction.PurchasedAtUtc!.Value, nameof(transaction.PurchasedAtUtc)));
    }

    private static void ValidateRemoteTransaction(PersonalTradingPostTransaction transaction, bool requiresCompletedTimestamp)
    {
        if (transaction is null || transaction.TransactionId <= 0 || transaction.ItemId <= 0 ||
            transaction.PriceInCopper < 0 || transaction.Quantity <= 0 ||
            transaction.CreatedAtUtc.Offset != TimeSpan.Zero ||
            (requiresCompletedTimestamp && transaction.PurchasedAtUtc is null) ||
            (transaction.PurchasedAtUtc is not null && transaction.PurchasedAtUtc.Value.Offset != TimeSpan.Zero))
        {
            throw new PersonalTradingPostSynchronizationException(Gw2ApiErrorCategory.InvalidPayload);
        }
    }

    private static (DateTimeOffset? StartUtc, DateTimeOffset? EndUtc) GetHistoryCoverage(
        IReadOnlyList<CompletedPersonalTradingPostTransaction> transactions,
        DateTimeOffset observedAtUtc) => transactions.Count == 0
            ? (null, null)
            : (
                transactions.Min(transaction => transaction.CompletedAtUtc),
                new[] { observedAtUtc, transactions.Max(transaction => transaction.CompletedAtUtc) }.Max());

    private static DateTimeOffset RequireUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamps must be UTC.", parameterName);
        }

        return value;
    }

    private sealed class PersonalTradingPostSynchronizationException(Gw2ApiErrorCategory errorCategory) : Exception
    {
        public Gw2ApiErrorCategory ErrorCategory { get; } = errorCategory;
    }
}

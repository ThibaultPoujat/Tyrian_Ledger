using Gw2Tp.Analytics.Finance;
using Gw2Tp.Application.Accounting;
using Gw2Tp.Application.Finance;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Persistence;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Application.LocalData;
using Gw2Tp.Application.Time;
using Gw2Tp.Domain.Finance;
using System.Globalization;

namespace Gw2Tp.Application.Dashboard;

/// <summary>
/// Builds the backend-authoritative dashboard from persisted personal evidence
/// and an explicit current public market read. It stores no derived accounting state.
/// </summary>
public sealed class PersonalDashboardService : IPersonalDashboardService
{
    private const int RecentTradeLimit = 10;
    private const int RealizedItemLimit = 3;

    private readonly IPersonalTradingPostGateway personalGateway;
    private readonly IPersonalTradingPostRepository repository;
    private readonly IItemMetadataRepository itemMetadataRepository;
    private readonly IGw2ApiClient marketDataClient;
    private readonly IClock clock;
    private readonly IPersonalDataOperationGate operationGate;
    private readonly PersonalPerformanceCalculator performanceCalculator = new();
    private readonly FifoLotMatcher fifoLotMatcher = new();
    private readonly FlipProfitCalculator saleCalculator = new(Gw2TradingPostFeePolicy.Create());

    public PersonalDashboardService(
        IPersonalTradingPostGateway personalGateway,
        IPersonalTradingPostRepository repository,
        IItemMetadataRepository itemMetadataRepository,
        IGw2ApiClient marketDataClient,
        IClock clock,
        IPersonalDataOperationGate? operationGate = null)
    {
        this.personalGateway = personalGateway ?? throw new ArgumentNullException(nameof(personalGateway));
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.itemMetadataRepository = itemMetadataRepository ?? throw new ArgumentNullException(nameof(itemMetadataRepository));
        this.marketDataClient = marketDataClient ?? throw new ArgumentNullException(nameof(marketDataClient));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.operationGate = operationGate ?? NoopPersonalDataOperationGate.Instance;
    }

    public async Task<PersonalDashboard> GetAsync(CancellationToken cancellationToken = default)
    {
        var accountResult = await personalGateway.GetAccountScopeAsync(cancellationToken).ConfigureAwait(false);
        if (!accountResult.IsSuccess || accountResult.Value is null || string.IsNullOrWhiteSpace(accountResult.Value.AccountId))
        {
            return PersonalDashboard.AccountUnavailable(accountResult.ErrorCategory ?? Gw2ApiErrorCategory.InvalidPayload);
        }

        AccountProfile profile;
        PersonalTradingPostHistoryCoverage coverage;
        IReadOnlyList<StoredCompletedPersonalTradingPostTransaction> transactions;
        CurrentPersonalTradingPostOrderSnapshot? currentOrders;
        IReadOnlyList<AccountScopedCompletedTransaction> performanceTransactions;
        int[] marketItemIds;
        IReadOnlyDictionary<int, string> metadata;
        await using (await operationGate.AcquireAsync(cancellationToken).ConfigureAwait(false))
        {
            var foundProfile = await repository.FindAccountProfileAsync(accountResult.Value.AccountId, cancellationToken).ConfigureAwait(false);
            if (foundProfile is null || foundProfile.LastSuccessfulSyncAtUtc is null)
            {
                return PersonalDashboard.NotSynchronized();
            }

            profile = foundProfile;
            coverage = await repository.GetHistoryCoverageAsync(profile, cancellationToken).ConfigureAwait(false);
            transactions = await repository.GetCompletedTransactionsAsync(profile, cancellationToken).ConfigureAwait(false);
            currentOrders = await repository.GetLatestCurrentOrderSnapshotAsync(profile, cancellationToken).ConfigureAwait(false);
            var scopedTransactions = transactions
                .Select(transaction => new AccountScopedCompletedTransaction(profile.Id, transaction.Transaction))
                .ToArray();
            performanceTransactions = coverage.StartUtc is { } coverageStartUtc && coverage.EndUtc is { } coverageEndUtc
                ? scopedTransactions.Where(transaction => transaction.Transaction.CompletedAtUtc >= coverageStartUtc &&
                                                         transaction.Transaction.CompletedAtUtc <= coverageEndUtc).ToArray()
                : [];
            var openItemIds = fifoLotMatcher.Rebuild(performanceTransactions).OpenLots.Select(lot => lot.ItemId);
            marketItemIds = openItemIds
                .Concat(currentOrders?.Orders.Select(order => order.ItemId) ?? [])
                .Distinct()
                .OrderBy(itemId => itemId)
                .ToArray();
            metadata = await ReadMetadataAsync(marketItemIds
                .Concat(transactions.Select(transaction => transaction.Transaction.ItemId))
                .Distinct()
                .ToArray(), cancellationToken).ConfigureAwait(false);
        }
        var marketResult = marketItemIds.Length == 0
            ? Gw2ApiResult<IReadOnlyList<MarketListing>>.Success([])
            : await marketDataClient.GetListingsAsync(marketItemIds, cancellationToken).ConfigureAwait(false);
        var listings = marketResult.IsSuccess && !marketResult.IsPartialData && marketResult.Value is not null
            ? marketResult.Value
            : [];
        var listingsByItem = listings.ToDictionary(listing => listing.ItemId);
        var marketState = marketResult.IsSuccess && !marketResult.IsPartialData
            ? DashboardMarketState.Available
            : DashboardMarketState.Unavailable;

        PersonalPerformanceRebuild? performance = null;
        if (coverage.StartUtc is not null && coverage.EndUtc is not null)
        {
            var asOfUtc = RequireUtc(clock.UtcNow);
            performance = performanceCalculator.Rebuild(new PersonalPerformanceRequest(
                asOfUtc,
                new PerformanceHistoryCoverage(coverage.StartUtc.Value, coverage.EndUtc.Value),
                performanceTransactions,
                listings.Select(listing => new CurrentMarketLiquidationEvidence(profile.Id, listing, asOfUtc)).ToArray(),
                coverage.EndUtc));
        }

        var orders = currentOrders?.Orders ?? [];
        var currentBuyCapital = Sum(orders.Where(order => order.Side == PersonalTradingPostSide.Buy)
            .Select(order => Total(order.UnitPriceInCopper, order.Quantity)));
        var sellGrossValue = Sum(orders.Where(order => order.Side == PersonalTradingPostSide.Sell)
            .Select(order => Total(order.UnitPriceInCopper, order.Quantity)));
        var sellNetValue = Sum(orders.Where(order => order.Side == PersonalTradingPostSide.Sell)
            .Select(order => saleCalculator.Calculate(Money.Zero, Total(order.UnitPriceInCopper, order.Quantity)).NetSaleProceeds));

        return new PersonalDashboard(
            PersonalDashboardState.Ready,
            null,
            profile.LastSuccessfulSyncAtUtc,
            currentOrders?.ObservedAtUtc,
            new DashboardHistoryCoverage(coverage.StartUtc, coverage.EndUtc),
            marketState,
            performance?.IsFeeRoundingExternallyVerified ?? Gw2TradingPostFeePolicy.IsFractionalCopperRoundingExternallyVerified,
            performance is null ? [] : performance.Windows.Select(MapWindow).ToArray(),
            performance is null ? null : ToDashboardMoney(performance.OpenPerformance.OpenAcquisitionBasis),
            performance is null ? null : ToDashboardMoney(performance.OpenPerformance.NetLiquidationValue),
            performance is null ? null : ToDashboardMoney(performance.OpenPerformance.UnrealizedProfit),
            performance?.OpenPerformance.IsFullyValued,
            performance is null ? [] : performance.OpenPerformance.Items.Select(item => MapOpenInventory(item, metadata)).ToArray(),
            ToDashboardMoney(currentBuyCapital),
            ToDashboardMoney(sellGrossValue),
            ToDashboardMoney(sellNetValue),
            orders.OrderBy(order => order.Side).ThenBy(order => NameFor(order.ItemId, metadata)).ThenBy(order => order.ExternalOrderId)
                .Select(order => MapOrder(order, metadata, listingsByItem, marketState)).ToArray(),
            transactions.OrderByDescending(transaction => transaction.Transaction.CompletedAtUtc)
                .ThenByDescending(transaction => transaction.Transaction.ExternalTransactionId)
                .Take(RecentTradeLimit)
                .Select(transaction => new DashboardRecentTrade(
                    transaction.Transaction.ExternalTransactionId.ToString(CultureInfo.InvariantCulture),
                    transaction.Transaction.Side,
                    transaction.Transaction.ItemId,
                    NameFor(transaction.Transaction.ItemId, metadata),
                    transaction.Transaction.Quantity,
                    ToDashboardMoney(new Money(transaction.Transaction.UnitPriceInCopper)),
                    transaction.Transaction.CompletedAtUtc)).ToArray(),
            performance is null ? [] : MapRealizedItems(performance, metadata, descending: true),
            performance is null ? [] : MapRealizedItems(performance, metadata, descending: false));
    }

    private async Task<IReadOnlyDictionary<int, string>> ReadMetadataAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken)
    {
        var uniqueIds = itemIds.Where(itemId => itemId > 0).Distinct().ToArray();
        var values = await itemMetadataRepository.GetManyAsync(uniqueIds, cancellationToken).ConfigureAwait(false);
        return values.ToDictionary(value => value.ItemId, value => value.Name);
    }

    private static DashboardRealizedWindow MapWindow(RealizedPerformanceWindowResult window) => new(
        (int)window.Window,
        window.Status,
        ToDashboardMoney(window.KnownBasisPerformance?.NetProfit),
        ToDashboardMoney(window.KnownBasisPerformance?.GrossSales),
        ToDashboardMoney(window.KnownBasisPerformance?.ListingFees),
        ToDashboardMoney(window.KnownBasisPerformance?.ExchangeFees),
        window.UnknownBasisQuantity);

    private static DashboardOpenInventory MapOpenInventory(
        OpenInventoryLiquidation item,
        IReadOnlyDictionary<int, string> metadata) => new(
        item.ItemId,
        NameFor(item.ItemId, metadata),
        item.OpenQuantity,
        ToDashboardMoney(item.OpenAcquisitionBasis),
        item.Status,
        item.UnliquidatedQuantity,
        ToDashboardMoney(item.NetLiquidationValue),
        ToDashboardMoney(item.UnrealizedProfit));

    private static DashboardOrder MapOrder(
        CurrentPersonalTradingPostOrder order,
        IReadOnlyDictionary<int, string> metadata,
        IReadOnlyDictionary<int, MarketListing> listings,
        DashboardMarketState marketState)
    {
        if (marketState == DashboardMarketState.Unavailable)
        {
            return new DashboardOrder(order.ExternalOrderId.ToString(CultureInfo.InvariantCulture), order.Side, order.ItemId, NameFor(order.ItemId, metadata), order.Quantity,
                ToDashboardMoney(new Money(order.UnitPriceInCopper)), DashboardOrderMarketComparisonStatus.Unavailable, null);
        }

        var marketPrice = listings.TryGetValue(order.ItemId, out var listing)
            ? order.Side == PersonalTradingPostSide.Buy
                ? listing.Buys.Select(level => level.UnitPriceInCopper).DefaultIfEmpty().Max()
                : listing.Sells.Select(level => level.UnitPriceInCopper).DefaultIfEmpty().Min()
            : 0;
        return new DashboardOrder(order.ExternalOrderId.ToString(CultureInfo.InvariantCulture), order.Side, order.ItemId, NameFor(order.ItemId, metadata), order.Quantity,
            ToDashboardMoney(new Money(order.UnitPriceInCopper)),
            marketPrice > 0 ? DashboardOrderMarketComparisonStatus.Available : DashboardOrderMarketComparisonStatus.MissingSide,
            marketPrice > 0 ? ToDashboardMoney(new Money(marketPrice)) : null);
    }

    private static IReadOnlyList<DashboardRealizedItem> MapRealizedItems(
        PersonalPerformanceRebuild performance,
        IReadOnlyDictionary<int, string> metadata,
        bool descending)
    {
        var items = performance.KnownBasisSaleAllocations
            .GroupBy(allocation => allocation.Match.ItemId)
            .Select(group => new
            {
                ItemId = group.Key,
                Quantity = checked((int)group.Sum(allocation => (long)allocation.Match.MatchedQuantity)),
                NetProfit = Sum(group.Select(allocation => allocation.NetProfit)),
            });
        return (descending
                ? items.OrderByDescending(item => item.NetProfit.Copper).ThenBy(item => item.ItemId)
                : items.OrderBy(item => item.NetProfit.Copper).ThenBy(item => item.ItemId))
            .Take(RealizedItemLimit)
            .Select(item => new DashboardRealizedItem(
                item.ItemId,
                NameFor(item.ItemId, metadata),
                item.Quantity,
                ToDashboardMoney(item.NetProfit)))
            .ToArray();
    }

    private static string NameFor(int itemId, IReadOnlyDictionary<int, string> metadata) =>
        metadata.TryGetValue(itemId, out var name) ? name : $"Item #{itemId}";

    private static DashboardMoney ToDashboardMoney(Money value) => new(value.Copper.ToString(CultureInfo.InvariantCulture));

    private static DashboardMoney? ToDashboardMoney(Money? value) => value is { } amount ? ToDashboardMoney(amount) : null;

    private static Money Total(int unitPriceInCopper, int quantity) => new(checked((long)unitPriceInCopper * quantity));

    private static Money Sum(IEnumerable<Money> values)
    {
        var total = Money.Zero;
        foreach (var value in values)
        {
            total += value;
        }

        return total;
    }

    private static DateTimeOffset RequireUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero
        ? value
        : throw new InvalidOperationException("The dashboard clock must return UTC.");

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
}

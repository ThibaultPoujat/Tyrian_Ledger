using Gw2Tp.Analytics.OrderBooks;
using Gw2Tp.Analytics.Finance;
using Gw2Tp.Application.Finance;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Application.Persistence;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Application.Time;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.Investments;

public enum InvestmentPortfolioState { Ready = 1, AccountUnavailable = 2 }
public enum InvestmentAction { Hold = 1, SellPartial = 2, Sell = 3 }
public enum InvestmentValuationState { Available = 1, InsufficientBuyDepth = 2, MarketUnavailable = 3 }

public sealed record InvestmentMoney(string Copper);
public sealed record InvestmentHistoricalEvidence(int AvailableWindowCount, int TotalWindowCount, int LatestEligibleObservationCount, int? MinimumBuyPriceInCopper, int? MaximumSellPriceInCopper, decimal? MedianAggregateBuyQuantity, decimal? MedianAggregateSellQuantity);
public sealed record InvestmentValuation(InvestmentValuationState State, InvestmentMoney? GrossLiquidationValue, InvestmentMoney? NetLiquidationValue, InvestmentMoney? UnrealizedProfit, int UnliquidatedQuantity, int? CurrentBestBuyPriceInCopper);
public sealed record InvestmentActionEvidence(InvestmentAction Action, int SuggestedQuantity, string Reason);
public sealed record InvestmentPositionView(InvestmentPosition Position, string ItemName, InvestmentValuation Valuation, InvestmentHistoricalEvidence HistoricalEvidence, InvestmentMoney? OpportunityCost, InvestmentActionEvidence Action);
public sealed record InvestmentPortfolio(InvestmentPortfolioState State, string? Error, IReadOnlyList<InvestmentPositionView> Positions);

public interface IInvestmentPortfolioService
{
    Task<InvestmentPortfolio> GetAsync(CancellationToken cancellationToken = default);
    Task<InvestmentPosition?> CreateAsync(CreateInvestmentPosition position, CancellationToken cancellationToken = default);
    Task<InvestmentPosition?> UpdateAsync(long positionId, UpdateInvestmentPosition position, CancellationToken cancellationToken = default);
    Task<InvestmentPosition?> ExitAsync(long positionId, int quantity, string? notes, CancellationToken cancellationToken = default);
}

/// <summary>
/// Keeps user-entered investment tracking separate from imported realized flip
/// accounting. Valuation only models immediately visible buy depth; it does not
/// forecast seasonal scarcity, fills, or appreciation.
/// </summary>
public sealed class InvestmentPortfolioService(
    IPersonalTradingPostGateway personalGateway,
    IPersonalTradingPostRepository profiles,
    IInvestmentPositionRepository positions,
    IItemMetadataRepository metadata,
    IGw2ApiClient marketData,
    IHistoricalMarketAnalyticsService historicalAnalytics,
    IClock clock) : IInvestmentPortfolioService
{
    private readonly OrderBookExecutionSimulator simulator = new();
    private readonly FlipProfitCalculator sales = new(Gw2TradingPostFeePolicy.Create());

    public async Task<InvestmentPortfolio> GetAsync(CancellationToken cancellationToken = default)
    {
        var account = await personalGateway.GetAccountScopeAsync(cancellationToken).ConfigureAwait(false);
        if (!account.IsSuccess || account.Value is null || string.IsNullOrWhiteSpace(account.Value.AccountId))
        {
            return new(InvestmentPortfolioState.AccountUnavailable, "account_unavailable", []);
        }
        var profile = await profiles.FindAccountProfileAsync(account.Value.AccountId, cancellationToken).ConfigureAwait(false);
        if (profile is null) return new(InvestmentPortfolioState.Ready, null, []);
        var values = await positions.GetAllAsync(profile, cancellationToken).ConfigureAwait(false);
        if (values.Count == 0) return new(InvestmentPortfolioState.Ready, null, []);
        var itemIds = values.Where(position => !position.IsClosed).Select(position => position.ItemId).Distinct().OrderBy(id => id).ToArray();
        var listingsResult = itemIds.Length == 0 ? Gw2ApiResult<IReadOnlyList<MarketListing>>.Success([]) : await marketData.GetListingsAsync(itemIds, cancellationToken).ConfigureAwait(false);
        var listings = listingsResult.IsSuccess && !listingsResult.IsPartialData && listingsResult.Value is not null
            ? listingsResult.Value.ToDictionary(listing => listing.ItemId) : new Dictionary<int, MarketListing>();
        var names = (await metadata.GetManyAsync(values.Select(position => position.ItemId).Distinct().ToArray(), cancellationToken).ConfigureAwait(false)).ToDictionary(item => item.ItemId, item => item.Name);
        var views = new List<InvestmentPositionView>();
        foreach (var position in values)
        {
            var history = await historicalAnalytics.GetAsync(position.ItemId, cancellationToken).ConfigureAwait(false);
            var valuation = position.IsClosed ? new InvestmentValuation(InvestmentValuationState.MarketUnavailable, null, null, null, 0, null) : Value(position, listings.GetValueOrDefault(position.ItemId), listingsResult.IsSuccess && !listingsResult.IsPartialData);
            views.Add(new(position, names.GetValueOrDefault(position.ItemId, $"Item {position.ItemId}"), valuation, ToHistorical(history), valuation.NetLiquidationValue, ActionFor(position, valuation)));
        }
        return new(InvestmentPortfolioState.Ready, null, views);
    }

    public async Task<InvestmentPosition?> CreateAsync(CreateInvestmentPosition position, CancellationToken cancellationToken = default)
    {
        var profile = await GetOrCreateProfileAsync(cancellationToken).ConfigureAwait(false);
        return profile is null ? null : await positions.CreateAsync(profile, position, RequireUtc(clock.UtcNow), cancellationToken).ConfigureAwait(false);
    }

    public async Task<InvestmentPosition?> UpdateAsync(long positionId, UpdateInvestmentPosition position, CancellationToken cancellationToken = default)
    {
        var profile = await FindProfileAsync(cancellationToken).ConfigureAwait(false);
        return profile is null ? null : await positions.UpdateAsync(profile, positionId, position, RequireUtc(clock.UtcNow), cancellationToken).ConfigureAwait(false);
    }

    public async Task<InvestmentPosition?> ExitAsync(long positionId, int quantity, string? notes, CancellationToken cancellationToken = default)
    {
        var profile = await FindProfileAsync(cancellationToken).ConfigureAwait(false);
        return profile is null ? null : await positions.RecordExitAsync(profile, positionId, quantity, RequireUtc(clock.UtcNow), notes, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AccountProfile?> FindProfileAsync(CancellationToken cancellationToken)
    {
        var account = await personalGateway.GetAccountScopeAsync(cancellationToken).ConfigureAwait(false);
        return !account.IsSuccess || account.Value is null || string.IsNullOrWhiteSpace(account.Value.AccountId)
            ? null : await profiles.FindAccountProfileAsync(account.Value.AccountId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AccountProfile?> GetOrCreateProfileAsync(CancellationToken cancellationToken)
    {
        var account = await personalGateway.GetAccountScopeAsync(cancellationToken).ConfigureAwait(false);
        return !account.IsSuccess || account.Value is null || string.IsNullOrWhiteSpace(account.Value.AccountId)
            ? null : await profiles.GetOrCreateAccountProfileAsync(account.Value.AccountId, RequireUtc(clock.UtcNow), cancellationToken).ConfigureAwait(false);
    }

    private InvestmentValuation Value(InvestmentPosition position, MarketListing? listing, bool marketAvailable)
    {
        if (!marketAvailable || listing is null) return new(InvestmentValuationState.MarketUnavailable, null, null, null, position.RemainingQuantity, null);
        var scenario = simulator.SimulateLiquidation(listing.Buys.Select(level => new OrderBookLevel(level.Quantity, new Money(level.UnitPriceInCopper))).ToArray(), position.RemainingQuantity);
        var bestBuy = listing.Buys.Select(level => level.UnitPriceInCopper).DefaultIfEmpty().Max();
        if (!scenario.IsFullyFilled) return new(InvestmentValuationState.InsufficientBuyDepth, null, null, null, scenario.RemainingQuantity, bestBuy > 0 ? bestBuy : null);
        var model = sales.Calculate(Money.Zero, scenario.TotalValue);
        int? remainingBasis = position.AcquisitionBasisInCopper is null ? null : position.AcquisitionBasisInCopper.Value - position.Exits.Sum(exit => exit.AllocatedBasisInCopper ?? 0);
        return new(InvestmentValuationState.Available, MoneyOf(model.GrossSaleValue), MoneyOf(model.NetSaleProceeds), remainingBasis is null ? null : MoneyOf(model.NetSaleProceeds - new Money(remainingBasis.Value)), 0, bestBuy > 0 ? bestBuy : null);
    }

    private static InvestmentHistoricalEvidence ToHistorical(HistoricalMarketAnalytics analytics)
    {
        var available = analytics.Windows.Where(window => window.State == HistoricalMarketWindowState.Available).ToArray();
        var metrics = available.LastOrDefault()?.Metrics;
        return new(available.Length, analytics.Windows.Count, available.LastOrDefault()?.Coverage.EligibleObservationCount ?? 0, metrics?.BuyPriceRange.MinimumCopper, metrics?.SellPriceRange.MaximumCopper, metrics?.MedianAggregateBuyQuantity, metrics?.MedianAggregateSellQuantity);
    }

    private static InvestmentActionEvidence ActionFor(InvestmentPosition position, InvestmentValuation valuation)
    {
        if (position.IsClosed) return new(InvestmentAction.Hold, 0, "This position is closed; its exit history is retained separately from realized flip reporting.");
        if (valuation.State != InvestmentValuationState.Available || valuation.CurrentBestBuyPriceInCopper is null) return new(InvestmentAction.Hold, 0, "Visible buy depth cannot currently support a complete modeled exit, so no sell action is suggested.");
        var target = position.Targets.OrderBy(target => target.UnitPriceInCopper).ThenBy(target => target.Ordinal).FirstOrDefault(target => valuation.CurrentBestBuyPriceInCopper >= target.UnitPriceInCopper);
        if (target is null) return new(InvestmentAction.Hold, 0, "No configured target is reached at the current visible buy price; this is not a price forecast.");
        var quantity = Math.Min(target.Quantity, position.RemainingQuantity);
        return quantity >= position.RemainingQuantity
            ? new(InvestmentAction.Sell, quantity, "The current visible buy price meets a configured full-exit target; verify depth before manually listing or selling.")
            : new(InvestmentAction.SellPartial, quantity, "The current visible buy price meets a configured staged target; verify depth before a manual partial exit.");
    }

    private static InvestmentMoney MoneyOf(Money money) => new(money.Copper.ToString(System.Globalization.CultureInfo.InvariantCulture));
    private static DateTimeOffset RequireUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero ? value : throw new InvalidOperationException("The investment clock must return UTC.");
}

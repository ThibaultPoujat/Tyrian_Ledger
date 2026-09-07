using Gw2Tp.Application.Accounting;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Persistence;

namespace Gw2Tp.Application.Dashboard;

public enum PersonalDashboardState
{
    Ready,
    NotSynchronized,
    AccountUnavailable,
}

public enum DashboardMarketState
{
    Available,
    Unavailable,
}

public enum DashboardOrderMarketComparisonStatus
{
    Available,
    MissingSide,
    Unavailable,
}

/// <summary>
/// A decimal integer copper amount encoded as text so the browser never has to
/// coerce a 64-bit server value through a lossy JavaScript number.
/// </summary>
public sealed record DashboardMoney(string Copper);

public sealed record DashboardHistoryCoverage(DateTimeOffset? StartUtc, DateTimeOffset? EndUtc);

public sealed record DashboardRealizedWindow(
    int Days,
    RealizedPerformanceWindowStatus Status,
    DashboardMoney? NetProfit,
    DashboardMoney? GrossSales,
    DashboardMoney? ListingFees,
    DashboardMoney? ExchangeFees,
    int UnknownBasisQuantity);

public sealed record DashboardOpenInventory(
    int ItemId,
    string ItemName,
    int Quantity,
    DashboardMoney AcquisitionBasis,
    CurrentLiquidationStatus LiquidationStatus,
    int UnliquidatedQuantity,
    DashboardMoney? NetLiquidationValue,
    DashboardMoney? UnrealizedProfit);

public sealed record DashboardOrder(
    string OrderId,
    PersonalTradingPostSide Side,
    int ItemId,
    string ItemName,
    int Quantity,
    DashboardMoney UnitPrice,
    DashboardOrderMarketComparisonStatus MarketComparisonStatus,
    DashboardMoney? CurrentMarketUnitPrice);

public sealed record DashboardRecentTrade(
    string TransactionId,
    PersonalTradingPostSide Side,
    int ItemId,
    string ItemName,
    int Quantity,
    DashboardMoney UnitPrice,
    DateTimeOffset CompletedAtUtc);

public sealed record DashboardRealizedItem(
    int ItemId,
    string ItemName,
    int Quantity,
    DashboardMoney NetProfit);

public sealed record PersonalDashboard(
    PersonalDashboardState State,
    Gw2ApiErrorCategory? AccountError,
    DateTimeOffset? LastSuccessfulSyncAtUtc,
    DateTimeOffset? CurrentOrdersObservedAtUtc,
    DashboardHistoryCoverage? HistoryCoverage,
    DashboardMarketState MarketState,
    bool IsFeeRoundingExternallyVerified,
    IReadOnlyList<DashboardRealizedWindow> RealizedWindows,
    DashboardMoney? OpenAcquisitionBasis,
    DashboardMoney? NetLiquidationValue,
    DashboardMoney? UnrealizedProfit,
    bool? IsOpenInventoryFullyValued,
    IReadOnlyList<DashboardOpenInventory> OpenInventory,
    DashboardMoney CurrentBuyCapital,
    DashboardMoney CurrentSellGrossValue,
    DashboardMoney CurrentSellNetValue,
    IReadOnlyList<DashboardOrder> CurrentOrders,
    IReadOnlyList<DashboardRecentTrade> RecentTrades,
    IReadOnlyList<DashboardRealizedItem> BestRealizedItems,
    IReadOnlyList<DashboardRealizedItem> WorstRealizedItems)
{
    public static PersonalDashboard AccountUnavailable(Gw2ApiErrorCategory error) => new(
        PersonalDashboardState.AccountUnavailable,
        error,
        null, null, null,
        DashboardMarketState.Unavailable,
        false,
        [], null, null, null, null, [],
        new DashboardMoney("0"), new DashboardMoney("0"), new DashboardMoney("0"),
        [], [], [], []);

    public static PersonalDashboard NotSynchronized() => new(
        PersonalDashboardState.NotSynchronized,
        null,
        null, null, null,
        DashboardMarketState.Unavailable,
        false,
        [], null, null, null, null, [],
        new DashboardMoney("0"), new DashboardMoney("0"), new DashboardMoney("0"),
        [], [], [], []);
}

public interface IPersonalDashboardService
{
    Task<PersonalDashboard> GetAsync(CancellationToken cancellationToken = default);
}

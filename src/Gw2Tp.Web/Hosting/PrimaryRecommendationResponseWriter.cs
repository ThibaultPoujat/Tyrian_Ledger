using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gw2Tp.Application.Recommendations;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Web.Hosting;

internal static class PrimaryRecommendationResponseWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    internal static Task WriteAsync(HttpContext context, PrimaryRecommendationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsync(JsonSerializer.Serialize(ToResponse(result), SerializerOptions));
    }

    private static object ToResponse(PrimaryRecommendationResult result) => new
    {
        result.State, result.EvidenceError, result.GeneratedAtUtc, result.LastSuccessfulSyncAtUtc,
        result.CurrentOrdersObservedAtUtc, result.ScannerObservedAtUtc,
        policies = new
        {
            result.Policies.ActionPolicyVersion,
            result.Policies.ScorePolicyVersion,
            result.Policies.PositionSizingPolicyVersion,
            result.Policies.FifoPolicyVersion,
            result.Policies.FeePolicyVersion,
            minimumProfit = MoneyResponse.From(new Money(result.Policies.MinimumProfitInCopper)),
            result.Policies.MinimumRoiBasisPoints,
            result.Policies.CashReserveBasisPoints,
            result.Policies.Strategy,
            result.Policies.Category,
        },
        portfolio = result.Portfolio is null ? null : new
        {
            availableCash = MoneyResponse.From(result.Portfolio.AvailableCash),
            totalBankroll = MoneyResponse.From(result.Portfolio.TotalBankroll),
            cashReserve = MoneyResponse.From(result.Portfolio.CashReserve),
            result.Portfolio.ReserveStatus,
            cashReserveShortfall = MoneyResponse.From(result.Portfolio.CashReserveShortfall),
            remainingCashAfterSizing = MoneyResponse.From(result.Portfolio.RemainingCashAfterSizing),
        },
        actions = result.Actions.Select(action => new
        {
            action = ActionName(action.Action), action.Source, action.OrderState, action.OrderId,
            action.ItemId, action.ItemName, action.Quantity,
            capital = MoneyResponse.From(action.Capital),
            prices = new
            {
                currentOrderUnitPrice = Optional(action.Prices.CurrentOrderUnitPrice),
                bestBuy = Optional(action.Prices.BestBuy),
                lowestSell = Optional(action.Prices.LowestSell),
                plannedBid = Optional(action.Prices.PlannedBid),
                plannedListPrice = Optional(action.Prices.PlannedListPrice),
                maximumBid = Optional(action.Prices.MaximumBid),
            },
            economics = action.Economics is null ? null : new
            {
                acquisitionCost = MoneyResponse.From(action.Economics.AcquisitionCost),
                grossSaleValue = MoneyResponse.From(action.Economics.GrossSaleValue),
                listingFee = MoneyResponse.From(action.Economics.ListingFee),
                exchangeFee = MoneyResponse.From(action.Economics.ExchangeFee),
                netSaleProceeds = MoneyResponse.From(action.Economics.NetSaleProceeds),
                netProfit = MoneyResponse.From(action.Economics.NetProfit),
                totalCost = MoneyResponse.From(action.Economics.TotalCost),
                action.Economics.RoiDisplayPercent,
            },
            action.Score, action.History, action.Liquidity,
            portfolioConstraints = action.PortfolioConstraints.Select(constraint => new
            {
                constraint.Name,
                capitalCapacity = MoneyResponse.From(constraint.CapitalCapacity),
                constraint.QuantityCapacity,
                constraint.IsBinding,
            }).ToArray(),
            action.Reasons,
        }).ToArray(),
    };

    private static MoneyResponse? Optional(Money? value) => value is { } amount ? MoneyResponse.From(amount) : null;
    private static string ActionName(PrimaryRecommendationAction action) => action switch
    {
        PrimaryRecommendationAction.Buy => "BUY",
        PrimaryRecommendationAction.BuySmall => "BUY SMALL",
        PrimaryRecommendationAction.Wait => "WAIT",
        PrimaryRecommendationAction.KeepBid => "KEEP BID",
        PrimaryRecommendationAction.UpdateBid => "UPDATE BID",
        PrimaryRecommendationAction.StopBidding => "STOP BIDDING",
        PrimaryRecommendationAction.CancelBid => "CANCEL BID",
        PrimaryRecommendationAction.List => "LIST",
        PrimaryRecommendationAction.LeaveSellListing => "LEAVE SELL LISTING",
        PrimaryRecommendationAction.Hold => "HOLD",
        PrimaryRecommendationAction.Reduce => "REDUCE",
        PrimaryRecommendationAction.SellPartial => "SELL PARTIAL",
        PrimaryRecommendationAction.Sell => "SELL",
        PrimaryRecommendationAction.Skip => "SKIP",
        PrimaryRecommendationAction.Review => "REVIEW",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown recommendation action."),
    };

    private sealed record MoneyResponse(string Copper)
    {
        public static MoneyResponse From(Money amount) => new(amount.Copper.ToString(CultureInfo.InvariantCulture));
    }
}

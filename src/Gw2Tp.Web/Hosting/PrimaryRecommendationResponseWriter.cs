using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gw2Tp.Application.Plans;
using Gw2Tp.Application.Recommendations;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Web.Hosting;

internal static class PrimaryRecommendationResponseWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    internal static Task WriteAsync(HttpContext context, PrimaryRecommendationResult result, DecisionLoopStatus? decisionLoop = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsync(JsonSerializer.Serialize(ToResponse(result, decisionLoop), SerializerOptions));
    }

    private static object ToResponse(PrimaryRecommendationResult result, DecisionLoopStatus? decisionLoop) => new
    {
        result.State, result.EvidenceError, result.GeneratedAtUtc, result.LastSuccessfulSyncAtUtc,
        result.CurrentOrdersObservedAtUtc, result.ScannerObservedAtUtc, result.AccountEvidenceExpiresAtUtc,
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
            action.ItemId, action.ItemName, action.ItemIconUrl, action.Quantity,
            capital = MoneyResponse.From(action.Capital),
            prices = new
            {
                currentOrderUnitPrice = Optional(action.Prices.CurrentOrderUnitPrice),
                bestBuy = Optional(action.Prices.BestBuy),
                lowestSell = Optional(action.Prices.LowestSell),
                plannedBid = Optional(action.Prices.PlannedBid),
                plannedListPrice = Optional(action.Prices.PlannedListPrice),
                maximumBid = Optional(action.Prices.MaximumBid),
                immediateSalePriceRange = action.Prices.ImmediateSalePriceRange is { } range ? new
                {
                    lowestUnitPrice = MoneyResponse.From(range.LowestUnitPrice),
                    highestUnitPrice = MoneyResponse.From(range.HighestUnitPrice),
                } : null,
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
        decisionLoop = decisionLoop is null ? null : new
        {
            state = decisionLoop.State,
            decisionLoop.LastCycleAtUtc,
            decisionLoop.NextCycleAtUtc,
            decisionLoop.ConsecutiveFailures,
            decisionLoop.LastErrorCode,
            notificationsEnabled = decisionLoop.NotificationsEnabled,
            market = Source(decisionLoop.Market),
            account = Source(decisionLoop.Account),
            history = Source(decisionLoop.History),
        },
        notifications = decisionLoop?.Notifications.Select(ToNotification).ToArray() ?? [],
    };

    private static object Source(DecisionLoopSourceStatus source) => new
    {
        state = source.State,
        source.LastSuccessfulAtUtc,
        source.ErrorCode,
    };

    private static object ToNotification(DecisionLoopNotification notification) => new
    {
        notification.Id,
        notification.Kind,
        notification.Route,
        action = notification.ActionCode,
        actionLabel = ActionLabel(notification.ActionCode),
        notification.ItemId,
        itemName = notification.ItemName == "Plan" ? "Parcours en cours" : notification.ItemName,
        notification.Quantity,
        unitPrice = Optional(notification.UnitPrice),
        reason = ReasonLabel(notification),
        reasonCode = notification.SignalReasonCode?.ToString(),
        notification.PlanId,
        urgency = notification.IsUrgent ? "high" : "normal",
        notification.CreatedAtUtc,
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

    private static string ActionLabel(string actionCode) => actionCode switch
    {
        nameof(PrimaryRecommendationAction.Buy) => "Acheter maintenant",
        nameof(PrimaryRecommendationAction.BuySmall) => "Acheter une petite quantité",
        nameof(PrimaryRecommendationAction.UpdateBid) => "Mettre à jour l'ordre d'achat",
        nameof(PrimaryRecommendationAction.CancelBid) => "Annuler l'ordre d'achat",
        nameof(PrimaryRecommendationAction.List) => "Mettre en vente",
        nameof(PrimaryRecommendationAction.Reduce) => "Réduire l'exposition",
        nameof(PrimaryRecommendationAction.SellPartial) => "Vendre partiellement",
        nameof(PrimaryRecommendationAction.Sell) => "Vendre maintenant",
        nameof(PlanStepAction.BuyNow) => "Acheter maintenant",
        nameof(PlanStepAction.PlaceBuyOrder) => "Placer un ordre d'achat",
        nameof(PlanStepAction.CancelBuyOrder) => "Annuler l'ordre d'achat",
        nameof(PlanStepAction.Relist) => "Remettre en vente",
        nameof(PlanStepAction.SellNow) => "Vendre maintenant",
        nameof(PlanStepAction.Craft) => "Fabriquer",
        "REVIEW" => "Vérifier le plan",
        _ => "Consulter le plan",
    };

    private static string ReasonLabel(DecisionLoopNotification notification)
    {
        if (notification.Reason == DecisionLoopNotificationReason.PlanReady)
            return "L'étape suivante de ce plan est prête à être réalisée manuellement.";
        if (notification.Reason == DecisionLoopNotificationReason.PlanRecheck)
            return "Les données ont changé : vérifiez le plan avant d'agir.";
        if (notification.Reason == DecisionLoopNotificationReason.PlanReconciliation)
            return "Une différence avec ArenaNet demande une réconciliation explicite.";

        return notification.SignalReasonCode switch
        {
            PrimaryRecommendationReasonCode.BidAboveMaximum => "Le prix de l'ordre dépasse maintenant le maximum autorisé.",
            PrimaryRecommendationReasonCode.ReserveRestoration => "Cette action protège la réserve de liquidités configurée.",
            PrimaryRecommendationReasonCode.PositiveImmediateExit => "La liquidation immédiate reste positive après les frais.",
            PrimaryRecommendationReasonCode.PositiveListingExit => "La mise en vente reste positive après les frais.",
            PrimaryRecommendationReasonCode.ItemExposureExceeded => "L'exposition connue sur cet objet dépasse la limite actuelle.",
            PrimaryRecommendationReasonCode.BidOutbid => "L'ordre doit être ajusté dans la limite de prix autorisée.",
            _ => "Les preuves de marché et de portefeuille justifient cette action manuelle.",
        };
    }

    private sealed record MoneyResponse(string Copper)
    {
        public static MoneyResponse From(Money amount) => new(amount.Copper.ToString(CultureInfo.InvariantCulture));
    }
}

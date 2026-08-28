using Gw2Tp.Analytics.Finance;
using Gw2Tp.Analytics.OrderBooks;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Analytics.OwnedItems;

/// <summary>
/// Deterministically values owned items as foregone market proceeds instead of
/// free crafting inputs. It never performs I/O or assumes a default fee policy.
/// </summary>
public sealed class OwnedItemOpportunityCostCalculator
{
    private readonly FlipProfitCalculator profitCalculator;
    private readonly OrderBookExecutionSimulator orderBookExecutionSimulator = new();

    public OwnedItemOpportunityCostCalculator(TransactionFeePolicy feePolicy)
    {
        ArgumentNullException.ThrowIfNull(feePolicy);
        profitCalculator = new FlipProfitCalculator(feePolicy);
    }

    public OwnedItemOpportunityCostAnalysis Analyze(OwnedItemOpportunityCostRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var eligibleOwnedQuantity = 0;
        var restrictedQuantities = new Dictionary<OwnedItemRestriction, int>();
        foreach (var lot in request.OwnedLots)
        {
            if (lot.Restriction is { } restriction)
            {
                restrictedQuantities.TryGetValue(restriction, out var restrictedQuantity);
                restrictedQuantities[restriction] = checked(restrictedQuantity + lot.Quantity);
            }
            else
            {
                eligibleOwnedQuantity = checked(eligibleOwnedQuantity + lot.Quantity);
            }
        }

        var restrictionFlags = restrictedQuantities
            .Select(quantity => new OwnedItemRestrictionFlag(quantity.Key, quantity.Value))
            .OrderBy(flag => flag.Restriction)
            .ToArray();
        var strategies = new[]
        {
            CalculateStrategy(
                OwnedItemStrategy.BuyAll,
                ownedQuantity: 0,
                purchasedQuantity: request.RequiredQuantity,
                request),
            eligibleOwnedQuantity >= request.RequiredQuantity
                ? CalculateStrategy(
                    OwnedItemStrategy.UseOwned,
                    ownedQuantity: request.RequiredQuantity,
                    purchasedQuantity: 0,
                    request)
                : CreateUnavailableStrategy(
                    OwnedItemStrategy.UseOwned,
                    ownedQuantity: request.RequiredQuantity,
                    purchasedQuantity: 0,
                    OwnedItemOpportunityCostReason.InsufficientEligibleOwnedQuantity),
            eligibleOwnedQuantity > 0 && eligibleOwnedQuantity < request.RequiredQuantity
                ? CalculateStrategy(
                    OwnedItemStrategy.Mixed,
                    ownedQuantity: eligibleOwnedQuantity,
                    purchasedQuantity: request.RequiredQuantity - eligibleOwnedQuantity,
                    request)
                : CreateUnavailableStrategy(
                    OwnedItemStrategy.Mixed,
                    ownedQuantity: Math.Min(eligibleOwnedQuantity, request.RequiredQuantity),
                    purchasedQuantity: Math.Max(request.RequiredQuantity - eligibleOwnedQuantity, 0),
                    OwnedItemOpportunityCostReason.NoGenuineMixedAllocation),
        };

        return new OwnedItemOpportunityCostAnalysis(
            request.ItemId,
            request.RequiredQuantity,
            request.ValuationRoute,
            eligibleOwnedQuantity,
            restrictionFlags,
            strategies);
    }

    private OwnedItemStrategyAnalysis CalculateStrategy(
        OwnedItemStrategy strategy,
        int ownedQuantity,
        int purchasedQuantity,
        OwnedItemOpportunityCostRequest request)
    {
        var reasons = new List<OwnedItemOpportunityCostReason>();
        var ownedOpportunityCost = Money.Zero;
        var purchasedCost = Money.Zero;

        if (ownedQuantity > 0 && !TryCalculateOwnedOpportunityCost(
                request.MarketEvidence,
                request.ValuationRoute,
                ownedQuantity,
                out ownedOpportunityCost,
                out var ownedFailureReason))
        {
            reasons.Add(ownedFailureReason);
        }

        if (purchasedQuantity > 0 && !TryCalculatePurchasedCost(
                request.MarketEvidence,
                purchasedQuantity,
                out purchasedCost,
                out var purchaseFailureReason))
        {
            reasons.Add(purchaseFailureReason);
        }

        return reasons.Count == 0
            ? new OwnedItemStrategyAnalysis(
                strategy,
                isAvailable: true,
                ownedQuantity,
                purchasedQuantity,
                ownedOpportunityCost,
                purchasedCost,
                ownedOpportunityCost + purchasedCost,
                reasons)
            : CreateUnavailableStrategy(strategy, ownedQuantity, purchasedQuantity, reasons);
    }

    private bool TryCalculatePurchasedCost(
        OwnedItemMarketEvidence marketEvidence,
        int quantity,
        out Money purchasedCost,
        out OwnedItemOpportunityCostReason failureReason)
    {
        var execution = orderBookExecutionSimulator.SimulateAcquisition(marketEvidence.SellLevels, quantity);
        if (execution.Fills.Any(fill => fill.UnitPrice.Copper <= 0))
        {
            purchasedCost = Money.Zero;
            failureReason = OwnedItemOpportunityCostReason.MissingPurchaseMarketEvidence;
            return false;
        }

        if (!execution.IsFullyFilled)
        {
            purchasedCost = Money.Zero;
            failureReason = execution.FilledQuantity == 0
                ? OwnedItemOpportunityCostReason.MissingPurchaseMarketEvidence
                : OwnedItemOpportunityCostReason.InsufficientPurchaseMarketDepth;
            return false;
        }

        purchasedCost = execution.TotalValue;
        failureReason = default;
        return true;
    }

    private bool TryCalculateOwnedOpportunityCost(
        OwnedItemMarketEvidence marketEvidence,
        OwnedItemValuationRoute valuationRoute,
        int quantity,
        out Money ownedOpportunityCost,
        out OwnedItemOpportunityCostReason failureReason)
    {
        if (valuationRoute == OwnedItemValuationRoute.ListingAtBestAsk)
        {
            var bestAsk = marketEvidence.SellLevels
                .OrderBy(level => level.UnitPrice.Copper)
                .FirstOrDefault();
            if (bestAsk is null || bestAsk.UnitPrice.Copper <= 0)
            {
                ownedOpportunityCost = Money.Zero;
                failureReason = OwnedItemOpportunityCostReason.MissingListingMarketEvidence;
                return false;
            }

            var grossListingValue = new Money(checked(bestAsk.UnitPrice.Copper * quantity));
            ownedOpportunityCost = profitCalculator.Calculate(Money.Zero, grossListingValue).NetSaleProceeds;
            failureReason = default;
            return true;
        }

        var execution = orderBookExecutionSimulator.SimulateLiquidation(marketEvidence.BuyLevels, quantity);
        if (execution.Fills.Any(fill => fill.UnitPrice.Copper <= 0))
        {
            ownedOpportunityCost = Money.Zero;
            failureReason = OwnedItemOpportunityCostReason.MissingImmediateLiquidationMarketEvidence;
            return false;
        }

        if (!execution.IsFullyFilled)
        {
            ownedOpportunityCost = Money.Zero;
            failureReason = execution.FilledQuantity == 0
                ? OwnedItemOpportunityCostReason.MissingImmediateLiquidationMarketEvidence
                : OwnedItemOpportunityCostReason.InsufficientImmediateLiquidationMarketDepth;
            return false;
        }

        ownedOpportunityCost = profitCalculator.Calculate(Money.Zero, execution.TotalValue).NetSaleProceeds;
        failureReason = default;
        return true;
    }

    private static OwnedItemStrategyAnalysis CreateUnavailableStrategy(
        OwnedItemStrategy strategy,
        int ownedQuantity,
        int purchasedQuantity,
        OwnedItemOpportunityCostReason reason) =>
        CreateUnavailableStrategy(strategy, ownedQuantity, purchasedQuantity, [reason]);

    private static OwnedItemStrategyAnalysis CreateUnavailableStrategy(
        OwnedItemStrategy strategy,
        int ownedQuantity,
        int purchasedQuantity,
        IReadOnlyList<OwnedItemOpportunityCostReason> reasons) =>
        new(
            strategy,
            isAvailable: false,
            ownedQuantity,
            purchasedQuantity,
            ownedOpportunityCost: null,
            purchasedCost: null,
            totalEconomicCost: null,
            reasons);
}

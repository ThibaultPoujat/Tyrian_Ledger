using Gw2Tp.Analytics.Finance;
using Gw2Tp.Analytics.OrderBooks;
using Gw2Tp.Analytics.OwnedItems;
using Gw2Tp.Domain.Finance;
using Xunit;

namespace Gw2Tp.Analytics.Tests.OwnedItems;

public sealed class OwnedItemOpportunityCostCalculatorTests
{
    private readonly OwnedItemOpportunityCostCalculator calculator = new(
        new TransactionFeePolicy(
            new FeeRule(500, FeeRounding.Down),
            new FeeRule(1_000, FeeRounding.Down)));

    [Fact]
    public void Values_fully_owned_stock_economically_instead_of_treating_it_as_free()
    {
        var analysis = calculator.Analyze(CreateRequest(
            requiredQuantity: 5,
            ownedLots: [new OwnedItemLot(5)],
            buyLevels: [Level(5, 90)],
            sellLevels: [Level(5, 100)]));

        var buyAll = GetStrategy(analysis, OwnedItemStrategy.BuyAll);
        var useOwned = GetStrategy(analysis, OwnedItemStrategy.UseOwned);
        var mixed = GetStrategy(analysis, OwnedItemStrategy.Mixed);

        Assert.True(buyAll.IsAvailable);
        Assert.Equal(new Money(500), buyAll.PurchasedCost);
        Assert.Equal(new Money(500), buyAll.TotalEconomicCost);
        Assert.True(useOwned.IsAvailable);
        Assert.Equal(5, useOwned.OwnedQuantity);
        Assert.Equal(new Money(383), useOwned.OwnedOpportunityCost);
        Assert.Equal(new Money(383), useOwned.TotalEconomicCost);
        Assert.NotEqual(Money.Zero, useOwned.TotalEconomicCost);
        Assert.False(mixed.IsAvailable);
        Assert.Equal([OwnedItemOpportunityCostReason.NoGenuineMixedAllocation], mixed.Reasons);
    }

    [Fact]
    public void Combines_partial_eligible_stock_with_exact_purchase_cost()
    {
        var analysis = calculator.Analyze(CreateRequest(
            requiredQuantity: 5,
            ownedLots: [new OwnedItemLot(2)],
            buyLevels: [Level(2, 90)],
            sellLevels: [Level(5, 100)]));

        var useOwned = GetStrategy(analysis, OwnedItemStrategy.UseOwned);
        var mixed = GetStrategy(analysis, OwnedItemStrategy.Mixed);

        Assert.False(useOwned.IsAvailable);
        Assert.Equal([OwnedItemOpportunityCostReason.InsufficientEligibleOwnedQuantity], useOwned.Reasons);
        Assert.True(mixed.IsAvailable);
        Assert.Equal(2, mixed.OwnedQuantity);
        Assert.Equal(3, mixed.PurchasedQuantity);
        Assert.Equal(new Money(153), mixed.OwnedOpportunityCost);
        Assert.Equal(new Money(300), mixed.PurchasedCost);
        Assert.Equal(new Money(453), mixed.TotalEconomicCost);
    }

    [Fact]
    public void Rejects_zero_priced_immediate_liquidation_evidence_without_making_owned_stock_free()
    {
        var analysis = calculator.Analyze(CreateRequest(
            requiredQuantity: 5,
            ownedLots: [new OwnedItemLot(5)],
            buyLevels: [Level(5, 0)],
            sellLevels: [Level(5, 100)]));

        var buyAll = GetStrategy(analysis, OwnedItemStrategy.BuyAll);
        var useOwned = GetStrategy(analysis, OwnedItemStrategy.UseOwned);

        Assert.True(buyAll.IsAvailable);
        Assert.False(useOwned.IsAvailable);
        Assert.Null(useOwned.OwnedOpportunityCost);
        Assert.Null(useOwned.TotalEconomicCost);
        Assert.Equal(
            [OwnedItemOpportunityCostReason.MissingImmediateLiquidationMarketEvidence],
            useOwned.Reasons);
    }

    [Fact]
    public void Flags_bound_and_non_sellable_lots_and_excludes_them_from_owned_strategies()
    {
        var analysis = calculator.Analyze(CreateRequest(
            requiredQuantity: 5,
            ownedLots:
            [
                new OwnedItemLot(3, OwnedItemRestriction.Bound),
                new OwnedItemLot(2, OwnedItemRestriction.NonSellable),
            ],
            buyLevels: [Level(5, 90)],
            sellLevels: [Level(5, 100)]));

        var buyAll = GetStrategy(analysis, OwnedItemStrategy.BuyAll);
        var useOwned = GetStrategy(analysis, OwnedItemStrategy.UseOwned);
        var mixed = GetStrategy(analysis, OwnedItemStrategy.Mixed);

        Assert.Equal(0, analysis.EligibleOwnedQuantity);
        Assert.Equal(
            [
                new OwnedItemRestrictionFlag(OwnedItemRestriction.Bound, 3),
                new OwnedItemRestrictionFlag(OwnedItemRestriction.NonSellable, 2),
            ],
            analysis.RestrictionFlags);
        Assert.True(buyAll.IsAvailable);
        Assert.False(useOwned.IsAvailable);
        Assert.Equal([OwnedItemOpportunityCostReason.InsufficientEligibleOwnedQuantity], useOwned.Reasons);
        Assert.False(mixed.IsAvailable);
        Assert.Equal([OwnedItemOpportunityCostReason.NoGenuineMixedAllocation], mixed.Reasons);
    }

    [Fact]
    public void Models_listing_at_best_ask_without_claiming_immediate_fill_depth()
    {
        var analysis = calculator.Analyze(CreateRequest(
            requiredQuantity: 5,
            ownedLots: [new OwnedItemLot(5)],
            buyLevels: [],
            sellLevels: [Level(1, 110)],
            valuationRoute: OwnedItemValuationRoute.ListingAtBestAsk));

        var buyAll = GetStrategy(analysis, OwnedItemStrategy.BuyAll);
        var useOwned = GetStrategy(analysis, OwnedItemStrategy.UseOwned);

        Assert.False(buyAll.IsAvailable);
        Assert.Equal([OwnedItemOpportunityCostReason.InsufficientPurchaseMarketDepth], buyAll.Reasons);
        Assert.True(useOwned.IsAvailable);
        Assert.Equal(new Money(468), useOwned.OwnedOpportunityCost);
        Assert.Equal(new Money(468), useOwned.TotalEconomicCost);
    }

    [Fact]
    public void Rejects_inconsistent_unavailable_strategy_results()
    {
        Assert.Throws<ArgumentException>(() => new OwnedItemStrategyAnalysis(
            OwnedItemStrategy.BuyAll,
            isAvailable: false,
            ownedQuantity: 0,
            purchasedQuantity: 5,
            ownedOpportunityCost: null,
            purchasedCost: null,
            totalEconomicCost: null,
            reasons: []));
        Assert.Throws<ArgumentException>(() => new OwnedItemStrategyAnalysis(
            OwnedItemStrategy.BuyAll,
            isAvailable: false,
            ownedQuantity: 0,
            purchasedQuantity: 5,
            ownedOpportunityCost: Money.Zero,
            purchasedCost: new Money(500),
            totalEconomicCost: new Money(500),
            reasons: [OwnedItemOpportunityCostReason.MissingPurchaseMarketEvidence]));
    }

    private static OwnedItemOpportunityCostRequest CreateRequest(
        int requiredQuantity,
        IReadOnlyList<OwnedItemLot> ownedLots,
        IReadOnlyList<OrderBookLevel> buyLevels,
        IReadOnlyList<OrderBookLevel> sellLevels,
        OwnedItemValuationRoute valuationRoute = OwnedItemValuationRoute.ImmediateLiquidation) =>
        new(
            itemId: 900_001,
            requiredQuantity,
            ownedLots,
            new OwnedItemMarketEvidence(buyLevels, sellLevels),
            valuationRoute);

    private static OwnedItemStrategyAnalysis GetStrategy(
        OwnedItemOpportunityCostAnalysis analysis,
        OwnedItemStrategy strategy) =>
        Assert.Single(analysis.Strategies, candidate => candidate.Strategy == strategy);

    private static OrderBookLevel Level(int quantity, long copper) => new(quantity, new Money(copper));
}

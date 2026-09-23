using Gw2Tp.Analytics.OrderBooks;
using Gw2Tp.Application.Crafting;
using Gw2Tp.Domain.Finance;
using Xunit;

namespace Gw2Tp.Application.Tests;

public sealed class CraftingEconomicsCalculatorTests
{
    private readonly CraftingEconomicsCalculator calculator = new();

    [Fact]
    public void Values_owned_tradable_materials_at_fee_adjusted_realizable_opportunity_value()
    {
        var result = calculator.Calculate(Input(
            outputQuantity: 1,
            outputUnitPrice: 300,
            Ingredient(
                10,
                2,
                [new(2, CraftingOwnedMaterialState.Tradable)],
                Liquidation(2, 200))));

        var ingredient = Assert.Single(result.Ingredients);
        Assert.Equal(CraftingEconomicsState.Available, result.State);
        Assert.Equal(CraftingInputStrategy.Owned, ingredient.Strategy);
        Assert.Equal(new Money(170), ingredient.OwnedOpportunityCost);
        Assert.Equal(Money.Zero, ingredient.PurchasedAcquisitionCost);
        Assert.Equal(new Money(170), result.EconomicInputCost);
        Assert.Equal(new Money(85), result.NetProfit);
        Assert.Equal(new Money(185), result.TotalCost);
        Assert.Equal(new Money(200), result.BreakEvenOutputUnitPrice);
        Assert.NotNull(ingredient.OwnedLiquidationEvidence);
        Assert.True(result.IsProfitable);
        Assert.False(result.IsFeeRoundingExternallyVerified);
    }

    [Fact]
    public void Values_a_fully_instant_bought_ingredient_from_complete_execution_evidence()
    {
        var result = calculator.Calculate(Input(
            outputQuantity: 1,
            outputUnitPrice: 500,
            Ingredient(10, 2, [], alternatives: [InstantBuy(2, 400)])));

        var ingredient = Assert.Single(result.Ingredients);
        Assert.Equal(CraftingInputStrategy.Purchased, ingredient.Strategy);
        Assert.Equal(Money.Zero, ingredient.OwnedOpportunityCost);
        Assert.Equal(new Money(400), ingredient.PurchasedAcquisitionCost);
        Assert.Equal(CraftingAcquisitionStrategy.InstantBuy, ingredient.Acquisition!.Strategy);
        Assert.Equal(new Money(400), result.EconomicInputCost);
        Assert.Equal(new Money(25), result.NetProfit);
        Assert.True(result.IsProfitable);
    }

    [Fact]
    public void Combines_owned_opportunity_value_and_purchased_remainder_exactly()
    {
        var result = calculator.Calculate(Input(
            outputQuantity: 1,
            outputUnitPrice: 600,
            Ingredient(
                10,
                3,
                [new(1, CraftingOwnedMaterialState.Tradable)],
                Liquidation(1, 100),
                InstantBuy(2, 400))));

        var ingredient = Assert.Single(result.Ingredients);
        Assert.Equal(CraftingInputStrategy.Mixed, ingredient.Strategy);
        Assert.Equal(1, ingredient.OwnedTradableQuantity);
        Assert.Equal(2, ingredient.PurchasedQuantity);
        Assert.Equal(new Money(85), ingredient.OwnedOpportunityCost);
        Assert.Equal(new Money(400), ingredient.PurchasedAcquisitionCost);
        Assert.Equal(new Money(485), result.EconomicInputCost);
        Assert.Equal(new Money(25), result.NetProfit);
        Assert.Equal(new Money(572), result.BreakEvenOutputUnitPrice);
    }

    [Fact]
    public void Selects_the_cheapest_complete_direct_procurement_strategy_and_preserves_evidence()
    {
        var result = calculator.Calculate(Input(
            outputQuantity: 1,
            outputUnitPrice: 500,
            Ingredient(
                10,
                2,
                [],
                alternatives: [
                    InstantBuy(2, 400),
                    BuyOrder(2, 180)])));

        var ingredient = Assert.Single(result.Ingredients);
        Assert.Equal(CraftingAcquisitionStrategy.BuyOrder, ingredient.Acquisition!.Strategy);
        Assert.Equal(new Money(360), ingredient.PurchasedAcquisitionCost);
        Assert.Equal(new Money(360), ingredient.Acquisition.TotalCost);
        Assert.NotNull(ingredient.Acquisition.ExecutionEvidence);
        Assert.Equal(new Money(65), result.NetProfit);
    }

    [Fact]
    public void Accepts_a_precomputed_crafted_intermediate_as_a_composable_alternative()
    {
        var result = calculator.Calculate(Input(
            outputQuantity: 1,
            outputUnitPrice: 500,
            Ingredient(
                10,
                2,
                [],
                alternatives: [
                    InstantBuy(2, 400),
                    BuyOrder(2, 180),
                    CraftingAcquisitionAlternative.FromCraftedIntermediate(2, new Money(300))])));

        var ingredient = Assert.Single(result.Ingredients);
        Assert.Equal(CraftingAcquisitionStrategy.CraftedIntermediate, ingredient.Acquisition!.Strategy);
        Assert.Equal(new Money(300), ingredient.PurchasedAcquisitionCost);
        Assert.Null(ingredient.Acquisition.ExecutionEvidence);
        Assert.Equal(new Money(125), result.NetProfit);
    }

    [Fact]
    public void Uses_all_order_book_levels_instead_of_extrapolating_the_best_price()
    {
        var acquisition = OrderBookAcquisition(
            3,
            new OrderBookLevel(1, new Money(100)),
            new OrderBookLevel(2, new Money(150)));

        var result = calculator.Calculate(Input(
            outputQuantity: 1,
            outputUnitPrice: 500,
            Ingredient(10, 3, [], alternatives: [
                CraftingAcquisitionAlternative.FromExecution(CraftingAcquisitionStrategy.InstantBuy, acquisition)])));

        var ingredient = Assert.Single(result.Ingredients);
        Assert.Equal(new Money(400), ingredient.PurchasedAcquisitionCost);
        Assert.Equal(new Money(400), ingredient.Acquisition!.ExecutionEvidence!.TotalValue);
        Assert.Equal(new Money(25), result.NetProfit);
    }

    [Fact]
    public void Uses_all_order_book_levels_when_valuing_owned_opportunity_cost()
    {
        var liquidation = OrderBookLiquidation(
            3,
            new OrderBookLevel(1, new Money(200)),
            new OrderBookLevel(2, new Money(150)));

        var result = calculator.Calculate(Input(
            outputQuantity: 1,
            outputUnitPrice: 1_000,
            Ingredient(
                10,
                3,
                [new(3, CraftingOwnedMaterialState.Tradable)],
                liquidation)));

        var ingredient = Assert.Single(result.Ingredients);
        Assert.Equal(new Money(425), ingredient.OwnedOpportunityCost);
        Assert.Equal(new Money(425), result.NetProfit);
    }

    [Fact]
    public void Reports_insufficient_depth_for_an_underfilled_purchase()
    {
        var acquisition = OrderBookAcquisition(
            2,
            new OrderBookLevel(1, new Money(200)));

        var result = calculator.Calculate(Input(
            outputQuantity: 1,
            outputUnitPrice: 5_000,
            Ingredient(10, 2, [], alternatives: [
                CraftingAcquisitionAlternative.FromExecution(CraftingAcquisitionStrategy.InstantBuy, acquisition)])));

        var ingredient = Assert.Single(result.Ingredients);
        Assert.Equal(CraftingEconomicsState.Incomplete, result.State);
        Assert.Null(ingredient.PurchasedAcquisitionCost);
        Assert.Contains(CraftingEconomicsUncertainty.InsufficientMarketDepth, ingredient.Uncertainties);
        Assert.False(result.IsProfitable);
    }

    [Fact]
    public void Reports_insufficient_depth_for_an_underfilled_owned_liquidation()
    {
        var liquidation = OrderBookLiquidation(
            2,
            new OrderBookLevel(1, new Money(100)));

        var result = calculator.Calculate(Input(
            outputQuantity: 1,
            outputUnitPrice: 5_000,
            Ingredient(
                10,
                2,
                [new(2, CraftingOwnedMaterialState.Tradable)],
                liquidation)));

        var ingredient = Assert.Single(result.Ingredients);
        Assert.Equal(CraftingEconomicsState.Incomplete, result.State);
        Assert.Null(ingredient.OwnedOpportunityCost);
        Assert.Contains(CraftingEconomicsUncertainty.InsufficientMarketDepth, ingredient.Uncertainties);
        Assert.False(result.IsProfitable);
    }

    [Theory]
    [InlineData(CraftingOwnedMaterialState.Bound, CraftingEconomicsUncertainty.BoundOwnedInput)]
    [InlineData(CraftingOwnedMaterialState.Unknown, CraftingEconomicsUncertainty.UnknownOwnedInput)]
    public void Keeps_bound_or_unknown_owned_inputs_explicit_never_free(
        CraftingOwnedMaterialState materialState,
        CraftingEconomicsUncertainty expectedUncertainty)
    {
        var result = calculator.Calculate(Input(
            outputQuantity: 1,
            outputUnitPrice: 10_000,
            Ingredient(10, 1, [new(1, materialState)])));

        var ingredient = Assert.Single(result.Ingredients);
        Assert.Equal(CraftingEconomicsState.Incomplete, result.State);
        Assert.Equal(CraftingInputStrategy.Indeterminate, ingredient.Strategy);
        Assert.Null(ingredient.EconomicInputCost);
        Assert.Null(result.EconomicInputCost);
        Assert.Null(result.ModeledRoi);
        Assert.False(result.IsProfitable);
        Assert.Contains(expectedUncertainty, result.Uncertainties);
    }

    [Fact]
    public void Reports_missing_liquidation_and_acquisition_evidence_separately()
    {
        var owned = calculator.Calculate(Input(
            outputQuantity: 1,
            outputUnitPrice: 100,
            Ingredient(10, 1, [new(1, CraftingOwnedMaterialState.Tradable)])));
        var purchased = calculator.Calculate(Input(
            outputQuantity: 1,
            outputUnitPrice: 100,
            Ingredient(11, 1, [])));

        Assert.Contains(CraftingEconomicsUncertainty.OpportunityPriceUnavailable, owned.Uncertainties);
        Assert.Contains(CraftingEconomicsUncertainty.AcquisitionPriceUnavailable, purchased.Uncertainties);
        Assert.Null(owned.EconomicInputCost);
        Assert.Null(purchased.EconomicInputCost);
    }

    [Fact]
    public void Reports_an_unavailable_output_price_without_inferring_profit()
    {
        var result = calculator.Calculate(Input(
            outputQuantity: 1,
            outputUnitPrice: null,
            Ingredient(10, 1, [], alternatives: [InstantBuy(1, 100)])));

        Assert.Equal(CraftingEconomicsState.Incomplete, result.State);
        Assert.Equal(new Money(100), result.EconomicInputCost);
        Assert.Null(result.OutputSale);
        Assert.Null(result.ModeledRoi);
        Assert.False(result.IsProfitable);
        Assert.Contains(CraftingEconomicsUncertainty.OutputPriceUnavailable, result.Uncertainties);
    }

    [Fact]
    public void A_raw_sale_alternative_can_make_the_craft_unprofitable()
    {
        var result = calculator.Calculate(Input(
            outputQuantity: 1,
            outputUnitPrice: 99,
            Ingredient(
                10,
                1,
                [new(1, CraftingOwnedMaterialState.Tradable)],
                Liquidation(1, 100))));

        Assert.Equal(new Money(85), result.EconomicInputCost);
        Assert.Equal(new Money(-1), result.NetProfit);
        Assert.False(result.IsProfitable);
    }

    [Fact]
    public void Floors_a_loss_making_one_copper_liquidation_at_the_hold_alternative()
    {
        var result = calculator.Calculate(Input(
            outputQuantity: 1,
            outputUnitPrice: 1,
            Ingredient(
                10,
                1,
                [new(1, CraftingOwnedMaterialState.Tradable)],
                Liquidation(1, 1))));

        Assert.Equal(Money.Zero, result.EconomicInputCost);
        Assert.Equal(new Money(-1), result.NetProfit);
        Assert.Equal(new Money(2), result.BreakEvenOutputUnitPrice);
        Assert.False(result.IsProfitable);
    }

    [Fact]
    public void Applies_output_fees_once_to_total_output_value_and_preserves_rounding()
    {
        var result = calculator.Calculate(Input(
            outputQuantity: 3,
            outputUnitPrice: 101,
            Ingredient(10, 1, [], alternatives: [InstantBuy(1, 100)])));

        Assert.Equal(new Money(303), result.OutputSale!.GrossSaleValue);
        Assert.Equal(new Money(16), result.OutputSale.ListingFee);
        Assert.Equal(new Money(31), result.OutputSale.ExchangeFee);
        Assert.Equal(new Money(256), result.OutputSale.NetSaleProceeds);
        Assert.Equal(new Money(156), result.NetProfit);
        Assert.Equal(new Money(116), result.TotalCost);
        Assert.Equal(new Money(40), result.BreakEvenOutputUnitPrice);
        Assert.True(result.ModeledRoi!.Value.MeetsOrExceedsBasisPoints(10_000));
    }

    [Fact]
    public void Gross_output_price_cannot_make_a_craft_profitable_when_input_cost_is_unknown()
    {
        var result = calculator.Calculate(Input(
            outputQuantity: 1,
            outputUnitPrice: long.MaxValue,
            Ingredient(10, 1, [new(1, CraftingOwnedMaterialState.Bound)])));

        Assert.NotNull(result.OutputSale);
        Assert.Null(result.EconomicInputCost);
        Assert.Null(result.ModeledRoi);
        Assert.False(result.IsProfitable);
    }

    [Fact]
    public void Reports_arithmetic_overflow_instead_of_wrapping_financial_truth()
    {
        var result = calculator.Calculate(Input(
            outputQuantity: 1,
            outputUnitPrice: 100,
            Ingredient(10, 1, [], alternatives: [Intermediate(1, long.MaxValue)]),
            Ingredient(11, 1, [], alternatives: [Intermediate(1, long.MaxValue)]),
            Ingredient(12, 1, [], alternatives: [Intermediate(1, long.MaxValue)])));

        Assert.Equal(CraftingEconomicsState.Incomplete, result.State);
        Assert.False(result.IsProfitable);
        Assert.Equal([CraftingEconomicsUncertainty.ArithmeticOverflow], result.Uncertainties);
    }

    private static CraftingEconomicsInput Input(
        int outputQuantity,
        long? outputUnitPrice,
        params CraftingIngredientEconomicsInput[] ingredients) => new(
        OutputItemId: 99,
        OutputQuantity: outputQuantity,
        OutputUnitSalePrice: outputUnitPrice is null ? null : new Money(outputUnitPrice.Value),
        Ingredients: ingredients);

    private static CraftingIngredientEconomicsInput Ingredient(
        int itemId,
        int requiredQuantity,
        IReadOnlyList<CraftingOwnedMaterial> owned,
        CraftingExecutionEvidence? liquidation = null,
        params CraftingAcquisitionAlternative[] alternatives) => new(
        itemId,
        requiredQuantity,
        owned,
        liquidation,
        alternatives);

    private static CraftingAcquisitionAlternative InstantBuy(int quantity, long totalCost) =>
        CraftingAcquisitionAlternative.FromExecution(
            CraftingAcquisitionStrategy.InstantBuy,
            Evidence(quantity, quantity, totalCost));

    private static CraftingAcquisitionAlternative BuyOrder(int quantity, long unitPrice) =>
        CraftingAcquisitionAlternative.FromExecution(
            CraftingAcquisitionStrategy.BuyOrder,
            CraftingExecutionEvidence.ForBoundedBuyOrder(quantity, new Money(unitPrice)));

    private static CraftingAcquisitionAlternative Intermediate(int quantity, long totalCost) =>
        CraftingAcquisitionAlternative.FromCraftedIntermediate(quantity, new Money(totalCost));

    private static CraftingExecutionEvidence Liquidation(int quantity, long totalValue) => Evidence(quantity, quantity, totalValue);

    private static CraftingExecutionEvidence Evidence(int requestedQuantity, int filledQuantity, long totalValue) =>
        new(requestedQuantity, filledQuantity, new Money(totalValue));

    private static CraftingExecutionEvidence OrderBookAcquisition(int quantity, params OrderBookLevel[] levels) =>
        CraftingExecutionEvidence.FromOrderBookExecution(
            new OrderBookExecutionSimulator().SimulateAcquisition(levels, quantity));

    private static CraftingExecutionEvidence OrderBookLiquidation(int quantity, params OrderBookLevel[] levels) =>
        CraftingExecutionEvidence.FromOrderBookExecution(
            new OrderBookExecutionSimulator().SimulateLiquidation(levels, quantity));
}

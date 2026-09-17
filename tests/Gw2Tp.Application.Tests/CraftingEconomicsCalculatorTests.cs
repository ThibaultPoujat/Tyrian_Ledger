using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.MarketData;
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
            Ingredient(10, 2, [new(2, CraftingOwnedMaterialState.Tradable)], buy: 100, sell: 200)));

        var ingredient = Assert.Single(result.Ingredients);
        Assert.Equal(CraftingEconomicsState.Available, result.State);
        Assert.Equal(CraftingInputStrategy.Owned, ingredient.Strategy);
        Assert.Equal(new Money(170), ingredient.OwnedOpportunityCost);
        Assert.Equal(Money.Zero, ingredient.PurchasedAcquisitionCost);
        Assert.Equal(new Money(170), result.EconomicInputCost);
        Assert.Equal(new Money(85), result.NetProfit);
        Assert.Equal(new Money(185), result.TotalCost);
        Assert.Equal(new Money(200), result.BreakEvenOutputUnitPrice);
        Assert.True(result.IsProfitable);
        Assert.False(result.IsFeeRoundingExternallyVerified);
    }

    [Fact]
    public void Values_a_fully_purchased_ingredient_at_its_market_replacement_cost()
    {
        var result = calculator.Calculate(Input(
            outputQuantity: 1,
            outputUnitPrice: 500,
            Ingredient(10, 2, [], buy: 100, sell: 200)));

        var ingredient = Assert.Single(result.Ingredients);
        Assert.Equal(CraftingInputStrategy.Purchased, ingredient.Strategy);
        Assert.Equal(Money.Zero, ingredient.OwnedOpportunityCost);
        Assert.Equal(new Money(400), ingredient.PurchasedAcquisitionCost);
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
            Ingredient(10, 3, [new(1, CraftingOwnedMaterialState.Tradable)], buy: 100, sell: 200)));

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
            Ingredient(10, 1, [new(1, materialState)], buy: 100, sell: 200)));

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
    public void Reports_missing_opportunity_and_acquisition_prices_separately()
    {
        var owned = calculator.Calculate(Input(
            outputQuantity: 1,
            outputUnitPrice: 100,
            Ingredient(10, 1, [new(1, CraftingOwnedMaterialState.Tradable)], buy: null, sell: 200)));
        var purchased = calculator.Calculate(Input(
            outputQuantity: 1,
            outputUnitPrice: 100,
            Ingredient(11, 1, [], buy: 100, sell: null)));

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
            Ingredient(10, 1, [], buy: 100, sell: 100)));

        Assert.Equal(CraftingEconomicsState.Incomplete, result.State);
        Assert.Equal(new Money(100), result.EconomicInputCost);
        Assert.Null(result.OutputSale);
        Assert.Null(result.ModeledRoi);
        Assert.False(result.IsProfitable);
        Assert.Contains(CraftingEconomicsUncertainty.OutputPriceUnavailable, result.Uncertainties);
    }

    [Fact]
    public void Applies_output_fees_once_to_total_output_value_and_preserves_rounding()
    {
        var result = calculator.Calculate(Input(
            outputQuantity: 3,
            outputUnitPrice: 101,
            Ingredient(10, 1, [], buy: 100, sell: 100)));

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
            Ingredient(10, 1, [new(1, CraftingOwnedMaterialState.Bound)], buy: 100, sell: 100)));

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
            Ingredient(10, int.MaxValue, [], buy: 1, sell: int.MaxValue),
            Ingredient(11, int.MaxValue, [], buy: 1, sell: int.MaxValue),
            Ingredient(12, int.MaxValue, [], buy: 1, sell: int.MaxValue)));

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
        int? buy,
        int? sell) => new(
            itemId,
            requiredQuantity,
            owned,
            Quote(itemId, buy, sell));

    private static MarketPrice? Quote(int itemId, int? buy, int? sell) =>
        buy is null && sell is null ? null : new MarketPrice(
            itemId,
            IsWhitelisted: false,
            new MarketOrderSummary(buy is null ? 0 : 1, buy ?? 0),
            new MarketOrderSummary(sell is null ? 0 : 1, sell ?? 0));
}

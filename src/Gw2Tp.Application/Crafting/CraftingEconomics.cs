using Gw2Tp.Analytics.Finance;
using Gw2Tp.Application.Finance;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.Crafting;

/// <summary>
/// The known state of an account-owned ingredient allocation. Bound and unknown
/// quantities are deliberately not assigned a zero opportunity cost.
/// </summary>
public enum CraftingOwnedMaterialState
{
    Tradable = 1,
    Bound = 2,
    Unknown = 3,
}

public enum CraftingInputStrategy
{
    Owned = 1,
    Purchased = 2,
    Mixed = 3,
    Indeterminate = 4,
}

public enum CraftingEconomicsState
{
    Available = 1,
    Incomplete = 2,
}

/// <summary>
/// Explains why a craft is not a complete economic scenario. These are evidence
/// states, not a claim that an unknown or bound input is free.
/// </summary>
public enum CraftingEconomicsUncertainty
{
    BoundOwnedInput = 1,
    UnknownOwnedInput = 2,
    OpportunityPriceUnavailable = 3,
    AcquisitionPriceUnavailable = 4,
    OutputPriceUnavailable = 5,
    ArithmeticOverflow = 6,
    BreakEvenPriceOutOfRange = 7,
}

public sealed record CraftingOwnedMaterial(int Quantity, CraftingOwnedMaterialState State);

/// <summary>
/// Current public aggregate prices for one item. The typed market contract is
/// retained so the caller cannot bypass the application's market-data boundary.
/// </summary>
public sealed record CraftingIngredientEconomicsInput(
    int ItemId,
    int RequiredQuantity,
    IReadOnlyList<CraftingOwnedMaterial> OwnedMaterials,
    MarketPrice? MarketPrice);

public sealed record CraftingEconomicsInput(
    int OutputItemId,
    int OutputQuantity,
    Money? OutputUnitSalePrice,
    IReadOnlyList<CraftingIngredientEconomicsInput> Ingredients);

/// <summary>
/// The source quantities used for an ingredient. Costs are total copper costs
/// for the allocation rather than rounded per-unit approximations.
/// </summary>
public sealed record CraftingIngredientEconomics(
    int ItemId,
    int RequiredQuantity,
    int OwnedTradableQuantity,
    int BoundQuantity,
    int UnknownQuantity,
    int PurchasedQuantity,
    CraftingInputStrategy Strategy,
    Money? OwnedOpportunityCost,
    Money? PurchasedAcquisitionCost,
    Money? EconomicInputCost,
    IReadOnlyList<CraftingEconomicsUncertainty> Uncertainties);

/// <summary>
/// Completed-sale proceeds for the craft output before subtracting input cost.
/// Net profit remains nullable on <see cref="CraftingEconomics"/> until every
/// input has a known economic value.
/// </summary>
public sealed record CraftingOutputEconomics(
    Money GrossSaleValue,
    Money ListingFee,
    Money ExchangeFee,
    Money NetSaleProceeds);

/// <summary>
/// A deterministic, backend-authoritative completed-craft sale model. Any
/// missing cost evidence leaves profit, ROI, and break-even explicit rather
/// than silently treating a resource as free.
/// </summary>
public sealed record CraftingEconomics(
    CraftingEconomicsState State,
    int OutputItemId,
    int OutputQuantity,
    IReadOnlyList<CraftingIngredientEconomics> Ingredients,
    Money? EconomicInputCost,
    CraftingOutputEconomics? OutputSale,
    Money? NetProfit,
    Money? TotalCost,
    ExactRoi? ModeledRoi,
    Money? BreakEvenOutputUnitPrice,
    Money? BreakEvenGrossSaleValue,
    bool IsProfitable,
    bool IsFeeRoundingExternallyVerified,
    IReadOnlyList<CraftingEconomicsUncertainty> Uncertainties);

public interface ICraftingEconomicsCalculator
{
    CraftingEconomics Calculate(CraftingEconomicsInput input);
}

/// <summary>
/// Calculates owned-material opportunity cost and completed-sale economics.
/// An owned tradable stack is valued at the net proceeds of immediately selling
/// that whole allocation to the current best buy order; bought remainder uses
/// the current lowest sell price. Both output and opportunity-sale fees use the
/// single canonical fee policy.
/// </summary>
public sealed class CraftingEconomicsCalculator : ICraftingEconomicsCalculator
{
    private readonly FlipProfitCalculator profitCalculator;

    public CraftingEconomicsCalculator()
        : this(new FlipProfitCalculator(Gw2TradingPostFeePolicy.Create()))
    {
    }

    internal CraftingEconomicsCalculator(FlipProfitCalculator profitCalculator)
    {
        this.profitCalculator = profitCalculator ?? throw new ArgumentNullException(nameof(profitCalculator));
    }

    public CraftingEconomics Calculate(CraftingEconomicsInput input)
    {
        Validate(input);

        try
        {
            var ingredients = input.Ingredients.Select(CalculateIngredient).ToArray();
            var uncertainties = ingredients.SelectMany(ingredient => ingredient.Uncertainties).ToHashSet();
            var economicInputCost = uncertainties.Count == 0
                ? Sum(ingredients.Select(ingredient => ingredient.EconomicInputCost!.Value))
                : (Money?)null;
            var outputSale = CalculateOutputSale(input.OutputQuantity, input.OutputUnitSalePrice, uncertainties);

            Money? netProfit = null;
            Money? totalCost = null;
            ExactRoi? roi = null;
            Money? breakEvenUnitPrice = null;
            Money? breakEvenGrossSaleValue = null;
            var isProfitable = false;
            if (economicInputCost is not null && outputSale is not null)
            {
                netProfit = outputSale.NetSaleProceeds - economicInputCost.Value;
                totalCost = Gw2TradingPostFeePolicy.CalculateFullUpFrontCost(economicInputCost.Value, outputSale.ListingFee);
                roi = Gw2TradingPostFeePolicy.TryCalculateExactRoi(
                    netProfit.Value,
                    economicInputCost.Value,
                    outputSale.ListingFee);
                breakEvenUnitPrice = FindBreakEvenOutputUnitPrice(economicInputCost.Value, input.OutputQuantity);
                if (breakEvenUnitPrice is null)
                {
                    uncertainties.Add(CraftingEconomicsUncertainty.BreakEvenPriceOutOfRange);
                }
                else
                {
                    breakEvenGrossSaleValue = Multiply(breakEvenUnitPrice.Value, input.OutputQuantity);
                }

                isProfitable = uncertainties.Count == 0 && netProfit.Value.Copper > 0;
            }

            return new CraftingEconomics(
                uncertainties.Count == 0 ? CraftingEconomicsState.Available : CraftingEconomicsState.Incomplete,
                input.OutputItemId,
                input.OutputQuantity,
                ingredients,
                economicInputCost,
                outputSale,
                netProfit,
                totalCost,
                roi,
                breakEvenUnitPrice,
                breakEvenGrossSaleValue,
                isProfitable,
                Gw2TradingPostFeePolicy.IsFractionalCopperRoundingExternallyVerified,
                uncertainties.OrderBy(uncertainty => uncertainty).ToArray());
        }
        catch (OverflowException)
        {
            return new CraftingEconomics(
                CraftingEconomicsState.Incomplete,
                input.OutputItemId,
                input.OutputQuantity,
                [],
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                false,
                Gw2TradingPostFeePolicy.IsFractionalCopperRoundingExternallyVerified,
                [CraftingEconomicsUncertainty.ArithmeticOverflow]);
        }
    }

    private CraftingIngredientEconomics CalculateIngredient(CraftingIngredientEconomicsInput ingredient)
    {
        var remaining = ingredient.RequiredQuantity;
        var tradableQuantity = TakeQuantity(ingredient.OwnedMaterials, CraftingOwnedMaterialState.Tradable, ref remaining);
        var boundQuantity = TakeQuantity(ingredient.OwnedMaterials, CraftingOwnedMaterialState.Bound, ref remaining);
        var unknownQuantity = TakeQuantity(ingredient.OwnedMaterials, CraftingOwnedMaterialState.Unknown, ref remaining);
        var purchasedQuantity = remaining;
        var uncertainties = new HashSet<CraftingEconomicsUncertainty>();

        Money? ownedOpportunityCost = null;
        if (tradableQuantity > 0)
        {
            if (!TryGetBuyPrice(ingredient.MarketPrice, ingredient.ItemId, out var buyPrice))
            {
                uncertainties.Add(CraftingEconomicsUncertainty.OpportunityPriceUnavailable);
            }
            else
            {
                ownedOpportunityCost = profitCalculator.Calculate(Money.Zero, Multiply(buyPrice, tradableQuantity)).NetSaleProceeds;
            }
        }
        else
        {
            ownedOpportunityCost = Money.Zero;
        }

        if (boundQuantity > 0) uncertainties.Add(CraftingEconomicsUncertainty.BoundOwnedInput);
        if (unknownQuantity > 0) uncertainties.Add(CraftingEconomicsUncertainty.UnknownOwnedInput);

        Money? purchasedAcquisitionCost = null;
        if (purchasedQuantity > 0)
        {
            if (!TryGetSellPrice(ingredient.MarketPrice, ingredient.ItemId, out var sellPrice))
            {
                uncertainties.Add(CraftingEconomicsUncertainty.AcquisitionPriceUnavailable);
            }
            else
            {
                purchasedAcquisitionCost = Multiply(sellPrice, purchasedQuantity);
            }
        }
        else
        {
            purchasedAcquisitionCost = Money.Zero;
        }

        var economicInputCost = uncertainties.Count == 0
            ? ownedOpportunityCost!.Value + purchasedAcquisitionCost!.Value
            : (Money?)null;
        return new CraftingIngredientEconomics(
            ingredient.ItemId,
            ingredient.RequiredQuantity,
            tradableQuantity,
            boundQuantity,
            unknownQuantity,
            purchasedQuantity,
            Strategy(tradableQuantity, boundQuantity, unknownQuantity, purchasedQuantity),
            ownedOpportunityCost,
            purchasedAcquisitionCost,
            economicInputCost,
            uncertainties.OrderBy(uncertainty => uncertainty).ToArray());
    }

    private CraftingOutputEconomics? CalculateOutputSale(
        int outputQuantity,
        Money? outputUnitSalePrice,
        ISet<CraftingEconomicsUncertainty> uncertainties)
    {
        if (outputUnitSalePrice is not { Copper: > 0 })
        {
            uncertainties.Add(CraftingEconomicsUncertainty.OutputPriceUnavailable);
            return null;
        }

        var scenario = profitCalculator.Calculate(Money.Zero, Multiply(outputUnitSalePrice.Value, outputQuantity));
        return new CraftingOutputEconomics(
            scenario.GrossSaleValue,
            scenario.ListingFee,
            scenario.ExchangeFee,
            scenario.NetSaleProceeds);
    }

    private Money? FindBreakEvenOutputUnitPrice(Money economicInputCost, int outputQuantity)
        => Gw2TradingPostFeePolicy.TryCalculateBreakEvenUnitPrice(economicInputCost, outputQuantity);

    private static int TakeQuantity(
        IEnumerable<CraftingOwnedMaterial> materials,
        CraftingOwnedMaterialState state,
        ref int remaining)
    {
        var taken = 0;
        foreach (var material in materials.Where(material => material.State == state))
        {
            var quantity = Math.Min(remaining, material.Quantity);
            taken = checked(taken + quantity);
            remaining -= quantity;
            if (remaining == 0) break;
        }

        return taken;
    }

    private static CraftingInputStrategy Strategy(int tradable, int bound, int unknown, int purchased) =>
        bound > 0 || unknown > 0 ? CraftingInputStrategy.Indeterminate :
        tradable > 0 && purchased > 0 ? CraftingInputStrategy.Mixed :
        tradable > 0 ? CraftingInputStrategy.Owned :
        CraftingInputStrategy.Purchased;

    private static bool TryGetBuyPrice(MarketPrice? marketPrice, int itemId, out Money price)
    {
        if (marketPrice is { ItemId: var marketItemId, Buys: { Quantity: > 0, UnitPriceInCopper: > 0 } buys } && marketItemId == itemId)
        {
            price = new Money(buys.UnitPriceInCopper);
            return true;
        }

        price = default;
        return false;
    }

    private static bool TryGetSellPrice(MarketPrice? marketPrice, int itemId, out Money price)
    {
        if (marketPrice is { ItemId: var marketItemId, Sells: { Quantity: > 0, UnitPriceInCopper: > 0 } sells } && marketItemId == itemId)
        {
            price = new Money(sells.UnitPriceInCopper);
            return true;
        }

        price = default;
        return false;
    }

    private static Money Sum(IEnumerable<Money> values) => values.Aggregate(Money.Zero, static (sum, value) => sum + value);

    private static Money Multiply(Money unitPrice, int quantity) => new(checked(unitPrice.Copper * quantity));

    private static void Validate(CraftingEconomicsInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.OutputItemId <= 0) throw new ArgumentOutOfRangeException(nameof(input.OutputItemId));
        if (input.OutputQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(input.OutputQuantity));
        ArgumentNullException.ThrowIfNull(input.Ingredients);
        if (input.Ingredients.Count == 0 || input.Ingredients.Any(ingredient => ingredient is null))
        {
            throw new ArgumentException("Crafting economics requires at least one ingredient.", nameof(input));
        }

        if (input.Ingredients.Select(ingredient => ingredient.ItemId).Distinct().Count() != input.Ingredients.Count)
        {
            throw new ArgumentException("Crafting ingredients must have distinct item IDs.", nameof(input));
        }

        foreach (var ingredient in input.Ingredients)
        {
            if (ingredient.ItemId <= 0 || ingredient.RequiredQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(input));
            ArgumentNullException.ThrowIfNull(ingredient.OwnedMaterials);
            if (ingredient.OwnedMaterials.Any(material => material is null || material.Quantity <= 0 || !Enum.IsDefined(material.State)))
            {
                throw new ArgumentException("Owned ingredient quantities must be positive.", nameof(input));
            }
        }
    }
}

using Gw2Tp.Analytics.Finance;
using Gw2Tp.Analytics.OrderBooks;
using Gw2Tp.Application.Finance;
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

public enum CraftingAcquisitionStrategy
{
    InstantBuy = 1,
    BuyOrder = 2,
    CraftedIntermediate = 3,
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
    InsufficientMarketDepth = 8,
}

public sealed record CraftingOwnedMaterial(int Quantity, CraftingOwnedMaterialState State);

/// <summary>
/// Fully quantified execution evidence for an owned liquidation or a direct
/// acquisition. A top-of-book unit price without enough quantity is not valid
/// evidence for a multi-unit economic calculation.
/// </summary>
public sealed record CraftingExecutionEvidence(
    int RequestedQuantity,
    int FilledQuantity,
    Money TotalValue,
    OrderBookExecutionScenario? SourceScenario = null)
{
    public bool IsFullyFilled => RequestedQuantity > 0 && FilledQuantity == RequestedQuantity;

    public static CraftingExecutionEvidence FromOrderBookExecution(OrderBookExecutionScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        return new(
            scenario.RequestedQuantity,
            scenario.FilledQuantity,
            scenario.TotalValue,
            scenario);
    }

    public static CraftingExecutionEvidence ForBoundedBuyOrder(int quantity, Money unitPrice)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (unitPrice.Copper <= 0) throw new ArgumentOutOfRangeException(nameof(unitPrice));
        return new(quantity, quantity, new Money(checked(unitPrice.Copper * quantity)));
    }
}

/// <summary>
/// One direct acquisition alternative. Market alternatives must carry complete
/// execution evidence; a crafted intermediate carries the already-computed
/// economic cost supplied by the bounded recipe planner.
/// </summary>
public sealed record CraftingAcquisitionAlternative(
    CraftingAcquisitionStrategy Strategy,
    int Quantity,
    Money? TotalCost,
    CraftingExecutionEvidence? ExecutionEvidence)
{
    public static CraftingAcquisitionAlternative FromExecution(
        CraftingAcquisitionStrategy strategy,
        CraftingExecutionEvidence evidence)
    {
        if (strategy == CraftingAcquisitionStrategy.CraftedIntermediate)
        {
            throw new ArgumentException("A crafted intermediate must use FromCraftedIntermediate.", nameof(strategy));
        }

        ArgumentNullException.ThrowIfNull(evidence);
        return new(strategy, evidence.RequestedQuantity, null, evidence);
    }

    public static CraftingAcquisitionAlternative FromCraftedIntermediate(int quantity, Money totalCost)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (totalCost.Copper < 0) throw new ArgumentOutOfRangeException(nameof(totalCost));
        return new(CraftingAcquisitionStrategy.CraftedIntermediate, quantity, totalCost, null);
    }
}

public sealed record CraftingAcquisitionSelection(
    CraftingAcquisitionStrategy Strategy,
    int Quantity,
    Money TotalCost,
    CraftingExecutionEvidence? ExecutionEvidence);

public sealed record CraftingIngredientEconomicsInput(
    int ItemId,
    int RequiredQuantity,
    IReadOnlyList<CraftingOwnedMaterial> OwnedMaterials,
    CraftingExecutionEvidence? OwnedLiquidationEvidence,
    IReadOnlyList<CraftingAcquisitionAlternative> AcquisitionAlternatives);

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
    CraftingExecutionEvidence? OwnedLiquidationEvidence,
    Money? PurchasedAcquisitionCost,
    CraftingAcquisitionSelection? Acquisition,
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
/// An owned tradable stack is valued at the non-negative proceeds of a complete
/// supplied liquidation scenario. Missing input quantity is selected from the
/// cheapest complete instant-buy, buy-order, or crafted-intermediate
/// alternative. Both output and opportunity-sale fees use the single canonical
/// fee policy.
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
        CraftingExecutionEvidence? ownedLiquidationEvidence = null;
        if (tradableQuantity > 0)
        {
            if (!IsCompleteEvidence(ingredient.OwnedLiquidationEvidence, tradableQuantity, OrderBookExecutionKind.Liquidation))
            {
                AddEvidenceUncertainty(
                    ingredient.OwnedLiquidationEvidence,
                    CraftingEconomicsUncertainty.OpportunityPriceUnavailable,
                    uncertainties);
            }
            else
            {
                var evidence = ingredient.OwnedLiquidationEvidence!;
                ownedLiquidationEvidence = evidence;
                var liquidation = profitCalculator.Calculate(Money.Zero, evidence.TotalValue);
                ownedOpportunityCost = liquidation.NetSaleProceeds.Copper > 0
                    ? liquidation.NetSaleProceeds
                    : Money.Zero;
            }
        }
        else
        {
            ownedOpportunityCost = Money.Zero;
        }

        if (boundQuantity > 0) uncertainties.Add(CraftingEconomicsUncertainty.BoundOwnedInput);
        if (unknownQuantity > 0) uncertainties.Add(CraftingEconomicsUncertainty.UnknownOwnedInput);

        Money? purchasedAcquisitionCost = null;
        CraftingAcquisitionSelection? acquisition = null;
        if (purchasedQuantity > 0)
        {
            acquisition = SelectAcquisition(ingredient.AcquisitionAlternatives, purchasedQuantity, uncertainties);
            if (acquisition is not null)
            {
                purchasedAcquisitionCost = acquisition.TotalCost;
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
            ownedLiquidationEvidence,
            purchasedAcquisitionCost,
            acquisition,
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

    private static CraftingAcquisitionSelection? SelectAcquisition(
        IReadOnlyList<CraftingAcquisitionAlternative> alternatives,
        int requiredQuantity,
        ISet<CraftingEconomicsUncertainty> uncertainties)
    {
        var candidates = new List<CraftingAcquisitionSelection>();
        var hadInsufficientDepth = false;
        foreach (var alternative in alternatives.Where(alternative => alternative.Quantity == requiredQuantity))
        {
            if (alternative.Strategy is CraftingAcquisitionStrategy.InstantBuy or CraftingAcquisitionStrategy.BuyOrder)
            {
                var evidence = alternative.ExecutionEvidence;
                if (evidence is null ||
                    evidence.RequestedQuantity != requiredQuantity ||
                    evidence.SourceScenario is { Kind: not OrderBookExecutionKind.Acquisition })
                {
                    continue;
                }

                if (!evidence.IsFullyFilled)
                {
                    hadInsufficientDepth = true;
                    continue;
                }

                if (evidence.TotalValue.Copper <= 0)
                {
                    continue;
                }

                candidates.Add(new(
                    alternative.Strategy,
                    requiredQuantity,
                    evidence.TotalValue,
                    evidence));
                continue;
            }

            if (alternative.Strategy == CraftingAcquisitionStrategy.CraftedIntermediate &&
                alternative.ExecutionEvidence is null &&
                alternative.TotalCost is { } intermediateCost &&
                intermediateCost.Copper >= 0)
            {
                candidates.Add(new(
                    alternative.Strategy,
                    requiredQuantity,
                    intermediateCost,
                    null));
            }
        }

        if (candidates.Count == 0)
        {
            uncertainties.Add(hadInsufficientDepth
                ? CraftingEconomicsUncertainty.InsufficientMarketDepth
                : CraftingEconomicsUncertainty.AcquisitionPriceUnavailable);
            return null;
        }

        return candidates
            .OrderBy(candidate => candidate.TotalCost.Copper)
            .ThenBy(candidate => candidate.Strategy)
            .First();
    }

    private static bool IsCompleteEvidence(
        CraftingExecutionEvidence? evidence,
        int requiredQuantity,
        OrderBookExecutionKind expectedKind) =>
        evidence is not null &&
        evidence.RequestedQuantity == requiredQuantity &&
        evidence.IsFullyFilled &&
        evidence.TotalValue.Copper >= 0 &&
        (evidence.SourceScenario is null || evidence.SourceScenario.Kind == expectedKind);

    private static void AddEvidenceUncertainty(
        CraftingExecutionEvidence? evidence,
        CraftingEconomicsUncertainty unavailableUncertainty,
        ISet<CraftingEconomicsUncertainty> uncertainties)
    {
        if (evidence is not null && evidence.RequestedQuantity > evidence.FilledQuantity)
        {
            uncertainties.Add(CraftingEconomicsUncertainty.InsufficientMarketDepth);
        }
        else
        {
            uncertainties.Add(unavailableUncertainty);
        }
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

            ArgumentNullException.ThrowIfNull(ingredient.AcquisitionAlternatives);
            if (ingredient.AcquisitionAlternatives.Any(alternative => alternative is null))
            {
                throw new ArgumentException("Acquisition alternatives cannot be null.", nameof(input));
            }

            ValidateEvidence(ingredient.OwnedLiquidationEvidence, nameof(ingredient.OwnedLiquidationEvidence));
            foreach (var alternative in ingredient.AcquisitionAlternatives)
            {
                if (!Enum.IsDefined(alternative.Strategy) || alternative.Quantity <= 0)
                {
                    throw new ArgumentException("Acquisition alternatives must have valid positive quantities.", nameof(input));
                }

                if (alternative.TotalCost is { Copper: < 0 })
                {
                    throw new ArgumentException("Acquisition alternatives cannot have negative costs.", nameof(input));
                }

                ValidateEvidence(alternative.ExecutionEvidence, nameof(alternative.ExecutionEvidence));
            }
        }
    }

    private static void ValidateEvidence(CraftingExecutionEvidence? evidence, string parameterName)
    {
        if (evidence is null) return;
        if (evidence.RequestedQuantity <= 0 || evidence.FilledQuantity < 0 || evidence.FilledQuantity > evidence.RequestedQuantity || evidence.TotalValue.Copper < 0)
        {
            throw new ArgumentException("Execution evidence must have a valid non-negative filled value.", parameterName);
        }
    }
}

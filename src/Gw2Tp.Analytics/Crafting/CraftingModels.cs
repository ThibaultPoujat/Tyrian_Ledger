using Gw2Tp.Analytics.Finance;
using Gw2Tp.Analytics.OrderBooks;
using Gw2Tp.Analytics.OwnedItems;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Analytics.Crafting;

/// <summary>
/// Identifies whether the caller can establish a recipe's account-availability
/// requirement without treating absent external data as proof.
/// </summary>
public enum CraftingRecipeAvailability
{
    AlwaysAvailable = 0,
    RequiresAccountUnlock = 1,
    Unknown = 2,
}

/// <summary>
/// One item quantity consumed by a crafting recipe batch.
/// </summary>
public sealed record CraftingIngredient
{
    public CraftingIngredient(int itemId, int quantity)
    {
        if (itemId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemId), "An ingredient item ID must be positive.");
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "An ingredient quantity must be positive.");
        }

        ItemId = itemId;
        Quantity = quantity;
    }

    public int ItemId { get; }

    public int Quantity { get; }
}

/// <summary>
/// A discipline option that can satisfy a recipe when an active character has
/// the named discipline at or above the required rating.
/// </summary>
public sealed record CraftingDisciplineRequirement
{
    public CraftingDisciplineRequirement(string discipline, int minimumRating)
    {
        if (string.IsNullOrWhiteSpace(discipline))
        {
            throw new ArgumentException("A discipline name is required.", nameof(discipline));
        }

        if (minimumRating < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumRating), "A minimum rating cannot be negative.");
        }

        Discipline = discipline.Trim();
        MinimumRating = minimumRating;
    }

    public string Discipline { get; }

    public int MinimumRating { get; }
}

/// <summary>
/// A caller-supplied recipe definition. Discipline requirements are
/// alternatives: one active character discipline satisfying any entry is
/// sufficient for this deterministic feasibility model.
/// </summary>
public sealed record CraftingRecipe
{
    public CraftingRecipe(
        int recipeId,
        int outputItemId,
        int outputQuantity,
        IReadOnlyList<CraftingIngredient> ingredients,
        CraftingRecipeAvailability availability,
        IReadOnlyList<CraftingDisciplineRequirement> disciplineRequirements)
    {
        if (recipeId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recipeId), "A recipe ID must be positive.");
        }

        if (outputItemId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outputItemId), "An output item ID must be positive.");
        }

        if (outputQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outputQuantity), "An output quantity must be positive.");
        }

        ArgumentNullException.ThrowIfNull(ingredients);
        ArgumentNullException.ThrowIfNull(disciplineRequirements);

        if (!Enum.IsDefined(availability))
        {
            throw new ArgumentOutOfRangeException(nameof(availability), availability, "The recipe availability is unknown.");
        }

        if (ingredients.Any(ingredient => ingredient is null))
        {
            throw new ArgumentException("Ingredients cannot contain null values.", nameof(ingredients));
        }

        if (disciplineRequirements.Any(requirement => requirement is null))
        {
            throw new ArgumentException("Discipline requirements cannot contain null values.", nameof(disciplineRequirements));
        }

        if (ingredients.Select(ingredient => ingredient.ItemId).Distinct().Count() != ingredients.Count)
        {
            throw new ArgumentException("A recipe cannot contain duplicate ingredient items.", nameof(ingredients));
        }

        if (disciplineRequirements.Select(requirement => requirement.Discipline).Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
            disciplineRequirements.Count)
        {
            throw new ArgumentException("A recipe cannot contain duplicate discipline requirements.", nameof(disciplineRequirements));
        }

        RecipeId = recipeId;
        OutputItemId = outputItemId;
        OutputQuantity = outputQuantity;
        Ingredients = Array.AsReadOnly(ingredients.OrderBy(ingredient => ingredient.ItemId).ToArray());
        Availability = availability;
        DisciplineRequirements = Array.AsReadOnly(disciplineRequirements
            .OrderBy(requirement => requirement.Discipline, StringComparer.Ordinal)
            .ToArray());
    }

    public int RecipeId { get; }

    public int OutputItemId { get; }

    public int OutputQuantity { get; }

    public IReadOnlyList<CraftingIngredient> Ingredients { get; }

    public CraftingRecipeAvailability Availability { get; }

    public IReadOnlyList<CraftingDisciplineRequirement> DisciplineRequirements { get; }
}

/// <summary>
/// A discipline observed in a minimized account crafting snapshot.
/// </summary>
public sealed record CraftingDisciplineCapability
{
    public CraftingDisciplineCapability(string discipline, int rating, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(discipline))
        {
            throw new ArgumentException("A discipline name is required.", nameof(discipline));
        }

        if (rating < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rating), "A discipline rating cannot be negative.");
        }

        Discipline = discipline.Trim();
        Rating = rating;
        IsActive = isActive;
    }

    public string Discipline { get; }

    public int Rating { get; }

    public bool IsActive { get; }
}

/// <summary>
/// The verified portions of account crafting capability. A false verification
/// flag means the analyzer must report uncertainty instead of inferring facts.
/// </summary>
public sealed record CraftingAccountCapabilities
{
    public CraftingAccountCapabilities(
        bool hasVerifiedUnlockedRecipes,
        IReadOnlyList<int> unlockedRecipeIds,
        bool hasVerifiedDisciplines,
        IReadOnlyList<CraftingDisciplineCapability> disciplines)
    {
        ArgumentNullException.ThrowIfNull(unlockedRecipeIds);
        ArgumentNullException.ThrowIfNull(disciplines);

        if (unlockedRecipeIds.Any(recipeId => recipeId <= 0))
        {
            throw new ArgumentException("Unlocked recipe IDs must be positive.", nameof(unlockedRecipeIds));
        }

        if (disciplines.Any(discipline => discipline is null))
        {
            throw new ArgumentException("Disciplines cannot contain null values.", nameof(disciplines));
        }

        HasVerifiedUnlockedRecipes = hasVerifiedUnlockedRecipes;
        UnlockedRecipeIds = Array.AsReadOnly(unlockedRecipeIds.Distinct().Order().ToArray());
        HasVerifiedDisciplines = hasVerifiedDisciplines;
        Disciplines = Array.AsReadOnly(disciplines
            .OrderBy(discipline => discipline.Discipline, StringComparer.Ordinal)
            .ThenByDescending(discipline => discipline.Rating)
            .ThenByDescending(discipline => discipline.IsActive)
            .ToArray());
    }

    public bool HasVerifiedUnlockedRecipes { get; }

    public IReadOnlyList<int> UnlockedRecipeIds { get; }

    public bool HasVerifiedDisciplines { get; }

    public IReadOnlyList<CraftingDisciplineCapability> Disciplines { get; }
}

/// <summary>
/// Explicit upper bounds for deterministic crafting search.
/// </summary>
public sealed record CraftingSearchLimits
{
    public CraftingSearchLimits(int maximumDepth, int maximumCandidatePaths)
    {
        if (maximumDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDepth), "The maximum depth must be positive.");
        }

        if (maximumCandidatePaths <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCandidatePaths), "The maximum candidate paths must be positive.");
        }

        MaximumDepth = maximumDepth;
        MaximumCandidatePaths = maximumCandidatePaths;
    }

    public int MaximumDepth { get; }

    public int MaximumCandidatePaths { get; }
}

/// <summary>
/// Complete pure input for one bounded crafting-path analysis.
/// </summary>
public sealed record CraftingPathAnalysisRequest
{
    public CraftingPathAnalysisRequest(
        int targetItemId,
        int requestedQuantity,
        IReadOnlyList<CraftingRecipe> recipes,
        IReadOnlyDictionary<int, OwnedItemMarketEvidence> marketEvidenceByItem,
        IReadOnlyDictionary<int, IReadOnlyList<OwnedItemLot>> ownedLotsByItem,
        CraftingAccountCapabilities accountCapabilities,
        CraftingSearchLimits searchLimits)
    {
        if (targetItemId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetItemId), "A target item ID must be positive.");
        }

        if (requestedQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedQuantity), "A requested quantity must be positive.");
        }

        ArgumentNullException.ThrowIfNull(recipes);
        ArgumentNullException.ThrowIfNull(marketEvidenceByItem);
        ArgumentNullException.ThrowIfNull(ownedLotsByItem);
        ArgumentNullException.ThrowIfNull(accountCapabilities);
        ArgumentNullException.ThrowIfNull(searchLimits);

        if (recipes.Any(recipe => recipe is null))
        {
            throw new ArgumentException("Recipes cannot contain null values.", nameof(recipes));
        }

        if (recipes.Select(recipe => recipe.RecipeId).Distinct().Count() != recipes.Count)
        {
            throw new ArgumentException("Recipe IDs must be unique.", nameof(recipes));
        }

        ValidateMarketEvidence(marketEvidenceByItem);
        ValidateOwnedLots(ownedLotsByItem);

        TargetItemId = targetItemId;
        RequestedQuantity = requestedQuantity;
        Recipes = Array.AsReadOnly(recipes.OrderBy(recipe => recipe.RecipeId).ToArray());
        MarketEvidenceByItem = new Dictionary<int, OwnedItemMarketEvidence>(marketEvidenceByItem);
        OwnedLotsByItem = ownedLotsByItem.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<OwnedItemLot>)Array.AsReadOnly(entry.Value.ToArray()));
        AccountCapabilities = accountCapabilities;
        SearchLimits = searchLimits;
    }

    public int TargetItemId { get; }

    public int RequestedQuantity { get; }

    public IReadOnlyList<CraftingRecipe> Recipes { get; }

    public IReadOnlyDictionary<int, OwnedItemMarketEvidence> MarketEvidenceByItem { get; }

    public IReadOnlyDictionary<int, IReadOnlyList<OwnedItemLot>> OwnedLotsByItem { get; }

    public CraftingAccountCapabilities AccountCapabilities { get; }

    public CraftingSearchLimits SearchLimits { get; }

    private static void ValidateMarketEvidence(IReadOnlyDictionary<int, OwnedItemMarketEvidence> marketEvidenceByItem)
    {
        foreach (var entry in marketEvidenceByItem)
        {
            if (entry.Key <= 0 || entry.Value is null)
            {
                throw new ArgumentException("Market evidence must have positive item IDs and non-null values.", nameof(marketEvidenceByItem));
            }
        }
    }

    private static void ValidateOwnedLots(IReadOnlyDictionary<int, IReadOnlyList<OwnedItemLot>> ownedLotsByItem)
    {
        foreach (var entry in ownedLotsByItem)
        {
            if (entry.Key <= 0 || entry.Value is null || entry.Value.Any(lot => lot is null))
            {
                throw new ArgumentException("Owned lots must have positive item IDs and non-null values.", nameof(ownedLotsByItem));
            }
        }
    }
}

/// <summary>
/// Stable explanations for search, feasibility, and valuation outcomes.
/// </summary>
public enum CraftingAnalysisReason
{
    CycleDetected = 0,
    DepthLimitReached = 1,
    CandidateLimitReached = 2,
    RecipeUnavailable = 3,
    DisciplineRequirementNotMet = 4,
    RecipeAvailabilityUnknown = 5,
    DisciplineAvailabilityUnknown = 6,
    MissingIngredientMarketEvidence = 7,
    IngredientCostUnavailable = 8,
    MissingOutputMarketEvidence = 9,
    InsufficientOutputLiquidationDepth = 10,
}

public enum CraftingPathStepKind
{
    Purchase = 0,
    Craft = 1,
}

/// <summary>
/// One nested, explainable acquisition step. Crafted steps carry actual whole
/// batches, allowing callers to expose batch surplus.
/// </summary>
public sealed record CraftingPathStep
{
    public CraftingPathStep(
        CraftingPathStepKind kind,
        int itemId,
        int requiredQuantity,
        int producedQuantity,
        int batchCount,
        int? recipeId,
        IReadOnlyList<CraftingPathStep> ingredients,
        IReadOnlyList<CraftingAnalysisReason> reasons)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "The path step kind is unknown.");
        }

        if (itemId <= 0 || requiredQuantity <= 0 || producedQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemId), "Path item and quantities must be positive.");
        }

        ArgumentNullException.ThrowIfNull(ingredients);
        ArgumentNullException.ThrowIfNull(reasons);

        if (ingredients.Any(ingredient => ingredient is null) || reasons.Any(reason => !Enum.IsDefined(reason)))
        {
            throw new ArgumentException("Path details contain an invalid value.");
        }

        if (kind == CraftingPathStepKind.Purchase && (recipeId is not null || batchCount != 0 || ingredients.Count != 0 || producedQuantity != requiredQuantity))
        {
            throw new ArgumentException("A purchase step cannot contain crafting details.");
        }

        if (kind == CraftingPathStepKind.Craft && (recipeId is null or <= 0 || batchCount <= 0 || producedQuantity < requiredQuantity))
        {
            throw new ArgumentException("A crafting step must contain complete whole-batch details.");
        }

        Kind = kind;
        ItemId = itemId;
        RequiredQuantity = requiredQuantity;
        ProducedQuantity = producedQuantity;
        BatchCount = batchCount;
        RecipeId = recipeId;
        Ingredients = Array.AsReadOnly(ingredients.ToArray());
        Reasons = Array.AsReadOnly(reasons.Distinct().Order().ToArray());
    }

    public CraftingPathStepKind Kind { get; }

    public int ItemId { get; }

    public int RequiredQuantity { get; }

    public int ProducedQuantity { get; }

    public int BatchCount { get; }

    public int? RecipeId { get; }

    public IReadOnlyList<CraftingPathStep> Ingredients { get; }

    public IReadOnlyList<CraftingAnalysisReason> Reasons { get; }
}

public sealed record CraftingIngredientCostAnalysis(
    int ItemId,
    int RequiredQuantity,
    OwnedItemOpportunityCostAnalysis? OpportunityCost,
    OwnedItemStrategyAnalysis? SelectedStrategy);

public sealed record CraftingOutputSaleAnalysis(
    int ItemId,
    int Quantity,
    OrderBookExecutionScenario? Liquidation,
    FlipProfitScenario? Sale);

/// <summary>
/// Aggregate result for a fully-valued crafting candidate.
/// </summary>
public sealed record CraftingProfitScenario(
    Money AcquisitionCost,
    Money GrossSaleValue,
    Money ListingFee,
    Money ExchangeFee,
    Money NetSaleProceeds,
    Money NetProfit);

/// <summary>
/// One root crafting path and its economic evidence.
/// </summary>
public sealed record CraftingPathCandidate
{
    public CraftingPathCandidate(
        int rootRecipeId,
        CraftingPathStep rootStep,
        IReadOnlyList<CraftingIngredientCostAnalysis> ingredientCosts,
        IReadOnlyList<CraftingOutputSaleAnalysis> outputSales,
        CraftingProfitScenario? profit,
        IReadOnlyList<CraftingAnalysisReason> reasons)
    {
        if (rootRecipeId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rootRecipeId), "A root recipe ID must be positive.");
        }

        ArgumentNullException.ThrowIfNull(rootStep);
        ArgumentNullException.ThrowIfNull(ingredientCosts);
        ArgumentNullException.ThrowIfNull(outputSales);
        ArgumentNullException.ThrowIfNull(reasons);

        if (rootStep.Kind != CraftingPathStepKind.Craft || rootStep.RecipeId != rootRecipeId ||
            ingredientCosts.Any(cost => cost is null) || outputSales.Any(sale => sale is null) ||
            reasons.Any(reason => !Enum.IsDefined(reason)))
        {
            throw new ArgumentException("A crafting candidate contains inconsistent path details.");
        }

        RootRecipeId = rootRecipeId;
        RootStep = rootStep;
        IngredientCosts = Array.AsReadOnly(ingredientCosts.OrderBy(cost => cost.ItemId).ToArray());
        OutputSales = Array.AsReadOnly(outputSales.OrderBy(sale => sale.ItemId).ToArray());
        Profit = profit;
        Reasons = Array.AsReadOnly(reasons.Distinct().Order().ToArray());
    }

    public int RootRecipeId { get; }

    public CraftingPathStep RootStep { get; }

    public IReadOnlyList<CraftingIngredientCostAnalysis> IngredientCosts { get; }

    public IReadOnlyList<CraftingOutputSaleAnalysis> OutputSales { get; }

    public CraftingProfitScenario? Profit { get; }

    public IReadOnlyList<CraftingAnalysisReason> Reasons { get; }

    public bool IsProfitable => Profit is { NetProfit.Copper: > 0 };
}

/// <summary>
/// Bounded-search diagnostics, including explicit truncation and memoization evidence.
/// </summary>
public sealed record CraftingSearchDiagnostics(
    int ExpandedRecipeCandidates,
    int MemoizedSubproblemHits,
    bool IsTruncated,
    IReadOnlyList<CraftingAnalysisReason> Reasons);

/// <summary>
/// Complete deterministic result for the requested crafted item.
/// </summary>
public sealed record CraftingPathAnalysis(
    int TargetItemId,
    int RequestedQuantity,
    IReadOnlyList<CraftingPathCandidate> Candidates,
    CraftingSearchDiagnostics Diagnostics);

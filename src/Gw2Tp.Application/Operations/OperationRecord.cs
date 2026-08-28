using Gw2Tp.Analytics.Crafting;
using Gw2Tp.Analytics.FlipOpportunities;
using Gw2Tp.Analytics.OwnedItems;
using Gw2Tp.Analytics.Reconciliation;

namespace Gw2Tp.Application.Operations;

/// <summary>
/// The locally persisted state of a user-saved operation.
/// </summary>
public sealed record OperationRecord
{
    public const int MaximumVersionIdentifierLength = 128;

    public OperationRecord(
        Guid id,
        DateTimeOffset createdAtUtc,
        DateTimeOffset lastModifiedAtUtc,
        OperationStatus status,
        string calculationVersionId,
        string configurationVersionId,
        OperationScenarioSnapshot scenario,
        OperationActualOutcome? actualOutcome = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An operation ID is required.", nameof(id));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "The operation status is unknown.");
        }

        CalculationVersionId = ValidateVersionIdentifier(calculationVersionId, nameof(calculationVersionId));
        ConfigurationVersionId = ValidateVersionIdentifier(configurationVersionId, nameof(configurationVersionId));
        Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
        ActualOutcome = actualOutcome;
        Id = id;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        LastModifiedAtUtc = lastModifiedAtUtc.ToUniversalTime();

        if (LastModifiedAtUtc < CreatedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lastModifiedAtUtc),
                "The last-modified timestamp cannot precede the creation timestamp.");
        }

        Status = status;
    }

    public Guid Id { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset LastModifiedAtUtc { get; }

    public OperationStatus Status { get; }

    public string CalculationVersionId { get; }

    public string ConfigurationVersionId { get; }

    public OperationScenarioSnapshot Scenario { get; }

    /// <summary>
    /// Optional locally recorded actual acquisition and sale values. Modeled scenario values
    /// are intentionally not used as a substitute when this evidence is absent.
    /// </summary>
    public OperationActualOutcome? ActualOutcome { get; }

    private static string ValidateVersionIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A version identifier is required.", parameterName);
        }

        var normalized = value.Trim();
        if (normalized.Length > MaximumVersionIdentifierLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"A version identifier cannot exceed {MaximumVersionIdentifierLength} characters.");
        }

        return normalized;
    }
}

/// <summary>
/// The manual lifecycle state for a locally saved operation.
/// </summary>
public enum OperationStatus
{
    Planned = 0,
    InProgress = 1,
    Completed = 2,
    Cancelled = 3,
}

/// <summary>
/// The typed calculation context retained with one saved operation.
/// </summary>
public abstract record OperationScenarioSnapshot
{
    protected OperationScenarioSnapshot(int itemId, int requestedQuantity, DateTimeOffset analyzedAtUtc)
    {
        if (itemId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemId), "An item ID must be positive.");
        }

        if (requestedQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedQuantity), "A requested quantity must be positive.");
        }

        ItemId = itemId;
        RequestedQuantity = requestedQuantity;
        AnalyzedAtUtc = analyzedAtUtc.ToUniversalTime();
    }

    public int ItemId { get; }

    public int RequestedQuantity { get; }

    public DateTimeOffset AnalyzedAtUtc { get; }

    public abstract OperationScenarioKind Kind { get; }
}

public enum OperationScenarioKind
{
    MarketFlip = 0,
    Crafting = 1,
}

public sealed record MarketFlipOperationScenarioSnapshot : OperationScenarioSnapshot
{
    public MarketFlipOperationScenarioSnapshot(
        int itemId,
        int requestedQuantity,
        DateTimeOffset analyzedAtUtc,
        OperationMarketFreshnessSnapshot? freshness,
        OperationFeePolicySnapshot feePolicy,
        OperationFlipConstraintsSnapshot constraints,
        MarketFlipAnalysisSnapshot analysis,
        MarketFlipScoreSnapshot? score)
        : base(itemId, requestedQuantity, analyzedAtUtc)
    {
        Freshness = freshness;
        FeePolicy = feePolicy ?? throw new ArgumentNullException(nameof(feePolicy));
        Constraints = constraints ?? throw new ArgumentNullException(nameof(constraints));
        Analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
        Score = score;
    }

    public override OperationScenarioKind Kind => OperationScenarioKind.MarketFlip;

    public OperationMarketFreshnessSnapshot? Freshness { get; }

    public OperationFeePolicySnapshot FeePolicy { get; }

    public OperationFlipConstraintsSnapshot Constraints { get; }

    public MarketFlipAnalysisSnapshot Analysis { get; }

    public MarketFlipScoreSnapshot? Score { get; }
}

public sealed record CraftingOperationScenarioSnapshot : OperationScenarioSnapshot
{
    public CraftingOperationScenarioSnapshot(
        int itemId,
        int requestedQuantity,
        DateTimeOffset analyzedAtUtc,
        OperationFeePolicySnapshot feePolicy,
        CraftingSearchLimitsSnapshot searchLimits,
        CraftingPathStepSnapshot selectedPath,
        IReadOnlyList<CraftingIngredientCostSnapshot> ingredientCosts,
        IReadOnlyList<CraftingOutputSaleSnapshot> outputSales,
        OperationFinancialSnapshot? modeledFinancials,
        IReadOnlyList<CraftingAnalysisReason> reasons,
        CraftingSearchDiagnosticsSnapshot diagnostics)
        : base(itemId, requestedQuantity, analyzedAtUtc)
    {
        FeePolicy = feePolicy ?? throw new ArgumentNullException(nameof(feePolicy));
        SearchLimits = searchLimits ?? throw new ArgumentNullException(nameof(searchLimits));
        SelectedPath = selectedPath ?? throw new ArgumentNullException(nameof(selectedPath));
        ArgumentNullException.ThrowIfNull(ingredientCosts);
        ArgumentNullException.ThrowIfNull(outputSales);
        ArgumentNullException.ThrowIfNull(reasons);
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

        if (SelectedPath.Kind != CraftingPathStepKind.Craft || SelectedPath.ItemId != itemId)
        {
            throw new ArgumentException("The selected crafting path must craft the requested target item.", nameof(selectedPath));
        }

        if (ingredientCosts.Any(cost => cost is null) || outputSales.Any(sale => sale is null) ||
            reasons.Any(reason => !Enum.IsDefined(reason)))
        {
            throw new ArgumentException("Crafting snapshot details contain an invalid value.");
        }

        IngredientCosts = Array.AsReadOnly(ingredientCosts.OrderBy(cost => cost.ItemId).ToArray());
        OutputSales = Array.AsReadOnly(outputSales.OrderBy(sale => sale.ItemId).ToArray());
        ModeledFinancials = modeledFinancials;
        Reasons = Array.AsReadOnly(reasons.Distinct().Order().ToArray());
    }

    public override OperationScenarioKind Kind => OperationScenarioKind.Crafting;

    public OperationFeePolicySnapshot FeePolicy { get; }

    public CraftingSearchLimitsSnapshot SearchLimits { get; }

    public CraftingPathStepSnapshot SelectedPath { get; }

    public IReadOnlyList<CraftingIngredientCostSnapshot> IngredientCosts { get; }

    public IReadOnlyList<CraftingOutputSaleSnapshot> OutputSales { get; }

    public OperationFinancialSnapshot? ModeledFinancials { get; }

    public IReadOnlyList<CraftingAnalysisReason> Reasons { get; }

    public CraftingSearchDiagnosticsSnapshot Diagnostics { get; }
}

public sealed record OperationMarketFreshnessSnapshot
{
    public OperationMarketFreshnessSnapshot(DateTimeOffset capturedAtUtc, DateTimeOffset expiresAtUtc)
    {
        CapturedAtUtc = capturedAtUtc.ToUniversalTime();
        ExpiresAtUtc = expiresAtUtc.ToUniversalTime();
        if (ExpiresAtUtc < CapturedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc), "Market-data expiry cannot precede capture time.");
        }
    }

    public DateTimeOffset CapturedAtUtc { get; }

    public DateTimeOffset ExpiresAtUtc { get; }
}

public sealed record OperationFeePolicySnapshot
{
    public OperationFeePolicySnapshot(OperationFeeRuleSnapshot listingFee, OperationFeeRuleSnapshot exchangeFee)
    {
        ListingFee = listingFee ?? throw new ArgumentNullException(nameof(listingFee));
        ExchangeFee = exchangeFee ?? throw new ArgumentNullException(nameof(exchangeFee));
    }

    public OperationFeeRuleSnapshot ListingFee { get; }

    public OperationFeeRuleSnapshot ExchangeFee { get; }
}

public sealed record OperationFeeRuleSnapshot
{
    public OperationFeeRuleSnapshot(int basisPoints, OperationFeeRounding rounding)
    {
        if (basisPoints is < 0 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(basisPoints), "Fee basis points must be between zero and 10,000.");
        }

        if (!Enum.IsDefined(rounding))
        {
            throw new ArgumentOutOfRangeException(nameof(rounding), rounding, "The fee rounding mode is unknown.");
        }

        BasisPoints = basisPoints;
        Rounding = rounding;
    }

    public int BasisPoints { get; }

    public OperationFeeRounding Rounding { get; }
}

public enum OperationFeeRounding
{
    Down = 0,
    Up = 1,
}

public sealed record OperationFlipConstraintsSnapshot
{
    public OperationFlipConstraintsSnapshot(long minimumNetProfitCopper, long? maximumCapitalRequiredCopper)
    {
        if (minimumNetProfitCopper < 0 || maximumCapitalRequiredCopper < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumNetProfitCopper), "Flip constraint copper values cannot be negative.");
        }

        MinimumNetProfitCopper = minimumNetProfitCopper;
        MaximumCapitalRequiredCopper = maximumCapitalRequiredCopper;
    }

    public long MinimumNetProfitCopper { get; }

    public long? MaximumCapitalRequiredCopper { get; }
}

public sealed record MarketFlipAnalysisSnapshot
{
    public MarketFlipAnalysisSnapshot(
        MarketFlipOperationUsability usability,
        MarketFlipOperationConfidence confidence,
        bool meetsFinancialConstraints,
        bool isPartialData,
        IReadOnlyList<FlipAnalysisReason> reasons,
        OperationExecutionSnapshot? acquisition,
        OperationExecutionSnapshot? liquidation,
        OperationFinancialSnapshot? financials,
        OperationLiquiditySnapshot? liquidity,
        OperationReturnOnInvestmentSnapshot? returnOnInvestment,
        long? capitalRequiredCopper)
    {
        if (!Enum.IsDefined(usability) || !Enum.IsDefined(confidence))
        {
            throw new ArgumentOutOfRangeException(nameof(usability), "The market-flip analysis contains an unknown enum value.");
        }

        ArgumentNullException.ThrowIfNull(reasons);
        if (reasons.Any(reason => !Enum.IsDefined(reason)))
        {
            throw new ArgumentException("Market-flip reasons contain an unknown value.", nameof(reasons));
        }

        if (capitalRequiredCopper < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capitalRequiredCopper), "Capital required cannot be negative.");
        }

        Usability = usability;
        Confidence = confidence;
        MeetsFinancialConstraints = meetsFinancialConstraints;
        IsPartialData = isPartialData;
        Reasons = Array.AsReadOnly(reasons.Distinct().Order().ToArray());
        Acquisition = acquisition;
        Liquidation = liquidation;
        Financials = financials;
        Liquidity = liquidity;
        ReturnOnInvestment = returnOnInvestment;
        CapitalRequiredCopper = capitalRequiredCopper;
    }

    public MarketFlipOperationUsability Usability { get; }

    public MarketFlipOperationConfidence Confidence { get; }

    public bool MeetsFinancialConstraints { get; }

    public bool IsPartialData { get; }

    public IReadOnlyList<FlipAnalysisReason> Reasons { get; }

    public OperationExecutionSnapshot? Acquisition { get; }

    public OperationExecutionSnapshot? Liquidation { get; }

    public OperationFinancialSnapshot? Financials { get; }

    public OperationLiquiditySnapshot? Liquidity { get; }

    public OperationReturnOnInvestmentSnapshot? ReturnOnInvestment { get; }

    public long? CapitalRequiredCopper { get; }
}

public enum MarketFlipOperationUsability
{
    Usable = 0,
    Unusable = 1,
}

public enum MarketFlipOperationConfidence
{
    Normal = 0,
    Reduced = 1,
}

public sealed record OperationExecutionSnapshot
{
    public OperationExecutionSnapshot(
        OperationExecutionKind kind,
        int requestedQuantity,
        int filledQuantity,
        int remainingQuantity,
        long totalValueCopper,
        long priceImpactCopper,
        IReadOnlyList<OperationExecutionFillSnapshot> fills)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "The execution kind is unknown.");
        }

        if (requestedQuantity <= 0 || filledQuantity < 0 || remainingQuantity < 0 ||
            filledQuantity + remainingQuantity != requestedQuantity || totalValueCopper < 0 || priceImpactCopper < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedQuantity), "Execution quantities and copper values are invalid.");
        }

        ArgumentNullException.ThrowIfNull(fills);
        if (fills.Any(fill => fill is null) || fills.Sum(fill => (long)fill.Quantity) != filledQuantity)
        {
            throw new ArgumentException("Execution fills must exactly cover the filled quantity.", nameof(fills));
        }

        Kind = kind;
        RequestedQuantity = requestedQuantity;
        FilledQuantity = filledQuantity;
        RemainingQuantity = remainingQuantity;
        TotalValueCopper = totalValueCopper;
        PriceImpactCopper = priceImpactCopper;
        Fills = Array.AsReadOnly(fills.ToArray());
    }

    public OperationExecutionKind Kind { get; }

    public int RequestedQuantity { get; }

    public int FilledQuantity { get; }

    public int RemainingQuantity { get; }

    public long TotalValueCopper { get; }

    public long PriceImpactCopper { get; }

    public bool IsFullyFilled => RemainingQuantity == 0;

    public IReadOnlyList<OperationExecutionFillSnapshot> Fills { get; }
}

public enum OperationExecutionKind
{
    Acquisition = 0,
    Liquidation = 1,
}

public sealed record OperationExecutionFillSnapshot
{
    public OperationExecutionFillSnapshot(int quantity, long unitPriceCopper, long totalValueCopper)
    {
        if (quantity <= 0 || unitPriceCopper < 0 || totalValueCopper < 0 || totalValueCopper != checked(quantity * unitPriceCopper))
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Execution fill values are invalid.");
        }

        Quantity = quantity;
        UnitPriceCopper = unitPriceCopper;
        TotalValueCopper = totalValueCopper;
    }

    public int Quantity { get; }

    public long UnitPriceCopper { get; }

    public long TotalValueCopper { get; }
}

public sealed record OperationFinancialSnapshot
{
    public OperationFinancialSnapshot(
        long acquisitionCostCopper,
        long grossSaleValueCopper,
        long listingFeeCopper,
        long exchangeFeeCopper,
        long netSaleProceedsCopper,
        long netProfitCopper)
    {
        if (acquisitionCostCopper < 0 || grossSaleValueCopper < 0 || listingFeeCopper < 0 || exchangeFeeCopper < 0 ||
            netSaleProceedsCopper != grossSaleValueCopper - listingFeeCopper - exchangeFeeCopper ||
            netProfitCopper != netSaleProceedsCopper - acquisitionCostCopper)
        {
            throw new ArgumentException("Modeled financial values are inconsistent.");
        }

        AcquisitionCostCopper = acquisitionCostCopper;
        GrossSaleValueCopper = grossSaleValueCopper;
        ListingFeeCopper = listingFeeCopper;
        ExchangeFeeCopper = exchangeFeeCopper;
        NetSaleProceedsCopper = netSaleProceedsCopper;
        NetProfitCopper = netProfitCopper;
    }

    public long AcquisitionCostCopper { get; }

    public long GrossSaleValueCopper { get; }

    public long ListingFeeCopper { get; }

    public long ExchangeFeeCopper { get; }

    public long NetSaleProceedsCopper { get; }

    public long NetProfitCopper { get; }
}

public sealed record OperationLiquiditySnapshot
{
    public OperationLiquiditySnapshot(
        int requestedQuantity,
        int acquisitionFilledQuantity,
        int liquidationFilledQuantity,
        long acquisitionPriceImpactCopper,
        long liquidationPriceImpactCopper)
    {
        if (requestedQuantity <= 0 || acquisitionFilledQuantity < 0 || acquisitionFilledQuantity > requestedQuantity ||
            liquidationFilledQuantity < 0 || liquidationFilledQuantity > requestedQuantity || acquisitionPriceImpactCopper < 0 || liquidationPriceImpactCopper < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedQuantity), "Liquidity values are invalid.");
        }

        RequestedQuantity = requestedQuantity;
        AcquisitionFilledQuantity = acquisitionFilledQuantity;
        LiquidationFilledQuantity = liquidationFilledQuantity;
        AcquisitionPriceImpactCopper = acquisitionPriceImpactCopper;
        LiquidationPriceImpactCopper = liquidationPriceImpactCopper;
    }

    public int RequestedQuantity { get; }

    public int AcquisitionFilledQuantity { get; }

    public int LiquidationFilledQuantity { get; }

    public long AcquisitionPriceImpactCopper { get; }

    public long LiquidationPriceImpactCopper { get; }
}

public sealed record OperationReturnOnInvestmentSnapshot
{
    public OperationReturnOnInvestmentSnapshot(long netProfitCopper, long capitalRequiredCopper)
    {
        if (capitalRequiredCopper <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capitalRequiredCopper), "ROI capital required must be positive.");
        }

        NetProfitCopper = netProfitCopper;
        CapitalRequiredCopper = capitalRequiredCopper;
    }

    public long NetProfitCopper { get; }

    public long CapitalRequiredCopper { get; }
}

public sealed record MarketFlipScoreSnapshot
{
    public MarketFlipScoreSnapshot(
        int scoreBasisPoints,
        long targetNetProfitCopper,
        int targetReturnOnInvestmentBasisPoints,
        int acceptablePriceImpactBasisPoints,
        MarketFlipScoringWeightsSnapshot weights,
        int freshDataScoreBasisPoints,
        int staleDataScoreBasisPoints,
        int normalConfidenceRiskScoreBasisPoints,
        int reducedConfidenceRiskScoreBasisPoints,
        int twoLegFlipComplexityScoreBasisPoints,
        IReadOnlyList<MarketFlipScoreContributionSnapshot> contributions)
    {
        if (scoreBasisPoints is < 0 or > 10_000 || targetNetProfitCopper <= 0 || targetReturnOnInvestmentBasisPoints <= 0 ||
            acceptablePriceImpactBasisPoints <= 0 || freshDataScoreBasisPoints is < 0 or > 10_000 ||
            staleDataScoreBasisPoints is < 0 or > 10_000 || normalConfidenceRiskScoreBasisPoints is < 0 or > 10_000 ||
            reducedConfidenceRiskScoreBasisPoints is < 0 or > 10_000 || twoLegFlipComplexityScoreBasisPoints is < 0 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(scoreBasisPoints), "Scoring values are invalid.");
        }

        Weights = weights ?? throw new ArgumentNullException(nameof(weights));
        ArgumentNullException.ThrowIfNull(contributions);
        if (contributions.Any(contribution => contribution is null))
        {
            throw new ArgumentException("Score contributions cannot contain null values.", nameof(contributions));
        }

        ScoreBasisPoints = scoreBasisPoints;
        TargetNetProfitCopper = targetNetProfitCopper;
        TargetReturnOnInvestmentBasisPoints = targetReturnOnInvestmentBasisPoints;
        AcceptablePriceImpactBasisPoints = acceptablePriceImpactBasisPoints;
        FreshDataScoreBasisPoints = freshDataScoreBasisPoints;
        StaleDataScoreBasisPoints = staleDataScoreBasisPoints;
        NormalConfidenceRiskScoreBasisPoints = normalConfidenceRiskScoreBasisPoints;
        ReducedConfidenceRiskScoreBasisPoints = reducedConfidenceRiskScoreBasisPoints;
        TwoLegFlipComplexityScoreBasisPoints = twoLegFlipComplexityScoreBasisPoints;
        Contributions = Array.AsReadOnly(contributions.OrderBy(contribution => contribution.Factor).ToArray());
    }

    public int ScoreBasisPoints { get; }
    public long TargetNetProfitCopper { get; }
    public int TargetReturnOnInvestmentBasisPoints { get; }
    public int AcceptablePriceImpactBasisPoints { get; }
    public MarketFlipScoringWeightsSnapshot Weights { get; }
    public int FreshDataScoreBasisPoints { get; }
    public int StaleDataScoreBasisPoints { get; }
    public int NormalConfidenceRiskScoreBasisPoints { get; }
    public int ReducedConfidenceRiskScoreBasisPoints { get; }
    public int TwoLegFlipComplexityScoreBasisPoints { get; }
    public IReadOnlyList<MarketFlipScoreContributionSnapshot> Contributions { get; }
}

public sealed record MarketFlipScoringWeightsSnapshot
{
    public MarketFlipScoringWeightsSnapshot(int netProfit, int capitalEfficiency, int liquidity, int freshness, int risk, int complexity)
    {
        if (netProfit < 0 || capitalEfficiency < 0 || liquidity < 0 || freshness < 0 || risk < 0 || complexity < 0 ||
            (long)netProfit + capitalEfficiency + liquidity + freshness + risk + complexity == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(netProfit), "Score weights must be non-negative and include at least one positive value.");
        }

        NetProfit = netProfit;
        CapitalEfficiency = capitalEfficiency;
        Liquidity = liquidity;
        Freshness = freshness;
        Risk = risk;
        Complexity = complexity;
    }

    public int NetProfit { get; }
    public int CapitalEfficiency { get; }
    public int Liquidity { get; }
    public int Freshness { get; }
    public int Risk { get; }
    public int Complexity { get; }
}

public sealed record MarketFlipScoreContributionSnapshot
{
    public MarketFlipScoreContributionSnapshot(
        OpportunityScoreFactor factor,
        int factorScoreBasisPoints,
        int weight,
        int weightedContributionBasisPoints)
    {
        if (!Enum.IsDefined(factor) || factorScoreBasisPoints is < 0 or > 10_000 || weight < 0 || weightedContributionBasisPoints < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(factor), "Score contribution values are invalid.");
        }

        Factor = factor;
        FactorScoreBasisPoints = factorScoreBasisPoints;
        Weight = weight;
        WeightedContributionBasisPoints = weightedContributionBasisPoints;
    }

    public OpportunityScoreFactor Factor { get; }
    public int FactorScoreBasisPoints { get; }
    public int Weight { get; }
    public int WeightedContributionBasisPoints { get; }
}

public sealed record CraftingSearchLimitsSnapshot
{
    public CraftingSearchLimitsSnapshot(int maximumDepth, int maximumCandidatePaths)
    {
        if (maximumDepth <= 0 || maximumCandidatePaths <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDepth), "Crafting search limits must be positive.");
        }

        MaximumDepth = maximumDepth;
        MaximumCandidatePaths = maximumCandidatePaths;
    }

    public int MaximumDepth { get; }

    public int MaximumCandidatePaths { get; }
}

public sealed record CraftingPathStepSnapshot
{
    public CraftingPathStepSnapshot(
        CraftingPathStepKind kind,
        int itemId,
        int requiredQuantity,
        int producedQuantity,
        int batchCount,
        int? recipeId,
        IReadOnlyList<CraftingPathStepSnapshot> ingredients,
        IReadOnlyList<CraftingAnalysisReason> reasons)
    {
        if (!Enum.IsDefined(kind) || itemId <= 0 || requiredQuantity <= 0 || producedQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemId), "Crafting path values are invalid.");
        }

        ArgumentNullException.ThrowIfNull(ingredients);
        ArgumentNullException.ThrowIfNull(reasons);
        if (ingredients.Any(ingredient => ingredient is null) || reasons.Any(reason => !Enum.IsDefined(reason)))
        {
            throw new ArgumentException("Crafting path details contain an invalid value.");
        }

        if (kind == CraftingPathStepKind.Purchase && (recipeId is not null || batchCount != 0 || ingredients.Count != 0 || producedQuantity != requiredQuantity))
        {
            throw new ArgumentException("A purchase step cannot contain crafting details.");
        }

        if (kind == CraftingPathStepKind.Craft && (recipeId is null or <= 0 || batchCount <= 0 || producedQuantity < requiredQuantity))
        {
            throw new ArgumentException("A crafting step must contain whole-batch details.");
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
    public IReadOnlyList<CraftingPathStepSnapshot> Ingredients { get; }
    public IReadOnlyList<CraftingAnalysisReason> Reasons { get; }
}

public sealed record CraftingIngredientCostSnapshot
{
    public CraftingIngredientCostSnapshot(int itemId, int requiredQuantity, CraftingSelectedStrategySnapshot? selectedStrategy)
    {
        if (itemId <= 0 || requiredQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemId), "Crafting ingredient values must be positive.");
        }

        ItemId = itemId;
        RequiredQuantity = requiredQuantity;
        SelectedStrategy = selectedStrategy;
    }

    public int ItemId { get; }
    public int RequiredQuantity { get; }
    public CraftingSelectedStrategySnapshot? SelectedStrategy { get; }
}

public sealed record CraftingSelectedStrategySnapshot
{
    public CraftingSelectedStrategySnapshot(
        OwnedItemStrategy strategy,
        int ownedQuantity,
        int purchasedQuantity,
        long ownedOpportunityCostCopper,
        long purchasedCostCopper,
        long totalEconomicCostCopper)
    {
        if (!Enum.IsDefined(strategy) || ownedQuantity < 0 || purchasedQuantity < 0 || ownedOpportunityCostCopper < 0 ||
            purchasedCostCopper < 0 || totalEconomicCostCopper != ownedOpportunityCostCopper + purchasedCostCopper)
        {
            throw new ArgumentOutOfRangeException(nameof(strategy), "Crafting strategy values are invalid.");
        }

        Strategy = strategy;
        OwnedQuantity = ownedQuantity;
        PurchasedQuantity = purchasedQuantity;
        OwnedOpportunityCostCopper = ownedOpportunityCostCopper;
        PurchasedCostCopper = purchasedCostCopper;
        TotalEconomicCostCopper = totalEconomicCostCopper;
    }

    public OwnedItemStrategy Strategy { get; }
    public int OwnedQuantity { get; }
    public int PurchasedQuantity { get; }
    public long OwnedOpportunityCostCopper { get; }
    public long PurchasedCostCopper { get; }
    public long TotalEconomicCostCopper { get; }
}

public sealed record CraftingOutputSaleSnapshot
{
    public CraftingOutputSaleSnapshot(int itemId, int quantity, OperationExecutionSnapshot? liquidation, OperationFinancialSnapshot? sale)
    {
        if (itemId <= 0 || quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemId), "Crafting output values must be positive.");
        }

        if (liquidation is { Kind: not OperationExecutionKind.Liquidation })
        {
            throw new ArgumentException("Crafting output liquidation must be a liquidation execution.", nameof(liquidation));
        }

        ItemId = itemId;
        Quantity = quantity;
        Liquidation = liquidation;
        Sale = sale;
    }

    public int ItemId { get; }
    public int Quantity { get; }
    public OperationExecutionSnapshot? Liquidation { get; }
    public OperationFinancialSnapshot? Sale { get; }
}

public sealed record CraftingSearchDiagnosticsSnapshot
{
    public CraftingSearchDiagnosticsSnapshot(
        int expandedRecipeCandidates,
        int memoizedSubproblemHits,
        bool isTruncated,
        IReadOnlyList<CraftingAnalysisReason> reasons)
    {
        if (expandedRecipeCandidates < 0 || memoizedSubproblemHits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expandedRecipeCandidates), "Crafting diagnostic counts cannot be negative.");
        }

        ArgumentNullException.ThrowIfNull(reasons);
        if (reasons.Any(reason => !Enum.IsDefined(reason)))
        {
            throw new ArgumentException("Crafting diagnostics contain an invalid reason.", nameof(reasons));
        }

        ExpandedRecipeCandidates = expandedRecipeCandidates;
        MemoizedSubproblemHits = memoizedSubproblemHits;
        IsTruncated = isTruncated;
        Reasons = Array.AsReadOnly(reasons.Distinct().Order().ToArray());
    }

    public int ExpandedRecipeCandidates { get; }
    public int MemoizedSubproblemHits { get; }
    public bool IsTruncated { get; }
    public IReadOnlyList<CraftingAnalysisReason> Reasons { get; }
}

using Gw2Tp.Analytics.Crafting;
using Gw2Tp.Analytics.FlipOpportunities;
using Gw2Tp.Analytics.OwnedItems;
using Gw2Tp.Analytics.Reconciliation;
using Gw2Tp.Application.Operations;
using Gw2Tp.Domain.Finance;
using Xunit;

namespace Gw2Tp.Application.Tests.Operations;

public sealed class OperationRecordTests
{
    [Fact]
    public void Retains_a_typed_market_flip_snapshot_and_normalizes_utc_metadata()
    {
        var operation = new OperationRecord(
            Guid.NewGuid(),
            new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 8, 28, 8, 5, 0, TimeSpan.Zero),
            OperationStatus.Planned,
            " calc-v1 ",
            " config-v1 ",
            CreateMarketFlipScenario());

        var scenario = Assert.IsType<MarketFlipOperationScenarioSnapshot>(operation.Scenario);
        Assert.Equal(TimeSpan.Zero, operation.CreatedAtUtc.Offset);
        Assert.Equal("calc-v1", operation.CalculationVersionId);
        Assert.Equal("config-v1", operation.ConfigurationVersionId);
        Assert.Equal(900_001, scenario.ItemId);
        Assert.Equal(5, scenario.RequestedQuantity);
        Assert.Equal(55, scenario.Analysis.Financials!.NetProfitCopper);
        Assert.Equal(4, scenario.Score!.Weights.NetProfit);
    }

    [Fact]
    public void Retains_a_selected_crafting_path_without_raw_account_or_market_snapshots()
    {
        var scenario = CreateCraftingScenario();

        Assert.Equal(OperationScenarioKind.Crafting, scenario.Kind);
        Assert.Equal(800_001, scenario.ItemId);
        Assert.Equal(42, scenario.SelectedPath.RecipeId);
        Assert.Single(scenario.SelectedPath.Ingredients);
        Assert.Equal(40, Assert.Single(scenario.IngredientCosts).SelectedStrategy!.TotalEconomicCostCopper);
        Assert.Equal(45, scenario.ModeledFinancials!.NetProfitCopper);
    }

    [Fact]
    public void Retains_actual_outcome_evidence_for_a_cancelled_operation_without_changing_its_status()
    {
        var actualOutcome = new OperationActualOutcome(
        [
            new ActualAcquisitionLot(
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                new DateTimeOffset(2026, 8, 28, 8, 0, 0, TimeSpan.Zero),
                2,
                new Money(60)),
        ],
        []);
        var operation = new OperationRecord(
            Guid.NewGuid(),
            new DateTimeOffset(2026, 8, 28, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 28, 8, 0, 0, TimeSpan.Zero),
            OperationStatus.Cancelled,
            "calc-v1",
            "config-v1",
            CreateMarketFlipScenario(),
            actualOutcome);

        Assert.Equal(OperationStatus.Cancelled, operation.Status);
        Assert.Same(actualOutcome, operation.ActualOutcome);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(99)]
    public void Rejects_unknown_operation_statuses(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OperationRecord(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            (OperationStatus)value,
            "calc-v1",
            "config-v1",
            CreateMarketFlipScenario()));
    }

    [Fact]
    public void Rejects_empty_versions_and_reverse_timestamp_order()
    {
        var createdAtUtc = new DateTimeOffset(2026, 8, 28, 8, 0, 0, TimeSpan.Zero);

        Assert.Throws<ArgumentException>(() => new OperationRecord(
            Guid.NewGuid(),
            createdAtUtc,
            createdAtUtc,
            OperationStatus.Planned,
            " ",
            "config-v1",
            CreateMarketFlipScenario()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OperationRecord(
            Guid.NewGuid(),
            createdAtUtc,
            createdAtUtc.AddMinutes(-1),
            OperationStatus.Planned,
            "calc-v1",
            "config-v1",
            CreateMarketFlipScenario()));
    }

    internal static MarketFlipOperationScenarioSnapshot CreateMarketFlipScenario() => new(
        itemId: 900_001,
        requestedQuantity: 5,
        analyzedAtUtc: new DateTimeOffset(2026, 8, 28, 8, 0, 0, TimeSpan.Zero),
        freshness: new OperationMarketFreshnessSnapshot(
            new DateTimeOffset(2026, 8, 28, 7, 59, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 28, 8, 4, 0, TimeSpan.Zero)),
        feePolicy: FeePolicy(),
        constraints: new OperationFlipConstraintsSnapshot(50, 250),
        analysis: new MarketFlipAnalysisSnapshot(
            MarketFlipOperationUsability.Usable,
            MarketFlipOperationConfidence.Normal,
            meetsFinancialConstraints: true,
            isPartialData: false,
            reasons: [],
            acquisition: Execution(OperationExecutionKind.Acquisition, 5, 200, 0),
            liquidation: Execution(OperationExecutionKind.Liquidation, 5, 300, 0),
            financials: new OperationFinancialSnapshot(200, 300, 15, 30, 255, 55),
            liquidity: new OperationLiquiditySnapshot(5, 5, 5, 0, 0),
            returnOnInvestment: new OperationReturnOnInvestmentSnapshot(55, 215),
            capitalRequiredCopper: 215),
        score: new MarketFlipScoreSnapshot(
            scoreBasisPoints: 9_000,
            targetNetProfitCopper: 50,
            targetReturnOnInvestmentBasisPoints: 2_000,
            acceptablePriceImpactBasisPoints: 500,
            weights: new MarketFlipScoringWeightsSnapshot(4, 3, 1, 1, 1, 1),
            freshDataScoreBasisPoints: 10_000,
            staleDataScoreBasisPoints: 0,
            normalConfidenceRiskScoreBasisPoints: 10_000,
            reducedConfidenceRiskScoreBasisPoints: 0,
            twoLegFlipComplexityScoreBasisPoints: 10_000,
            contributions:
            [
                new MarketFlipScoreContributionSnapshot(OpportunityScoreFactor.NetProfit, 10_000, 4, 4_000),
            ]));

    internal static CraftingOperationScenarioSnapshot CreateCraftingScenario() => new(
        itemId: 800_001,
        requestedQuantity: 1,
        analyzedAtUtc: new DateTimeOffset(2026, 8, 28, 8, 0, 0, TimeSpan.Zero),
        feePolicy: FeePolicy(),
        searchLimits: new CraftingSearchLimitsSnapshot(4, 12),
        selectedPath: new CraftingPathStepSnapshot(
            CraftingPathStepKind.Craft,
            itemId: 800_001,
            requiredQuantity: 1,
            producedQuantity: 1,
            batchCount: 1,
            recipeId: 42,
            ingredients:
            [
                new CraftingPathStepSnapshot(
                    CraftingPathStepKind.Purchase,
                    itemId: 800_002,
                    requiredQuantity: 2,
                    producedQuantity: 2,
                    batchCount: 0,
                    recipeId: null,
                    ingredients: [],
                    reasons: []),
            ],
            reasons: []),
        ingredientCosts:
        [
            new CraftingIngredientCostSnapshot(
                800_002,
                2,
                new CraftingSelectedStrategySnapshot(OwnedItemStrategy.BuyAll, 0, 2, 0, 40, 40)),
        ],
        outputSales:
        [
            new CraftingOutputSaleSnapshot(
                800_001,
                1,
                Execution(OperationExecutionKind.Liquidation, 1, 100, 0),
                new OperationFinancialSnapshot(0, 100, 5, 10, 85, 85)),
        ],
        modeledFinancials: new OperationFinancialSnapshot(40, 100, 5, 10, 85, 45),
        reasons: [],
        diagnostics: new CraftingSearchDiagnosticsSnapshot(3, 1, isTruncated: false, reasons: []));

    private static OperationFeePolicySnapshot FeePolicy() => new(
        new OperationFeeRuleSnapshot(500, OperationFeeRounding.Down),
        new OperationFeeRuleSnapshot(1_000, OperationFeeRounding.Down));

    private static OperationExecutionSnapshot Execution(
        OperationExecutionKind kind,
        int quantity,
        long totalValueCopper,
        long priceImpactCopper) => new(
        kind,
        quantity,
        quantity,
        0,
        totalValueCopper,
        priceImpactCopper,
        [new OperationExecutionFillSnapshot(quantity, totalValueCopper / quantity, totalValueCopper)]);
}

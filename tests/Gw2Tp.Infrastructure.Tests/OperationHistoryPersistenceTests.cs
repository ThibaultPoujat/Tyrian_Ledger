using System.Data.Common;
using System.Text.Json;
using Gw2Tp.Analytics.Crafting;
using Gw2Tp.Analytics.FlipOpportunities;
using Gw2Tp.Analytics.OwnedItems;
using Gw2Tp.Application.Operations;
using Gw2Tp.Infrastructure.Preferences;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Gw2Tp.Infrastructure.Tests;

public sealed class OperationHistoryPersistenceTests
{
    private const string InitialMigrationId = "20260827183917_InitialUserSessionPreferences";

    [Fact]
    public async Task Round_trips_full_market_flip_and_selected_crafting_scenarios_and_updates_status()
    {
        var databasePath = CreateDatabasePath();
        using var serviceProvider = CreateServiceProvider(databasePath);
        await serviceProvider.MigrateTyrianLedgerUserSessionPreferencesAsync();
        var store = serviceProvider.GetRequiredService<IOperationHistoryStore>();
        var createdAtUtc = new DateTimeOffset(2026, 8, 28, 8, 0, 0, TimeSpan.Zero);
        var marketOperation = new OperationRecord(
            Guid.Parse("10e4705d-c52c-4f9b-9669-f0805e613ec0"),
            createdAtUtc,
            createdAtUtc,
            OperationStatus.Planned,
            "flip-calculation-v1",
            "flip-config-v1",
            CreateMarketFlipScenario());
        var craftingOperation = new OperationRecord(
            Guid.Parse("0e15e7d9-86ee-4aa2-9b73-6cfe60be7eb1"),
            createdAtUtc.AddMinutes(1),
            createdAtUtc.AddMinutes(1),
            OperationStatus.Planned,
            "crafting-calculation-v1",
            "crafting-config-v1",
            CreateCraftingScenario());

        await store.CreateAsync(marketOperation, CancellationToken.None);
        await store.CreateAsync(craftingOperation, CancellationToken.None);

        var storedMarket = await store.GetAsync(marketOperation.Id, CancellationToken.None);
        var storedCrafting = await store.GetAsync(craftingOperation.Id, CancellationToken.None);
        var listedOperations = await store.ListAsync(CancellationToken.None);

        var marketScenario = Assert.IsType<MarketFlipOperationScenarioSnapshot>(storedMarket!.Scenario);
        Assert.Equal(marketOperation.CalculationVersionId, storedMarket.CalculationVersionId);
        Assert.Equal(55, marketScenario.Analysis.Financials!.NetProfitCopper);
        Assert.Equal(4, marketScenario.Score!.Weights.NetProfit);
        Assert.Single(marketScenario.Analysis.Acquisition!.Fills);

        var craftingScenario = Assert.IsType<CraftingOperationScenarioSnapshot>(storedCrafting!.Scenario);
        Assert.Equal(42, craftingScenario.SelectedPath.RecipeId);
        Assert.Equal(40, Assert.Single(craftingScenario.IngredientCosts).SelectedStrategy!.TotalEconomicCostCopper);
        Assert.Equal(45, craftingScenario.ModeledFinancials!.NetProfitCopper);
        Assert.Single(craftingScenario.SelectedPath.Ingredients);
        Assert.Equal([marketOperation.Id, craftingOperation.Id], listedOperations.Select(operation => operation.Id));
        Assert.Contains("\"kind\":\"acquisition\"", await ReadScenarioPayloadAsync(databasePath, marketOperation.Id));

        await ReplaceScenarioPayloadAsync(
            databasePath,
            marketOperation.Id,
            JsonSerializer.Serialize(
                marketOperation.Scenario,
                marketOperation.Scenario.GetType(),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var legacyMarketOperation = await store.GetAsync(marketOperation.Id, CancellationToken.None);
        Assert.IsType<MarketFlipOperationScenarioSnapshot>(legacyMarketOperation!.Scenario);

        var completedAtUtc = createdAtUtc.AddHours(1);
        await store.UpdateStatusAsync(craftingOperation.Id, OperationStatus.Completed, completedAtUtc, CancellationToken.None);

        var completedOperation = await store.GetAsync(craftingOperation.Id, CancellationToken.None);
        Assert.Equal(OperationStatus.Completed, completedOperation!.Status);
        Assert.Equal(completedAtUtc, completedOperation.LastModifiedAtUtc);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.UpdateStatusAsync(
            craftingOperation.Id,
            OperationStatus.Cancelled,
            createdAtUtc,
            CancellationToken.None));
    }

    [Fact]
    public async Task Migration_upgrades_the_existing_preferences_database_without_losing_data()
    {
        var databasePath = CreateDatabasePath();
        var options = new DbContextOptionsBuilder<UserSessionPreferencesDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        await using var dbContext = new UserSessionPreferencesDbContext(options);
        await dbContext.Database.MigrateAsync(InitialMigrationId);
        await dbContext.Database.ExecuteSqlRawAsync(
            "INSERT INTO UserSessionPreferences (Id, CapitalLimitCopper, MinimumProfitCopper, RiskPreference, StrategyPreference, AllocationPercent) VALUES (1, 120000, 500, 'normal', 'market-flip', 65)");

        await dbContext.Database.MigrateAsync();

        await dbContext.Database.OpenConnectionAsync();
        try
        {
            Assert.Equal(65L, await ExecuteScalarAsync(dbContext.Database.GetDbConnection(), "SELECT AllocationPercent FROM UserSessionPreferences WHERE Id = 1"));
            Assert.Equal(1L, await ExecuteScalarAsync(
                dbContext.Database.GetDbConnection(),
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Operations'"));
            Assert.Equal(1L, await ExecuteScalarAsync(
                dbContext.Database.GetDbConnection(),
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'OperationScenarios'"));
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private static ServiceProvider CreateServiceProvider(string databasePath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [UserSessionPreferencesServiceCollectionExtensions.DatabasePathConfigurationKey] = databasePath,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddTyrianLedgerUserSessionPreferences(configuration, new TestHostEnvironment("Testing"));
        return services.BuildServiceProvider();
    }

    private static async Task<long> ExecuteScalarAsync(DbConnection connection, string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<string> ReadScenarioPayloadAsync(string databasePath, Guid operationId)
    {
        var options = new DbContextOptionsBuilder<UserSessionPreferencesDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        await using var dbContext = new UserSessionPreferencesDbContext(options);
        await dbContext.Database.OpenConnectionAsync();
        try
        {
            await using var command = dbContext.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT PayloadJson FROM OperationScenarios WHERE OperationId = $operationId";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$operationId";
            parameter.Value = operationId;
            command.Parameters.Add(parameter);
            return (string)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("The scenario payload was not stored."));
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private static async Task ReplaceScenarioPayloadAsync(string databasePath, Guid operationId, string payloadJson)
    {
        var options = new DbContextOptionsBuilder<UserSessionPreferencesDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        await using var dbContext = new UserSessionPreferencesDbContext(options);
        await dbContext.Database.OpenConnectionAsync();
        try
        {
            await using var command = dbContext.Database.GetDbConnection().CreateCommand();
            command.CommandText = "UPDATE OperationScenarios SET PayloadJson = $payloadJson WHERE OperationId = $operationId";
            var operationIdParameter = command.CreateParameter();
            operationIdParameter.ParameterName = "$operationId";
            operationIdParameter.Value = operationId;
            command.Parameters.Add(operationIdParameter);
            var payloadParameter = command.CreateParameter();
            payloadParameter.ParameterName = "$payloadJson";
            payloadParameter.Value = payloadJson;
            command.Parameters.Add(payloadParameter);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private static string CreateDatabasePath() => Path.Combine(
        Path.GetTempPath(),
        "TyrianLedger",
        "InfrastructureTests",
        $"operations-{Guid.NewGuid():N}.db");

    private static MarketFlipOperationScenarioSnapshot CreateMarketFlipScenario() => new(
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
            acquisition: Execution(OperationExecutionKind.Acquisition, 5, 200),
            liquidation: Execution(OperationExecutionKind.Liquidation, 5, 300),
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

    private static CraftingOperationScenarioSnapshot CreateCraftingScenario() => new(
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
                Execution(OperationExecutionKind.Liquidation, 1, 100),
                new OperationFinancialSnapshot(0, 100, 5, 10, 85, 85)),
        ],
        modeledFinancials: new OperationFinancialSnapshot(40, 100, 5, 10, 85, 45),
        reasons: [],
        diagnostics: new CraftingSearchDiagnosticsSnapshot(3, 1, isTruncated: false, reasons: []));

    private static OperationFeePolicySnapshot FeePolicy() => new(
        new OperationFeeRuleSnapshot(500, OperationFeeRounding.Down),
        new OperationFeeRuleSnapshot(1_000, OperationFeeRounding.Down));

    private static OperationExecutionSnapshot Execution(OperationExecutionKind kind, int quantity, long totalValueCopper) => new(
        kind,
        quantity,
        quantity,
        0,
        totalValueCopper,
        0,
        [new OperationExecutionFillSnapshot(quantity, totalValueCopper / quantity, totalValueCopper)]);

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Gw2Tp.Infrastructure.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

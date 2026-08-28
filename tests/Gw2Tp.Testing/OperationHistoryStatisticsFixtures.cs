using Gw2Tp.Analytics.FlipOpportunities;
using Gw2Tp.Analytics.Reconciliation;
using Gw2Tp.Application.Operations;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Testing;

public static class OperationHistoryStatisticsFixtures
{
    public static readonly DateTimeOffset StartedAtUtc = new(2026, 8, 28, 8, 0, 0, TimeSpan.Zero);

    public static IReadOnlyList<OperationRecord> CreatePopulated() =>
    [
        CreateOperation("11111111-1111-1111-1111-111111111111", StartedAtUtc, StartedAtUtc.AddMinutes(1), OperationStatus.Planned, 10),
        CreateOperation(
            "22222222-2222-2222-2222-222222222222",
            StartedAtUtc.AddMinutes(2),
            StartedAtUtc.AddMinutes(3),
            OperationStatus.Completed,
            11,
            CreateActualOutcome("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "dddddddd-dddd-dddd-dddd-dddddddddddd", quantity: 1, acquisitionCostCopper: 100, soldQuantity: 1, grossSaleCopper: 130, listingFeeCopper: 10, exchangeFeeCopper: 10)),
        CreateOperation(
            "33333333-3333-3333-3333-333333333333",
            StartedAtUtc.AddMinutes(4),
            StartedAtUtc.AddMinutes(8),
            OperationStatus.Cancelled,
            modeledNetProfitCopper: null,
            CreateActualOutcome("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee", quantity: 2, acquisitionCostCopper: 200, soldQuantity: 1, grossSaleCopper: 150, listingFeeCopper: 10, exchangeFeeCopper: 10)),
        CreateOperation(
            "44444444-4444-4444-4444-444444444444",
            StartedAtUtc.AddMinutes(5),
            StartedAtUtc.AddMinutes(6),
            OperationStatus.InProgress,
            modeledNetProfitCopper: null,
            CreateActualOutcome("cccccccc-cccc-cccc-cccc-cccccccccccc", saleId: null, quantity: 1, acquisitionCostCopper: 90, soldQuantity: 0, grossSaleCopper: 0, listingFeeCopper: 0, exchangeFeeCopper: 0)),
    ];

    public static IReadOnlyList<OperationRecord> Empty() => [];

    private static OperationRecord CreateOperation(
        string id,
        DateTimeOffset createdAtUtc,
        DateTimeOffset lastModifiedAtUtc,
        OperationStatus status,
        long? modeledNetProfitCopper,
        OperationActualOutcome? actualOutcome = null) => new(
        Guid.Parse(id),
        createdAtUtc,
        lastModifiedAtUtc,
        status,
        "calculation-v1",
        "configuration-v1",
        CreateScenario(modeledNetProfitCopper),
        actualOutcome);

    private static MarketFlipOperationScenarioSnapshot CreateScenario(long? modeledNetProfitCopper)
    {
        var financials = modeledNetProfitCopper is null
            ? null
            : new OperationFinancialSnapshot(
                acquisitionCostCopper: 100,
                grossSaleValueCopper: 100 + modeledNetProfitCopper.Value,
                listingFeeCopper: 0,
                exchangeFeeCopper: 0,
                netSaleProceedsCopper: 100 + modeledNetProfitCopper.Value,
                netProfitCopper: modeledNetProfitCopper.Value);

        return new MarketFlipOperationScenarioSnapshot(
            itemId: 900_001,
            requestedQuantity: 1,
            analyzedAtUtc: StartedAtUtc,
            freshness: null,
            feePolicy: new OperationFeePolicySnapshot(
                new OperationFeeRuleSnapshot(0, OperationFeeRounding.Down),
                new OperationFeeRuleSnapshot(0, OperationFeeRounding.Down)),
            constraints: new OperationFlipConstraintsSnapshot(0, null),
            analysis: new MarketFlipAnalysisSnapshot(
                MarketFlipOperationUsability.Usable,
                MarketFlipOperationConfidence.Normal,
                meetsFinancialConstraints: true,
                isPartialData: false,
                reasons: [],
                acquisition: null,
                liquidation: null,
                financials,
                liquidity: null,
                returnOnInvestment: null,
                capitalRequiredCopper: null),
            score: null);
    }

    private static OperationActualOutcome CreateActualOutcome(
        string acquisitionId,
        string? saleId,
        int quantity,
        long acquisitionCostCopper,
        int soldQuantity,
        long grossSaleCopper,
        long listingFeeCopper,
        long exchangeFeeCopper)
    {
        var acquisition = new ActualAcquisitionLot(
            Guid.Parse(acquisitionId),
            StartedAtUtc,
            quantity,
            new Money(acquisitionCostCopper));
        IReadOnlyList<ActualSaleSettlement> sales = soldQuantity == 0
            ? Array.Empty<ActualSaleSettlement>()
            :
            [
                new ActualSaleSettlement(
                    Guid.Parse(saleId ?? throw new InvalidOperationException("A sale fixture ID is required.")),
                    StartedAtUtc.AddMinutes(1),
                    soldQuantity,
                    new Money(grossSaleCopper),
                    new Money(listingFeeCopper),
                    new Money(exchangeFeeCopper)),
            ];

        return new OperationActualOutcome([acquisition], sales);
    }
}

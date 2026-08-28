using Gw2Tp.Application.Operations;
using Gw2Tp.Testing;
using Xunit;

namespace Gw2Tp.Application.Tests.Operations;

public sealed class OperationHistoryStatisticsCalculatorTests
{
    [Fact]
    public void Calculates_evidence_scoped_statistics_from_saved_operations()
    {
        var statistics = new OperationHistoryStatisticsCalculator()
            .Calculate(OperationHistoryStatisticsFixtures.CreatePopulated());

        Assert.Equal(4, statistics.OperationCount);
        Assert.Equal(OperationHistoryStatisticsFixtures.StartedAtUtc, statistics.FirstRecordedAtUtc);
        Assert.Equal(OperationHistoryStatisticsFixtures.StartedAtUtc.AddMinutes(8), statistics.LastRecordedAtUtc);
        Assert.Equal(2, statistics.ModeledNetProfit.EligibleOperationCount);
        Assert.Equal(21, statistics.ModeledNetProfit.TotalCopper);
        Assert.Equal(2, statistics.RealizedProfit.EligibleOperationCount);
        Assert.Equal(40, statistics.RealizedProfit.TotalCopper);
        Assert.Equal(1, statistics.Lifecycle.CompletedOperationCount);
        Assert.Equal(1, statistics.Lifecycle.CancelledOperationCount);
        Assert.Equal(2, statistics.Lifecycle.TerminalOperationCount);
    }

    [Fact]
    public void Returns_empty_statistics_without_replacing_missing_evidence_with_zero()
    {
        var statistics = new OperationHistoryStatisticsCalculator()
            .Calculate(OperationHistoryStatisticsFixtures.Empty());

        Assert.Equal(0, statistics.OperationCount);
        Assert.Null(statistics.FirstRecordedAtUtc);
        Assert.Null(statistics.LastRecordedAtUtc);
        Assert.Equal(0, statistics.ModeledNetProfit.EligibleOperationCount);
        Assert.Null(statistics.ModeledNetProfit.TotalCopper);
        Assert.Equal(0, statistics.RealizedProfit.EligibleOperationCount);
        Assert.Null(statistics.RealizedProfit.TotalCopper);
        Assert.Equal(0, statistics.Lifecycle.TerminalOperationCount);
    }

    [Fact]
    public void Includes_saved_crafting_financial_snapshots_in_modeled_statistics()
    {
        var operation = new OperationRecord(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            OperationHistoryStatisticsFixtures.StartedAtUtc,
            OperationHistoryStatisticsFixtures.StartedAtUtc,
            OperationStatus.Planned,
            "calculation-v1",
            "configuration-v1",
            OperationRecordTests.CreateCraftingScenario());

        var statistics = new OperationHistoryStatisticsCalculator().Calculate([operation]);

        Assert.Equal(1, statistics.ModeledNetProfit.EligibleOperationCount);
        Assert.Equal(45, statistics.ModeledNetProfit.TotalCopper);
    }
}

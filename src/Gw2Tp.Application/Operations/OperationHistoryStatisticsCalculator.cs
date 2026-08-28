using Gw2Tp.Analytics.Reconciliation;

namespace Gw2Tp.Application.Operations;

/// <summary>
/// Produces reproducible, evidence-scoped statistics from locally saved operation records.
/// Modeled and realized values intentionally retain separate eligibility counts so missing
/// evidence is never represented as zero.
/// </summary>
public sealed class OperationHistoryStatisticsCalculator
{
    private readonly OperationReconciliationCalculator reconciliationCalculator = new();

    public OperationHistoryStatistics Calculate(IReadOnlyList<OperationRecord> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);

        if (operations.Any(operation => operation is null))
        {
            throw new ArgumentException("Operation history cannot contain null records.", nameof(operations));
        }

        if (operations.Count == 0)
        {
            return new OperationHistoryStatistics(
                0,
                null,
                null,
                new OperationProfitStatistics(0, null),
                new OperationProfitStatistics(0, null),
                new OperationLifecycleStatistics(0, 0));
        }

        var firstRecordedAtUtc = operations.Min(operation => operation.CreatedAtUtc);
        var lastRecordedAtUtc = operations.Max(operation => operation.LastModifiedAtUtc);
        var modeledNetProfit = new ProfitAccumulator();
        var realizedProfit = new ProfitAccumulator();
        var completedOperationCount = 0;
        var cancelledOperationCount = 0;

        foreach (var operation in operations)
        {
            var modeledNetProfitCopper = GetModeledNetProfitCopper(operation.Scenario);
            if (modeledNetProfitCopper is not null)
            {
                modeledNetProfit.Add(modeledNetProfitCopper.Value);
            }

            var reconciliation = reconciliationCalculator.Calculate(operation.ActualOutcome);
            if (reconciliation.RealizedProfit is not null)
            {
                realizedProfit.Add(reconciliation.RealizedProfit.Value.Copper);
            }

            switch (operation.Status)
            {
                case OperationStatus.Completed:
                    completedOperationCount++;
                    break;
                case OperationStatus.Cancelled:
                    cancelledOperationCount++;
                    break;
            }
        }

        return new OperationHistoryStatistics(
            operations.Count,
            firstRecordedAtUtc,
            lastRecordedAtUtc,
            modeledNetProfit.ToStatistics(),
            realizedProfit.ToStatistics(),
            new OperationLifecycleStatistics(completedOperationCount, cancelledOperationCount));
    }

    private static long? GetModeledNetProfitCopper(OperationScenarioSnapshot scenario) => scenario switch
    {
        MarketFlipOperationScenarioSnapshot { Analysis.Financials: { } financials } => financials.NetProfitCopper,
        CraftingOperationScenarioSnapshot { ModeledFinancials: { } financials } => financials.NetProfitCopper,
        _ => null,
    };

    private sealed class ProfitAccumulator
    {
        private long totalCopper;

        public int EligibleOperationCount { get; private set; }

        public void Add(long valueCopper)
        {
            totalCopper = checked(totalCopper + valueCopper);
            EligibleOperationCount = checked(EligibleOperationCount + 1);
        }

        public OperationProfitStatistics ToStatistics() => new(
            EligibleOperationCount,
            EligibleOperationCount == 0 ? null : totalCopper);
    }
}

/// <summary>
/// A snapshot of the locally recorded history period and its separately eligible metrics.
/// </summary>
public sealed record OperationHistoryStatistics(
    int OperationCount,
    DateTimeOffset? FirstRecordedAtUtc,
    DateTimeOffset? LastRecordedAtUtc,
    OperationProfitStatistics ModeledNetProfit,
    OperationProfitStatistics RealizedProfit,
    OperationLifecycleStatistics Lifecycle);

/// <summary>
/// A profit total and the exact number of operations eligible for it. Consumers must render
/// the average as the exact total-to-count ratio rather than rounding fractional copper.
/// </summary>
public sealed record OperationProfitStatistics(int EligibleOperationCount, long? TotalCopper);

/// <summary>
/// Counts of terminal lifecycle records. The exact completion rate is completed divided by
/// terminal operations; active records are deliberately excluded from the denominator.
/// </summary>
public sealed record OperationLifecycleStatistics(int CompletedOperationCount, int CancelledOperationCount)
{
    public int TerminalOperationCount => checked(CompletedOperationCount + CancelledOperationCount);
}

using Gw2Tp.Application.Operations;

namespace Gw2Tp.Web.History;

internal sealed record OperationHistoryStatisticsResponse(
    int OperationCount,
    DateTimeOffset? FirstRecordedAtUtc,
    DateTimeOffset? LastRecordedAtUtc,
    OperationProfitStatisticsResponse ModeledNetProfit,
    OperationProfitStatisticsResponse RealizedProfit,
    OperationLifecycleStatisticsResponse Lifecycle)
{
    internal static OperationHistoryStatisticsResponse From(OperationHistoryStatistics statistics)
    {
        ArgumentNullException.ThrowIfNull(statistics);

        return new OperationHistoryStatisticsResponse(
            statistics.OperationCount,
            statistics.FirstRecordedAtUtc,
            statistics.LastRecordedAtUtc,
            new OperationProfitStatisticsResponse(
                statistics.ModeledNetProfit.EligibleOperationCount,
                statistics.ModeledNetProfit.TotalCopper),
            new OperationProfitStatisticsResponse(
                statistics.RealizedProfit.EligibleOperationCount,
                statistics.RealizedProfit.TotalCopper),
            new OperationLifecycleStatisticsResponse(
                statistics.Lifecycle.CompletedOperationCount,
                statistics.Lifecycle.CancelledOperationCount,
                statistics.Lifecycle.TerminalOperationCount));
    }
}

internal sealed record OperationProfitStatisticsResponse(int EligibleOperationCount, long? TotalCopper);

internal sealed record OperationLifecycleStatisticsResponse(
    int CompletedOperationCount,
    int CancelledOperationCount,
    int TerminalOperationCount);

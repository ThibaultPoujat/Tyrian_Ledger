using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Application.Time;
using Microsoft.Extensions.Hosting;

namespace Gw2Tp.Web.Hosting;

internal sealed record MarketHistoryCollectionSchedulerSettings(TimeSpan SourceRefreshInterval)
{
    internal static MarketHistoryCollectionSchedulerSettings Default { get; } = new(TimeSpan.FromMinutes(1));

    internal void Validate()
    {
        if (SourceRefreshInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(SourceRefreshInterval));
        }
    }
}

/// <summary>
/// Keeps the local collector alive with a bounded source re-evaluation cadence.
/// The collection service applies per-item intervals; this worker only decides
/// when local source membership is checked again.
/// </summary>
internal sealed class MarketHistoryCollectorHostedService(
    IMarketHistoryCollector collector,
    MarketHistoryCollectionSchedulerSettings settings,
    IClock clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        settings.Validate();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var run = await collector.CollectDueAsync(stoppingToken).ConfigureAwait(false);
                var now = RequireUtc(clock.UtcNow);
                var sourceRefreshAtUtc = now + settings.SourceRefreshInterval;
                var nextRunAtUtc = run.NextDueAtUtc is { } nextDue && nextDue > now && nextDue < sourceRefreshAtUtc
                    ? nextDue
                    : sourceRefreshAtUtc;
                collector.SetNextRunAtUtc(nextRunAtUtc);
                await Task.Delay(nextRunAtUtc - now, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal application shutdown does not represent a failed capture.
        }
        finally
        {
            collector.SetNextRunAtUtc(null);
        }
    }

    private static DateTimeOffset RequireUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero
        ? value
        : throw new ArgumentException("Collection timestamps must be UTC.", nameof(value));
}

namespace Gw2Tp.Application.MarketData;

/// <summary>
/// Read-only, process-local diagnostics for public market-data access.
/// Snapshots contain only aggregate counters and fixed endpoint categories.
/// </summary>
public interface IMarketDataDiagnostics
{
    MarketDataDiagnosticsSnapshot GetSnapshot();
}

/// <summary>
/// Safe aggregate view of public market-data activity for local diagnostics.
/// </summary>
public sealed record MarketDataDiagnosticsSnapshot(
    IReadOnlyList<MarketDataEndpointDiagnostics> Endpoints);

/// <summary>
/// Aggregate counters for one fixed public market-data endpoint category.
/// </summary>
public sealed record MarketDataEndpointDiagnostics(
    string Endpoint,
    long RequestCount,
    long CacheHitCount,
    long CacheMissCount,
    long RateLimitedResponseCount,
    long ParsingFailureCount,
    long LatencySampleCount,
    long TotalRequestLatencyMilliseconds,
    long AverageRequestLatencyMilliseconds);

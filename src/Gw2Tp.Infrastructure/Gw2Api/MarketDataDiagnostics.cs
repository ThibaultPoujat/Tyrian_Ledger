using System.Threading;
using Gw2Tp.Application.MarketData;

namespace Gw2Tp.Infrastructure.Gw2Api;

/// <summary>
/// Internal write boundary for the safe, application-facing diagnostics view.
/// </summary>
internal interface IMarketDataDiagnosticsRecorder : IMarketDataDiagnostics
{
    void RecordRequest(Gw2MarketDataEndpoint endpoint, TimeSpan latency);

    void RecordRateLimitedResponse(Gw2MarketDataEndpoint endpoint);

    void RecordParsingFailure(Gw2MarketDataEndpoint endpoint);
}

internal enum Gw2MarketDataEndpoint
{
    PriceIndex,
    Prices,
    Listings,
    Items,
}

/// <summary>
/// Thread-safe in-memory aggregate diagnostics. It intentionally stores no
/// request identity, query values, headers, response bodies, or credentials.
/// </summary>
internal sealed class MarketDataDiagnostics : IMarketDataDiagnosticsRecorder
{
    private readonly EndpointCounters _priceIndex = new();
    private readonly EndpointCounters _prices = new();
    private readonly EndpointCounters _listings = new();
    private readonly EndpointCounters _items = new();

    public MarketDataDiagnosticsSnapshot GetSnapshot() => new(
        [
        _priceIndex.CreateSnapshot("commerce/prices/index"),
        _prices.CreateSnapshot("commerce/prices"),
        _listings.CreateSnapshot("commerce/listings"),
        _items.CreateSnapshot("items"),
    ]);

    public void RecordRequest(Gw2MarketDataEndpoint endpoint, TimeSpan latency)
    {
        var latencyMilliseconds = Math.Max(0L, latency.Ticks / TimeSpan.TicksPerMillisecond);
        GetCounters(endpoint).RecordRequest(latencyMilliseconds);
    }

    public void RecordRateLimitedResponse(Gw2MarketDataEndpoint endpoint) =>
        GetCounters(endpoint).RecordRateLimitedResponse();

    public void RecordParsingFailure(Gw2MarketDataEndpoint endpoint) =>
        GetCounters(endpoint).RecordParsingFailure();

    private EndpointCounters GetCounters(Gw2MarketDataEndpoint endpoint) => endpoint switch
    {
        Gw2MarketDataEndpoint.PriceIndex => _priceIndex,
        Gw2MarketDataEndpoint.Prices => _prices,
        Gw2MarketDataEndpoint.Listings => _listings,
        Gw2MarketDataEndpoint.Items => _items,
        _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, "Unknown market endpoint."),
    };

    private sealed class EndpointCounters
    {
        private long _requestCount;
        private long _rateLimitedResponseCount;
        private long _parsingFailureCount;
        private long _latencySampleCount;
        private long _totalRequestLatencyMilliseconds;

        public void RecordRequest(long latencyMilliseconds)
        {
            Interlocked.Increment(ref _requestCount);
            Interlocked.Increment(ref _latencySampleCount);
            Interlocked.Add(ref _totalRequestLatencyMilliseconds, latencyMilliseconds);
        }

        public void RecordRateLimitedResponse() => Interlocked.Increment(ref _rateLimitedResponseCount);

        public void RecordParsingFailure() => Interlocked.Increment(ref _parsingFailureCount);

        public MarketDataEndpointDiagnostics CreateSnapshot(string endpoint)
        {
            var latencySampleCount = Interlocked.Read(ref _latencySampleCount);
            var totalLatencyMilliseconds = Interlocked.Read(ref _totalRequestLatencyMilliseconds);

            return new MarketDataEndpointDiagnostics(
                endpoint,
                Interlocked.Read(ref _requestCount),
                Interlocked.Read(ref _rateLimitedResponseCount),
                Interlocked.Read(ref _parsingFailureCount),
                latencySampleCount,
                totalLatencyMilliseconds,
                latencySampleCount == 0 ? 0 : totalLatencyMilliseconds / latencySampleCount);
        }
    }
}

internal sealed class NullMarketDataDiagnostics : IMarketDataDiagnosticsRecorder
{
    public static readonly NullMarketDataDiagnostics Instance = new();

    private NullMarketDataDiagnostics()
    {
    }

    public MarketDataDiagnosticsSnapshot GetSnapshot() => new([]);

    public void RecordRequest(Gw2MarketDataEndpoint endpoint, TimeSpan latency)
    {
    }

    public void RecordRateLimitedResponse(Gw2MarketDataEndpoint endpoint)
    {
    }

    public void RecordParsingFailure(Gw2MarketDataEndpoint endpoint)
    {
    }
}

using System.Text.Json;
using Gw2Tp.Application.MarketData;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Gw2Tp.IntegrationTests;

public sealed class MarketDataDiagnosticsEndpointTests
{
    [Fact]
    public async Task Diagnostics_endpoint_exposes_only_safe_aggregate_values()
    {
        var snapshot = new MarketDataDiagnosticsSnapshot(
        [
            new MarketDataEndpointDiagnostics(
                "commerce/prices",
                RequestCount: 4,
                CacheHitCount: 3,
                CacheMissCount: 1,
                RateLimitedResponseCount: 1,
                ParsingFailureCount: 2,
                LatencySampleCount: 4,
                TotalRequestLatencyMilliseconds: 45,
                AverageRequestLatencyMilliseconds: 11),
        ]);
        using var factory = new TestWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IMarketDataDiagnostics>();
                    services.AddSingleton<IMarketDataDiagnostics>(
                        new TestMarketDataDiagnostics(snapshot));
                });
            });
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/diagnostics/market-data");
        var responseBody = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(responseBody);
        var endpoint = document.RootElement.GetProperty("endpoints")[0];
        var propertyNames = endpoint
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal("commerce/prices", endpoint.GetProperty("endpoint").GetString());
        Assert.Equal(4, endpoint.GetProperty("requestCount").GetInt64());
        Assert.Equal(3, endpoint.GetProperty("cacheHitCount").GetInt64());
        Assert.Equal(1, endpoint.GetProperty("cacheMissCount").GetInt64());
        Assert.Equal(1, endpoint.GetProperty("rateLimitedResponseCount").GetInt64());
        Assert.Equal(2, endpoint.GetProperty("parsingFailureCount").GetInt64());
        Assert.Equal(4, endpoint.GetProperty("latencySampleCount").GetInt64());
        Assert.Equal(45, endpoint.GetProperty("totalRequestLatencyMilliseconds").GetInt64());
        Assert.Equal(11, endpoint.GetProperty("averageRequestLatencyMilliseconds").GetInt64());
        Assert.Equal(
        [
            "averageRequestLatencyMilliseconds",
            "cacheHitCount",
            "cacheMissCount",
            "endpoint",
            "latencySampleCount",
            "parsingFailureCount",
            "rateLimitedResponseCount",
            "requestCount",
            "totalRequestLatencyMilliseconds",
        ],
        propertyNames);
    }

    private sealed class TestMarketDataDiagnostics : IMarketDataDiagnostics
    {
        private readonly MarketDataDiagnosticsSnapshot _snapshot;

        public TestMarketDataDiagnostics(MarketDataDiagnosticsSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public MarketDataDiagnosticsSnapshot GetSnapshot() => _snapshot;
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Domain.MarketData;
using Gw2Tp.Infrastructure.Preferences;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Gw2Tp.IntegrationTests;

public sealed class MarketResearchWatchlistEndpointTests
{
    [Fact]
    public async Task Watchlist_compares_local_observations_and_discloses_coverage()
    {
        using var factory = CreateFactory(CreateDatabasePath());
        var client = factory.CreateClient();

        using var addFirst = await client.PostAsJsonAsync("/api/market-research/watchlist", new { itemId = 101 });
        using var addSecond = await client.PostAsJsonAsync("/api/market-research/watchlist", new { itemId = 202 });
        Assert.Equal(HttpStatusCode.NoContent, addFirst.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, addSecond.StatusCode);

        var snapshotStore = factory.Services.GetRequiredService<IMarketSnapshotStore>();
        var capturedAtUtc = new DateTimeOffset(2026, 8, 28, 8, 0, 0, TimeSpan.Zero);
        foreach (var index in Enumerable.Range(0, HistoricalMarketAnalyticsCalculator.MinimumObservationCount))
        {
            await snapshotStore.AppendAsync(
                CreateSnapshot(101, index, capturedAtUtc, buyPrice: 100 + index, buyQuantity: 20 + index),
                CancellationToken.None);
        }

        using var response = await client.GetAsync("/api/market-research/watchlist");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(MarketSamplingPolicy.MaximumTrackedItemCount, root.GetProperty("maximumTrackedItemCount").GetInt32());
        Assert.Equal(2, root.GetProperty("trackedItemCount").GetInt32());
        var items = root.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal([101, 202], items.Select(item => item.GetProperty("itemId").GetInt32()));

        var observed = items[0];
        Assert.Equal(HistoricalMarketAnalyticsCalculator.MinimumObservationCount, observed.GetProperty("coverage").GetProperty("observationCount").GetInt32());
        Assert.Equal("2026-08-28T08:00:00+00:00", observed.GetProperty("coverage").GetProperty("firstCapturedAtUtc").GetString());
        Assert.Equal(102, observed.GetProperty("buyPrices").GetProperty("tenthPercentileCopper").GetInt32());
        Assert.Equal(114, observed.GetProperty("buyPrices").GetProperty("medianCopper").GetInt32());
        Assert.Equal(126, observed.GetProperty("buyPrices").GetProperty("ninetiethPercentileCopper").GetInt32());

        var noEvidence = items[1];
        Assert.Equal(0, noEvidence.GetProperty("coverage").GetProperty("observationCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, noEvidence.GetProperty("buyPrices").GetProperty("medianCopper").ValueKind);

        using var removeResponse = await client.DeleteAsync("/api/market-research/watchlist/202");
        Assert.Equal(HttpStatusCode.NoContent, removeResponse.StatusCode);
    }

    [Fact]
    public async Task Watchlist_rejects_an_invalid_or_duplicate_item_id()
    {
        using var factory = CreateFactory(CreateDatabasePath());
        var client = factory.CreateClient();

        using var invalid = await client.PostAsJsonAsync("/api/market-research/watchlist", new { itemId = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var first = await client.PostAsJsonAsync("/api/market-research/watchlist", new { itemId = 101 });
        using var duplicate = await client.PostAsJsonAsync("/api/market-research/watchlist", new { itemId = 101 });
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    private static MarketPriceSnapshot CreateSnapshot(
        int itemId,
        int index,
        DateTimeOffset capturedAtUtc,
        int buyPrice,
        int buyQuantity) => new(
        Guid.Parse($"00000000-0000-0000-0000-{itemId + index:000000000000}"),
        new MarketPrice(
            itemId,
            IsWhitelisted: false,
            new MarketOrderSummary(buyQuantity, buyPrice),
            new MarketOrderSummary(50, buyPrice + 10)),
        new DataFreshness(capturedAtUtc.AddHours(index), capturedAtUtc.AddHours(index + 1)));

    private static WebApplicationFactory<Program> CreateFactory(string databasePath) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting(UserSessionPreferencesServiceCollectionExtensions.DatabasePathConfigurationKey, databasePath);
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IGw2ApiClient>();
                    services.AddSingleton<IGw2ApiClient, UnavailableMarketClient>();
                });
            });

    private static string CreateDatabasePath() => Path.Combine(
        Path.GetTempPath(),
        "TyrianLedger",
        "IntegrationTests",
        $"market-research-{Guid.NewGuid():N}.db");
}

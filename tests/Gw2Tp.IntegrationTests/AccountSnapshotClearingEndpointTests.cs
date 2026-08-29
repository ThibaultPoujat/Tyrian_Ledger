using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gw2Tp.Application.AccountSnapshots;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Application.Operations;
using Gw2Tp.Domain.MarketData;
using Gw2Tp.Infrastructure.Preferences;
using Gw2Tp.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Gw2Tp.IntegrationTests;

public sealed class AccountSnapshotClearingEndpointTests
{
    [Fact]
    public async Task Clear_endpoint_clears_snapshots_without_erasing_local_history_or_preferences()
    {
        var databasePath = CreateDatabasePath();
        var cacheClearer = new RecordingAccountSnapshotCacheClearer();
        using var factory = CreateFactory(databasePath, cacheClearer);
        var client = factory.CreateClient();
        var operationHistoryStore = factory.Services.GetRequiredService<IOperationHistoryStore>();
        var marketSnapshotStore = factory.Services.GetRequiredService<IMarketSnapshotStore>();
        await operationHistoryStore.CreateAsync(
            OperationHistoryStatisticsFixtures.CreatePopulated()[0],
            CancellationToken.None);
        var capturedAtUtc = new DateTimeOffset(2026, 8, 28, 9, 0, 0, TimeSpan.Zero);
        await marketSnapshotStore.AppendAsync(
            new MarketPriceSnapshot(
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                new MarketPrice(
                    19721,
                    true,
                    new MarketOrderSummary(10, 100),
                    new MarketOrderSummary(10, 110)),
                new DataFreshness(capturedAtUtc, capturedAtUtc.AddMinutes(5))),
            CancellationToken.None);

        using var savePreferencesResponse = await client.PutAsJsonAsync("/api/preferences/user-session", new
        {
            capitalLimitCopper = 120_000,
            minimumProfitCopper = 500,
            riskPreference = "normal",
            strategyPreference = "market-flip",
            allocationPercent = 65,
        });
        savePreferencesResponse.EnsureSuccessStatusCode();

        using var clearResponse = await client.DeleteAsync("/api/account/snapshots");

        Assert.Equal(HttpStatusCode.NoContent, clearResponse.StatusCode);
        Assert.Equal(1, cacheClearer.ClearCallCount);

        using var preferencesResponse = await client.GetAsync("/api/preferences/user-session");
        preferencesResponse.EnsureSuccessStatusCode();
        using var preferencesDocument = JsonDocument.Parse(await preferencesResponse.Content.ReadAsStringAsync());
        var preferences = preferencesDocument.RootElement;
        Assert.Equal(120_000, preferences.GetProperty("capitalLimitCopper").GetInt64());
        Assert.Equal(500, preferences.GetProperty("minimumProfitCopper").GetInt64());
        Assert.Equal("normal", preferences.GetProperty("riskPreference").GetString());
        Assert.Equal("market-flip", preferences.GetProperty("strategyPreference").GetString());
        Assert.Equal(65, preferences.GetProperty("allocationPercent").GetInt32());

        using var historyResponse = await client.GetAsync("/api/history/statistics");
        historyResponse.EnsureSuccessStatusCode();
        using var historyDocument = JsonDocument.Parse(await historyResponse.Content.ReadAsStringAsync());
        Assert.Equal(1, historyDocument.RootElement.GetProperty("operationCount").GetInt32());
        Assert.Single(await marketSnapshotStore.ListPriceSnapshotsAsync(19721, CancellationToken.None));
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string databasePath,
        IAccountSnapshotCacheClearer cacheClearer) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting(
                    UserSessionPreferencesServiceCollectionExtensions.DatabasePathConfigurationKey,
                    databasePath);
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IGw2ApiClient>();
                    services.AddSingleton<IGw2ApiClient, UnavailableMarketClient>();
                    services.RemoveAll<IAccountSnapshotCacheClearer>();
                    services.AddSingleton(cacheClearer);
                });
            });

    private static string CreateDatabasePath() => Path.Combine(
        Path.GetTempPath(),
        "TyrianLedger",
        "IntegrationTests",
        $"account-snapshot-clearing-{Guid.NewGuid():N}.db");

    private sealed class RecordingAccountSnapshotCacheClearer : IAccountSnapshotCacheClearer
    {
        public int ClearCallCount { get; private set; }

        public void ClearCachedSnapshots() => ClearCallCount++;
    }
}

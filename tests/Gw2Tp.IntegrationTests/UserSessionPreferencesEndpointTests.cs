using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Infrastructure.Preferences;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Gw2Tp.IntegrationTests;

public sealed class UserSessionPreferencesEndpointTests
{
    [Fact]
    public async Task Preferences_are_persisted_in_the_local_sqlite_profile_across_application_instances()
    {
        var databasePath = CreateDatabasePath();

        using (var firstFactory = CreateFactory(databasePath))
        {
            var firstClient = firstFactory.CreateClient();

            using var defaultsResponse = await firstClient.GetAsync("/api/preferences/user-session");
            defaultsResponse.EnsureSuccessStatusCode();
            using var defaultsDocument = JsonDocument.Parse(await defaultsResponse.Content.ReadAsStringAsync());
            var defaults = defaultsDocument.RootElement;
            Assert.Equal(JsonValueKind.Null, defaults.GetProperty("capitalLimitCopper").ValueKind);
            Assert.Equal(JsonValueKind.Null, defaults.GetProperty("minimumProfitCopper").ValueKind);
            Assert.Equal("all", defaults.GetProperty("riskPreference").GetString());
            Assert.Equal("all", defaults.GetProperty("strategyPreference").GetString());
            Assert.Equal(100, defaults.GetProperty("allocationPercent").GetInt32());

            using var saveResponse = await SavePreferencesAsync(
                firstClient,
                capitalLimitCopper: 120_000,
                minimumProfitCopper: 500,
                riskPreference: "normal",
                strategyPreference: "market-flip",
                allocationPercent: 65);
            saveResponse.EnsureSuccessStatusCode();
        }

        using var secondFactory = CreateFactory(databasePath);
        var secondClient = secondFactory.CreateClient();
        using var persistedResponse = await secondClient.GetAsync("/api/preferences/user-session");
        persistedResponse.EnsureSuccessStatusCode();
        using var persistedDocument = JsonDocument.Parse(await persistedResponse.Content.ReadAsStringAsync());
        var persisted = persistedDocument.RootElement;

        Assert.Equal(120_000, persisted.GetProperty("capitalLimitCopper").GetInt64());
        Assert.Equal(500, persisted.GetProperty("minimumProfitCopper").GetInt64());
        Assert.Equal("normal", persisted.GetProperty("riskPreference").GetString());
        Assert.Equal("market-flip", persisted.GetProperty("strategyPreference").GetString());
        Assert.Equal(65, persisted.GetProperty("allocationPercent").GetInt32());
    }

    [Fact]
    public async Task Preferences_endpoint_rejects_invalid_numeric_ranges_and_option_values()
    {
        using var factory = CreateFactory(CreateDatabasePath());
        var client = factory.CreateClient();
        using var response = await client.PutAsJsonAsync("/api/preferences/user-session", new
        {
            capitalLimitCopper = -1,
            minimumProfitCopper = 9_007_199_254_740_992,
            riskPreference = "high",
            strategyPreference = "speculation",
            allocationPercent = 101,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = document.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("capitalLimitCopper", out _));
        Assert.True(errors.TryGetProperty("minimumProfitCopper", out _));
        Assert.True(errors.TryGetProperty("riskPreference", out _));
        Assert.True(errors.TryGetProperty("strategyPreference", out _));
        Assert.True(errors.TryGetProperty("allocationPercent", out _));
    }

    [Fact]
    public async Task Saved_preferences_filter_and_rerank_dashboard_results_deterministically()
    {
        using var factory = CreateFactory(CreateDatabasePath());
        var client = factory.CreateClient();
        using var saveResponse = await SavePreferencesAsync(
            client,
            capitalLimitCopper: 1_600,
            minimumProfitCopper: 0,
            riskPreference: "normal",
            strategyPreference: "all",
            allocationPercent: 50);
        saveResponse.EnsureSuccessStatusCode();

        using var dashboardResponse = await client.GetAsync("/api/dashboard/opportunities");
        dashboardResponse.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await dashboardResponse.Content.ReadAsStringAsync());
        var opportunities = document.RootElement.GetProperty("opportunities").EnumerateArray().ToArray();

        Assert.Equal(2, opportunities.Length);
        Assert.Equal([900_004, 900_001], opportunities.Select(opportunity => opportunity.GetProperty("itemId").GetInt32()));
        Assert.Equal([1, 2], opportunities.Select(opportunity => opportunity.GetProperty("rank").GetInt32()));
        Assert.All(
            opportunities,
            opportunity => Assert.Equal("normal", opportunity.GetProperty("confidence").GetString()));
    }

    private static WebApplicationFactory<Program> CreateFactory(string databasePath) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting(UserSessionPreferencesServiceCollectionExtensions.DatabasePathConfigurationKey, databasePath);
                builder.ConfigureServices(services => services.RemoveAll<IGw2ApiClient>());
            });

    private static Task<HttpResponseMessage> SavePreferencesAsync(
        HttpClient client,
        long? capitalLimitCopper,
        long? minimumProfitCopper,
        string riskPreference,
        string strategyPreference,
        int allocationPercent) => client.PutAsJsonAsync("/api/preferences/user-session", new
        {
            capitalLimitCopper,
            minimumProfitCopper,
            riskPreference,
            strategyPreference,
            allocationPercent,
        });

    private static string CreateDatabasePath() => Path.Combine(
        Path.GetTempPath(),
        "TyrianLedger",
        "IntegrationTests",
        $"preferences-{Guid.NewGuid():N}.db");
}

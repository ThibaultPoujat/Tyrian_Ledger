using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gw2Tp.Infrastructure.Preferences;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
            Assert.Equal(1, defaults.GetProperty("analysisQuantity").GetInt32());
            Assert.Equal(JsonValueKind.Null, defaults.GetProperty("listingFeeBasisPoints").ValueKind);
            Assert.Equal(JsonValueKind.Null, defaults.GetProperty("listingFeeRounding").ValueKind);
            Assert.Equal(JsonValueKind.Null, defaults.GetProperty("exchangeFeeBasisPoints").ValueKind);
            Assert.Equal(JsonValueKind.Null, defaults.GetProperty("exchangeFeeRounding").ValueKind);

            using var saveResponse = await SavePreferencesAsync(
                firstClient,
                capitalLimitCopper: 120_000,
                minimumProfitCopper: 500,
                riskPreference: "normal",
                strategyPreference: "market-flip",
                allocationPercent: 65,
                analysisQuantity: 4,
                listingFeeBasisPoints: 500,
                listingFeeRounding: "down",
                exchangeFeeBasisPoints: 1_000,
                exchangeFeeRounding: "up");
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
        Assert.Equal(4, persisted.GetProperty("analysisQuantity").GetInt32());
        Assert.Equal(500, persisted.GetProperty("listingFeeBasisPoints").GetInt32());
        Assert.Equal("down", persisted.GetProperty("listingFeeRounding").GetString());
        Assert.Equal(1_000, persisted.GetProperty("exchangeFeeBasisPoints").GetInt32());
        Assert.Equal("up", persisted.GetProperty("exchangeFeeRounding").GetString());
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
            analysisQuantity = 0,
            listingFeeBasisPoints = 10_001,
            listingFeeRounding = "nearest",
            exchangeFeeBasisPoints = 500,
            exchangeFeeRounding = "down",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = document.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("capitalLimitCopper", out _));
        Assert.True(errors.TryGetProperty("minimumProfitCopper", out _));
        Assert.True(errors.TryGetProperty("riskPreference", out _));
        Assert.True(errors.TryGetProperty("strategyPreference", out _));
        Assert.True(errors.TryGetProperty("allocationPercent", out _));
        Assert.True(errors.TryGetProperty("analysisQuantity", out _));
        Assert.True(errors.TryGetProperty("listingFeeBasisPoints", out _));
        Assert.True(errors.TryGetProperty("listingFeeRounding", out _));
    }

    [Fact]
    public async Task Preferences_endpoint_rejects_incomplete_fee_rules()
    {
        using var factory = CreateFactory(CreateDatabasePath());
        var client = factory.CreateClient();
        using var response = await client.PutAsJsonAsync("/api/preferences/user-session", new
        {
            capitalLimitCopper = (long?)null,
            minimumProfitCopper = (long?)null,
            riskPreference = "all",
            strategyPreference = "all",
            allocationPercent = 100,
            analysisQuantity = 1,
            listingFeeBasisPoints = 500,
            listingFeeRounding = "down",
            exchangeFeeBasisPoints = (int?)null,
            exchangeFeeRounding = (string?)null,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("fees", out _));
    }

    private static WebApplicationFactory<Program> CreateFactory(string databasePath) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting(UserSessionPreferencesServiceCollectionExtensions.DatabasePathConfigurationKey, databasePath);
            });

    private static Task<HttpResponseMessage> SavePreferencesAsync(
        HttpClient client,
        long? capitalLimitCopper,
        long? minimumProfitCopper,
        string riskPreference,
        string strategyPreference,
        int allocationPercent,
        int analysisQuantity,
        int? listingFeeBasisPoints,
        string? listingFeeRounding,
        int? exchangeFeeBasisPoints,
        string? exchangeFeeRounding) => client.PutAsJsonAsync("/api/preferences/user-session", new
        {
            capitalLimitCopper,
            minimumProfitCopper,
            riskPreference,
            strategyPreference,
            allocationPercent,
            analysisQuantity,
            listingFeeBasisPoints,
            listingFeeRounding,
            exchangeFeeBasisPoints,
            exchangeFeeRounding,
        });

    private static string CreateDatabasePath() => Path.Combine(
        Path.GetTempPath(),
        "TyrianLedger",
        "IntegrationTests",
        $"preferences-{Guid.NewGuid():N}.db");
}

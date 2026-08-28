using System.Net;
using System.Text.Json;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Time;
using Gw2Tp.Infrastructure.Preferences;
using Gw2Tp.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Gw2Tp.IntegrationTests;

public sealed class DashboardOpportunitiesEndpointTests
{
    [Fact]
    public async Task Dashboard_endpoint_returns_ranked_local_sample_data_without_the_gw2_client()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting(
                    UserSessionPreferencesServiceCollectionExtensions.DatabasePathConfigurationKey,
                    CreateDatabasePath());
                builder.ConfigureServices(services => services.RemoveAll<IGw2ApiClient>());
            });
        var client = factory.CreateClient();

        var firstResponse = await client.GetAsync("/api/dashboard/opportunities");
        var secondResponse = await client.GetAsync("/api/dashboard/opportunities");

        firstResponse.EnsureSuccessStatusCode();
        secondResponse.EnsureSuccessStatusCode();
        using var firstDocument = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
        using var secondDocument = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());
        var firstRoot = firstDocument.RootElement;
        var firstOpportunities = firstRoot.GetProperty("opportunities");
        var secondOpportunities = secondDocument.RootElement.GetProperty("opportunities");

        Assert.True(firstRoot.GetProperty("isSampleData").GetBoolean());
        Assert.Equal(
            "Deterministic local sample data. No live market scan was performed.",
            firstRoot.GetProperty("sourceDescription").GetString());
        Assert.Equal(5, firstOpportunities.GetArrayLength());
        Assert.Equal(
            [1, 2, 3, 4, 5],
            firstOpportunities.EnumerateArray().Select(opportunity => opportunity.GetProperty("rank").GetInt32()));
        Assert.True(firstOpportunities.EnumerateArray()
            .Select(opportunity => opportunity.GetProperty("scoreBasisPoints").GetInt32())
            .Zip(
                firstOpportunities.EnumerateArray()
                    .Select(opportunity => opportunity.GetProperty("scoreBasisPoints").GetInt32())
                    .Skip(1),
                (current, next) => current >= next)
            .All(isDescending => isDescending));
        Assert.All(
            firstOpportunities.EnumerateArray(),
            opportunity => Assert.Equal("market-flip", opportunity.GetProperty("strategy").GetString()));
        Assert.Contains(
            firstOpportunities.EnumerateArray(),
            opportunity => opportunity.GetProperty("freshness").GetString() == "current");
        Assert.Contains(
            firstOpportunities.EnumerateArray(),
            opportunity => opportunity.GetProperty("freshness").GetString() == "stale");
        Assert.Equal(
            ["high", "low", "medium", "ongoing-patient", "very-low"],
            firstOpportunities
                .EnumerateArray()
                .Select(opportunity => opportunity.GetProperty("effortCategory").GetString()
                    ?? throw new InvalidOperationException("Dashboard effort category must be present."))
                .Order()
                .ToArray());
        Assert.Equal(
            firstOpportunities.EnumerateArray().Select(opportunity => opportunity.GetProperty("itemId").GetInt32()),
            secondOpportunities.EnumerateArray().Select(opportunity => opportunity.GetProperty("itemId").GetInt32()));
    }

    [Fact]
    public async Task Dashboard_detail_matches_the_known_deterministic_fixture()
    {
        var frozenNow = new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting(
                    UserSessionPreferencesServiceCollectionExtensions.DatabasePathConfigurationKey,
                    CreateDatabasePath());
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IGw2ApiClient>();
                    services.RemoveAll<IClock>();
                    services.AddSingleton<IClock>(new FrozenClock(frozenNow));
                });
            });
        var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/dashboard/opportunities");
        response.EnsureSuccessStatusCode();
        using var responseDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        using var fixtureDocument = await new JsonFixtureLoader(
                Path.Combine(AppContext.BaseDirectory, "Fixtures"))
            .LoadAsync("dashboard/opportunity-detail.json");

        var actualDetail = responseDocument.RootElement
            .GetProperty("opportunities")
            .EnumerateArray()
            .Single(opportunity => opportunity.GetProperty("itemId").GetInt32() == 900_004)
            .GetProperty("detail");

        Assert.True(
            JsonElement.DeepEquals(fixtureDocument.RootElement, actualDetail),
            "The dashboard detail must remain consistent with its deterministic fixture.");
    }

    [Fact]
    public async Task Dashboard_endpoint_filters_the_session_shortlist_by_effort_category()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting(
                    UserSessionPreferencesServiceCollectionExtensions.DatabasePathConfigurationKey,
                    CreateDatabasePath());
                builder.ConfigureServices(services => services.RemoveAll<IGw2ApiClient>());
            });
        var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/dashboard/opportunities?effortCategory=high");

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var opportunity = Assert.Single(document.RootElement.GetProperty("opportunities").EnumerateArray());
        Assert.Equal("high", opportunity.GetProperty("effortCategory").GetString());
        Assert.Equal(1, opportunity.GetProperty("rank").GetInt32());
    }

    [Fact]
    public async Task Dashboard_endpoint_rejects_unknown_effort_category_values()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting(
                    UserSessionPreferencesServiceCollectionExtensions.DatabasePathConfigurationKey,
                    CreateDatabasePath());
                builder.ConfigureServices(services => services.RemoveAll<IGw2ApiClient>());
            });
        var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/dashboard/opportunities?effortCategory=minutes");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("effortCategory", out _));
    }

    private static string CreateDatabasePath() => Path.Combine(
        Path.GetTempPath(),
        "TyrianLedger",
        "IntegrationTests",
        $"dashboard-preferences-{Guid.NewGuid():N}.db");
}

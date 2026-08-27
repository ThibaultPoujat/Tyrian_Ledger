using System.Text.Json;
using Gw2Tp.Application.MarketData;
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
        Assert.Equal(4, firstOpportunities.GetArrayLength());
        Assert.Equal(
            [1, 2, 3, 4],
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
            firstOpportunities.EnumerateArray().Select(opportunity => opportunity.GetProperty("itemId").GetInt32()),
            secondOpportunities.EnumerateArray().Select(opportunity => opportunity.GetProperty("itemId").GetInt32()));
    }
}

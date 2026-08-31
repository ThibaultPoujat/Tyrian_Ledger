using System.Net;
using Xunit;

namespace Gw2Tp.IntegrationTests;

public sealed class RetiredProductEndpointsTests
{
    [Theory]
    [InlineData("GET", "/api/status")]
    [InlineData("GET", "/api/account/access")]
    [InlineData("DELETE", "/api/account/snapshots")]
    [InlineData("GET", "/api/market-research/watchlist")]
    [InlineData("GET", "/api/history/statistics")]
    [InlineData("GET", "/api/dashboard/opportunities")]
    public async Task Retired_endpoint_is_not_reachable(string method, string path)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

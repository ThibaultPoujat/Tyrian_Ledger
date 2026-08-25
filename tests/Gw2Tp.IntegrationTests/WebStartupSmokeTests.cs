using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Gw2Tp.IntegrationTests;

// Skeleton smoke test: confirms the local app starts (TKT-M1-01). No business assertions.
public class WebStartupSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public WebStartupSmokeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Root_endpoint_responds_when_app_starts()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_endpoint_responds_when_app_starts()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

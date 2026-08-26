using System.Net;
using System.Collections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Validation;
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

    [Fact]
    public void Built_in_minimal_api_validation_is_registered_when_app_starts()
    {
        var validationOptions = _factory.Services
            .GetRequiredService<IOptions<ValidationOptions>>()
            .Value;
        var resolvers = typeof(ValidationOptions)
            .GetProperty("Resolvers")
            ?.GetValue(validationOptions) as IEnumerable;

        Assert.NotNull(resolvers);
        Assert.NotEmpty(resolvers.Cast<object>());
    }
}

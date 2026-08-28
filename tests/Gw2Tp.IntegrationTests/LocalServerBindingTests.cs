using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Gw2Tp.Web;
using Xunit;

namespace Gw2Tp.IntegrationTests;

public sealed class LocalServerBindingTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public async Task Default_url_is_loopback_only_for_local_startup_environments(string environmentName)
    {
        using var factory = new TestWebApplicationFactory()
            .WithWebHostBuilder(builder => builder.UseSetting(WebHostDefaults.EnvironmentKey, environmentName));
        var client = factory.CreateClient();
        var configuration = factory.Services.GetRequiredService<IConfiguration>();

        var urls = LocalServerBinding.ResolveUrls(configuration);
        var response = await client.GetAsync("/healthz");

        Assert.Equal(LocalServerBinding.DefaultUrl, urls);
        Assert.Equal("http://127.0.0.1:5000", urls);
        Assert.Equal(environmentName, factory.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Explicit_server_url_override_is_preserved()
    {
        const string configuredUrl = "http://127.0.0.1:5050";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [WebHostDefaults.ServerUrlsKey] = configuredUrl,
            })
            .Build();

        var urls = LocalServerBinding.ResolveUrls(configuration);

        Assert.Equal(configuredUrl, urls);
    }
}

using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Gw2Tp.Web;
using Xunit;

namespace Gw2Tp.IntegrationTests;

public sealed class SecurityHeaderTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public SecurityHeaderTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/healthz", HttpStatusCode.OK)]
    [InlineData("/not-found", HttpStatusCode.NotFound)]
    public async Task Responses_include_the_local_http_security_baseline_without_hsts(
        string requestPath,
        HttpStatusCode expectedStatusCode)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(requestPath);

        Assert.Equal(expectedStatusCode, response.StatusCode);
        Assert.Equal(SecurityHeaderApplicationBuilderExtensions.ContentSecurityPolicy, GetHeader(response, "Content-Security-Policy"));
        Assert.Equal(SecurityHeaderApplicationBuilderExtensions.XContentTypeOptions, GetHeader(response, "X-Content-Type-Options"));
        Assert.Equal(SecurityHeaderApplicationBuilderExtensions.XFrameOptions, GetHeader(response, "X-Frame-Options"));
        Assert.Equal(SecurityHeaderApplicationBuilderExtensions.ReferrerPolicy, GetHeader(response, "Referrer-Policy"));
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    private static string GetHeader(HttpResponseMessage response, string headerName)
    {
        return Assert.Single(response.Headers.GetValues(headerName));
    }
}

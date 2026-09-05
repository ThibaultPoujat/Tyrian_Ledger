using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Gw2Tp.Web.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Gw2Tp.Web.Tests;

public sealed class LocalHostIntegrationTests
{
    [Fact]
    public async Task StartsWithoutArenaNetKeyAndReturnsHealthyStatus()
    {
        await using var app = await StartApplicationAsync("Production");
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/api/health");
        var payload = await response.Content.ReadFromJsonAsync<HealthPayload>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("healthy", payload?.Status);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public void DefaultsToExplicitIpv4AndIpv6LoopbackBindings()
    {
        var options = new LocalHostOptions();

        Assert.Equal(5080, options.Port);
        Assert.Equal([IPAddress.Loopback, IPAddress.IPv6Loopback], options.GetListenAddresses());
    }

    [Fact]
    public async Task ActualKestrelHostListensOnBothDefaultLoopbackAddresses()
    {
        var port = ReserveAvailablePort();
        await using var app = Program.CreateApplication([], builder =>
        {
            builder.Environment.EnvironmentName = "Production";
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TyrianLedger:Host:Port"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
        });
        await app.StartAsync();

        var server = app.Services.GetRequiredService<IServer>();
        var boundAddresses = server.Features.Get<IServerAddressesFeature>()!.Addresses;
        Assert.Contains($"http://127.0.0.1:{port}", boundAddresses);
        Assert.Contains($"http://[::1]:{port}", boundAddresses);

        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });
        using var ipv4Response = await client.GetAsync($"http://127.0.0.1:{port}/api/health");
        using var ipv6Response = await client.GetAsync($"http://[::1]:{port}/api/health");

        Assert.Equal(HttpStatusCode.OK, ipv4Response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ipv6Response.StatusCode);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("192.168.1.50")]
    [InlineData("localhost")]
    public void RejectsWildcardLanAndNonExplicitListenAddresses(string address)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CreateApplication(
            "Production",
            new Dictionary<string, string?>
            {
                ["TyrianLedger:Host:ListenAddresses:0"] = address,
                ["TyrianLedger:Host:ListenAddresses:1"] = null,
            }));

        Assert.Contains("explicit IPv4 or IPv6 loopback", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("urls", "http://0.0.0.0:5080")]
    [InlineData("http_ports", "5080")]
    [InlineData("Kestrel:Endpoints:Public:Url", "http://*:5080")]
    public void RejectsAlternateServerBindingOverrides(string key, string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CreateApplication(
            "Production",
            new Dictionary<string, string?> { [key] = value }));

        Assert.Contains("disabled", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("*.example")]
    public void RejectsEmptyOrWildcardAllowedHosts(string allowedHost)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CreateApplication(
            "Production",
            new Dictionary<string, string?>
            {
                ["TyrianLedger:Host:AllowedHosts:0"] = allowedHost,
                ["TyrianLedger:Host:AllowedHosts:1"] = null,
                ["TyrianLedger:Host:AllowedHosts:2"] = null,
            }));

        Assert.Contains("non-wildcard allowlist", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllowsApprovedHostAndRejectsSpoofedHostHeaders()
    {
        await using var app = await StartApplicationAsync("Production");
        using var client = app.GetTestClient();

        using var approved = await SendWithHostAsync(client, "127.0.0.1:5080");
        using var spoofed = await SendWithHostAsync(client, "attacker.example");
        using var rebindingShaped = await SendWithHostAsync(client, "127.0.0.1.attacker.example");

        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, spoofed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, rebindingShaped.StatusCode);
    }

    [Fact]
    public async Task DevelopmentCorsAllowsOnlyExactTrustedOrigins()
    {
        await using var app = await StartApplicationAsync("Development");
        using var client = app.GetTestClient();

        using var trusted = await SendWithOriginAsync(client, HttpMethod.Get, "http://localhost:5173");
        using var wrongPort = await SendWithOriginAsync(client, HttpMethod.Get, "http://localhost:5174");
        using var arbitrary = await SendWithOriginAsync(client, HttpMethod.Get, "https://attacker.example");

        Assert.Equal("http://localhost:5173", GetAllowedOrigin(trusted));
        Assert.Null(GetAllowedOrigin(wrongPort));
        Assert.Null(GetAllowedOrigin(arbitrary));
    }

    [Fact]
    public async Task ProductionDoesNotEnableCrossOriginAccess()
    {
        await using var app = await StartApplicationAsync("Production");
        using var client = app.GetTestClient();

        using var response = await SendWithOriginAsync(client, HttpMethod.Get, "https://attacker.example");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(GetAllowedOrigin(response));
    }

    [Fact]
    public async Task UnsafeRequestsRequireSameOrExplicitDevelopmentOriginIndependentlyOfCors()
    {
        await using var productionApp = await StartApplicationAsync("Production");
        using var productionClient = productionApp.GetTestClient();

        using var missingOrigin = await productionClient.PostAsync("/api/health", null);
        using var untrustedOrigin = await SendWithOriginAsync(
            productionClient,
            HttpMethod.Post,
            "https://attacker.example",
            includeRequestHeader: true);
        using var sameOriginWithoutHeader = await SendWithOriginAsync(
            productionClient,
            HttpMethod.Post,
            "http://localhost");
        using var sameOrigin = await SendWithOriginAsync(
            productionClient,
            HttpMethod.Post,
            "http://localhost",
            includeRequestHeader: true);

        Assert.Equal(HttpStatusCode.Forbidden, missingOrigin.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, untrustedOrigin.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, sameOriginWithoutHeader.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, sameOrigin.StatusCode);

        await using var developmentApp = await StartApplicationAsync("Development");
        using var developmentClient = developmentApp.GetTestClient();
        using var trustedDevelopmentOrigin = await SendWithOriginAsync(
            developmentClient,
            HttpMethod.Post,
            "http://localhost:5173",
            includeRequestHeader: true);

        Assert.Equal(HttpStatusCode.NotFound, trustedDevelopmentOrigin.StatusCode);
    }

    [Fact]
    public async Task ProductionServesFrontendAndApiFromTheSameOrigin()
    {
        var frontendDirectory = Directory.CreateTempSubdirectory("tyrian-ledger-frontend-");
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(frontendDirectory.FullName, "index.html"),
                "<!doctype html><title>Tyrian Ledger test shell</title><div id=\"root\"></div>");

            await using var app = await StartApplicationAsync(
                "Production",
                new Dictionary<string, string?>
                {
                    ["TyrianLedger:Frontend:Path"] = frontendDirectory.FullName,
                });
            using var client = app.GetTestClient();

            var frontend = await client.GetStringAsync("/dashboard/route");
            using var health = await client.GetAsync("/api/health");
            using var unknownApi = await client.GetAsync("/api/not-a-route");

            Assert.Contains("Tyrian Ledger test shell", frontend, StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, unknownApi.StatusCode);
        }
        finally
        {
            frontendDirectory.Delete(recursive: true);
        }
    }

    private static async Task<WebApplication> StartApplicationAsync(
        string environment,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        var app = CreateApplication(environment, settings);
        await app.StartAsync();
        return app;
    }

    private static WebApplication CreateApplication(
        string environment,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        return Program.CreateApplication([], builder =>
        {
            builder.Environment.EnvironmentName = environment;
            builder.WebHost.UseTestServer();
            if (string.Equals(environment, "Development", StringComparison.Ordinal))
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TyrianLedger:Host:TrustedDevelopmentOrigins:0"] = "http://localhost:5173",
                    ["TyrianLedger:Host:TrustedDevelopmentOrigins:1"] = "http://127.0.0.1:5173",
                });
            }

            if (settings is not null)
            {
                builder.Configuration.AddInMemoryCollection(settings);
            }
        });
    }

    private static Task<HttpResponseMessage> SendWithHostAsync(HttpClient client, string host)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Host = host;
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SendWithOriginAsync(
        HttpClient client,
        HttpMethod method,
        string origin,
        bool includeRequestHeader = false)
    {
        var request = new HttpRequestMessage(method, "/api/health");
        request.Headers.Add("Origin", origin);
        if (includeRequestHeader)
        {
            request.Headers.Add(
                LocalRequestOriginProtectionMiddleware.RequestHeader,
                LocalRequestOriginProtectionMiddleware.RequestHeaderValue);
        }

        return client.SendAsync(request);
    }

    private static string? GetAllowedOrigin(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values)
            ? Assert.Single(values)
            : null;
    }

    private static int ReserveAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed record HealthPayload(string Status);
}

using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Gw2Tp.Application.AccountConnection;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Web.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
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
    public async Task Startup_initializes_the_configured_sqlite_schema()
    {
        var databaseDirectory = Path.Combine(Path.GetTempPath(), "TyrianLedger.Web.Tests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(databaseDirectory, "tyrian-ledger.db");
        try
        {
            await using (var app = await StartApplicationAsync(
                "Production",
                new Dictionary<string, string?>
                {
                    ["TyrianLedger:Database:Path"] = databasePath,
                }))
            {
                Assert.True(File.Exists(databasePath));
                await using var connection = new SqliteConnection($"Data Source={databasePath};Foreign Keys=True");
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM schema_migrations;";
                Assert.Equal(3L, await command.ExecuteScalarAsync());
            }

        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(databaseDirectory))
            {
                Directory.Delete(databaseDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Account_connection_endpoint_returns_only_safe_non_cacheable_status_data()
    {
        const string syntheticKey = "synthetic-key-that-must-not-reach-the-browser";
        const string maliciousMetadata = "<img src=x onerror=alert('synthetic')>";
        await using var app = await StartApplicationAsync(
            "Production",
            configureServices: services =>
            {
                services.RemoveAll<IAccountConnectionStatusService>();
                services.AddSingleton<IAccountConnectionStatusService>(new FixedAccountConnectionStatusService(
                    new AccountConnectionStatus(
                        AccountConnectionState.Valid,
                        ["account", "tradingpost"],
                        [])));
            });
        using var client = app.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/account-connection");
        request.Headers.Add(
            LocalRequestOriginProtectionMiddleware.RequestHeader,
            LocalRequestOriginProtectionMiddleware.RequestHeaderValue);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains("\"state\":\"valid\"", body, StringComparison.Ordinal);
        Assert.Contains("\"grantedPermissions\":[\"account\",\"tradingpost\"]", body, StringComparison.Ordinal);
        Assert.DoesNotContain(syntheticKey, body, StringComparison.Ordinal);
        Assert.DoesNotContain(maliciousMetadata, body, StringComparison.Ordinal);
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Protected_personal_sync_endpoint_returns_only_safe_non_cacheable_data()
    {
        var synchronizationService = new FixedSynchronizationService(
            PersonalTradingPostSynchronizationResult.Succeeded(
                new DateTimeOffset(2026, 9, 6, 14, 0, 0, TimeSpan.Zero),
                completedTransactionCount: 4,
                currentOrderCount: 2,
                new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 9, 6, 13, 0, 0, TimeSpan.Zero)));
        await using var app = await StartApplicationAsync(
            "Production",
            configureServices: services =>
            {
                services.RemoveAll<IPersonalTradingPostSynchronizationService>();
                services.AddSingleton<IPersonalTradingPostSynchronizationService>(synchronizationService);
            });
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/personal-trading-post/sync");
        request.Headers.Add("Origin", "http://localhost");
        request.Headers.Add(
            LocalRequestOriginProtectionMiddleware.RequestHeader,
            LocalRequestOriginProtectionMiddleware.RequestHeaderValue);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal(1, synchronizationService.CallCount);
        Assert.Contains("\"outcome\":\"succeeded\"", body, StringComparison.Ordinal);
        Assert.Contains("\"completedTransactionCount\":4", body, StringComparison.Ordinal);
        Assert.DoesNotContain("opaque-account", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authorization", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Untrusted_origin_cannot_invoke_personal_sync_service()
    {
        var synchronizationService = new FixedSynchronizationService(
            PersonalTradingPostSynchronizationResult.PersistenceFailed(DateTimeOffset.UtcNow));
        await using var app = await StartApplicationAsync(
            "Production",
            configureServices: services =>
            {
                services.RemoveAll<IPersonalTradingPostSynchronizationService>();
                services.AddSingleton<IPersonalTradingPostSynchronizationService>(synchronizationService);
            });
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/personal-trading-post/sync");
        request.Headers.Add("Origin", "https://attacker.example");
        request.Headers.Add(
            LocalRequestOriginProtectionMiddleware.RequestHeader,
            LocalRequestOriginProtectionMiddleware.RequestHeaderValue);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, synchronizationService.CallCount);
    }

    [Fact]
    public async Task Local_data_endpoints_are_no_store_guarded_and_return_only_safe_recovery_metadata()
    {
        var databaseDirectory = Path.Combine(Path.GetTempPath(), "TyrianLedger.Web.Tests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(databaseDirectory, "tyrian-ledger.db");
        try
        {
            await using var app = await StartApplicationAsync("Production", new Dictionary<string, string?>
            {
                ["TyrianLedger:Database:Path"] = databasePath,
            });
            using var client = app.GetTestClient();

            var restoreEndpoint = app.Services.GetServices<EndpointDataSource>()
                .SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .Single(endpoint => string.Equals(endpoint.RoutePattern.RawText, "/api/local-data/restore", StringComparison.Ordinal));
            Assert.Equal(
                LocalDataEndpoints.MaxRestoreRequestBytes,
                restoreEndpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize);
            Assert.Equal(
                LocalDataEndpoints.MaxRestoreRequestBytes,
                restoreEndpoint.Metadata.GetMetadata<RequestFormLimitsAttribute>()?.MultipartBodyLengthLimit);
            Assert.True(LocalDataEndpoints.MaxRestoreRequestBytes > LocalDataEndpoints.MaxRestoreBackupBytes);

            using var locationResponse = await client.GetAsync("/api/local-data");
            var locationBody = await locationResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, locationResponse.StatusCode);
            Assert.Equal("no-store", locationResponse.Headers.CacheControl?.ToString());
            Assert.Contains("backupDirectoryPath", locationBody, StringComparison.Ordinal);
            Assert.DoesNotContain("credential", locationBody, StringComparison.OrdinalIgnoreCase);

            using var rejectedClear = await SendJsonAsync(client, "/api/local-data/clear-personal", new { confirmation = "clear" });
            Assert.Equal(HttpStatusCode.BadRequest, rejectedClear.StatusCode);
            Assert.Equal("no-store", rejectedClear.Headers.CacheControl?.ToString());

            using var backup = await SendUnsafeAsync(client, HttpMethod.Post, "/api/local-data/backup");
            var backupBody = await backup.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.Created, backup.StatusCode);
            Assert.Equal("no-store", backup.Headers.CacheControl?.ToString());
            Assert.Contains("fileName", backupBody, StringComparison.Ordinal);
            Assert.DoesNotContain(databasePath, backupBody, StringComparison.Ordinal);

            using var invalidRestore = await SendRestoreAsync(client, "RESTORE LOCAL DATA", "not a database");
            Assert.Equal(HttpStatusCode.BadRequest, invalidRestore.StatusCode);
            Assert.Equal("no-store", invalidRestore.Headers.CacheControl?.ToString());

            using var clear = await SendJsonAsync(client, "/api/local-data/clear-personal", new { confirmation = "CLEAR PERSONAL DATA" });
            Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
            Assert.Equal("no-store", clear.Headers.CacheControl?.ToString());

            foreach (var request in new[]
                     {
                         CreateUntrustedLocalDataRequest("/api/local-data/backup"),
                         CreateUntrustedLocalDataRequest("/api/local-data/restore", new MultipartFormDataContent()),
                         CreateUntrustedLocalDataRequest("/api/local-data/clear-personal", JsonContent.Create(new { confirmation = "CLEAR PERSONAL DATA" })),
                     })
            {
                using (request)
                using (var untrustedResponse = await client.SendAsync(request))
                {
                    Assert.Equal(HttpStatusCode.Forbidden, untrustedResponse.StatusCode);
                }
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(databaseDirectory))
            {
                Directory.Delete(databaseDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Untrusted_origin_cannot_invoke_credential_dependent_status_service()
    {
        var statusService = new CountingAccountConnectionStatusService();
        await using var app = await StartApplicationAsync(
            "Production",
            configureServices: services =>
            {
                services.RemoveAll<IAccountConnectionStatusService>();
                services.AddSingleton<IAccountConnectionStatusService>(statusService);
            });
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/account-connection");
        request.Headers.Add("Origin", "https://attacker.example");
        request.Headers.Add(
            LocalRequestOriginProtectionMiddleware.RequestHeader,
            LocalRequestOriginProtectionMiddleware.RequestHeaderValue);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, statusService.CallCount);
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

    [Fact]
    public async Task ActualKestrelHostAccepts_restore_uploads_above_the_default_request_limit()
    {
        var port = ReserveAvailablePort();
        var databaseDirectory = Path.Combine(Path.GetTempPath(), "TyrianLedger.Web.Tests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(databaseDirectory, "tyrian-ledger.db");
        try
        {
            await using var app = Program.CreateApplication([], builder =>
            {
                builder.Environment.EnvironmentName = "Production";
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TyrianLedger:Host:Port"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["TyrianLedger:Database:Path"] = databasePath,
                });
            });
            await app.StartAsync();

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(LocalDataEndpoints.RestoreConfirmation), "confirmation");
            form.Add(
                new ByteArrayContent(new byte[31 * 1024 * 1024]),
                "backup",
                "selected-backup.db");
            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/api/local-data/restore")
            {
                Content = form,
            };
            request.Headers.Add("Origin", $"http://127.0.0.1:{port}");
            request.Headers.Add(LocalRequestOriginProtectionMiddleware.RequestHeader, LocalRequestOriginProtectionMiddleware.RequestHeaderValue);
            using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(databaseDirectory))
            {
                Directory.Delete(databaseDirectory, recursive: true);
            }
        }
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
    [InlineData("0.0.0.0")]
    [InlineData("[::]")]
    [InlineData("::")]
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
    public async Task ReloadingKestrelEndpointConfigurationCannotAddAListener()
    {
        var port = ReserveAvailablePort();
        var injectedPort = ReserveAvailablePort();
        ConfigurationManager? runtimeConfiguration = null;
        await using var app = Program.CreateApplication([], builder =>
        {
            builder.Environment.EnvironmentName = "Production";
            runtimeConfiguration = builder.Configuration;
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TyrianLedger:Host:Port"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
        });
        await app.StartAsync();

        var kestrelOptions = app.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;
        Assert.NotNull(kestrelOptions.ConfigurationLoader);
        Assert.Empty(kestrelOptions.ConfigurationLoader.Configuration.GetChildren());

        var server = app.Services.GetRequiredService<IServer>();
        var boundAddresses = server.Features.Get<IServerAddressesFeature>()!.Addresses;
        var initialAddresses = boundAddresses.Order(StringComparer.Ordinal).ToArray();

        runtimeConfiguration!["Kestrel:Endpoints:Injected:Url"] = $"http://0.0.0.0:{injectedPort}";
        ((IConfigurationRoot)runtimeConfiguration).Reload();
        await Task.Delay(TimeSpan.FromMilliseconds(250));

        Assert.Equal(initialAddresses, boundAddresses.Order(StringComparer.Ordinal));
        Assert.DoesNotContain($"http://0.0.0.0:{injectedPort}", boundAddresses);
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
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var app = CreateApplication(environment, settings, configureServices);
        await app.StartAsync();
        return app;
    }

    private static WebApplication CreateApplication(
        string environment,
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var effectiveSettings = settings is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?>(settings);
        effectiveSettings.TryAdd(
            "TyrianLedger:Database:Path",
            Path.Combine(Path.GetTempPath(), "TyrianLedger.Web.Tests", Guid.NewGuid().ToString("N"), "tyrian-ledger.db"));

        return Program.CreateApplication(
            [],
            builder =>
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

                builder.Configuration.AddInMemoryCollection(effectiveSettings);
            },
            configureServices);
    }

    private static Task<HttpResponseMessage> SendWithHostAsync(HttpClient client, string host)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Host = host;
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SendUnsafeAsync(HttpClient client, HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Origin", "http://localhost");
        request.Headers.Add(LocalRequestOriginProtectionMiddleware.RequestHeader, LocalRequestOriginProtectionMiddleware.RequestHeaderValue);
        return client.SendAsync(request);
    }

    private static HttpRequestMessage CreateUntrustedLocalDataRequest(string path, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        request.Headers.Add("Origin", "https://attacker.example");
        request.Headers.Add(LocalRequestOriginProtectionMiddleware.RequestHeader, LocalRequestOriginProtectionMiddleware.RequestHeaderValue);
        return request;
    }

    private static Task<HttpResponseMessage> SendJsonAsync(HttpClient client, string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("Origin", "http://localhost");
        request.Headers.Add(LocalRequestOriginProtectionMiddleware.RequestHeader, LocalRequestOriginProtectionMiddleware.RequestHeaderValue);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SendRestoreAsync(HttpClient client, string confirmation, string contents)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(confirmation), "confirmation");
        form.Add(new StringContent(contents), "backup", "selected-backup.db");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/local-data/restore") { Content = form };
        request.Headers.Add("Origin", "http://localhost");
        request.Headers.Add(LocalRequestOriginProtectionMiddleware.RequestHeader, LocalRequestOriginProtectionMiddleware.RequestHeaderValue);
        return client.SendAsync(request);
    }

    private sealed class FixedSynchronizationService(PersonalTradingPostSynchronizationResult result)
        : IPersonalTradingPostSynchronizationService
    {
        public int CallCount { get; private set; }

        public Task<PersonalTradingPostSynchronizationResult> SynchronizeAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(result);
        }
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

    private sealed class FixedAccountConnectionStatusService : IAccountConnectionStatusService
    {
        private readonly AccountConnectionStatus _status;

        public FixedAccountConnectionStatusService(AccountConnectionStatus status) => _status = status;

        public Task<AccountConnectionStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_status);
    }

    private sealed class CountingAccountConnectionStatusService : IAccountConnectionStatusService
    {
        public int CallCount { get; private set; }

        public Task<AccountConnectionStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new AccountConnectionStatus(
                AccountConnectionState.NotConfigured,
                [],
                AccountConnectionPermissions.Required));
        }
    }
}

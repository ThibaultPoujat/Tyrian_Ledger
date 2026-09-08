using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Gw2Tp.Analytics.Finance;
using Gw2Tp.Analytics.OrderBooks;
using Gw2Tp.Application.AccountConnection;
using Gw2Tp.Application.Dashboard;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Application.Time;
using Gw2Tp.Application.MarketScanning;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Domain.Finance;
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
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
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
                Assert.Equal(6L, await command.ExecuteScalarAsync());
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
    public async Task Personal_dashboard_endpoint_returns_safe_non_cacheable_structured_results()
    {
        await using var app = await StartApplicationAsync(
            "Production",
            configureServices: services =>
            {
                services.RemoveAll<IPersonalDashboardService>();
                services.AddSingleton<IPersonalDashboardService>(new FixedDashboardService());
            });
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/api/personal-dashboard");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains("\"state\":\"notSynchronized\"", body, StringComparison.Ordinal);
        Assert.Contains("\"currentBuyCapital\":{\"copper\":\"0\"}", body, StringComparison.Ordinal);
        Assert.DoesNotContain("credential", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authorization", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accountScope", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Live_market_scanner_endpoint_returns_safe_exact_money_strings_and_validates_query_settings()
    {
        var scanner = new FixedLiveMarketScanner(ReadyScannerResult());
        await using var app = await StartApplicationAsync(
            "Production",
            configureServices: services =>
            {
                services.RemoveAll<ILiveMarketScanner>();
                services.AddSingleton<ILiveMarketScanner>(scanner);
            });
        using var client = app.GetTestClient();

        using var scannerRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/live-market-scanner?minimumRoiBasisPoints=5000&minimumNetProfitCopper=60&bidIncrementCopper=1&listUndercutCopper=1&intendedQuantity=7");
        scannerRequest.Headers.Add(
            LocalRequestOriginProtectionMiddleware.RequestHeader,
            LocalRequestOriginProtectionMiddleware.RequestHeaderValue);
        using var response = await client.SendAsync(scannerRequest);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal(1, scanner.CallCount);
        Assert.Contains("\"state\":\"ready\"", body, StringComparison.Ordinal);
        Assert.Contains("\"netProfit\":{\"copper\":\"9007199254740993\"}", body, StringComparison.Ordinal);
        Assert.Contains("\"minimumRoiBasisPoints\":5000", body, StringComparison.Ordinal);
        Assert.Contains("\"intendedQuantity\":7", body, StringComparison.Ordinal);
        Assert.Contains("\"participationCapQuantity\":1", body, StringComparison.Ordinal);
        Assert.Contains("\"totalBuyQuantity\":{\"value\":\"10\"}", body, StringComparison.Ordinal);
        Assert.Contains("\"buyNextLevelGap\":{\"copper\":\"5\"}", body, StringComparison.Ordinal);
        Assert.Contains("\"sellNextLevelGap\":{\"copper\":\"10\"}", body, StringComparison.Ordinal);
        Assert.Contains("\"hasBuyPriceCliff\":true", body, StringComparison.Ordinal);
        Assert.Contains("\"requestedQuantity\":7", body, StringComparison.Ordinal);
        Assert.Contains("\"filledQuantity\":7", body, StringComparison.Ordinal);
        Assert.Contains("\"totalValue\":{\"copper\":\"740\"}", body, StringComparison.Ordinal);
        Assert.Contains("\"priceImpact\":{\"copper\":\"40\"}", body, StringComparison.Ordinal);
        Assert.Contains("\"reasons\":[\"buyPriceCliff\",\"participationCapBelowIntendedQuantity\"]", body, StringComparison.Ordinal);
        Assert.Contains("\"topBuyLevels\":[{\"listings\":2,\"quantity\":5,\"unitPrice\":{\"copper\":\"200\"}}]", body, StringComparison.Ordinal);
        Assert.Contains("\"displayPercent\":\"100.00%\"", body, StringComparison.Ordinal);
        Assert.Contains("\"qualifyingCandidateCount\":1", body, StringComparison.Ordinal);
        Assert.Contains("\"isTruncated\":false", body, StringComparison.Ordinal);
        Assert.DoesNotContain("credential", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authorization", body, StringComparison.OrdinalIgnoreCase);

        using var invalidRequest = new HttpRequestMessage(HttpMethod.Get, "/api/live-market-scanner?minimumRoiBasisPoints=not-a-number");
        invalidRequest.Headers.Add(
            LocalRequestOriginProtectionMiddleware.RequestHeader,
            LocalRequestOriginProtectionMiddleware.RequestHeaderValue);
        using var invalid = await client.SendAsync(invalidRequest);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("no-store", invalid.Headers.CacheControl?.ToString());
        Assert.Equal(1, scanner.CallCount);
        Assert.Contains("invalid_scanner_settings", await invalid.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Market_history_collector_endpoints_are_safe_no_store_and_manual_runs_are_origin_protected()
    {
        var collector = new FixedMarketHistoryCollector();
        await using var app = await StartApplicationAsync(
            "Production",
            configureServices: services =>
            {
                services.RemoveAll<IMarketHistoryCollector>();
                services.AddSingleton<IMarketHistoryCollector>(collector);
            });
        using var client = app.GetTestClient();

        using var health = new HttpRequestMessage(HttpMethod.Get, "/api/market-history/collector");
        health.Headers.Add(LocalRequestOriginProtectionMiddleware.RequestHeader, LocalRequestOriginProtectionMiddleware.RequestHeaderValue);
        using var healthResponse = await client.SendAsync(health);
        var healthBody = await healthResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
        Assert.Equal("no-store", healthResponse.Headers.CacheControl?.ToString());
        Assert.Contains("\"state\":\"idle\"", healthBody, StringComparison.Ordinal);
        Assert.Contains("\"trackedItemCount\":2", healthBody, StringComparison.Ordinal);
        Assert.DoesNotContain("credential", healthBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authorization", healthBody, StringComparison.OrdinalIgnoreCase);

        using var attacker = new HttpRequestMessage(HttpMethod.Post, "/api/market-history/collector/run");
        attacker.Headers.Add("Origin", "https://attacker.example");
        attacker.Headers.Add(LocalRequestOriginProtectionMiddleware.RequestHeader, LocalRequestOriginProtectionMiddleware.RequestHeaderValue);
        using var attackerResponse = await client.SendAsync(attacker);
        Assert.Equal(HttpStatusCode.Forbidden, attackerResponse.StatusCode);
        Assert.Equal(0, collector.ManualRunCount);

        using var manual = new HttpRequestMessage(HttpMethod.Post, "/api/market-history/collector/run");
        manual.Headers.Add("Origin", "http://localhost");
        manual.Headers.Add(LocalRequestOriginProtectionMiddleware.RequestHeader, LocalRequestOriginProtectionMiddleware.RequestHeaderValue);
        using var manualResponse = await client.SendAsync(manual);
        var manualBody = await manualResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, manualResponse.StatusCode);
        Assert.Equal("no-store", manualResponse.Headers.CacheControl?.ToString());
        Assert.Equal(1, collector.ManualRunCount);
        Assert.Contains("\"outcome\":\"succeeded\"", manualBody, StringComparison.Ordinal);
        Assert.DoesNotContain("credential", manualBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Market_history_status_endpoint_exposes_safe_non_cacheable_governance_data_and_validates_filters()
    {
        await using var app = await StartApplicationAsync("Production");
        using var client = app.GetTestClient();

        using var statusRequest = new HttpRequestMessage(HttpMethod.Get, "/api/market-history");
        statusRequest.Headers.Add(LocalRequestOriginProtectionMiddleware.RequestHeader, LocalRequestOriginProtectionMiddleware.RequestHeaderValue);
        using var status = await client.SendAsync(statusRequest);
        var statusBody = await status.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal("no-store", status.Headers.CacheControl?.ToString());
        Assert.Contains("\"evidenceKind\":\"aggregatePrices\"", statusBody, StringComparison.Ordinal);
        Assert.Contains("\"evidenceKind\":\"detailedOrderBooks\"", statusBody, StringComparison.Ordinal);
        Assert.Contains("\"mode\":\"preserveAllRawEvidence\"", statusBody, StringComparison.Ordinal);
        Assert.Contains("\"integrityState\":\"passed\"", statusBody, StringComparison.Ordinal);
        Assert.DoesNotContain("credential", statusBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authorization", statusBody, StringComparison.OrdinalIgnoreCase);

        using var browserTimestampRequest = new HttpRequestMessage(HttpMethod.Get, "/api/market-history?itemId=42&fromInclusiveUtc=2026-09-08T12:00:00.000Z&toInclusiveUtc=2026-09-08T12:00:01Z");
        browserTimestampRequest.Headers.Add(LocalRequestOriginProtectionMiddleware.RequestHeader, LocalRequestOriginProtectionMiddleware.RequestHeaderValue);
        using var browserTimestamp = await client.SendAsync(browserTimestampRequest);
        Assert.Equal(HttpStatusCode.OK, browserTimestamp.StatusCode);
        Assert.Equal("no-store", browserTimestamp.Headers.CacheControl?.ToString());

        using var invalidRequest = new HttpRequestMessage(HttpMethod.Get, "/api/market-history?fromInclusiveUtc=2026-09-08T12:00:00.0000000Z");
        invalidRequest.Headers.Add(LocalRequestOriginProtectionMiddleware.RequestHeader, LocalRequestOriginProtectionMiddleware.RequestHeaderValue);
        using var invalid = await client.SendAsync(invalidRequest);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("no-store", invalid.Headers.CacheControl?.ToString());
        Assert.Contains("invalid_market_history_coverage_query", await invalid.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var missingZoneRequest = new HttpRequestMessage(HttpMethod.Get, "/api/market-history?fromInclusiveUtc=2026-09-08T12:00:00&toInclusiveUtc=2026-09-08T12:00:01Z");
        missingZoneRequest.Headers.Add(LocalRequestOriginProtectionMiddleware.RequestHeader, LocalRequestOriginProtectionMiddleware.RequestHeaderValue);
        using var missingZone = await client.SendAsync(missingZoneRequest);
        Assert.Equal(HttpStatusCode.BadRequest, missingZone.StatusCode);

        using var untrustedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/market-history");
        untrustedRequest.Headers.Add("Origin", "https://attacker.example");
        untrustedRequest.Headers.Add(LocalRequestOriginProtectionMiddleware.RequestHeader, LocalRequestOriginProtectionMiddleware.RequestHeaderValue);
        using var untrusted = await client.SendAsync(untrustedRequest);
        Assert.Equal(HttpStatusCode.Forbidden, untrusted.StatusCode);
    }

    [Fact]
    public async Task Market_history_collector_hosted_service_runs_and_stops_without_turning_shutdown_into_a_failure()
    {
        var collector = new FixedMarketHistoryCollector();
        var service = new MarketHistoryCollectorHostedService(
            collector,
            new MarketHistoryCollectionSchedulerSettings(TimeSpan.FromMinutes(1)),
            new FixedClock(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)),
            new BlockingCollectionDelay());

        await service.StartAsync(CancellationToken.None);
        await collector.DueRunStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(collector.LastScheduledRunAtUtc);

        await service.StopAsync(CancellationToken.None);

        Assert.Null(collector.LastScheduledRunAtUtc);
    }

    [Fact]
    public async Task Market_history_collector_hosted_service_prefers_the_next_due_capture_then_bounds_it_by_source_refresh()
    {
        var now = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        var collector = new RepeatingMarketHistoryCollector(now);
        var delay = new RecordingCollectionDelay();
        var service = new MarketHistoryCollectorHostedService(
            collector,
            new MarketHistoryCollectionSchedulerSettings(TimeSpan.FromMinutes(1)),
            new FixedClock(now),
            delay);

        await service.StartAsync(CancellationToken.None);
        await delay.SecondDelayRecorded.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(1)], delay.Delays);
        await service.StopAsync(CancellationToken.None);
        Assert.Null(collector.LastScheduledRunAtUtc);
    }

    [Fact]
    public async Task Watchlist_endpoints_are_durable_no_store_and_origin_protected()
    {
        await using var app = await StartApplicationAsync("Production");
        using var client = app.GetTestClient();

        using var add = new HttpRequestMessage(HttpMethod.Put, "/api/watchlist/42");
        add.Headers.Add("Origin", "http://localhost");
        add.Headers.Add(LocalRequestOriginProtectionMiddleware.RequestHeader, LocalRequestOriginProtectionMiddleware.RequestHeaderValue);
        using var addResponse = await client.SendAsync(add);
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);
        Assert.Equal("no-store", addResponse.Headers.CacheControl?.ToString());

        using var list = new HttpRequestMessage(HttpMethod.Get, "/api/watchlist");
        list.Headers.Add(LocalRequestOriginProtectionMiddleware.RequestHeader, LocalRequestOriginProtectionMiddleware.RequestHeaderValue);
        using var listResponse = await client.SendAsync(list);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal("no-store", listResponse.Headers.CacheControl?.ToString());
        Assert.Contains("42", await listResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var attacker = new HttpRequestMessage(HttpMethod.Delete, "/api/watchlist/42");
        attacker.Headers.Add("Origin", "https://attacker.example");
        attacker.Headers.Add(LocalRequestOriginProtectionMiddleware.RequestHeader, LocalRequestOriginProtectionMiddleware.RequestHeaderValue);
        using var attackerResponse = await client.SendAsync(attacker);
        Assert.Equal(HttpStatusCode.Forbidden, attackerResponse.StatusCode);

        using var attackerRead = new HttpRequestMessage(HttpMethod.Get, "/api/watchlist");
        attackerRead.Headers.Add("Origin", "https://attacker.example");
        attackerRead.Headers.Add(LocalRequestOriginProtectionMiddleware.RequestHeader, LocalRequestOriginProtectionMiddleware.RequestHeaderValue);
        using var attackerReadResponse = await client.SendAsync(attackerRead);
        Assert.Equal(HttpStatusCode.Forbidden, attackerReadResponse.StatusCode);
    }

    [Fact]
    public async Task Untrusted_origin_cannot_trigger_live_market_scan()
    {
        var scanner = new FixedLiveMarketScanner(ReadyScannerResult());
        await using var app = await StartApplicationAsync(
            "Production",
            configureServices: services =>
            {
                services.RemoveAll<ILiveMarketScanner>();
                services.AddSingleton<ILiveMarketScanner>(scanner);
            });
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/live-market-scanner");
        request.Headers.Add("Origin", "https://attacker.example");
        request.Headers.Add(
            LocalRequestOriginProtectionMiddleware.RequestHeader,
            LocalRequestOriginProtectionMiddleware.RequestHeaderValue);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, scanner.CallCount);

        using var trailingSlashRequest = new HttpRequestMessage(HttpMethod.Get, "/api/live-market-scanner/");
        trailingSlashRequest.Headers.Add("Origin", "https://attacker.example");
        trailingSlashRequest.Headers.Add(
            LocalRequestOriginProtectionMiddleware.RequestHeader,
            LocalRequestOriginProtectionMiddleware.RequestHeaderValue);
        using var trailingSlashResponse = await client.SendAsync(trailingSlashRequest);

        Assert.Equal(HttpStatusCode.Forbidden, trailingSlashResponse.StatusCode);
        Assert.Equal(0, scanner.CallCount);

        using var headRequest = new HttpRequestMessage(HttpMethod.Head, "/api/live-market-scanner");
        headRequest.Headers.Add("Origin", "https://attacker.example");
        using var headResponse = await client.SendAsync(headRequest);

        Assert.Equal(HttpStatusCode.Forbidden, headResponse.StatusCode);
        Assert.Equal(0, scanner.CallCount);
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

    private sealed class FixedMarketHistoryCollector : IMarketHistoryCollector
    {
        private readonly MarketHistoryCollectorHealth health = new(
            MarketHistoryCollectorState.Idle,
            new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero),
            null,
            0,
            2,
            new DateTimeOffset(2026, 9, 8, 12, 1, 0, TimeSpan.Zero));

        public int ManualRunCount { get; private set; }
        public TaskCompletionSource DueRunStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DateTimeOffset? LastScheduledRunAtUtc { get; private set; }

        public Task<MarketHistoryCollectionRun> CollectDueAsync(CancellationToken cancellationToken = default)
        {
            DueRunStarted.TrySetResult();
            return Task.FromResult(new MarketHistoryCollectionRun(MarketHistoryCollectionOutcome.Succeeded, 2, 0, 0, 0, null, health.NextRunAtUtc));
        }

        public Task<MarketHistoryCollectionRun> CollectNowAsync(CancellationToken cancellationToken = default)
        {
            ManualRunCount++;
            return Task.FromResult(new MarketHistoryCollectionRun(MarketHistoryCollectionOutcome.Succeeded, 2, 2, 2, 0, null, health.NextRunAtUtc));
        }

        public MarketHistoryCollectorHealth GetHealth() => health;

        public void SetNextRunAtUtc(DateTimeOffset? nextRunAtUtc)
        {
            LastScheduledRunAtUtc = nextRunAtUtc;
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class BlockingCollectionDelay : IMarketHistoryCollectionDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class RecordingCollectionDelay : IMarketHistoryCollectionDelay
    {
        public List<TimeSpan> Delays { get; } = [];
        public TaskCompletionSource SecondDelayRecorded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            if (Delays.Count == 2)
            {
                SecondDelayRecorded.TrySetResult();
            }

            return Delays.Count == 1
                ? Task.CompletedTask
                : Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class RepeatingMarketHistoryCollector(DateTimeOffset now) : IMarketHistoryCollector
    {
        private int dueRunCount;

        public TaskCompletionSource SecondRunStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DateTimeOffset? LastScheduledRunAtUtc { get; private set; }

        public Task<MarketHistoryCollectionRun> CollectDueAsync(CancellationToken cancellationToken = default)
        {
            dueRunCount++;
            if (dueRunCount == 2)
            {
                SecondRunStarted.TrySetResult();
            }

            var nextDueAtUtc = dueRunCount == 1 ? now.AddSeconds(20) : now.AddMinutes(2);
            return Task.FromResult(new MarketHistoryCollectionRun(MarketHistoryCollectionOutcome.Succeeded, 1, 0, 0, 0, null, nextDueAtUtc));
        }

        public Task<MarketHistoryCollectionRun> CollectNowAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new MarketHistoryCollectionRun(MarketHistoryCollectionOutcome.Succeeded, 1, 1, 1, 0, null, now));

        public MarketHistoryCollectorHealth GetHealth() => new(MarketHistoryCollectorState.Idle, null, null, 0, 1, LastScheduledRunAtUtc);

        public void SetNextRunAtUtc(DateTimeOffset? nextRunAtUtc) => LastScheduledRunAtUtc = nextRunAtUtc;
    }

    private sealed class FixedDashboardService : IPersonalDashboardService
    {
        public Task<PersonalDashboard> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(PersonalDashboard.NotSynchronized());
    }

    private static LiveMarketScannerResult ReadyScannerResult()
    {
        var profit = new Money(9_007_199_254_740_993);
        var totalCost = new Money(9_007_199_254_741_003);
        return new LiveMarketScannerResult(
            LiveMarketScannerState.Ready,
            null,
            new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero),
            LiveMarketScannerSettings.Default,
            IsFeeRoundingExternallyVerified: false,
            QualifyingCandidateCount: 1,
            IsTruncated: false,
            [new LiveMarketScannerCandidate(
                new MarketItemMetadata(42, "Synthetic item", MarketItemStackPolicy.NormalStackLimit),
                new MarketOrderSummary(10, 100),
                new MarketOrderSummary(20, 200),
                new Money(101),
                new Money(199),
                new FlipProfitScenario(new Money(101), new Money(199), new Money(10), new Money(20), new Money(169), profit),
                totalCost,
                new ExactRoi(profit, totalCost),
                new Money(168),
                [LiveMarketScannerInclusionReason.MeetsMinimumRoi],
                LiquidityEvidence())],
            []);
    }

    private static LiveMarketScannerLiquidityEvidence LiquidityEvidence()
    {
        var acquisition = new OrderBookExecutionScenario(
            OrderBookExecutionKind.Acquisition,
            RequestedQuantity: 7,
            FilledQuantity: 7,
            RemainingQuantity: 0,
            IsFullyFilled: true,
            [new OrderBookExecutionFill(3, new Money(100), new Money(300)), new OrderBookExecutionFill(4, new Money(110), new Money(440))],
            new Money(740),
            new WeightedAverageExecutionPrice(new Money(740), 7),
            new Money(40));
        var liquidation = new OrderBookExecutionScenario(
            OrderBookExecutionKind.Liquidation,
            RequestedQuantity: 7,
            FilledQuantity: 7,
            RemainingQuantity: 0,
            IsFullyFilled: true,
            [new OrderBookExecutionFill(3, new Money(200), new Money(600)), new OrderBookExecutionFill(4, new Money(180), new Money(720))],
            new Money(1_320),
            new WeightedAverageExecutionPrice(new Money(1_320), 7),
            new Money(80));
        return new LiveMarketScannerLiquidityEvidence(
            TotalBuyQuantity: 10,
            TotalSellQuantity: 20,
            NearBestBuyQuantity: 5,
            NearBestSellQuantity: 8,
            NearBestBuyListings: 3,
            NearBestSellListings: 4,
            BuyNextLevelGap: new Money(5),
            SellNextLevelGap: new Money(10),
            HasBuyPriceCliff: true,
            HasSellPriceCliff: true,
            Acquisition: acquisition,
            Liquidation: liquidation,
            ParticipationCapQuantity: 1,
            Reasons: [
                LiveMarketScannerLiquidityReason.BuyPriceCliff,
                LiveMarketScannerLiquidityReason.ParticipationCapBelowIntendedQuantity,
            ],
            TopBuyLevels: [new MarketOrderLevel(2, 5, 200)],
            TopSellLevels: [new MarketOrderLevel(3, 8, 100)]);
    }

    private sealed class FixedLiveMarketScanner(LiveMarketScannerResult result) : ILiveMarketScanner
    {
        public int CallCount { get; private set; }

        public Task<LiveMarketScannerResult> ScanAsync(
            LiveMarketScannerSettings settings,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(result with { Settings = settings });
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

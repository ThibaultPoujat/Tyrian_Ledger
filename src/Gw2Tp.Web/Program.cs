using Gw2Tp.Application.AccountConnection;
using Gw2Tp.Application.Accounting;
using Gw2Tp.Application.Dashboard;
using Gw2Tp.Application.Finance;
using Gw2Tp.Application.MarketScanning;
using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Application.MarketSnapshots;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Application.Recommendations;
using Gw2Tp.Application.Investments;
using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Plans;
using Gw2Tp.Infrastructure.AccountConnection;
using Gw2Tp.Infrastructure.Diagnostics;
using Gw2Tp.Infrastructure.Persistence;
using Gw2Tp.Web.Hosting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Routing;
using System.Diagnostics;
using System.Globalization;

namespace Gw2Tp.Web;

public static class Program
{
    public static void Main(string[] args)
    {
        CreateApplication(args).Run();
    }

    internal static WebApplication CreateApplication(
        string[] args,
        Action<WebApplicationBuilder>? configureBuilder = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configureBuilder?.Invoke(builder);

        var hostOptions = LocalHostOptions.FromConfiguration(builder.Configuration);
        LocalHostOptionsValidator.ValidateAndThrow(hostOptions, builder.Configuration);

        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            // Replace WebApplicationBuilder's reloadable Kestrel configuration loader.
            // Listener authority belongs exclusively to the validated options below.
            serverOptions.Configure(new ConfigurationBuilder().Build(), reloadOnChange: false);

            foreach (var address in hostOptions.GetListenAddresses())
            {
                serverOptions.Listen(address, hostOptions.Port);
            }
        });

        builder.Services.AddHealthChecks();
        builder.Services.AddSingleton<LocalDiagnosticLog>();
        builder.Services.AddSingleton<AccountViewScopeTokenService>();
        builder.Services.AddTyrianLedgerAccountConnection(builder.Environment, builder.Configuration);
        builder.Services.AddTyrianLedgerPersistence(builder.Configuration);
        builder.Services.AddSingleton<IPersonalDashboardService, PersonalDashboardService>();
        builder.Services.AddSingleton<PublicMarketSnapshotCollector>();
        builder.Services.AddSingleton<ILiveMarketScanner, LiveMarketScanner>();
        builder.Services.AddSingleton(CreateMarketSamplingSettings(builder.Configuration));
        builder.Services.AddSingleton<IMarketSamplingSource, CurrentPersonalOrderMarketSamplingSource>();
        builder.Services.AddSingleton<IMarketSamplingSource, WatchlistMarketSamplingSource>();
        builder.Services.AddSingleton<IMarketSamplingSource, OpenInvestmentPositionMarketSamplingSource>();
        builder.Services.AddSingleton<IAdaptiveMarketSamplingPolicy, AdaptiveMarketSamplingPolicy>();
        builder.Services.AddSingleton(CreateMarketHistoryCollectionSchedulerSettings(builder.Configuration));
        builder.Services.AddSingleton<IMarketHistoryCollector, MarketHistoryCollector>();
        builder.Services.AddSingleton<IHistoricalMarketAnalyticsService, HistoricalMarketAnalyticsService>();
        builder.Services.AddSingleton<IOpportunityScoreService, OpportunityScoreService>();
        builder.Services.AddSingleton<IPrimaryRecommendationPolicy, PrimaryRecommendationPolicy>();
        builder.Services.AddSingleton<IPrimaryRecommendationService, PrimaryRecommendationService>();
        builder.Services.AddSingleton<ICraftingEconomicsCalculator, CraftingEconomicsCalculator>();
        builder.Services.AddSingleton<ICraftingOpportunityPlanner, CraftingOpportunityPlanner>();
        builder.Services.AddSingleton<ICraftingOpportunityService, CraftingOpportunityService>();
        builder.Services.AddSingleton<IPlanOrchestrationService, PlanOrchestrationService>();
        builder.Services.AddSingleton(CreateDecisionLoopSchedulerSettings(builder.Configuration));
        builder.Services.AddSingleton<PlanDecisionProjectionStore>();
        builder.Services.AddSingleton<PlanEndpointService>();
        builder.Services.AddSingleton<IInvestmentPortfolioService, InvestmentPortfolioService>();
        builder.Services.AddSingleton<IAccountCraftingSnapshotService, AccountCraftingSnapshotService>();
        builder.Services.AddSingleton<IMarketHistoryCollectionDelay>(SystemMarketHistoryCollectionDelay.Instance);
        builder.Services.AddHostedService<MarketHistoryCollectorHostedService>();
        builder.Services.AddSingleton<IDecisionLoopDelay>(SystemDecisionLoopDelay.Instance);
        builder.Services.AddSingleton<IContinuousDecisionLoopService, ContinuousDecisionLoopService>();
        builder.Services.AddHostedService<ContinuousDecisionLoopHostedService>();
        builder.Services.AddHostFiltering(options =>
        {
            options.AllowedHosts = hostOptions.AllowedHosts;
        });

        if (builder.Environment.IsDevelopment())
        {
            builder.Services.AddCors(options =>
            {
                options.AddPolicy(LocalHostOptions.DevelopmentCorsPolicy, policy =>
                {
                    policy
                        .WithOrigins(hostOptions.TrustedDevelopmentOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                });
            });
        }

        builder.Services.AddSingleton(hostOptions);
        builder.Services.AddSingleton<LocalRequestOriginValidator>();
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();

        app.UseHostFiltering();
        app.Use(async (context, next) =>
        {
            var instrumented = string.Equals(context.Request.Path, "/api/recommendations", StringComparison.Ordinal) ||
                string.Equals(context.Request.Path, "/api/plans", StringComparison.Ordinal) ||
                string.Equals(context.Request.Path, "/api/crafting-opportunities", StringComparison.Ordinal);
            var stopwatch = instrumented ? Stopwatch.StartNew() : null;
            if (stopwatch is not null)
            {
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers["Server-Timing"] = "app;dur=" + stopwatch.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture);
                    return Task.CompletedTask;
                });
            }
            await next(context).ConfigureAwait(false);
        });
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Content-Security-Policy"] = "frame-ancestors 'none'";
            await next(context).ConfigureAwait(false);
        });
        if (app.Environment.IsDevelopment())
        {
            app.UseCors(LocalHostOptions.DevelopmentCorsPolicy);
        }

        app.UseMiddleware<LocalRequestOriginProtectionMiddleware>();

        app.MapHealthChecks(
                "/api/health",
                new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteAsync })
            .WithMetadata(new HttpMethodMetadata([HttpMethods.Get]));

        app.MapGet("/api/diagnostics/export", (HttpContext context, LocalDiagnosticLog diagnostics, SafeTransportDiagnosticBuffer transportDiagnostics) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var export = diagnostics.ExportText();
            var transport = transportDiagnostics.Snapshot();
            if (transport.Count > 0)
            {
                export += Environment.NewLine + "Transport ArenaNet" + Environment.NewLine;
                foreach (var item in transport)
                {
                    export += $"{item.TimestampUtc:O} [ERROR] ArenaNet/{item.Operation} code={item.Code} exception={item.ExceptionType}";
                    if (item.InnerExceptionType is not null)
                    {
                        export += $" inner={item.InnerExceptionType}";
                    }
                    export += $" elapsedMs={item.ElapsedMilliseconds}" + Environment.NewLine;
                }
            }
            var market = transportDiagnostics.SnapshotMarketGatewayDiagnostics();
            if (market.Count > 0)
            {
                export += Environment.NewLine + "Marché ArenaNet (diagnostic assaini)" + Environment.NewLine;
                foreach (var item in market)
                {
                    export += $"{item.TimestampUtc:O} stage={item.Stage} operation={item.Operation}";
                    if (item.BatchIndex is { } batchIndex && item.BatchCount is { } batchCount)
                        export += $" batch={batchIndex}/{batchCount}";
                    if (item.RequestedItemIdCount is { } requested)
                        export += $" requestedIdCount={requested}";
                    if (item.ResponseItemIdCount is { } responseCount)
                        export += $" responseIdCount={responseCount}";
                    if (item.MissingItemIdCount is { } missing)
                        export += $" missingIdCount={missing}";
                    if (item.UnexpectedItemIdCount is { } unexpected)
                        export += $" unexpectedIdCount={unexpected}";
                    if (item.DuplicateItemIdCount is { } duplicate)
                        export += $" duplicateIdCount={duplicate}";
                    if (item.HttpStatusCode is { } status)
                        export += $" httpStatus={status}";
                    if (item.ErrorCategory is { } error)
                        export += $" errorCategory={error}";
                    if (item.IsPartialResponse is { } partial)
                        export += $" partialResponse={partial.ToString().ToLowerInvariant()}";
                    if (item.Attempt is { } attempt)
                        export += $" attempt={attempt}";
                    if (item.BackoffMilliseconds is { } backoff)
                        export += $" backoffMs={backoff}";
                    if (item.Outcome is { } outcome)
                        export += $" outcome={outcome}";
                    export += $" elapsedMs={item.ElapsedMilliseconds}" + Environment.NewLine;
                }
            }
            return Results.Text(export, "text/plain; charset=utf-8");
        }).WithMetadata(new HttpMethodMetadata([HttpMethods.Get]));
        app.MapGet(
            "/api/account-connection",
            async (
                HttpContext context,
                IAccountConnectionStatusService accountConnectionStatusService,
                CancellationToken cancellationToken) =>
            {
                var status = await accountConnectionStatusService
                    .GetStatusAsync(cancellationToken)
                    .ConfigureAwait(false);
                await AccountConnectionResponseWriter.WriteAsync(context, status).ConfigureAwait(false);
            });
        app.MapPost(
            "/api/personal-trading-post/sync",
            async (
                HttpContext context,
                IPersonalTradingPostSynchronizationService synchronizationService,
                LocalDiagnosticLog diagnostics,
                CancellationToken cancellationToken) =>
            {
                var correlationId = Guid.NewGuid().ToString("N");
                diagnostics.Record("INFO", "ArenaNet", "personal-trading-post-sync", "SYNC_STARTED", "Synchronisation du Comptoir démarrée.", correlationId: correlationId);
                var result = await synchronizationService.SynchronizeAsync(cancellationToken).ConfigureAwait(false);
                diagnostics.Record(
                    result.IsSuccess ? "INFO" : "ERROR",
                    "ArenaNet",
                    "personal-trading-post-sync",
                    result.IsSuccess ? "SYNC_SUCCEEDED" : (result.IsPersistenceFailure ? "PERSISTENCE_FAILURE" : result.ErrorCategory?.ToString().ToUpperInvariant() ?? "UNKNOWN_FAILURE"),
                    result.IsSuccess ? "Synchronisation du Comptoir terminée." : "La synchronisation du Comptoir a échoué. Consultez le code pour la catégorie de panne.",
                    correlationId: correlationId);
                await PersonalTradingPostSynchronizationResponseWriter.WriteAsync(context, result).ConfigureAwait(false);
            });
        app.MapPost(
            "/api/account-crafting/refresh",
            async (
                HttpContext context,
                IAccountCraftingSnapshotService accountCraftingSnapshotService,
                CancellationToken cancellationToken) =>
            {
                var refresh = await accountCraftingSnapshotService.RefreshWithOutcomeAsync(cancellationToken).ConfigureAwait(false);
                var result = refresh.Result;
                if (!result.IsSuccess && result.ErrorCategory is
                    Gw2ApiErrorCategory.CredentialUnavailable or
                    Gw2ApiErrorCategory.RateLimited or
                    Gw2ApiErrorCategory.UpstreamUnavailable or
                    Gw2ApiErrorCategory.TransportFailure or
                    Gw2ApiErrorCategory.IncompleteData or
                    Gw2ApiErrorCategory.InvalidPayload or
                    Gw2ApiErrorCategory.UnexpectedResponse)
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                }
                await AccountCraftingResponseWriter.WriteAsync(context, refresh).ConfigureAwait(false);
            });
        app.MapGet(
            "/api/personal-dashboard",
            async (
                HttpContext context,
                IPersonalDashboardService dashboardService,
                CancellationToken cancellationToken) =>
            {
                var dashboard = await dashboardService.GetAsync(cancellationToken).ConfigureAwait(false);
                await PersonalDashboardResponseWriter.WriteAsync(context, dashboard).ConfigureAwait(false);
            });
        app.MapGet(
            "/api/live-market-scanner",
            async (
                HttpContext context,
                ILiveMarketScanner scanner,
                CancellationToken cancellationToken) =>
            {
                if (!LiveMarketScannerResponseWriter.TryReadSettings(context.Request.Query, out var settings))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await LiveMarketScannerResponseWriter.WriteInvalidSettingsAsync(context).ConfigureAwait(false);
                    return;
                }

                var result = await scanner.ScanAsync(settings, cancellationToken).ConfigureAwait(false);
                if (result.State == LiveMarketScannerState.Unavailable)
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                }

                await LiveMarketScannerResponseWriter.WriteAsync(context, result).ConfigureAwait(false);
            });
        app.MapGet(
            "/api/recommendations",
            async (
                HttpContext context,
                IPrimaryRecommendationService recommendationService,
                IContinuousDecisionLoopService decisionLoop,
                IPersonalTradingPostGateway personalTradingPost,
                PlanEndpointService plans,
                AccountViewScopeTokenService accountViewScopes,
                CancellationToken cancellationToken) =>
            {
                PrimaryRecommendationResult result;
                var loopStatus = decisionLoop.GetStatus();
                var accountScopeId = loopStatus.AccountScopeId;
                if (context.Request.Headers.TryGetValue("X-Tyrian-Ledger-Manual-Refresh", out var refreshHeader) &&
                    string.Equals(refreshHeader.ToString(), "1", StringComparison.Ordinal))
                {
                    var run = await decisionLoop.RunNowAsync(cancellationToken).ConfigureAwait(false);
                    result = run.Recommendations ?? PrimaryRecommendationResult.Unavailable(
                        PrimaryRecommendationState.EvidenceUnavailable,
                        run.Status.LastErrorCode,
                        DefaultRecommendationPolicies());
                    accountScopeId = run.Status.AccountScopeId;
                }
                else
                {
                    var scope = await personalTradingPost.GetAccountScopeAsync(cancellationToken).ConfigureAwait(false);
                    accountScopeId = scope.IsSuccess && scope.Value is not null ? scope.Value.AccountId : null;
                    if (scope.IsSuccess && scope.Value is not null &&
                        string.Equals(loopStatus.AccountScopeId, scope.Value.AccountId, StringComparison.Ordinal) &&
                        loopStatus.Recommendations is { } latest)
                    {
                        result = latest;
                    }
                    else if (loopStatus.State is DecisionLoopRunState.Running or DecisionLoopRunState.Degraded)
                    {
                        result = PrimaryRecommendationResult.Unavailable(
                            PrimaryRecommendationState.EvidenceUnavailable,
                            loopStatus.LastErrorCode ?? "decision_loop_running",
                            DefaultRecommendationPolicies());
                    }
                    else
                    {
                        try
                        {
                            result = await recommendationService.GetAsync(cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception exception) when (exception is not OutOfMemoryException)
                        {
                            result = PrimaryRecommendationResult.Unavailable(
                                PrimaryRecommendationState.EvidenceUnavailable,
                                "recommendation_generation_failed",
                                DefaultRecommendationPolicies());
                        }
                    }
                }
                if (result.State == PrimaryRecommendationState.EvidenceUnavailable ||
                    result.State == PrimaryRecommendationState.AccountUnavailable && result.EvidenceError is
                        "CredentialUnavailable" or "RateLimited" or "UpstreamUnavailable" or "TransportFailure" or
                        "IncompleteData" or "InvalidPayload" or "UnexpectedResponse")
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                }
                var latestLoopStatus = decisionLoop.GetStatus();
                var responseLoopStatus = ReferenceEquals(result, latestLoopStatus.Recommendations)
                    ? latestLoopStatus
                    : latestLoopStatus with { Recommendations = null, Notifications = [] };
                var accountCacheScope = responseLoopStatus.AccountScopeId is { } responseAccountScopeId
                    ? accountViewScopes.GetToken(responseAccountScopeId)
                    : null;
                await PrimaryRecommendationResponseWriter.WriteAsync(context, result, responseLoopStatus, accountCacheScope).ConfigureAwait(false);
            });
        app.MapGet(
            "/api/notifications/preferences",
            async (
                HttpContext context,
                IPersonalTradingPostGateway personalTradingPost,
                IContinuousDecisionLoopService decisionLoop,
                CancellationToken cancellationToken) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                var scope = await personalTradingPost.GetAccountScopeAsync(cancellationToken).ConfigureAwait(false);
                if (!scope.IsSuccess || scope.Value is null)
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                var preferences = decisionLoop.GetPreferences(scope.Value.AccountId);
                return Results.Json(new { enabled = preferences.Enabled });
            });
        app.MapPut(
            "/api/notifications/preferences",
            async (
                HttpContext context,
                NotificationPreferenceRequest request,
                IPersonalTradingPostGateway personalTradingPost,
                IContinuousDecisionLoopService decisionLoop,
                CancellationToken cancellationToken) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                var scope = await personalTradingPost.GetAccountScopeAsync(cancellationToken).ConfigureAwait(false);
                if (!scope.IsSuccess || scope.Value is null)
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                decisionLoop.SetNotificationsEnabled(scope.Value.AccountId, request.Enabled);
                return Results.Json(new { enabled = request.Enabled });
            });
        app.MapPost(
            "/api/notifications/{notificationId}/acknowledge",
            async (
                HttpContext context,
                string notificationId,
                IPersonalTradingPostGateway personalTradingPost,
                IContinuousDecisionLoopService decisionLoop,
                CancellationToken cancellationToken) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                var scope = await personalTradingPost.GetAccountScopeAsync(cancellationToken).ConfigureAwait(false);
                if (!scope.IsSuccess || scope.Value is null)
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                return decisionLoop.Acknowledge(scope.Value.AccountId, notificationId)
                    ? Results.Json(new { state = "acknowledged" })
                    : Results.NotFound(new { error = "notification_not_found" });
            });
        app.MapLocalDataEndpoints();
        app.MapWatchlistEndpoints();
        app.MapInvestmentEndpoints();
        app.MapMarketHistoryCollectorEndpoints();
        app.MapHistoricalMarketAnalyticsEndpoint();
        app.MapCraftingOpportunityEndpoints();
        app.MapPlanEndpoints();
        app.Map("/api/{**path}", () => Results.NotFound(new { error = "api_route_not_found" }));

        MapFrontend(app, builder.Configuration);

        return app;
    }

    internal static PrimaryRecommendationPolicies DefaultRecommendationPolicies()
    {
        var sizing = PositionSizingPolicy.Default;
        return new PrimaryRecommendationPolicies(
            PrimaryRecommendationPolicy.Version,
            OpportunityScorePolicy.CurrentVersion,
            sizing.Version,
            FifoAccountingPolicy.Version,
            Gw2TradingPostFeePolicy.PolicyVersion,
            checked((int)LiveMarketScannerSettings.Default.MinimumNetProfit.Copper),
            LiveMarketScannerSettings.Default.MinimumRoiBasisPoints,
            sizing.CashReserveBasisPoints,
            PrimaryRecommendationService.Strategy,
            PrimaryRecommendationService.Category);
    }

    private static void MapFrontend(WebApplication app, IConfiguration configuration)
    {
        if (app.Environment.IsDevelopment())
        {
            return;
        }

        var frontendPath = FrontendPathResolver.Resolve(app.Environment.ContentRootPath, configuration);
        if (frontendPath is null)
        {
            return;
        }

        var fileProvider = new PhysicalFileProvider(frontendPath);
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
        app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = fileProvider });
    }

    private static MarketSamplingSettings CreateMarketSamplingSettings(IConfiguration configuration)
    {
        var defaults = MarketSamplingSettings.Default;
        var settings = new MarketSamplingSettings(
            configuration.GetValue<int?>("TyrianLedger:MarketSampling:PolicyVersion") ?? defaults.PolicyVersion,
            TimeSpan.FromMinutes(configuration.GetValue<double?>("TyrianLedger:MarketSampling:CurrentPersonalOrderIntervalMinutes") ?? defaults.CurrentPersonalOrderInterval.TotalMinutes),
            TimeSpan.FromMinutes(configuration.GetValue<double?>("TyrianLedger:MarketSampling:WatchlistIntervalMinutes") ?? defaults.WatchlistInterval.TotalMinutes),
            TimeSpan.FromMinutes(configuration.GetValue<double?>("TyrianLedger:MarketSampling:BroadMarketIntervalMinutes") ?? defaults.BroadMarketInterval.TotalMinutes));
        settings.Validate();
        return settings;
    }

    private static MarketHistoryCollectionSchedulerSettings CreateMarketHistoryCollectionSchedulerSettings(IConfiguration configuration)
    {
        var defaults = MarketHistoryCollectionSchedulerSettings.Default;
        var settings = new MarketHistoryCollectionSchedulerSettings(
            TimeSpan.FromSeconds(configuration.GetValue<double?>("TyrianLedger:MarketCollection:SourceRefreshIntervalSeconds") ?? defaults.SourceRefreshInterval.TotalSeconds));
        settings.Validate();
        return settings;
    }

    private static DecisionLoopSchedulerSettings CreateDecisionLoopSchedulerSettings(IConfiguration configuration)
    {
        var defaults = DecisionLoopSchedulerSettings.Default;
        var settings = new DecisionLoopSchedulerSettings(
            TimeSpan.FromSeconds(configuration.GetValue<double?>("TyrianLedger:DecisionLoop:CycleIntervalSeconds") ?? defaults.CycleInterval.TotalSeconds),
            TimeSpan.FromSeconds(configuration.GetValue<double?>("TyrianLedger:DecisionLoop:MinimumRetryIntervalSeconds") ?? defaults.MinimumRetryInterval.TotalSeconds),
            TimeSpan.FromSeconds(configuration.GetValue<double?>("TyrianLedger:DecisionLoop:MaximumRetryIntervalSeconds") ?? defaults.MaximumRetryInterval.TotalSeconds));
        settings.Validate();
        return settings;
    }
}

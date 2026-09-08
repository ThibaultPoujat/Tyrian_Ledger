using Gw2Tp.Application.AccountConnection;
using Gw2Tp.Application.Dashboard;
using Gw2Tp.Application.MarketScanning;
using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Application.MarketSnapshots;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Infrastructure.AccountConnection;
using Gw2Tp.Infrastructure.Persistence;
using Gw2Tp.Web.Hosting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Routing;

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
        builder.Services.AddTyrianLedgerAccountConnection(builder.Environment, builder.Configuration);
        builder.Services.AddTyrianLedgerPersistence(builder.Configuration);
        builder.Services.AddSingleton<IPersonalDashboardService, PersonalDashboardService>();
        builder.Services.AddSingleton<PublicMarketSnapshotCollector>();
        builder.Services.AddSingleton<ILiveMarketScanner, LiveMarketScanner>();
        builder.Services.AddSingleton(CreateMarketSamplingSettings(builder.Configuration));
        builder.Services.AddSingleton<IMarketSamplingSource, CurrentPersonalOrderMarketSamplingSource>();
        builder.Services.AddSingleton<IMarketSamplingSource, WatchlistMarketSamplingSource>();
        builder.Services.AddSingleton<IAdaptiveMarketSamplingPolicy, AdaptiveMarketSamplingPolicy>();
        builder.Services.AddSingleton(CreateMarketHistoryCollectionSchedulerSettings(builder.Configuration));
        builder.Services.AddSingleton<IMarketHistoryCollector, MarketHistoryCollector>();
        builder.Services.AddSingleton<IHistoricalMarketAnalyticsService, HistoricalMarketAnalyticsService>();
        builder.Services.AddSingleton<IMarketHistoryCollectionDelay>(SystemMarketHistoryCollectionDelay.Instance);
        builder.Services.AddHostedService<MarketHistoryCollectorHostedService>();
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
                CancellationToken cancellationToken) =>
            {
                var result = await synchronizationService.SynchronizeAsync(cancellationToken).ConfigureAwait(false);
                await PersonalTradingPostSynchronizationResponseWriter.WriteAsync(context, result).ConfigureAwait(false);
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
        app.MapLocalDataEndpoints();
        app.MapWatchlistEndpoints();
        app.MapMarketHistoryCollectorEndpoints();
        app.MapHistoricalMarketAnalyticsEndpoint();
        app.Map("/api/{**path}", () => Results.NotFound(new { error = "api_route_not_found" }));

        MapFrontend(app, builder.Configuration);

        return app;
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
}

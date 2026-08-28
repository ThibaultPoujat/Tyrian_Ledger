using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Preferences;
using Gw2Tp.Application.SessionPlanning;
using Gw2Tp.Application.Secrets;
using Gw2Tp.Infrastructure.Gw2Api;
using Gw2Tp.Infrastructure.Preferences;
using Gw2Tp.Infrastructure.Secrets;
using Gw2Tp.Web;
using Gw2Tp.Web.Dashboard;
using Gw2Tp.Web.Preferences;

// Tyrian Ledger Web composition root. Public GW2 market access begins in M2.

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(LocalServerBinding.ResolveUrls(builder.Configuration));
builder.Services.AddValidation();
builder.Services.AddTyrianLedgerSecretStore(builder.Environment);
builder.Services.AddTyrianLedgerGw2ApiClient(builder.Configuration);
builder.Services.AddTyrianLedgerUserSessionPreferences(builder.Configuration, builder.Environment);
builder.Services.AddSingleton<SessionPlanner>();
builder.Services.AddSingleton<DashboardSampleOpportunityProvider>();
var app = builder.Build();

await app.Services.MigrateTyrianLedgerUserSessionPreferencesAsync();

app.UseTyrianLedgerSecurityHeaders();

app.MapGet("/", () => Results.Ok(new { service = "Tyrian Ledger API", status = "running" }));
app.MapGet("/healthz", () => Results.Ok("ok"));
app.MapGet("/api/status", async (ISecretStore secretStore, CancellationToken cancellationToken) =>
{
    var credentialAvailability = await secretStore
        .GetGw2ApiCredentialAvailabilityAsync(cancellationToken);

    return Results.Ok(new ServiceStatusResponse(
        credentialAvailability == SecretAvailability.Available ? "configured" : "not-configured"));
});
app.MapGet("/api/diagnostics/market-data", (IMarketDataDiagnostics diagnostics) =>
    Results.Ok(MarketDataDiagnosticsResponse.From(diagnostics.GetSnapshot())));
app.MapUserSessionPreferencesEndpoints();
app.MapGet("/api/dashboard/opportunities", async (
    string? effortCategory,
    DashboardSampleOpportunityProvider provider,
    IUserSessionPreferencesStore preferencesStore,
    CancellationToken cancellationToken) =>
{
    if (!DashboardEffortCategoryValues.TryParse(effortCategory, out var selectedEffortCategory))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["effortCategory"] = ["Effort category must be very-low, low, medium, high, or ongoing-patient."],
        });
    }

    var preferences = await preferencesStore.GetAsync(cancellationToken);
    return Results.Ok(provider.GetDashboard(preferences, selectedEffortCategory));
});

app.Run();

public partial class Program;

internal sealed record ServiceStatusResponse(string CredentialStatus);

internal sealed record MarketDataDiagnosticsResponse(
    IReadOnlyList<MarketDataEndpointDiagnosticsResponse> Endpoints)
{
    internal static MarketDataDiagnosticsResponse From(MarketDataDiagnosticsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new MarketDataDiagnosticsResponse(
        [
            .. snapshot.Endpoints.Select(endpoint => new MarketDataEndpointDiagnosticsResponse(
                endpoint.Endpoint,
                endpoint.RequestCount,
                endpoint.CacheHitCount,
                endpoint.CacheMissCount,
                endpoint.RateLimitedResponseCount,
                endpoint.ParsingFailureCount,
                endpoint.LatencySampleCount,
                endpoint.TotalRequestLatencyMilliseconds,
                endpoint.AverageRequestLatencyMilliseconds)),
        ]);
    }
}

internal sealed record MarketDataEndpointDiagnosticsResponse(
    string Endpoint,
    long RequestCount,
    long CacheHitCount,
    long CacheMissCount,
    long RateLimitedResponseCount,
    long ParsingFailureCount,
    long LatencySampleCount,
    long TotalRequestLatencyMilliseconds,
    long AverageRequestLatencyMilliseconds);

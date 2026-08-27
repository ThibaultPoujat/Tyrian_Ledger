using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Secrets;
using Gw2Tp.Infrastructure.Gw2Api;
using Gw2Tp.Infrastructure.Secrets;
using Gw2Tp.Web;
using Gw2Tp.Web.Dashboard;

// Tyrian Ledger Web composition root. Public GW2 market access begins in M2.

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(LocalServerBinding.ResolveUrls(builder.Configuration));
builder.Services.AddValidation();
builder.Services.AddTyrianLedgerSecretStore(builder.Environment);
builder.Services.AddTyrianLedgerGw2ApiClient(builder.Configuration);
builder.Services.AddSingleton<DashboardSampleOpportunityProvider>();
var app = builder.Build();

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
app.MapGet("/api/dashboard/opportunities", (DashboardSampleOpportunityProvider provider) =>
    Results.Ok(provider.GetDashboard()));

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

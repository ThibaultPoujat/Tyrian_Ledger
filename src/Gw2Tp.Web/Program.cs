using Gw2Tp.Application.MarketData;
using Gw2Tp.Infrastructure.Gw2Api;
using Gw2Tp.Infrastructure.Preferences;
using Gw2Tp.Web;
using Gw2Tp.Web.Preferences;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(LocalServerBinding.ResolveUrls(builder.Configuration));
builder.Services.AddValidation();
builder.Services.AddTyrianLedgerGw2ApiClient(builder.Configuration);
builder.Services.AddTyrianLedgerUserSessionPreferences(builder.Configuration, builder.Environment);
var app = builder.Build();

await app.Services.MigrateTyrianLedgerUserSessionPreferencesAsync();

app.UseTyrianLedgerSecurityHeaders();

app.MapGet("/", () => Results.Ok(new { service = "Tyrian Ledger API", status = "running" }));
app.MapGet("/healthz", () => Results.Ok("ok"));
app.MapGet("/api/diagnostics/market-data", (IMarketDataDiagnostics diagnostics) =>
    Results.Ok(MarketDataDiagnosticsResponse.From(diagnostics.GetSnapshot())));
app.MapUserSessionPreferencesEndpoints();

app.Run();

public partial class Program;

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

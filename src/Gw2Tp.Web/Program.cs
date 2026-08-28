using Gw2Tp.Application.AccountAccess;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Operations;
using Gw2Tp.Application.Preferences;
using Gw2Tp.Application.SessionPlanning;
using Gw2Tp.Application.Secrets;
using Gw2Tp.Infrastructure.Gw2Api;
using Gw2Tp.Infrastructure.Preferences;
using Gw2Tp.Infrastructure.Secrets;
using Gw2Tp.Web;
using Gw2Tp.Web.AccountSnapshots;
using Gw2Tp.Web.Dashboard;
using Gw2Tp.Web.History;
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
builder.Services.AddSingleton<OperationHistoryStatisticsCalculator>();
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
app.MapGet("/api/account/access", async (
    IAccountAccessService accountAccessService,
    CancellationToken cancellationToken) =>
{
    var status = await accountAccessService.GetStatusAsync(cancellationToken);
    return Results.Ok(AccountAccessResponse.From(status));
});
app.MapGet("/api/diagnostics/market-data", (IMarketDataDiagnostics diagnostics) =>
    Results.Ok(MarketDataDiagnosticsResponse.From(diagnostics.GetSnapshot())));
app.MapAccountSnapshotEndpoints();
app.MapUserSessionPreferencesEndpoints();
app.MapGet("/api/history/statistics", async (
    IOperationHistoryStore operationHistoryStore,
    OperationHistoryStatisticsCalculator statisticsCalculator,
    CancellationToken cancellationToken) =>
{
    var operations = await operationHistoryStore.ListAsync(cancellationToken);
    return Results.Ok(OperationHistoryStatisticsResponse.From(statisticsCalculator.Calculate(operations)));
});
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

internal sealed record AccountAccessResponse(
    string ValidationStatus,
    string? KeyId,
    string? KeyName,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<AccountFeatureAccessResponse> Features)
{
    internal static AccountAccessResponse From(AccountAccessStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new AccountAccessResponse(
            ToWireValidationStatus(status.ValidationStatus),
            status.KeyId,
            status.KeyName,
            status.Permissions,
            status.Features.Select(feature => new AccountFeatureAccessResponse(
                feature.Feature,
                feature.IsAvailable,
                feature.MissingPermissions)).ToArray());
    }

    private static string ToWireValidationStatus(AccountAccessValidationStatus status) => status switch
    {
        AccountAccessValidationStatus.NotConfigured => "notconfigured",
        AccountAccessValidationStatus.Valid => "valid",
        AccountAccessValidationStatus.Invalid => "invalid",
        AccountAccessValidationStatus.Unavailable => "unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown account access validation status."),
    };
}

internal sealed record AccountFeatureAccessResponse(
    string Feature,
    bool IsAvailable,
    IReadOnlyList<string> MissingPermissions);

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

using Gw2Tp.Application.Secrets;
using Gw2Tp.Infrastructure.Gw2Api;
using Gw2Tp.Infrastructure.Secrets;
using Gw2Tp.Web;

// Tyrian Ledger Web composition root. Public GW2 market access begins in M2.

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(LocalServerBinding.ResolveUrls(builder.Configuration));
builder.Services.AddValidation();
builder.Services.AddTyrianLedgerSecretStore(builder.Environment);
builder.Services.AddTyrianLedgerGw2ApiClient();
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

app.Run();

public partial class Program;

internal sealed record ServiceStatusResponse(string CredentialStatus);

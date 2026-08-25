// Tyrian Ledger Web - M1 skeleton (TKT-M1-01).
// No business logic yet. GW2 access is a non-goal until M2.

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { service = "Tyrian Ledger API", status = "running" }));
app.MapGet("/healthz", () => Results.Ok("ok"));

app.Run();

public partial class Program;

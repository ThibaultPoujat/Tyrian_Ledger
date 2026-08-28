using Gw2Tp.Infrastructure.Preferences;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Gw2Tp.IntegrationTests;

/// <summary>
/// Gives every integration-test host an isolated SQLite database. The
/// production default path remains untouched, even when test hosts start in
/// the Development environment.
/// </summary>
public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string databasePath = Path.Combine(
        Path.GetTempPath(),
        "TyrianLedger",
        "IntegrationTests",
        $"web-host-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(
            UserSessionPreferencesServiceCollectionExtensions.DatabasePathConfigurationKey,
            databasePath);
    }
}

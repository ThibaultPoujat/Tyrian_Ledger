using System.Text.Json;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Operations;
using Gw2Tp.Infrastructure.Preferences;
using Gw2Tp.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Gw2Tp.IntegrationTests;

public sealed class OperationHistoryStatisticsEndpointTests
{
    [Fact]
    public async Task Statistics_endpoint_returns_the_deterministic_local_history_fixture()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<IOperationHistoryStore>();
        foreach (var operation in OperationHistoryStatisticsFixtures.CreatePopulated())
        {
            await store.CreateAsync(operation, CancellationToken.None);
        }

        using var response = await client.GetAsync("/api/history/statistics");
        response.EnsureSuccessStatusCode();
        using var actualDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        using var expectedDocument = await LoadFixtureAsync("history/operation-history-statistics-populated.json");

        Assert.True(
            JsonElement.DeepEquals(expectedDocument.RootElement, actualDocument.RootElement),
            "The history statistics endpoint must remain consistent with its deterministic fixture.");
    }

    [Fact]
    public async Task Statistics_endpoint_reports_an_explicit_empty_history()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/history/statistics");
        response.EnsureSuccessStatusCode();
        using var actualDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        using var expectedDocument = await LoadFixtureAsync("history/operation-history-statistics-empty.json");

        Assert.True(
            JsonElement.DeepEquals(expectedDocument.RootElement, actualDocument.RootElement),
            "Empty history must preserve missing evidence as null rather than zero.");
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting(
                    UserSessionPreferencesServiceCollectionExtensions.DatabasePathConfigurationKey,
                    Path.Combine(
                        Path.GetTempPath(),
                        "TyrianLedger",
                        "IntegrationTests",
                        $"operation-history-statistics-{Guid.NewGuid():N}.db"));
                builder.ConfigureServices(services => services.RemoveAll<IGw2ApiClient>());
            });

    private static Task<JsonDocument> LoadFixtureAsync(string fixtureName) =>
        new JsonFixtureLoader(Path.Combine(AppContext.BaseDirectory, "Fixtures")).LoadAsync(fixtureName);
}

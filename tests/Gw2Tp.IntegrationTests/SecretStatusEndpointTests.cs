using Gw2Tp.Application.Secrets;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.Json;
using Xunit;

namespace Gw2Tp.IntegrationTests;

public sealed class SecretStatusEndpointTests
{
    private const string SyntheticCredential = "synthetic-gw2-api-credential-for-web-boundary-test";

    [Fact]
    public async Task Status_endpoint_never_serializes_a_configured_credential()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISecretStore>();
                    services.AddSingleton<ISecretStore>(new ConfiguredTestSecretStore(SyntheticCredential));
                });
            });
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/status");
        var responseBody = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        using var responseDocument = JsonDocument.Parse(responseBody);
        Assert.Equal(
            "configured",
            responseDocument.RootElement.GetProperty("credentialStatus").GetString());
        Assert.DoesNotContain(SyntheticCredential, responseBody);
    }

    private sealed class ConfiguredTestSecretStore : ISecretStore
    {
        private readonly string _credential;

        public ConfiguredTestSecretStore(string credential)
        {
            _credential = credential;
        }

        public ValueTask<SecretAvailability> GetGw2ApiCredentialAvailabilityAsync(
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(SecretAvailability.Available);
        }

        public ValueTask EnsureGw2ApiCredentialAvailableAsync(
            CancellationToken cancellationToken = default)
        {
            return string.IsNullOrWhiteSpace(_credential)
                ? ValueTask.FromException(new LocalConfigurationException())
                : ValueTask.CompletedTask;
        }
    }
}

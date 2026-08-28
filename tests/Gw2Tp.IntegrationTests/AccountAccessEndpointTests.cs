using System.Text.Json;
using Gw2Tp.Application.AccountAccess;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Gw2Tp.IntegrationTests;

public sealed class AccountAccessEndpointTests
{
    private const string SyntheticCredential = "synthetic-gw2-api-credential-that-must-not-reach-the-browser";

    [Fact]
    public async Task Account_access_endpoint_returns_safe_metadata_and_never_serializes_a_credential()
    {
        using var factory = CreateFactory(new AccountAccessStatus(
            AccountAccessValidationStatus.Valid,
            "synthetic-token-id-fragment",
            "<img src=x onerror=alert('synthetic')>",
            ["account", "inventories"],
            [
                new AccountFeatureAccess("account-materials", true, []),
                new AccountFeatureAccess("account-crafting", false, ["characters", "unlocks"]),
            ]));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/account/access");
        var responseBody = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        Assert.Equal("valid", root.GetProperty("validationStatus").GetString());
        Assert.Equal("<img src=x onerror=alert('synthetic')>", root.GetProperty("keyName").GetString());
        Assert.Equal("account-crafting", root.GetProperty("features")[1].GetProperty("feature").GetString());
        Assert.False(root.GetProperty("features")[1].GetProperty("isAvailable").GetBoolean());
        Assert.DoesNotContain(SyntheticCredential, responseBody);
        Assert.DoesNotContain("apiKey", responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", responseBody, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(AccountAccessValidationStatus.NotConfigured, "notconfigured")]
    [InlineData(AccountAccessValidationStatus.Valid, "valid")]
    [InlineData(AccountAccessValidationStatus.Invalid, "invalid")]
    [InlineData(AccountAccessValidationStatus.Unavailable, "unavailable")]
    public async Task Account_access_endpoint_uses_stable_validation_status_literals(
        AccountAccessValidationStatus validationStatus,
        string expectedWireStatus)
    {
        using var factory = CreateFactory(new AccountAccessStatus(
            validationStatus,
            null,
            null,
            [],
            []));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/account/access");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        response.EnsureSuccessStatusCode();
        Assert.Equal(expectedWireStatus, document.RootElement.GetProperty("validationStatus").GetString());
    }

    private static WebApplicationFactory<Program> CreateFactory(AccountAccessStatus status) =>
        new TestWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAccountAccessService>();
                services.AddSingleton<IAccountAccessService>(new TestAccountAccessService(status));
            });
        });

    private sealed class TestAccountAccessService : IAccountAccessService
    {
        private readonly AccountAccessStatus _status;

        public TestAccountAccessService(AccountAccessStatus status)
        {
            _status = status;
        }

        public Task<AccountAccessStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_status);
    }
}

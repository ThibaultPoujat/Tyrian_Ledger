using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Gw2Tp.Application.AccountAccess;
using Gw2Tp.Infrastructure.AccountAccess;
using Gw2Tp.Infrastructure.Gw2Api;
using Gw2Tp.Infrastructure.Secrets;
using Gw2Tp.Testing;
using Xunit;

namespace Gw2Tp.Infrastructure.Tests;

public sealed class Gw2AccountAccessServiceTests
{
    private const string SyntheticCredential = "synthetic-gw2-api-credential-for-tokeninfo-tests";

    [Fact]
    public async Task Valid_token_fixture_uses_server_side_bearer_auth_and_reports_available_features()
    {
        var payload = await LoadFixturePayloadAsync("gw2/tokeninfo/valid.json");
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(CreateJsonResponse(payload)));
        using var httpClient = CreateHttpClient(handler);
        var service = CreateService(httpClient);

        var status = await service.GetStatusAsync();

        Assert.Equal(AccountAccessValidationStatus.Valid, status.ValidationStatus);
        Assert.Equal("synthetic-token-id-fragment", status.KeyId);
        Assert.Equal("Local crafting key", status.KeyName);
        Assert.Equal(["account", "characters", "inventories", "unlocks"], status.Permissions);
        Assert.All(status.Features, feature => Assert.True(feature.IsAvailable));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/v2/tokeninfo", request.RequestUri.AbsolutePath);
        Assert.Equal("Bearer", request.Authorization?.Scheme);
        Assert.Equal(SyntheticCredential, request.Authorization?.Parameter);
    }

    [Fact]
    public async Task Missing_permission_fixture_disables_only_unsupported_account_features()
    {
        var payload = await LoadFixturePayloadAsync("gw2/tokeninfo/missing-permission.json");
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(CreateJsonResponse(payload)));
        using var httpClient = CreateHttpClient(handler);
        var service = CreateService(httpClient);

        var status = await service.GetStatusAsync();

        Assert.Equal(AccountAccessValidationStatus.Valid, status.ValidationStatus);
        var materials = Assert.Single(status.Features, feature => feature.Feature == "account-materials");
        Assert.True(materials.IsAvailable);
        var crafting = Assert.Single(status.Features, feature => feature.Feature == "account-crafting");
        Assert.False(crafting.IsAvailable);
        Assert.Equal(["characters", "unlocks"], crafting.MissingPermissions);
    }

    [Fact]
    public async Task Xss_shaped_metadata_fixture_is_preserved_as_text_metadata()
    {
        var payload = await LoadFixturePayloadAsync("gw2/tokeninfo/xss-name.json");
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(CreateJsonResponse(payload)));
        using var httpClient = CreateHttpClient(handler);
        var service = CreateService(httpClient);

        var status = await service.GetStatusAsync();

        Assert.Equal("<img src=x onerror=alert('synthetic')>", status.KeyName);
    }

    [Fact]
    public async Task Malformed_token_metadata_is_not_treated_as_valid()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(CreateJsonResponse("{\"id\":\"synthetic\"}")));
        using var httpClient = CreateHttpClient(handler);
        var service = CreateService(httpClient);

        var status = await service.GetStatusAsync();

        Assert.Equal(AccountAccessValidationStatus.Unavailable, status.ValidationStatus);
        Assert.All(status.Features, feature => Assert.False(feature.IsAvailable));
    }

    [Fact]
    public async Task A_malformed_upstream_id_cannot_echo_the_credential_into_safe_metadata()
    {
        var payload = $$"""
            { "id": "{{SyntheticCredential}}", "name": "Synthetic key", "permissions": ["account"] }
            """;
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(CreateJsonResponse(payload)));
        using var httpClient = CreateHttpClient(handler);
        var service = CreateService(httpClient);

        var status = await service.GetStatusAsync();

        Assert.Equal(AccountAccessValidationStatus.Valid, status.ValidationStatus);
        Assert.Null(status.KeyId);
    }

    private static Gw2AccountAccessService CreateService(HttpClient httpClient) => new(
        httpClient,
        PassthroughRequestScheduler.Instance,
        new EnvironmentGw2ApiCredentialReader(_ => SyntheticCredential));

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri("https://api.guildwars2.com/v2/"),
    };

    private static HttpResponseMessage CreateJsonResponse(string payload) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(payload, Encoding.UTF8, "application/json"),
    };

    private static async Task<string> LoadFixturePayloadAsync(string relativePath)
    {
        var loader = new JsonFixtureLoader(Path.Combine(AppContext.BaseDirectory, "Fixtures"));
        using var document = await loader.LoadAsync(relativePath);
        return document.RootElement.GetRawText();
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responseFactory;

        public StubHttpMessageHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public List<CapturedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request.RequestUri);
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri, request.Headers.Authorization));
            return _responseFactory(request, cancellationToken);
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri RequestUri,
        AuthenticationHeaderValue? Authorization);

    private sealed class PassthroughRequestScheduler : IGw2RequestScheduler
    {
        public static readonly PassthroughRequestScheduler Instance = new();

        public async Task<T> ScheduleAsync<T>(
            Gw2RequestKey requestKey,
            Func<CancellationToken, Task<Gw2ScheduledResult<T>>> sendAsync,
            CancellationToken cancellationToken) =>
            (await sendAsync(cancellationToken)).Result;
    }
}

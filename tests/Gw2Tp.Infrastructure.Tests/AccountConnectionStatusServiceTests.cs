using System.Net;
using System.Text;
using Gw2Tp.Application.AccountConnection;
using Gw2Tp.Infrastructure.AccountConnection;
using Gw2Tp.Infrastructure.Gw2Api;
using Gw2Tp.Infrastructure.Secrets;
using Xunit;

namespace Gw2Tp.Infrastructure.Tests;

public sealed class AccountConnectionStatusServiceTests
{
    private const string SyntheticKey = "synthetic-gw2-api-key-that-must-not-cross-the-boundary";

    [Fact]
    public async Task Valid_tokeninfo_fixture_uses_server_side_bearer_auth_and_returns_only_safe_permissions()
    {
        var handler = new RecordingHandler(_ => CreateJsonResponse(HttpStatusCode.OK, LoadFixture("valid.json")));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.guildwars2.com/v2/") };
        var scheduler = new ImmediateRequestScheduler();
        var service = new AccountConnectionStatusService(new FixedKeySource(SyntheticKey), client, scheduler);

        var status = await service.GetStatusAsync();

        Assert.Equal(AccountConnectionState.Valid, status.State);
        Assert.Equal(["account", "tradingpost"], status.GrantedPermissions);
        Assert.Empty(status.MissingRequiredPermissions);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/v2/tokeninfo", request.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal(SyntheticKey, request.Headers.Authorization?.Parameter);
        Assert.Equal("account-connection/tokeninfo", Assert.Single(scheduler.RequestKeys).Value);
        Assert.DoesNotContain(SyntheticKey, Assert.Single(scheduler.RequestKeys).Value, StringComparison.Ordinal);
        Assert.DoesNotContain(SyntheticKey, string.Join('|', status.GrantedPermissions));
    }

    [Fact]
    public async Task Missing_key_does_not_make_an_upstream_request()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("No request expected."));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.guildwars2.com/v2/") };
        var service = new AccountConnectionStatusService(new FixedKeySource(null), client, new ImmediateRequestScheduler());

        var status = await service.GetStatusAsync();

        Assert.Equal(AccountConnectionState.NotConfigured, status.State);
        Assert.Empty(handler.Requests);
        Assert.Equal(["account", "tradingpost"], status.MissingRequiredPermissions);
    }

    [Fact]
    public async Task Missing_required_permissions_are_safe()
    {
        var handler = new RecordingHandler(_ => CreateJsonResponse(HttpStatusCode.OK, LoadFixture("missing-permission.json")));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.guildwars2.com/v2/") };
        var service = new AccountConnectionStatusService(new FixedKeySource(SyntheticKey), client, new ImmediateRequestScheduler());

        var status = await service.GetStatusAsync();

        Assert.Equal(AccountConnectionState.InsufficientPermissions, status.State);
        Assert.Equal(["account"], status.GrantedPermissions);
        Assert.Equal(["tradingpost"], status.MissingRequiredPermissions);
    }

    [Fact]
    public async Task Malicious_token_metadata_is_discarded_before_the_safe_status_contract()
    {
        var handler = new RecordingHandler(_ => CreateJsonResponse(HttpStatusCode.OK, LoadFixture("xss-name.json")));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.guildwars2.com/v2/") };
        var service = new AccountConnectionStatusService(new FixedKeySource(SyntheticKey), client, new ImmediateRequestScheduler());

        var status = await service.GetStatusAsync();

        Assert.Equal(AccountConnectionState.Valid, status.State);
        Assert.DoesNotContain("<img", status.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-token-id-fragment", status.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Invalid_or_revoked_responses_are_not_retried(HttpStatusCode statusCode)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(statusCode));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.guildwars2.com/v2/") };
        var service = new AccountConnectionStatusService(new FixedKeySource(SyntheticKey), client, new ImmediateRequestScheduler());

        var status = await service.GetStatusAsync();

        Assert.Equal(AccountConnectionState.Invalid, status.State);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "{}")]
    [InlineData(HttpStatusCode.InternalServerError, "{}")]
    [InlineData(HttpStatusCode.OK, "{")]
    public async Task Remote_failures_and_malformed_payloads_are_safe_unavailable_states(
        HttpStatusCode statusCode,
        string payload)
    {
        var handler = new RecordingHandler(_ => CreateJsonResponse(statusCode, payload));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.guildwars2.com/v2/") };
        var service = new AccountConnectionStatusService(new FixedKeySource(SyntheticKey), client, new ImmediateRequestScheduler());

        var status = await service.GetStatusAsync();

        Assert.Equal(AccountConnectionState.Unavailable, status.State);
        Assert.Empty(status.GrantedPermissions);
    }

    [Fact]
    public async Task Transport_failures_are_safe_and_do_not_return_the_key()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException(SyntheticKey));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.guildwars2.com/v2/") };
        var service = new AccountConnectionStatusService(new FixedKeySource(SyntheticKey), client, new ImmediateRequestScheduler());

        var status = await service.GetStatusAsync();

        Assert.Equal(AccountConnectionState.Unavailable, status.State);
        Assert.DoesNotContain(SyntheticKey, status.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Development_source_prefers_environment_without_consulting_the_os_source()
    {
        var operatingSystemSource = new ThrowingKeySource();
        var source = new DevelopmentGw2ApiKeySource(new FixedKeySource(SyntheticKey), operatingSystemSource);

        var value = await source.ReadAsync();

        Assert.Equal(SyntheticKey, value);
    }

    [Fact]
    public async Task Environment_source_trims_values_and_treats_whitespace_as_not_configured()
    {
        var configured = new EnvironmentGw2ApiKeySource(_ => $"  {SyntheticKey}  ");
        var missing = new EnvironmentGw2ApiKeySource(_ => "  ");

        Assert.Equal(SyntheticKey, await configured.ReadAsync());
        Assert.Null(await missing.ReadAsync());
    }

    [Theory]
    [InlineData(true, false, false, (int)OperatingSystemSecretStore.MacOsKeychain)]
    [InlineData(false, true, false, (int)OperatingSystemSecretStore.WindowsCredentialManager)]
    [InlineData(false, false, true, (int)OperatingSystemSecretStore.LinuxSecretService)]
    [InlineData(false, false, false, (int)OperatingSystemSecretStore.Unsupported)]
    public void Platform_selection_uses_only_supported_os_credential_vaults(
        bool isMacOs,
        bool isWindows,
        bool isLinux,
        int expectedStore)
    {
        var store = OperatingSystemGw2ApiKeySource.SelectStore(isMacOs, isWindows, isLinux);

        Assert.Equal((OperatingSystemSecretStore)expectedStore, store);
    }

    private static string LoadFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "gw2", "tokeninfo", name);
        return File.ReadAllText(path);
    }

    private static HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, string payload) =>
        new(statusCode) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };

    private sealed class FixedKeySource : IGw2ApiKeySource
    {
        private readonly string? _value;

        public FixedKeySource(string? value) => _value = value;

        public ValueTask<string?> ReadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_value);
    }

    private sealed class ThrowingKeySource : IGw2ApiKeySource
    {
        public ValueTask<string?> ReadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromException<string?>(new InvalidOperationException("OS source should not be used."));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) =>
            _responseFactory = responseFactory;

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responseFactory(request));
        }
    }

    private sealed class ImmediateRequestScheduler : IGw2RequestScheduler
    {
        public List<Gw2RequestKey> RequestKeys { get; } = [];

        public async Task<T> ScheduleAsync<T>(
            Gw2RequestKey requestKey,
            Func<CancellationToken, Task<Gw2ScheduledResult<T>>> sendAsync,
            CancellationToken cancellationToken)
        {
            RequestKeys.Add(requestKey);
            var result = await sendAsync(cancellationToken);
            return result.Result;
        }
    }
}

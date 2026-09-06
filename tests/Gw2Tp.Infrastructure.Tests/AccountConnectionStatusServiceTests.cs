using System.Net;
using System.Text;
using Gw2Tp.Application.AccountConnection;
using Gw2Tp.Infrastructure.AccountConnection;
using Gw2Tp.Infrastructure.Gw2Api;
using Gw2Tp.Infrastructure.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
        Assert.Equal(AccountConnectionStatusService.SchemaVersion, GetQueryParameters(request.RequestUri)["v"]);
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
    [InlineData(HttpStatusCode.PartialContent, "{\"permissions\":[\"account\",\"tradingpost\"]}")]
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
    public async Task Request_deadline_covers_a_stalled_tokeninfo_response_body()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new NeverEndingResponseContent(),
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.guildwars2.com/v2/") };
        var service = new AccountConnectionStatusService(
            new FixedKeySource(SyntheticKey),
            client,
            new ImmediateRequestScheduler(),
            TimeSpan.FromMilliseconds(20));

        var status = await service.GetStatusAsync();

        Assert.Equal(AccountConnectionState.Unavailable, status.State);
        Assert.Single(handler.Requests);
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
    public async Task Malformed_local_key_material_is_a_safe_unavailable_state()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("No request expected."));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.guildwars2.com/v2/") };
        var service = new AccountConnectionStatusService(
            new FixedKeySource("synthetic\r\nmalformed-key"),
            client,
            new ImmediateRequestScheduler());

        var status = await service.GetStatusAsync();

        Assert.Equal(AccountConnectionState.Unavailable, status.State);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Unavailable_secret_store_is_not_reported_as_definitely_not_configured()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("No request expected."));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.guildwars2.com/v2/") };
        var service = new AccountConnectionStatusService(
            new UnavailableKeySource(),
            client,
            new ImmediateRequestScheduler());

        var status = await service.GetStatusAsync();

        Assert.Equal(AccountConnectionState.Unavailable, status.State);
        Assert.Empty(handler.Requests);
        Assert.Equal(
            Gw2ApiKeyReadFailure.LocalConfigurationError,
            Gw2ApiKeyReadResult.Unavailable.Failure);
    }

    [Fact]
    public async Task Development_source_prefers_environment_without_consulting_the_os_source()
    {
        var operatingSystemSource = new ThrowingKeySource();
        var source = new DevelopmentGw2ApiKeySource(new FixedKeySource(SyntheticKey), operatingSystemSource);

        var result = await source.ReadAsync();

        Assert.Equal(Gw2ApiKeyReadState.Available, result.State);
        Assert.Equal(SyntheticKey, result.Value);
    }

    [Fact]
    public async Task Environment_source_trims_values_and_treats_whitespace_as_not_configured()
    {
        var configured = new EnvironmentGw2ApiKeySource(_ => $"  {SyntheticKey}  ");
        var missing = new EnvironmentGw2ApiKeySource(_ => "  ");

        var configuredResult = await configured.ReadAsync();
        var missingResult = await missing.ReadAsync();

        Assert.Equal(Gw2ApiKeyReadState.Available, configuredResult.State);
        Assert.Equal(SyntheticKey, configuredResult.Value);
        Assert.Equal(Gw2ApiKeyReadState.NotConfigured, missingResult.State);
        Assert.Null(missingResult.Value);
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

    [Fact]
    public void Service_registration_uses_the_configured_deadline_and_redacts_all_authenticated_headers()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gw2Api:RequestTimeoutMs"] = "1234",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddTyrianLedgerAccountConnection(new TestingHostEnvironment(), configuration);
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(AccountConnectionStatusService.HttpClientName);
        var httpClientOptions = provider
            .GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>()
            .Get(AccountConnectionStatusService.HttpClientName);

        Assert.Equal(TimeSpan.FromMilliseconds(1234), client.Timeout);
        Assert.True(httpClientOptions.ShouldRedactHeaderValue("Authorization"));
        Assert.True(httpClientOptions.ShouldRedactHeaderValue("X-Any-Header"));
    }

    [Fact]
    public async Task Registered_authenticated_pipeline_redacts_the_key_from_http_logs_on_a_failing_response()
    {
        const string sensitiveValue = "synthetic-sensitive-key-for-log-redaction";
        var handler = new RecordingHandler(_ => CreateJsonResponse(HttpStatusCode.InternalServerError, "{}"));
        using var logProvider = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders().AddProvider(logProvider));
        services.AddTyrianLedgerAccountConnection(new TestingHostEnvironment(), new ConfigurationBuilder().Build());
        services.RemoveAll<IGw2ApiKeySource>();
        services.AddSingleton<IGw2ApiKeySource>(new FixedKeySource(sensitiveValue));
        services.RemoveAll<IGw2RequestScheduler>();
        services.AddSingleton<IGw2RequestScheduler>(new ImmediateRequestScheduler());
        services.Configure<HttpClientFactoryOptions>(AccountConnectionStatusService.HttpClientName, options =>
            options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = handler));
        using var provider = services.BuildServiceProvider();

        var status = await provider.GetRequiredService<IAccountConnectionStatusService>().GetStatusAsync();

        Assert.Equal(AccountConnectionState.Unavailable, status.State);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(sensitiveValue, request.Headers.Authorization?.Parameter);
        Assert.DoesNotContain(sensitiveValue, logProvider.RenderedMessages, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveValue, status.ToString(), StringComparison.Ordinal);
    }

    private static string LoadFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "gw2", "tokeninfo", name);
        return File.ReadAllText(path);
    }

    private static HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, string payload) =>
        new(statusCode) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };

    private static IReadOnlyDictionary<string, string> GetQueryParameters(Uri requestUri) =>
        requestUri.Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0]),
                pair => pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : string.Empty,
                StringComparer.Ordinal);

    private sealed class FixedKeySource : IGw2ApiKeySource
    {
        private readonly string? _value;

        public FixedKeySource(string? value) => _value = value;

        public ValueTask<Gw2ApiKeyReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Gw2ApiKeyReadResult.FromValue(_value));
    }

    private sealed class ThrowingKeySource : IGw2ApiKeySource
    {
        public ValueTask<Gw2ApiKeyReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromException<Gw2ApiKeyReadResult>(new InvalidOperationException("OS source should not be used."));
    }

    private sealed class UnavailableKeySource : IGw2ApiKeySource
    {
        public ValueTask<Gw2ApiKeyReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Gw2ApiKeyReadResult.Unavailable);
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

    private sealed class NeverEndingResponseContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new NotSupportedException("The test consumes this response as a stream.");

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new NeverEndingReadStream());

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new NeverEndingReadStream());

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class NeverEndingReadStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            Task.FromException(new NotSupportedException());

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _renderedMessages = [];

        public string RenderedMessages => string.Join(Environment.NewLine, _renderedMessages);

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_renderedMessages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(List<string> renderedMessages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                renderedMessages.Add(formatter(state, exception));
                if (exception is not null)
                {
                    renderedMessages.Add(exception.ToString());
                }
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed class TestingHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";

        public string ApplicationName { get; set; } = "Gw2Tp.Infrastructure.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
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

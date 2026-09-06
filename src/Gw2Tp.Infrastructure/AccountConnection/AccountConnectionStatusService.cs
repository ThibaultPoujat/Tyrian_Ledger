using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Gw2Tp.Application.AccountConnection;
using Gw2Tp.Infrastructure.Gw2Api;
using Gw2Tp.Infrastructure.Secrets;

namespace Gw2Tp.Infrastructure.AccountConnection;

internal sealed class AccountConnectionStatusService : IAccountConnectionStatusService
{
    internal const string HttpClientName = "TyrianLedger.AccountConnection";
    // The currently verified global API schema. M13-03 retains ownership of
    // endpoint-specific revalidation in VERIFY-005.
    internal const string SchemaVersion = "2025-08-29T01:00:00.000Z";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IGw2ApiKeySource _apiKeySource;
    private readonly HttpClient _httpClient;
    private readonly IGw2RequestScheduler _requestScheduler;
    private readonly TimeSpan _requestTimeout;

    public AccountConnectionStatusService(
        IGw2ApiKeySource apiKeySource,
        HttpClient httpClient,
        IGw2RequestScheduler requestScheduler,
        TimeSpan? requestTimeout = null)
    {
        _apiKeySource = apiKeySource ?? throw new ArgumentNullException(nameof(apiKeySource));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _requestScheduler = requestScheduler ?? throw new ArgumentNullException(nameof(requestScheduler));
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(10);
        if (_requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }
    }

    public async Task<AccountConnectionStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        Gw2ApiKeyReadResult apiKeyResult;
        try
        {
            apiKeyResult = await _apiKeySource.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Unavailable();
        }

        if (apiKeyResult.State == Gw2ApiKeyReadState.NotConfigured)
        {
            return NotConfigured();
        }

        if (apiKeyResult.State != Gw2ApiKeyReadState.Available || string.IsNullOrWhiteSpace(apiKeyResult.Value))
        {
            return Unavailable();
        }

        var apiKey = apiKeyResult.Value;

        try
        {
            return await _requestScheduler.ScheduleAsync(
                // The scheduler identity must never include a credential. One
                // local user has one configured key, so the typed endpoint is
                // the complete non-secret request identity here.
                new Gw2RequestKey("account-connection/tokeninfo"),
                requestCancellationToken => SendTokenInfoAsync(apiKey, requestCancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Gw2RequestSchedulerCapacityExceededException)
        {
            return Unavailable();
        }
    }

    private async Task<Gw2ScheduledResult<AccountConnectionStatus>> SendTokenInfoAsync(
        string apiKey,
        CancellationToken cancellationToken)
    {
        try
        {
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(_requestTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"tokeninfo?v={SchemaVersion}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                requestTimeout.Token).ConfigureAwait(false);

            // VERIFY-012 remains open: neither status reliably distinguishes a
            // revoked/malformed key from every other invalid-key condition. Both
            // are permanent for this check and must never be retried here.
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new Gw2ScheduledResult<AccountConnectionStatus>(Invalid());
            }

            // tokeninfo is a single-resource validation endpoint. Unlike the
            // public batch endpoints, it has no supported partial-response
            // contract, so only its documented 200 result is acceptable.
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return new Gw2ScheduledResult<AccountConnectionStatus>(
                    Unavailable(),
                    GetRetryKind(response.StatusCode),
                    GetRetryAfter(response));
            }

            await using var responseStream = await response.Content
                .ReadAsStreamAsync(requestTimeout.Token)
                .ConfigureAwait(false);
            var payload = await JsonSerializer.DeserializeAsync<TokenInfoDto>(
                responseStream,
                SerializerOptions,
                requestTimeout.Token).ConfigureAwait(false);

            if (payload?.Permissions is null)
            {
                return new Gw2ScheduledResult<AccountConnectionStatus>(Unavailable());
            }

            var granted = AccountConnectionPermissions.Known
                .Where(permission => payload.Permissions.Contains(permission, StringComparer.Ordinal))
                .ToArray();
            var missing = AccountConnectionPermissions.Required
                .Where(permission => !granted.Contains(permission, StringComparer.Ordinal))
                .ToArray();

            var status = missing.Length == 0
                ? new AccountConnectionStatus(AccountConnectionState.Valid, granted, [])
                : new AccountConnectionStatus(AccountConnectionState.InsufficientPermissions, granted, missing);
            return new Gw2ScheduledResult<AccountConnectionStatus>(status);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new Gw2ScheduledResult<AccountConnectionStatus>(Unavailable());
        }
        catch (IOException)
        {
            return new Gw2ScheduledResult<AccountConnectionStatus>(Unavailable());
        }
        catch (JsonException)
        {
            return new Gw2ScheduledResult<AccountConnectionStatus>(Unavailable());
        }
        catch (TaskCanceledException)
        {
            return new Gw2ScheduledResult<AccountConnectionStatus>(Unavailable());
        }
        catch (FormatException)
        {
            return new Gw2ScheduledResult<AccountConnectionStatus>(Unavailable());
        }
    }

    private static Gw2RetryKind GetRetryKind(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.TooManyRequests => Gw2RetryKind.RateLimited,
        HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout =>
            Gw2RetryKind.UpstreamUnavailable,
        _ => Gw2RetryKind.None,
    };

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } retryAfterDelay && retryAfterDelay > TimeSpan.Zero)
        {
            return retryAfterDelay;
        }

        if (retryAfter?.Date is { } retryAfterDate)
        {
            var delay = retryAfterDate - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : null;
        }

        return null;
    }

    private static AccountConnectionStatus NotConfigured() =>
        new(AccountConnectionState.NotConfigured, [], AccountConnectionPermissions.Required);

    private static AccountConnectionStatus Invalid() =>
        new(AccountConnectionState.Invalid, [], AccountConnectionPermissions.Required);

    private static AccountConnectionStatus Unavailable() =>
        new(AccountConnectionState.Unavailable, [], AccountConnectionPermissions.Required);

    private sealed class TokenInfoDto
    {
        // The endpoint also returns id/name and may add further metadata. None
        // of that data is needed for feature readiness or crosses this boundary.
        public List<string>? Permissions { get; init; }
    }
}

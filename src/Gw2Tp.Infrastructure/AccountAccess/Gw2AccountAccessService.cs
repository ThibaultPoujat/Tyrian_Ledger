using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Gw2Tp.Application.AccountAccess;
using Gw2Tp.Infrastructure.Gw2Api;
using Gw2Tp.Infrastructure.Secrets;

namespace Gw2Tp.Infrastructure.AccountAccess;

/// <summary>
/// Typed, read-only validation of the locally stored GW2 API credential.
/// Only this infrastructure gateway observes the credential value.
/// </summary>
internal sealed class Gw2AccountAccessService : IAccountAccessService
{
    internal const string HttpClientName = Gw2ApiClient.HttpClientName;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly FeatureRequirement[] FeatureRequirements =
    [
        new("account-materials", ["account", "inventories"]),
        new("account-crafting", ["account", "characters", "unlocks"]),
    ];

    private readonly Func<HttpClient> _createHttpClient;
    private readonly IGw2RequestScheduler _requestScheduler;
    private readonly IGw2ApiCredentialReader _credentialReader;

    public Gw2AccountAccessService(
        IHttpClientFactory httpClientFactory,
        IGw2RequestScheduler requestScheduler,
        IGw2ApiCredentialReader credentialReader)
        : this(
            () => (httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory)))
                .CreateClient(HttpClientName),
            requestScheduler,
            credentialReader)
    {
    }

    internal Gw2AccountAccessService(
        HttpClient httpClient,
        IGw2RequestScheduler requestScheduler,
        IGw2ApiCredentialReader credentialReader)
        : this(
            () => httpClient ?? throw new ArgumentNullException(nameof(httpClient)),
            requestScheduler,
            credentialReader)
    {
    }

    private Gw2AccountAccessService(
        Func<HttpClient> createHttpClient,
        IGw2RequestScheduler requestScheduler,
        IGw2ApiCredentialReader credentialReader)
    {
        ArgumentNullException.ThrowIfNull(createHttpClient);
        ArgumentNullException.ThrowIfNull(requestScheduler);
        ArgumentNullException.ThrowIfNull(credentialReader);
        _createHttpClient = createHttpClient;
        _requestScheduler = requestScheduler;
        _credentialReader = credentialReader;
    }

    public async Task<AccountAccessStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        string? credential;
        try
        {
            credential = await _credentialReader.ReadGw2ApiCredentialAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return CreateFailure(AccountAccessValidationStatus.Unavailable);
        }

        if (credential is null)
        {
            return CreateFailure(AccountAccessValidationStatus.NotConfigured);
        }

        try
        {
            return await _requestScheduler.ScheduleAsync(
                new Gw2RequestKey("tokeninfo"),
                operationCancellationToken => SendTokenInfoAttemptAsync(credential, operationCancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Gw2RequestSchedulerCapacityExceededException)
        {
            return CreateFailure(AccountAccessValidationStatus.Unavailable);
        }
    }

    private async Task<Gw2ScheduledResult<AccountAccessStatus>> SendTokenInfoAttemptAsync(
        string credential,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "tokeninfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);

        try
        {
            using var response = await _createHttpClient().SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return Complete(CreateFailure(AccountAccessValidationStatus.Invalid));
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return new Gw2ScheduledResult<AccountAccessStatus>(
                    CreateFailure(AccountAccessValidationStatus.Unavailable),
                    GetRetryKind(response.StatusCode),
                    GetRetryAfter(response));
            }

            await using var responseStream = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var tokenInfo = await JsonSerializer.DeserializeAsync<TokenInfoDto>(
                responseStream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);

            return tokenInfo is null || string.IsNullOrWhiteSpace(tokenInfo.Id) || tokenInfo.Name is null || tokenInfo.Permissions is null
                ? Complete(CreateFailure(AccountAccessValidationStatus.Unavailable))
                : Complete(CreateValidatedStatus(tokenInfo, credential));
        }
        catch (JsonException)
        {
            return Complete(CreateFailure(AccountAccessValidationStatus.Unavailable));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return Complete(CreateFailure(AccountAccessValidationStatus.Unavailable));
        }
        catch (IOException)
        {
            return Complete(CreateFailure(AccountAccessValidationStatus.Unavailable));
        }
        catch (TaskCanceledException)
        {
            return Complete(CreateFailure(AccountAccessValidationStatus.Unavailable));
        }
    }

    private static AccountAccessStatus CreateValidatedStatus(TokenInfoDto tokenInfo, string credential)
    {
        var permissions = tokenInfo.Permissions!
            .Where(permission => !string.IsNullOrWhiteSpace(permission))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var grantedPermissions = permissions.ToHashSet(StringComparer.Ordinal);

        return new AccountAccessStatus(
            AccountAccessValidationStatus.Valid,
            string.Equals(tokenInfo.Id, credential, StringComparison.Ordinal) ? null : tokenInfo.Id,
            tokenInfo.Name,
            permissions,
            FeatureRequirements.Select(requirement => new AccountFeatureAccess(
                requirement.Feature,
                requirement.Permissions.All(grantedPermissions.Contains),
                requirement.Permissions.Where(permission => !grantedPermissions.Contains(permission)).ToArray()))
                .ToArray());
    }

    private static AccountAccessStatus CreateFailure(AccountAccessValidationStatus status) =>
        new(status, null, null, [], FeatureRequirements.Select(requirement => new AccountFeatureAccess(
            requirement.Feature,
            false,
            requirement.Permissions)).ToArray());

    private static Gw2ScheduledResult<AccountAccessStatus> Complete(AccountAccessStatus status) =>
        new(status, Gw2RetryKind.None, RetryAfter: null);

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
        if (retryAfter?.Delta is { } delay && delay > TimeSpan.Zero)
        {
            return delay;
        }

        if (retryAfter?.Date is { } date)
        {
            var dateDelay = date - DateTimeOffset.UtcNow;
            return dateDelay > TimeSpan.Zero ? dateDelay : null;
        }

        return null;
    }

    private sealed record FeatureRequirement(string Feature, IReadOnlyList<string> Permissions);
}

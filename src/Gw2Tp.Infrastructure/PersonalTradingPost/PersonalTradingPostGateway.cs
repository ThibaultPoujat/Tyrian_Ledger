using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Infrastructure.Gw2Api;
using Gw2Tp.Infrastructure.Secrets;

namespace Gw2Tp.Infrastructure.PersonalTradingPost;

/// <summary>
/// Typed, authenticated read-only gateway for personal account and Trading
/// Post data. It owns credential injection; no secret crosses its boundary.
/// </summary>
internal sealed class PersonalTradingPostGateway : IPersonalTradingPostGateway
{
    internal const string HttpClientName = "TyrianLedger.PersonalTradingPost";
    // The current global schema pin. Endpoint-specific schema verification is
    // deliberately retained in VERIFY-005 rather than inferred here.
    internal const string SchemaVersion = "2025-08-29T01:00:00.000Z";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IGw2ApiKeySource _apiKeySource;
    private readonly HttpClient _httpClient;
    private readonly IGw2RequestScheduler _requestScheduler;
    private readonly TimeSpan _requestTimeout;

    public PersonalTradingPostGateway(
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

    public Task<Gw2ApiResult<AccountScope>> GetAccountScopeAsync(
        CancellationToken cancellationToken = default) =>
        ReadAsync(
            "personal/account",
            $"account?v={SchemaVersion}",
            MapAccountScopeAsync,
            cancellationToken);

    public Task<Gw2ApiResult<PersonalTransactionPage>> GetCurrentBuyOrdersAsync(
        int page,
        CancellationToken cancellationToken = default) =>
        GetTransactionsAsync("current/buys", page, requiresPurchasedTimestamp: false, cancellationToken);

    public Task<Gw2ApiResult<PersonalTransactionPage>> GetCurrentSellListingsAsync(
        int page,
        CancellationToken cancellationToken = default) =>
        GetTransactionsAsync("current/sells", page, requiresPurchasedTimestamp: false, cancellationToken);

    public Task<Gw2ApiResult<PersonalTransactionPage>> GetCompletedBuyHistoryAsync(
        int page,
        CancellationToken cancellationToken = default) =>
        GetTransactionsAsync("history/buys", page, requiresPurchasedTimestamp: true, cancellationToken);

    public Task<Gw2ApiResult<PersonalTransactionPage>> GetCompletedSellHistoryAsync(
        int page,
        CancellationToken cancellationToken = default) =>
        GetTransactionsAsync("history/sells", page, requiresPurchasedTimestamp: true, cancellationToken);

    private Task<Gw2ApiResult<PersonalTransactionPage>> GetTransactionsAsync(
        string resourcePath,
        int page,
        bool requiresPurchasedTimestamp,
        CancellationToken cancellationToken)
    {
        if (page < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(page), "Page numbers must be zero or greater.");
        }

        var requestPath = $"commerce/transactions/{resourcePath}?page={page.ToString(CultureInfo.InvariantCulture)}&v={SchemaVersion}";
        return ReadAsync(
            $"personal/transactions/{resourcePath}/page-{page.ToString(CultureInfo.InvariantCulture)}",
            requestPath,
            (response, requestCancellationToken) => MapTransactionPageAsync(
                response,
                page,
                requiresPurchasedTimestamp,
                requestCancellationToken),
            cancellationToken);
    }

    private async Task<Gw2ApiResult<T>> ReadAsync<T>(
        string schedulerKey,
        string requestPath,
        Func<HttpResponseMessage, CancellationToken, Task<T>> mapAsync,
        CancellationToken cancellationToken)
    {
        Gw2ApiKeyReadResult keyResult;
        try
        {
            keyResult = await _apiKeySource.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Gw2ApiResult<T>.Failure(Gw2ApiErrorCategory.CredentialUnavailable);
        }

        if (keyResult.State == Gw2ApiKeyReadState.NotConfigured)
        {
            return Gw2ApiResult<T>.Failure(Gw2ApiErrorCategory.CredentialNotConfigured);
        }

        if (keyResult.State != Gw2ApiKeyReadState.Available || string.IsNullOrWhiteSpace(keyResult.Value))
        {
            return Gw2ApiResult<T>.Failure(Gw2ApiErrorCategory.CredentialUnavailable);
        }

        try
        {
            return await _requestScheduler.ScheduleAsync(
                new Gw2RequestKey(schedulerKey),
                requestCancellationToken => SendAsync(keyResult.Value, requestPath, mapAsync, requestCancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Gw2RequestSchedulerCapacityExceededException)
        {
            return Gw2ApiResult<T>.Failure(Gw2ApiErrorCategory.UpstreamUnavailable);
        }
    }

    private async Task<Gw2ScheduledResult<Gw2ApiResult<T>>> SendAsync<T>(
        string apiKey,
        string requestPath,
        Func<HttpResponseMessage, CancellationToken, Task<T>> mapAsync,
        CancellationToken cancellationToken)
    {
        try
        {
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(_requestTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, requestPath);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                requestTimeout.Token).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                return new Gw2ScheduledResult<Gw2ApiResult<T>>(
                    Gw2ApiResult<T>.Failure(Gw2ApiErrorCategory.IncompleteData));
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return new Gw2ScheduledResult<Gw2ApiResult<T>>(
                    Gw2ApiResult<T>.Failure(MapErrorCategory(response.StatusCode)),
                    GetRetryKind(response.StatusCode),
                    GetRetryAfter(response));
            }

            var value = await mapAsync(response, requestTimeout.Token).ConfigureAwait(false);
            return new Gw2ScheduledResult<Gw2ApiResult<T>>(Gw2ApiResult<T>.Success(value));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new Gw2ScheduledResult<Gw2ApiResult<T>>(
                Gw2ApiResult<T>.Failure(Gw2ApiErrorCategory.TransportFailure));
        }
        catch (IOException)
        {
            return new Gw2ScheduledResult<Gw2ApiResult<T>>(
                Gw2ApiResult<T>.Failure(Gw2ApiErrorCategory.TransportFailure));
        }
        catch (JsonException)
        {
            return new Gw2ScheduledResult<Gw2ApiResult<T>>(
                Gw2ApiResult<T>.Failure(Gw2ApiErrorCategory.InvalidPayload));
        }
        catch (TaskCanceledException)
        {
            return new Gw2ScheduledResult<Gw2ApiResult<T>>(
                Gw2ApiResult<T>.Failure(Gw2ApiErrorCategory.TransportFailure));
        }
        catch (FormatException)
        {
            return new Gw2ScheduledResult<Gw2ApiResult<T>>(
                Gw2ApiResult<T>.Failure(Gw2ApiErrorCategory.InvalidPayload));
        }
    }

    private static async Task<AccountScope> MapAccountScopeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var payload = await JsonSerializer.DeserializeAsync<AccountScopeDto>(
            stream,
            SerializerOptions,
            cancellationToken).ConfigureAwait(false);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Id))
        {
            throw new JsonException("The account scope payload is structurally incomplete.");
        }

        return new AccountScope(payload.Id);
    }

    private static async Task<PersonalTransactionPage> MapTransactionPageAsync(
        HttpResponseMessage response,
        int requestedPage,
        bool requiresPurchasedTimestamp,
        CancellationToken cancellationToken)
    {
        var pageSize = GetRequiredNonNegativeHeader(response, "X-Page-Size");
        var pageCount = GetRequiredNonNegativeHeader(response, "X-Page-Total");
        var resultCount = GetRequiredNonNegativeHeader(response, "X-Result-Count");
        var resultTotal = GetRequiredNonNegativeHeader(response, "X-Result-Total");
        if (pageSize <= 0 || pageCount <= 0 || requestedPage >= pageCount || resultCount > pageSize || resultCount > resultTotal)
        {
            throw new JsonException("The transaction page headers are inconsistent.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var payload = await JsonSerializer.DeserializeAsync<List<PersonalTradingPostTransactionDto>>(
            stream,
            SerializerOptions,
            cancellationToken).ConfigureAwait(false);
        if (payload is null || payload.Count != resultCount)
        {
            throw new JsonException("The transaction page payload is incomplete.");
        }

        return new PersonalTransactionPage(
            payload.Select(dto => MapTransaction(dto, requiresPurchasedTimestamp)).ToArray(),
            requestedPage,
            pageSize,
            pageCount,
            resultCount,
            resultTotal);
    }

    private static PersonalTradingPostTransaction MapTransaction(
        PersonalTradingPostTransactionDto dto,
        bool requiresPurchasedTimestamp)
    {
        if (dto is null || dto.Id <= 0 || dto.ItemId <= 0 || dto.Price < 0 || dto.Quantity <= 0 ||
            !TryParseTimestamp(dto.Created, out var createdAtUtc) ||
            !TryParseOptionalTimestamp(dto.Purchased, out var purchasedAtUtc) ||
            (requiresPurchasedTimestamp && purchasedAtUtc is null))
        {
            throw new JsonException("The transaction payload is structurally incomplete.");
        }

        return new PersonalTradingPostTransaction(
            dto.Id,
            dto.ItemId,
            dto.Price,
            dto.Quantity,
            createdAtUtc.ToUniversalTime(),
            purchasedAtUtc?.ToUniversalTime());
    }

    private static bool TryParseTimestamp(string? value, out DateTimeOffset timestamp) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out timestamp);

    private static bool TryParseOptionalTimestamp(string? value, out DateTimeOffset? timestamp)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            timestamp = null;
            return true;
        }

        if (TryParseTimestamp(value, out var parsedTimestamp))
        {
            timestamp = parsedTimestamp;
            return true;
        }

        timestamp = null;
        return false;
    }

    private static int GetRequiredNonNegativeHeader(HttpResponseMessage response, string headerName)
    {
        if (!response.Headers.TryGetValues(headerName, out var values) ||
            values.Take(2).ToArray() is not [var value] ||
            !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedValue) ||
            parsedValue < 0)
        {
            throw new JsonException($"The transaction response is missing a valid {headerName} header.");
        }

        return parsedValue;
    }

    private static Gw2ApiErrorCategory MapErrorCategory(HttpStatusCode statusCode)
    {
        if ((int)statusCode is >= 500 and <= 599)
        {
            return Gw2ApiErrorCategory.UpstreamUnavailable;
        }

        return statusCode switch
        {
            HttpStatusCode.BadRequest => Gw2ApiErrorCategory.InvalidRequest,
            HttpStatusCode.Unauthorized => Gw2ApiErrorCategory.Unauthorized,
            HttpStatusCode.Forbidden => Gw2ApiErrorCategory.Forbidden,
            HttpStatusCode.NotFound => Gw2ApiErrorCategory.NotFound,
            HttpStatusCode.TooManyRequests => Gw2ApiErrorCategory.RateLimited,
            _ => Gw2ApiErrorCategory.UnexpectedResponse,
        };
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
}

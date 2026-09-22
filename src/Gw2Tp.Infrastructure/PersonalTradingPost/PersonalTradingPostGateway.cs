using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Infrastructure.Gw2Api;
using Gw2Tp.Infrastructure.Diagnostics;
using Gw2Tp.Infrastructure.Secrets;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Infrastructure.PersonalTradingPost;

/// <summary>
/// Typed, authenticated read-only gateway for personal account and Trading
/// Post data. It owns credential injection; no secret crosses its boundary.
/// </summary>
internal sealed class PersonalTradingPostGateway : IPersonalTradingPostGateway, IAccountPortfolioGateway
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
    private readonly SafeTransportDiagnosticBuffer? _diagnostics;
    private readonly object _credentialScopeGate = new();
    private string? _lastCredential;
    private long _credentialScope;

    public PersonalTradingPostGateway(
        IGw2ApiKeySource apiKeySource,
        HttpClient httpClient,
        IGw2RequestScheduler requestScheduler,
        TimeSpan? requestTimeout = null,
        SafeTransportDiagnosticBuffer? diagnostics = null)
    {
        _apiKeySource = apiKeySource ?? throw new ArgumentNullException(nameof(apiKeySource));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _requestScheduler = requestScheduler ?? throw new ArgumentNullException(nameof(requestScheduler));
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(10);
        _diagnostics = diagnostics;
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

    public async Task<Gw2ApiResult<AccountPortfolioSnapshot>> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var credential = await ReadCredentialAsync(cancellationToken).ConfigureAwait(false);
        if (!credential.Result.IsSuccess || credential.ApiKey is null)
        {
            return Gw2ApiResult<AccountPortfolioSnapshot>.Failure(
                credential.Result.ErrorCategory ?? Gw2ApiErrorCategory.CredentialUnavailable);
        }

        var scope = GetCredentialScope(credential.ApiKey);
        try
        {
            var accountTask = ScheduleReadAsync(
                credential.ApiKey,
                scope,
                "personal/portfolio/account",
                $"account?v={SchemaVersion}",
                MapAccountScopeAsync,
                cancellationToken);
            var walletTask = ScheduleReadAsync(
                credential.ApiKey,
                scope,
                "personal/portfolio/wallet",
                $"account/wallet?v={SchemaVersion}",
                MapCoinBalanceAsync,
                cancellationToken);
            await Task.WhenAll(accountTask, walletTask).ConfigureAwait(false);
            var account = await accountTask.ConfigureAwait(false);
            var wallet = await walletTask.ConfigureAwait(false);
            if (!account.IsSuccess || account.IsPartialData || account.Value is null ||
                !wallet.IsSuccess || wallet.IsPartialData)
            {
                return Gw2ApiResult<AccountPortfolioSnapshot>.Failure(
                    account.ErrorCategory ?? wallet.ErrorCategory ?? Gw2ApiErrorCategory.IncompleteData);
            }

            return Gw2ApiResult<AccountPortfolioSnapshot>.Success(
                new AccountPortfolioSnapshot(account.Value, wallet.Value, null, DateTimeOffset.UtcNow));
        }
        catch (Gw2RequestSchedulerCapacityExceededException)
        {
            return Gw2ApiResult<AccountPortfolioSnapshot>.Failure(Gw2ApiErrorCategory.UpstreamUnavailable);
        }
    }

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
        var credential = await ReadCredentialAsync(cancellationToken).ConfigureAwait(false);
        if (!credential.Result.IsSuccess || credential.ApiKey is null)
        {
            return Gw2ApiResult<T>.Failure(
                credential.Result.ErrorCategory ?? Gw2ApiErrorCategory.CredentialUnavailable);
        }

        try
        {
            return await ScheduleReadAsync(
                credential.ApiKey,
                GetCredentialScope(credential.ApiKey),
                schedulerKey,
                requestPath,
                mapAsync,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Gw2RequestSchedulerCapacityExceededException)
        {
            return Gw2ApiResult<T>.Failure(Gw2ApiErrorCategory.UpstreamUnavailable);
        }
    }

    private async Task<(Gw2ApiResult<bool> Result, string? ApiKey)> ReadCredentialAsync(
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
            return (Gw2ApiResult<bool>.Failure(Gw2ApiErrorCategory.CredentialUnavailable), null);
        }

        if (keyResult.State == Gw2ApiKeyReadState.NotConfigured)
        {
            return (Gw2ApiResult<bool>.Failure(Gw2ApiErrorCategory.CredentialNotConfigured), null);
        }

        if (keyResult.State != Gw2ApiKeyReadState.Available || string.IsNullOrWhiteSpace(keyResult.Value))
        {
            return (Gw2ApiResult<bool>.Failure(Gw2ApiErrorCategory.CredentialUnavailable), null);
        }

        return (Gw2ApiResult<bool>.Success(true), keyResult.Value);
    }

    private Task<Gw2ApiResult<T>> ScheduleReadAsync<T>(
        string apiKey,
        long credentialScope,
        string schedulerKey,
        string requestPath,
        Func<HttpResponseMessage, CancellationToken, Task<T>> mapAsync,
        CancellationToken cancellationToken) =>
        _requestScheduler.ScheduleAsync(
            // A rotating generation prevents a replacement key from joining
            // an in-flight read made for the previous account.
            new Gw2RequestKey($"{schedulerKey}/credential-scope-{credentialScope.ToString(CultureInfo.InvariantCulture)}"),
            requestCancellationToken => SendAsync(apiKey, requestPath, mapAsync, requestCancellationToken),
            cancellationToken);

    private long GetCredentialScope(string apiKey)
    {
        lock (_credentialScopeGate)
        {
            if (!string.Equals(_lastCredential, apiKey, StringComparison.Ordinal))
            {
                _lastCredential = apiKey;
                _credentialScope++;
            }

            return _credentialScope;
        }
    }

    private async Task<Gw2ScheduledResult<Gw2ApiResult<T>>> SendAsync<T>(
        string apiKey,
        string requestPath,
        Func<HttpResponseMessage, CancellationToken, Task<T>> mapAsync,
        CancellationToken cancellationToken)
    {
        var startedAt = System.Diagnostics.Stopwatch.StartNew();
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
        catch (HttpRequestException exception)
        {
            _diagnostics?.Record(SanitizeOperation(requestPath), ClassifyTransportFailure(exception), exception, startedAt.Elapsed);
            return new Gw2ScheduledResult<Gw2ApiResult<T>>(
                Gw2ApiResult<T>.Failure(Gw2ApiErrorCategory.TransportFailure));
        }
        catch (IOException exception)
        {
            _diagnostics?.Record(SanitizeOperation(requestPath), "IO_FAILURE", exception, startedAt.Elapsed);
            return new Gw2ScheduledResult<Gw2ApiResult<T>>(
                Gw2ApiResult<T>.Failure(Gw2ApiErrorCategory.TransportFailure));
        }
        catch (JsonException)
        {
            return new Gw2ScheduledResult<Gw2ApiResult<T>>(
                Gw2ApiResult<T>.Failure(Gw2ApiErrorCategory.InvalidPayload));
        }
        catch (TaskCanceledException exception)
        {
            _diagnostics?.Record(SanitizeOperation(requestPath), "TIMEOUT", exception, startedAt.Elapsed);
            return new Gw2ScheduledResult<Gw2ApiResult<T>>(
                Gw2ApiResult<T>.Failure(Gw2ApiErrorCategory.TransportFailure));
        }
        catch (FormatException)
        {
            return new Gw2ScheduledResult<Gw2ApiResult<T>>(
                Gw2ApiResult<T>.Failure(Gw2ApiErrorCategory.InvalidPayload));
        }
    }

    private static string SanitizeOperation(string requestPath) =>
        requestPath.Split('?', 2, StringSplitOptions.TrimEntries)[0];

    private static string ClassifyTransportFailure(HttpRequestException exception)
    {
        var innerType = exception.InnerException?.GetType().Name;
        return innerType switch
        {
            "SocketException" => "SOCKET_FAILURE",
            "AuthenticationException" => "TLS_FAILURE",
            _ => "HTTP_TRANSPORT_FAILURE",
        };
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

    private static async Task<Money> MapCoinBalanceAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var payload = await JsonSerializer.DeserializeAsync<AccountWalletCurrencyDto[]>(
            stream,
            SerializerOptions,
            cancellationToken).ConfigureAwait(false);
        if (payload is null || payload.Any(value => value is null || value.Id is null || value.Id <= 0 || value.Value is null || value.Value < 0))
        {
            throw new JsonException("The account wallet payload is structurally invalid.");
        }
        if (payload.Select(value => value.Id!.Value).Distinct().Count() != payload.Length)
        {
            throw new JsonException("The account wallet payload contains duplicate currencies.");
        }
        var coins = payload.Where(value => value.Id == 1).ToArray();
        if (coins.Length != 1)
        {
            throw new JsonException("The account wallet payload must contain exactly one Coin currency.");
        }
        return new Money(coins[0].Value!.Value);
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
        if (pageSize <= 0)
        {
            throw new JsonException("The transaction response has an invalid X-Page-Size header.");
        }

        var expectedPageCount = resultTotal == 0
            ? 0
            : ((resultTotal - 1) / pageSize) + 1;
        var expectedResultCount = resultTotal == 0
            ? 0
            : requestedPage == expectedPageCount - 1
                ? ((resultTotal - 1) % pageSize) + 1
                : pageSize;
        if (pageCount != expectedPageCount ||
            (resultTotal == 0 ? requestedPage != 0 : requestedPage >= pageCount) ||
            resultCount != expectedResultCount)
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
        if (dto is null || dto.Id is not > 0 || dto.ItemId is not > 0 || dto.Price is not >= 0 || dto.Quantity is not > 0 ||
            !TryParseTimestamp(dto.Created, out var createdAtUtc) ||
            !TryParseOptionalTimestamp(dto.Purchased, out var purchasedAtUtc) ||
            (requiresPurchasedTimestamp && purchasedAtUtc is null))
        {
            throw new JsonException("The transaction payload is structurally incomplete.");
        }

        return new PersonalTradingPostTransaction(
            dto.Id.Value,
            dto.ItemId.Value,
            dto.Price.Value,
            dto.Quantity.Value,
            createdAtUtc.ToUniversalTime(),
            purchasedAtUtc?.ToUniversalTime());
    }

    private static bool TryParseTimestamp(string? value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (string.IsNullOrWhiteSpace(value) || !value.Contains('T', StringComparison.Ordinal))
        {
            return false;
        }

        var timeSeparatorIndex = value.IndexOf('T', StringComparison.Ordinal);
        var explicitOffsetIndex = Math.Max(value.LastIndexOf('+'), value.LastIndexOf('-'));
        if (!value.EndsWith('Z') && explicitOffsetIndex <= timeSeparatorIndex)
        {
            return false;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out timestamp);
    }

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

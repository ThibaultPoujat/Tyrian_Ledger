using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Diagnostics;
using Gw2Tp.Application.MarketData;

namespace Gw2Tp.Infrastructure.Gw2Api;

/// <summary>
/// HTTP transport for public GW2 market data. Caching remains outside this
/// type so all consumers enter through the cache-aware application gateway.
/// </summary>
internal sealed class Gw2ApiClient : IGw2ApiTransport
{
    internal const string SchemaVersion = "2025-08-29T01:00:00.000Z";
    internal const string HttpClientName = "TyrianLedger.Gw2Api";

    // System.Text.Json ignores unmapped JSON properties by default, keeping
    // additive upstream fields from breaking otherwise valid market payloads.
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly Func<HttpClient> _createHttpClient;
    private readonly IGw2RequestScheduler _requestScheduler;
    private readonly IMarketDataDiagnosticsRecorder _diagnostics;

    public Gw2ApiClient(
        IHttpClientFactory httpClientFactory,
        IGw2RequestScheduler requestScheduler,
        IMarketDataDiagnosticsRecorder diagnostics)
        : this(
            () => (httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory)))
                .CreateClient(HttpClientName),
            requestScheduler,
            diagnostics)
    {
    }

    public Gw2ApiClient(HttpClient httpClient, IGw2RequestScheduler requestScheduler)
        : this(
            () => httpClient ?? throw new ArgumentNullException(nameof(httpClient)),
            requestScheduler,
            NullMarketDataDiagnostics.Instance)
    {
    }

    internal Gw2ApiClient(
        HttpClient httpClient,
        IGw2RequestScheduler requestScheduler,
        IMarketDataDiagnosticsRecorder diagnostics)
        : this(
            () => httpClient ?? throw new ArgumentNullException(nameof(httpClient)),
            requestScheduler,
            diagnostics)
    {
    }

    private Gw2ApiClient(
        Func<HttpClient> createHttpClient,
        IGw2RequestScheduler requestScheduler,
        IMarketDataDiagnosticsRecorder diagnostics)
    {
        ArgumentNullException.ThrowIfNull(createHttpClient);
        ArgumentNullException.ThrowIfNull(requestScheduler);
        ArgumentNullException.ThrowIfNull(diagnostics);
        _createHttpClient = createHttpClient;
        _requestScheduler = requestScheduler;
        _diagnostics = diagnostics;
    }

    public Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken = default) =>
        SendBatchAsync<CommercePriceDto, MarketPrice>(
            "commerce/prices",
            itemIds,
            MapPrice,
            cancellationToken);

    public Task<Gw2ApiResult<IReadOnlyList<int>>> GetPriceItemIdsAsync(
        CancellationToken cancellationToken = default) =>
        SendIndexAsync(cancellationToken);

    public Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken = default) =>
        SendBatchAsync<CommerceListingDto, MarketListing>(
            "commerce/listings",
            itemIds,
            MapListing,
            cancellationToken);

    public Task<Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>> GetItemMetadataAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken = default) =>
        SendBatchAsync<ItemMetadataDto, MarketItemMetadata>(
            "items",
            itemIds,
            MapItemMetadata,
            cancellationToken);

    private async Task<Gw2ApiResult<IReadOnlyList<int>>> SendIndexAsync(
        CancellationToken cancellationToken)
    {
        var requestUri = CreateIndexRequestUri();

        try
        {
            return await _requestScheduler.ScheduleAsync(
                new Gw2RequestKey(requestUri.OriginalString),
                operationCancellationToken => SendIndexAttemptAsync(requestUri, operationCancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Gw2RequestSchedulerCapacityExceededException)
        {
            return Gw2ApiResult<IReadOnlyList<int>>.Failure(
                Gw2ApiErrorCategory.UpstreamUnavailable);
        }
    }

    private async Task<Gw2ApiResult<IReadOnlyList<TMarket>>> SendBatchAsync<TDto, TMarket>(
        string resourcePath,
        IReadOnlyCollection<int> itemIds,
        Func<TDto, TMarket> map,
        CancellationToken cancellationToken)
    {
        var requestUri = CreateBatchRequestUri(resourcePath, itemIds);
        var endpoint = GetEndpoint(resourcePath);

        try
        {
            return await _requestScheduler.ScheduleAsync(
                // Batch URIs are intentionally relative to the typed client's
                // fixed base address; their original string is the complete
                // public request identity for the scheduler.
                new Gw2RequestKey(requestUri.OriginalString),
                operationCancellationToken => SendBatchAttemptAsync(
                    endpoint,
                    requestUri,
                    map,
                    operationCancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Gw2RequestSchedulerCapacityExceededException)
        {
            return Gw2ApiResult<IReadOnlyList<TMarket>>.Failure(
                Gw2ApiErrorCategory.UpstreamUnavailable);
        }
    }

    private async Task<Gw2ScheduledResult<Gw2ApiResult<IReadOnlyList<TMarket>>>> SendBatchAttemptAsync<TDto, TMarket>(
        Gw2MarketDataEndpoint endpoint,
        Uri requestUri,
        Func<TDto, TMarket> map,
        CancellationToken operationCancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        try
        {
            using var response = await _createHttpClient().SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                operationCancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _diagnostics.RecordRateLimitedResponse(endpoint);
            }

            if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.PartialContent))
            {
                return new Gw2ScheduledResult<Gw2ApiResult<IReadOnlyList<TMarket>>>(
                    Gw2ApiResult<IReadOnlyList<TMarket>>.Failure(MapErrorCategory(response.StatusCode)),
                    GetRetryKind(response.StatusCode),
                    GetRetryAfter(response));
            }

            await using var responseStream = await response.Content
                .ReadAsStreamAsync(operationCancellationToken)
                .ConfigureAwait(false);
            var payload = await JsonSerializer.DeserializeAsync<List<TDto>>(
                responseStream,
                SerializerOptions,
                operationCancellationToken).ConfigureAwait(false);

            if (payload is null)
            {
                return new Gw2ScheduledResult<Gw2ApiResult<IReadOnlyList<TMarket>>>(
                    Gw2ApiResult<IReadOnlyList<TMarket>>.Failure(Gw2ApiErrorCategory.InvalidPayload));
            }

            var marketData = payload.Select(map).ToArray();
            return new Gw2ScheduledResult<Gw2ApiResult<IReadOnlyList<TMarket>>>(
                Gw2ApiResult<IReadOnlyList<TMarket>>.Success(
                    marketData,
                    response.StatusCode == HttpStatusCode.PartialContent));
        }
        catch (JsonException)
        {
            _diagnostics.RecordParsingFailure(endpoint);
            return new Gw2ScheduledResult<Gw2ApiResult<IReadOnlyList<TMarket>>>(
                Gw2ApiResult<IReadOnlyList<TMarket>>.Failure(Gw2ApiErrorCategory.InvalidPayload));
        }
        catch (OperationCanceledException) when (operationCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new Gw2ScheduledResult<Gw2ApiResult<IReadOnlyList<TMarket>>>(
                Gw2ApiResult<IReadOnlyList<TMarket>>.Failure(Gw2ApiErrorCategory.TransportFailure));
        }
        catch (IOException)
        {
            return new Gw2ScheduledResult<Gw2ApiResult<IReadOnlyList<TMarket>>>(
                Gw2ApiResult<IReadOnlyList<TMarket>>.Failure(Gw2ApiErrorCategory.TransportFailure));
        }
        catch (TaskCanceledException)
        {
            return new Gw2ScheduledResult<Gw2ApiResult<IReadOnlyList<TMarket>>>(
                Gw2ApiResult<IReadOnlyList<TMarket>>.Failure(Gw2ApiErrorCategory.TransportFailure));
        }
        finally
        {
            _diagnostics.RecordRequest(endpoint, stopwatch.Elapsed);
        }
    }

    private async Task<Gw2ScheduledResult<Gw2ApiResult<IReadOnlyList<int>>>> SendIndexAttemptAsync(
        Uri requestUri,
        CancellationToken operationCancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        try
        {
            using var response = await _createHttpClient().SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                operationCancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _diagnostics.RecordRateLimitedResponse(Gw2MarketDataEndpoint.PriceIndex);
            }

            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                return new Gw2ScheduledResult<Gw2ApiResult<IReadOnlyList<int>>>(
                    Gw2ApiResult<IReadOnlyList<int>>.Failure(Gw2ApiErrorCategory.IncompleteData));
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return new Gw2ScheduledResult<Gw2ApiResult<IReadOnlyList<int>>>(
                    Gw2ApiResult<IReadOnlyList<int>>.Failure(MapErrorCategory(response.StatusCode)),
                    GetRetryKind(response.StatusCode),
                    GetRetryAfter(response));
            }

            await using var responseStream = await response.Content
                .ReadAsStreamAsync(operationCancellationToken)
                .ConfigureAwait(false);
            var itemIds = await JsonSerializer.DeserializeAsync<List<int>>(
                responseStream,
                SerializerOptions,
                operationCancellationToken).ConfigureAwait(false);

            if (itemIds is null || itemIds.Count == 0 || itemIds.Any(itemId => itemId <= 0))
            {
                return new Gw2ScheduledResult<Gw2ApiResult<IReadOnlyList<int>>>(
                    Gw2ApiResult<IReadOnlyList<int>>.Failure(Gw2ApiErrorCategory.InvalidPayload));
            }

            if (itemIds.Distinct().Count() != itemIds.Count)
            {
                return new Gw2ScheduledResult<Gw2ApiResult<IReadOnlyList<int>>>(
                    Gw2ApiResult<IReadOnlyList<int>>.Failure(Gw2ApiErrorCategory.IncompleteData));
            }

            return new Gw2ScheduledResult<Gw2ApiResult<IReadOnlyList<int>>>(
                Gw2ApiResult<IReadOnlyList<int>>.Success(itemIds.OrderBy(itemId => itemId).ToArray()));
        }
        catch (JsonException)
        {
            _diagnostics.RecordParsingFailure(Gw2MarketDataEndpoint.PriceIndex);
            return new Gw2ScheduledResult<Gw2ApiResult<IReadOnlyList<int>>>(
                Gw2ApiResult<IReadOnlyList<int>>.Failure(Gw2ApiErrorCategory.InvalidPayload));
        }
        catch (OperationCanceledException) when (operationCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new Gw2ScheduledResult<Gw2ApiResult<IReadOnlyList<int>>>(
                Gw2ApiResult<IReadOnlyList<int>>.Failure(Gw2ApiErrorCategory.TransportFailure));
        }
        catch (IOException)
        {
            return new Gw2ScheduledResult<Gw2ApiResult<IReadOnlyList<int>>>(
                Gw2ApiResult<IReadOnlyList<int>>.Failure(Gw2ApiErrorCategory.TransportFailure));
        }
        catch (TaskCanceledException)
        {
            return new Gw2ScheduledResult<Gw2ApiResult<IReadOnlyList<int>>>(
                Gw2ApiResult<IReadOnlyList<int>>.Failure(Gw2ApiErrorCategory.TransportFailure));
        }
        finally
        {
            _diagnostics.RecordRequest(Gw2MarketDataEndpoint.PriceIndex, stopwatch.Elapsed);
        }
    }

    private static Gw2MarketDataEndpoint GetEndpoint(string resourcePath) => resourcePath switch
    {
        "commerce/prices" => Gw2MarketDataEndpoint.Prices,
        "commerce/listings" => Gw2MarketDataEndpoint.Listings,
        "items" => Gw2MarketDataEndpoint.Items,
        _ => throw new ArgumentOutOfRangeException(nameof(resourcePath), resourcePath, "Unknown market endpoint."),
    };

    private static Uri CreateIndexRequestUri() =>
        new($"commerce/prices?v={SchemaVersion}", UriKind.Relative);

    private static Uri CreateBatchRequestUri(
        string resourcePath,
        IReadOnlyCollection<int> itemIds)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        if (itemIds.Count == 0)
        {
            throw new ArgumentException("At least one item ID is required.", nameof(itemIds));
        }

        foreach (var itemId in itemIds)
        {
            if (itemId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(itemIds),
                    itemId,
                    "Item IDs must be positive.");
            }
        }

        var commaSeparatedIds = string.Join(
            ',',
            itemIds.Select(itemId => itemId.ToString(CultureInfo.InvariantCulture)));
        // Item IDs are validated positive invariant-culture integers. The
        // comma-separated value and pinned schema version are already safe
        // query components, so escaping the whole batch would only impose a
        // local input-length limit before the request reaches the gateway.
        var query = resourcePath == "items"
            ? $"ids={commaSeparatedIds}&lang=en&v={SchemaVersion}"
            : $"ids={commaSeparatedIds}&v={SchemaVersion}";

        return new Uri($"{resourcePath}?{query}", UriKind.Relative);
    }

    private static MarketPrice MapPrice(CommercePriceDto dto)
    {
        if (dto is null || dto.Id <= 0 || dto.Buys is null || dto.Sells is null)
        {
            throw new JsonException("The commerce price payload is structurally incomplete.");
        }

        return new MarketPrice(
            dto.Id,
            dto.Whitelisted,
            new MarketOrderSummary(dto.Buys.Quantity, dto.Buys.UnitPrice),
            new MarketOrderSummary(dto.Sells.Quantity, dto.Sells.UnitPrice));
    }

    private static MarketListing MapListing(CommerceListingDto dto)
    {
        if (dto is null || dto.Id <= 0 || dto.Buys is null || dto.Sells is null)
        {
            throw new JsonException("The commerce listings payload is structurally incomplete.");
        }

        return new MarketListing(
            dto.Id,
            dto.Buys.Select(MapListingLevel).ToArray(),
            dto.Sells.Select(MapListingLevel).ToArray());
    }

    private static MarketItemMetadata MapItemMetadata(ItemMetadataDto dto)
    {
        if (dto is null || dto.Id <= 0 || string.IsNullOrWhiteSpace(dto.Name))
        {
            throw new JsonException("The item metadata payload is structurally incomplete.");
        }

        return new MarketItemMetadata(dto.Id, dto.Name, MarketItemStackPolicy.NormalStackLimit);
    }

    private static MarketOrderLevel MapListingLevel(CommerceListingLevelDto dto)
    {
        if (dto is null)
        {
            throw new JsonException("The commerce listings payload contains an invalid order-book level.");
        }

        return new MarketOrderLevel(dto.Listings, dto.Quantity, dto.UnitPrice);
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
        if (retryAfter?.Delta is { } retryAfterDelta && retryAfterDelta > TimeSpan.Zero)
        {
            return retryAfterDelta;
        }

        if (retryAfter?.Date is { } retryAfterDate)
        {
            var retryAfterDateDelay = retryAfterDate - DateTimeOffset.UtcNow;
            return retryAfterDateDelay > TimeSpan.Zero ? retryAfterDateDelay : null;
        }

        return null;
    }
}

using System.Globalization;
using System.Net;
using System.Text.Json;
using Gw2Tp.Application.MarketData;

namespace Gw2Tp.Infrastructure.Gw2Api;

internal sealed class Gw2ApiClient : IGw2ApiClient
{
    internal const string SchemaVersion = "2025-08-29T01:00:00.000Z";

    // System.Text.Json ignores unmapped JSON properties by default, keeping
    // additive upstream fields from breaking otherwise valid market payloads.
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public Gw2ApiClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    public Task<Gw2ApiResult<IReadOnlyList<MarketPrice>>> GetPricesAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken = default) =>
        SendBatchAsync<CommercePriceDto, MarketPrice>(
            "commerce/prices",
            itemIds,
            MapPrice,
            cancellationToken);

    public Task<Gw2ApiResult<IReadOnlyList<MarketListing>>> GetListingsAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken = default) =>
        SendBatchAsync<CommerceListingDto, MarketListing>(
            "commerce/listings",
            itemIds,
            MapListing,
            cancellationToken);

    private async Task<Gw2ApiResult<IReadOnlyList<TMarket>>> SendBatchAsync<TDto, TMarket>(
        string resourcePath,
        IReadOnlyCollection<int> itemIds,
        Func<TDto, TMarket> map,
        CancellationToken cancellationToken)
    {
        var requestUri = CreateBatchRequestUri(resourcePath, itemIds);

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.PartialContent))
            {
                return Gw2ApiResult<IReadOnlyList<TMarket>>.Failure(MapErrorCategory(response.StatusCode));
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<List<TDto>>(
                responseStream,
                SerializerOptions,
                cancellationToken);

            if (payload is null)
            {
                return Gw2ApiResult<IReadOnlyList<TMarket>>.Failure(Gw2ApiErrorCategory.InvalidPayload);
            }

            var marketData = payload.Select(map).ToArray();
            return Gw2ApiResult<IReadOnlyList<TMarket>>.Success(
                marketData,
                response.StatusCode == HttpStatusCode.PartialContent);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException)
        {
            return Gw2ApiResult<IReadOnlyList<TMarket>>.Failure(Gw2ApiErrorCategory.InvalidPayload);
        }
        catch (HttpRequestException)
        {
            return Gw2ApiResult<IReadOnlyList<TMarket>>.Failure(Gw2ApiErrorCategory.TransportFailure);
        }
        catch (IOException)
        {
            return Gw2ApiResult<IReadOnlyList<TMarket>>.Failure(Gw2ApiErrorCategory.TransportFailure);
        }
        catch (TaskCanceledException)
        {
            return Gw2ApiResult<IReadOnlyList<TMarket>>.Failure(Gw2ApiErrorCategory.TransportFailure);
        }
    }

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
        var query = $"ids={commaSeparatedIds}&v={SchemaVersion}";

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
}

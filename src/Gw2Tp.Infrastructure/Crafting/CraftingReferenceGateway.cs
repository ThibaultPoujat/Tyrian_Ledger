using System.Globalization;
using System.Net;
using System.Text.Json;
using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Infrastructure.Gw2Api;

namespace Gw2Tp.Infrastructure.Crafting;

/// <summary>
/// Typed public recipe reader. Recipe definitions are reference data, not
/// account payloads, and requests are bounded to the documented conservative
/// 200-ID batch size until VERIFY-004 is resolved.
/// </summary>
internal sealed class CraftingReferenceGateway : ICraftingReferenceGateway
{
    internal const string HttpClientName = "TyrianLedger.CraftingReference";
    internal const int MaximumRecipeIdsPerRequest = 200;
    // Recipes retain the matrix's provisional recipe-specific schema pin.
    internal const string SchemaVersion = "2022-03-09T02:00:00.000Z";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;
    private readonly IGw2RequestScheduler requestScheduler;

    public CraftingReferenceGateway(HttpClient httpClient, IGw2RequestScheduler requestScheduler)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.requestScheduler = requestScheduler ?? throw new ArgumentNullException(nameof(requestScheduler));
    }

    public async Task<Gw2ApiResult<IReadOnlyList<CraftingRecipe>>> GetRecipesAsync(
        IReadOnlyCollection<int> recipeIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipeIds);
        if (recipeIds.Count == 0) return Gw2ApiResult<IReadOnlyList<CraftingRecipe>>.Success([]);
        if (recipeIds.Any(id => id <= 0)) throw new ArgumentOutOfRangeException(nameof(recipeIds));

        var requestedIds = recipeIds.OrderBy(id => id).ToArray();
        if (requestedIds.Distinct().Count() != requestedIds.Length) throw new ArgumentException("Recipe identifiers must be unique.", nameof(recipeIds));
        var all = new List<CraftingRecipe>(requestedIds.Length);
        foreach (var batch in requestedIds.Chunk(MaximumRecipeIdsPerRequest))
        {
            var result = await GetBatchAsync(batch, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess || result.Value is null)
            {
                return Gw2ApiResult<IReadOnlyList<CraftingRecipe>>.Failure(result.ErrorCategory ?? Gw2ApiErrorCategory.IncompleteData);
            }
            if (result.Value.Count != batch.Length || !result.Value.Select(recipe => recipe.RecipeId).ToHashSet().SetEquals(batch))
            {
                return Gw2ApiResult<IReadOnlyList<CraftingRecipe>>.Failure(Gw2ApiErrorCategory.IncompleteData);
            }
            all.AddRange(result.Value);
        }
        return Gw2ApiResult<IReadOnlyList<CraftingRecipe>>.Success(all.OrderBy(recipe => recipe.RecipeId).ToArray());
    }

    private async Task<Gw2ApiResult<IReadOnlyList<CraftingRecipe>>> GetBatchAsync(int[] ids, CancellationToken cancellationToken)
    {
        var queryIds = string.Join(',', ids.Select(id => id.ToString(CultureInfo.InvariantCulture)));
        var requestUri = new Uri($"recipes?ids={queryIds}&v={Uri.EscapeDataString(SchemaVersion)}", UriKind.Relative);
        try
        {
            return await requestScheduler.ScheduleAsync(
                new Gw2RequestKey(requestUri.OriginalString),
                operationCancellationToken => SendBatchAsync(requestUri, operationCancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Gw2RequestSchedulerCapacityExceededException)
        {
            return Gw2ApiResult<IReadOnlyList<CraftingRecipe>>.Failure(Gw2ApiErrorCategory.UpstreamUnavailable);
        }
    }

    private async Task<Gw2ScheduledResult<Gw2ApiResult<IReadOnlyList<CraftingRecipe>>>> SendBatchAsync(Uri requestUri, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return new(Gw2ApiResult<IReadOnlyList<CraftingRecipe>>.Failure(MapErrorCategory(response.StatusCode)), GetRetryKind(response.StatusCode));
            }
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var payload = await JsonSerializer.DeserializeAsync<CraftingRecipeDto[]>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
            if (payload is null) return new(Gw2ApiResult<IReadOnlyList<CraftingRecipe>>.Failure(Gw2ApiErrorCategory.InvalidPayload));
            return new(Gw2ApiResult<IReadOnlyList<CraftingRecipe>>.Success(payload.Select(MapRecipe).ToArray()));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new(Gw2ApiResult<IReadOnlyList<CraftingRecipe>>.Failure(Gw2ApiErrorCategory.TransportFailure));
        }
        catch (IOException)
        {
            return new(Gw2ApiResult<IReadOnlyList<CraftingRecipe>>.Failure(Gw2ApiErrorCategory.TransportFailure));
        }
        catch (JsonException)
        {
            return new(Gw2ApiResult<IReadOnlyList<CraftingRecipe>>.Failure(Gw2ApiErrorCategory.InvalidPayload));
        }
    }

    private static CraftingRecipe MapRecipe(CraftingRecipeDto dto)
    {
        if (dto is null || dto.Id is not > 0 || dto.OutputItemId is not > 0 || dto.OutputItemCount is not > 0 ||
            dto.Disciplines is null || dto.Disciplines.Any(string.IsNullOrWhiteSpace) || dto.Disciplines.Distinct(StringComparer.Ordinal).Count() != dto.Disciplines.Length ||
            dto.MinRating is not >= 0 || dto.Flags is null || dto.Flags.Any(string.IsNullOrWhiteSpace) || dto.Flags.Distinct(StringComparer.Ordinal).Count() != dto.Flags.Length ||
            dto.Ingredients is null || dto.Ingredients.Any(ingredient => ingredient is null || string.IsNullOrWhiteSpace(ingredient.Type) || ingredient.Id is not > 0 || ingredient.Count is not > 0))
        {
            throw new JsonException("The recipe payload is structurally incomplete.");
        }
        return new CraftingRecipe(
            dto.Id.Value,
            dto.OutputItemId.Value,
            dto.OutputItemCount.Value,
            dto.Disciplines.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            dto.MinRating.Value,
            dto.Flags.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            dto.Ingredients.Select(ingredient => new CraftingRecipeIngredient(ingredient.Type!, ingredient.Id!.Value, ingredient.Count!.Value)).ToArray());
    }

    private static Gw2ApiErrorCategory MapErrorCategory(HttpStatusCode statusCode) =>
        (int)statusCode is >= 500 and <= 599 ? Gw2ApiErrorCategory.UpstreamUnavailable : statusCode switch
        {
            HttpStatusCode.BadRequest => Gw2ApiErrorCategory.InvalidRequest,
            HttpStatusCode.NotFound => Gw2ApiErrorCategory.NotFound,
            HttpStatusCode.TooManyRequests => Gw2ApiErrorCategory.RateLimited,
            _ => Gw2ApiErrorCategory.UnexpectedResponse,
        };

    private static Gw2RetryKind GetRetryKind(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.TooManyRequests => Gw2RetryKind.RateLimited,
        HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout => Gw2RetryKind.UpstreamUnavailable,
        _ => Gw2RetryKind.None,
    };
}

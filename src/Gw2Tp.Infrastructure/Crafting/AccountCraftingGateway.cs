using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Infrastructure.Gw2Api;
using Gw2Tp.Infrastructure.PersonalTradingPost;
using Gw2Tp.Infrastructure.Secrets;

namespace Gw2Tp.Infrastructure.Crafting;

/// <summary>
/// Authenticated, read-only account-crafting gateway. It captures a credential
/// once, never puts it in a URI or scheduler key, and isolates each optional
/// account source so a missing scope disables only that source.
/// </summary>
internal sealed class AccountCraftingGateway : IAccountCraftingGateway
{
    internal const string HttpClientName = "TyrianLedger.AccountCrafting";
    internal const string SchemaVersion = PersonalTradingPostGateway.SchemaVersion;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IGw2ApiKeySource apiKeySource;
    private readonly HttpClient httpClient;
    private readonly IGw2RequestScheduler requestScheduler;
    private readonly TimeSpan requestTimeout;
    private readonly object credentialScopeGate = new();
    private string? lastCredential;
    private long credentialScope;

    public AccountCraftingGateway(
        IGw2ApiKeySource apiKeySource,
        HttpClient httpClient,
        IGw2RequestScheduler requestScheduler,
        TimeSpan? requestTimeout = null)
    {
        this.apiKeySource = apiKeySource ?? throw new ArgumentNullException(nameof(apiKeySource));
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.requestScheduler = requestScheduler ?? throw new ArgumentNullException(nameof(requestScheduler));
        this.requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(10);
        if (this.requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }
    }

    public async Task<Gw2ApiResult<AccountCraftingSnapshot>> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var credential = await ReadCredentialAsync(cancellationToken).ConfigureAwait(false);
        if (!credential.Result.IsSuccess || credential.ApiKey is null)
        {
            return Gw2ApiResult<AccountCraftingSnapshot>.Failure(
                credential.Result.ErrorCategory ?? Gw2ApiErrorCategory.CredentialUnavailable);
        }

        var scope = GetCredentialScope(credential.ApiKey);
        try
        {
            var account = await ReadAsync(credential.ApiKey, scope, "crafting/account", "account", MapAccountScopeAsync, cancellationToken)
                .ConfigureAwait(false);
            if (!account.IsSuccess || account.Value is null)
            {
                return Gw2ApiResult<AccountCraftingSnapshot>.Failure(account.ErrorCategory ?? Gw2ApiErrorCategory.IncompleteData);
            }

            var bankTask = ReadAsync(credential.ApiKey, scope, "crafting/bank", "account/bank", MapBankAsync, cancellationToken);
            var materialsTask = ReadAsync(credential.ApiKey, scope, "crafting/materials", "account/materials", MapMaterialsAsync, cancellationToken);
            var recipesTask = ReadAsync(credential.ApiKey, scope, "crafting/recipes", "account/recipes", MapRecipeUnlocksAsync, cancellationToken);
            var craftingTask = ReadCharacterCraftingAsync(credential.ApiKey, scope, cancellationToken);
            await Task.WhenAll(bankTask, materialsTask, recipesTask, craftingTask).ConfigureAwait(false);

            return Gw2ApiResult<AccountCraftingSnapshot>.Success(new AccountCraftingSnapshot(
                account.Value,
                DateTimeOffset.UtcNow,
                ToFeatureResult(await bankTask.ConfigureAwait(false)),
                ToFeatureResult(await materialsTask.ConfigureAwait(false)),
                ToFeatureResult(await recipesTask.ConfigureAwait(false)),
                ToFeatureResult(await craftingTask.ConfigureAwait(false))));
        }
        catch (Gw2RequestSchedulerCapacityExceededException)
        {
            return Gw2ApiResult<AccountCraftingSnapshot>.Failure(Gw2ApiErrorCategory.UpstreamUnavailable);
        }
    }

    private async Task<Gw2ApiResult<IReadOnlyList<CraftingDisciplineCapability>>> ReadCharacterCraftingAsync(
        string apiKey,
        long scope,
        CancellationToken cancellationToken)
    {
        var characters = await ReadAsync(apiKey, scope, "crafting/characters", "characters", MapCharacterNamesAsync, cancellationToken)
            .ConfigureAwait(false);
        if (!characters.IsSuccess || characters.Value is null)
        {
            return Gw2ApiResult<IReadOnlyList<CraftingDisciplineCapability>>.Failure(
                characters.ErrorCategory ?? Gw2ApiErrorCategory.IncompleteData);
        }

        var characterNames = characters.Value;
        var tasks = characterNames.Select((name, index) => ReadAsync(
            apiKey,
            scope,
            $"crafting/character/{index.ToString(CultureInfo.InvariantCulture)}",
            $"characters/{Uri.EscapeDataString(name)}/crafting",
            MapCharacterCraftingAsync,
            cancellationToken)).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        var failure = results.FirstOrDefault(result => !result.IsSuccess || result.Value is null);
        if (failure is not null)
        {
            return Gw2ApiResult<IReadOnlyList<CraftingDisciplineCapability>>.Failure(
                failure.ErrorCategory ?? Gw2ApiErrorCategory.IncompleteData);
        }

        var capabilities = results
            .SelectMany(result => result.Value!)
            .GroupBy(capability => capability.Discipline, StringComparer.Ordinal)
            .Select(group => new CraftingDisciplineCapability(
                group.Key,
                group.Max(capability => capability.Rating),
                group.Any(capability => capability.IsActive)))
            .OrderBy(capability => capability.Discipline, StringComparer.Ordinal)
            .ToArray();
        return Gw2ApiResult<IReadOnlyList<CraftingDisciplineCapability>>.Success(capabilities);
    }

    private static CraftingFeatureResult<T> ToFeatureResult<T>(Gw2ApiResult<T> result) =>
        result.IsSuccess && result.Value is not null
            ? CraftingFeatureResult<T>.Available(result.Value)
            : CraftingFeatureResult<T>.FromFailure(result.ErrorCategory ?? Gw2ApiErrorCategory.IncompleteData);

    private async Task<Gw2ApiResult<T>> ReadAsync<T>(
        string apiKey,
        long scope,
        string schedulerKey,
        string resourcePath,
        Func<HttpResponseMessage, CancellationToken, Task<T>> mapAsync,
        CancellationToken cancellationToken)
    {
        try
        {
            return await requestScheduler.ScheduleAsync(
                new Gw2RequestKey($"{schedulerKey}/credential-scope-{scope.ToString(CultureInfo.InvariantCulture)}"),
                requestCancellationToken => SendAsync(apiKey, resourcePath, mapAsync, requestCancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Gw2RequestSchedulerCapacityExceededException)
        {
            return Gw2ApiResult<T>.Failure(Gw2ApiErrorCategory.UpstreamUnavailable);
        }
    }

    private async Task<(Gw2ApiResult<bool> Result, string? ApiKey)> ReadCredentialAsync(CancellationToken cancellationToken)
    {
        Gw2ApiKeyReadResult keyResult;
        try
        {
            keyResult = await apiKeySource.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return (Gw2ApiResult<bool>.Failure(Gw2ApiErrorCategory.CredentialUnavailable), null);
        }

        return keyResult.State == Gw2ApiKeyReadState.NotConfigured
            ? (Gw2ApiResult<bool>.Failure(Gw2ApiErrorCategory.CredentialNotConfigured), null)
            : keyResult.State != Gw2ApiKeyReadState.Available || string.IsNullOrWhiteSpace(keyResult.Value)
                ? (Gw2ApiResult<bool>.Failure(Gw2ApiErrorCategory.CredentialUnavailable), null)
                : (Gw2ApiResult<bool>.Success(true), keyResult.Value);
    }

    private long GetCredentialScope(string apiKey)
    {
        lock (credentialScopeGate)
        {
            if (!string.Equals(lastCredential, apiKey, StringComparison.Ordinal))
            {
                lastCredential = apiKey;
                credentialScope++;
            }

            return credentialScope;
        }
    }

    private async Task<Gw2ScheduledResult<Gw2ApiResult<T>>> SendAsync<T>(
        string apiKey,
        string resourcePath,
        Func<HttpResponseMessage, CancellationToken, Task<T>> mapAsync,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(requestTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{resourcePath}?v={SchemaVersion}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                return new(Gw2ApiResult<T>.Failure(Gw2ApiErrorCategory.IncompleteData));
            }
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return new(Gw2ApiResult<T>.Failure(MapErrorCategory(response.StatusCode)), GetRetryKind(response.StatusCode), GetRetryAfter(response));
            }
            return new(Gw2ApiResult<T>.Success(await mapAsync(response, timeout.Token).ConfigureAwait(false)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new(Gw2ApiResult<T>.Failure(Gw2ApiErrorCategory.TransportFailure));
        }
        catch (IOException)
        {
            return new(Gw2ApiResult<T>.Failure(Gw2ApiErrorCategory.TransportFailure));
        }
        catch (JsonException)
        {
            return new(Gw2ApiResult<T>.Failure(Gw2ApiErrorCategory.InvalidPayload));
        }
        catch (TaskCanceledException)
        {
            return new(Gw2ApiResult<T>.Failure(Gw2ApiErrorCategory.TransportFailure));
        }
    }

    private static async Task<AccountScope> MapAccountScopeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var payload = await JsonSerializer.DeserializeAsync<AccountScopeDto>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Id)) throw new JsonException();
        return new AccountScope(payload.Id);
    }

    private static async Task<IReadOnlyList<AccountInventoryEntry>> MapBankAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var payload = await JsonSerializer.DeserializeAsync<AccountBankSlotDto?[]>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        if (payload is null) throw new JsonException();
        var entries = new List<AccountInventoryEntry>();
        foreach (var slot in payload)
        {
            if (slot is null) continue;
            if (slot.ItemId is not > 0 || slot.Count is not > 0) throw new JsonException();
            entries.Add(new(slot.ItemId.Value, slot.Count.Value, MapBinding(slot.Binding)));
        }
        return entries.OrderBy(entry => entry.ItemId).ThenBy(entry => entry.Binding).ThenBy(entry => entry.Quantity).ToArray();
    }

    private static async Task<IReadOnlyList<AccountMaterialEntry>> MapMaterialsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var payload = await JsonSerializer.DeserializeAsync<AccountMaterialDto[]>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        if (payload is null || payload.Any(entry => entry is null || entry.ItemId is not > 0 || entry.CategoryId is not > 0 || entry.Count is not >= 0) ||
            payload.Select(entry => entry.ItemId!.Value).Distinct().Count() != payload.Length) throw new JsonException();
        return payload.Select(entry => new AccountMaterialEntry(entry.ItemId!.Value, entry.CategoryId!.Value, entry.Count!.Value, MapBinding(entry.Binding)))
            .OrderBy(entry => entry.ItemId).ToArray();
    }

    private static async Task<IReadOnlyList<int>> MapRecipeUnlocksAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var payload = await JsonSerializer.DeserializeAsync<int[]>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        if (payload is null || payload.Any(id => id <= 0) || payload.Distinct().Count() != payload.Length) throw new JsonException();
        return payload.OrderBy(id => id).ToArray();
    }

    private static async Task<IReadOnlyList<string>> MapCharacterNamesAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var payload = await JsonSerializer.DeserializeAsync<string[]>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        if (payload is null || payload.Any(string.IsNullOrWhiteSpace) || payload.Distinct(StringComparer.Ordinal).Count() != payload.Length) throw new JsonException();
        return payload.OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    private static async Task<IReadOnlyList<CraftingDisciplineCapability>> MapCharacterCraftingAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var payload = await JsonSerializer.DeserializeAsync<CharacterCraftingDto>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        if (payload?.Crafting is null || payload.Crafting.Any(value => value is null || string.IsNullOrWhiteSpace(value.Discipline) || value.Rating is < 0 || value.Active is null) ||
            payload.Crafting.Select(value => value.Discipline!).Distinct(StringComparer.Ordinal).Count() != payload.Crafting.Length) throw new JsonException();
        return payload.Crafting.Select(value => new CraftingDisciplineCapability(value.Discipline!, value.Rating!.Value, value.Active!.Value)).ToArray();
    }

    private static AccountItemBinding MapBinding(string? value) => value switch
    {
        null or "" => AccountItemBinding.Unspecified,
        "Account" => AccountItemBinding.AccountBound,
        "Character" => AccountItemBinding.CharacterBound,
        _ => AccountItemBinding.OtherBound,
    };

    private static Gw2ApiErrorCategory MapErrorCategory(HttpStatusCode statusCode) =>
        (int)statusCode is >= 500 and <= 599 ? Gw2ApiErrorCategory.UpstreamUnavailable : statusCode switch
        {
            HttpStatusCode.BadRequest => Gw2ApiErrorCategory.InvalidRequest,
            HttpStatusCode.Unauthorized => Gw2ApiErrorCategory.Unauthorized,
            HttpStatusCode.Forbidden => Gw2ApiErrorCategory.Forbidden,
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

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response) => response.Headers.RetryAfter?.Delta is { } delay && delay > TimeSpan.Zero ? delay : null;
}

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Gw2Tp.Application.AccountAccess;
using Gw2Tp.Application.AccountSnapshots;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Time;
using Gw2Tp.Domain.MarketData;
using Gw2Tp.Infrastructure.Gw2Api;
using Gw2Tp.Infrastructure.Secrets;
using Microsoft.Extensions.Options;

namespace Gw2Tp.Infrastructure.AccountSnapshots;

/// <summary>
/// Typed, read-only ingestion for minimized authenticated account snapshots.
/// Account payloads are mapped before caching and are never persisted.
/// </summary>
internal sealed class Gw2AccountSnapshotService : IAccountSnapshotService
{
    private const string OwnedItemsFeature = "account-materials";
    private const string CraftingFeature = "account-crafting";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly Func<HttpClient> _createHttpClient;
    private readonly IGw2RequestScheduler _requestScheduler;
    private readonly IGw2ApiCredentialReader _credentialReader;
    private readonly IAccountAccessService _accountAccessService;
    private readonly ExpiringSnapshotCache<AccountOwnedItemsSnapshot> _ownedItemsCache;
    private readonly ExpiringSnapshotCache<AccountCraftingSnapshot> _craftingCache;

    public Gw2AccountSnapshotService(
        IHttpClientFactory httpClientFactory,
        IGw2RequestScheduler requestScheduler,
        IGw2ApiCredentialReader credentialReader,
        IAccountAccessService accountAccessService,
        IOptions<Gw2AccountSnapshotCacheOptions> options,
        IClock clock)
        : this(
            () => (httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory)))
                .CreateClient(Gw2ApiClient.HttpClientName),
            requestScheduler,
            credentialReader,
            accountAccessService,
            options?.Value ?? throw new ArgumentNullException(nameof(options)),
            clock)
    {
    }

    internal Gw2AccountSnapshotService(
        HttpClient httpClient,
        IGw2RequestScheduler requestScheduler,
        IGw2ApiCredentialReader credentialReader,
        IAccountAccessService accountAccessService,
        Gw2AccountSnapshotCacheOptions options,
        IClock clock)
        : this(
            () => httpClient ?? throw new ArgumentNullException(nameof(httpClient)),
            requestScheduler,
            credentialReader,
            accountAccessService,
            options,
            clock)
    {
    }

    private Gw2AccountSnapshotService(
        Func<HttpClient> createHttpClient,
        IGw2RequestScheduler requestScheduler,
        IGw2ApiCredentialReader credentialReader,
        IAccountAccessService accountAccessService,
        Gw2AccountSnapshotCacheOptions options,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(createHttpClient);
        ArgumentNullException.ThrowIfNull(requestScheduler);
        ArgumentNullException.ThrowIfNull(credentialReader);
        ArgumentNullException.ThrowIfNull(accountAccessService);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        if (!options.TryValidate(out var validationError))
        {
            throw new OptionsValidationException(
                "Gw2Api:AccountSnapshotCache",
                typeof(Gw2AccountSnapshotCacheOptions),
                [validationError]);
        }

        _createHttpClient = createHttpClient;
        _requestScheduler = requestScheduler;
        _credentialReader = credentialReader;
        _accountAccessService = accountAccessService;
        var timeToLive = TimeSpan.FromSeconds(options.TimeToLiveSeconds);
        _ownedItemsCache = new ExpiringSnapshotCache<AccountOwnedItemsSnapshot>(
            OwnedItemsFeature,
            timeToLive,
            clock);
        _craftingCache = new ExpiringSnapshotCache<AccountCraftingSnapshot>(
            CraftingFeature,
            timeToLive,
            clock);
    }

    public Task<AccountSnapshotLoadResult<AccountOwnedItemsSnapshot>> GetOwnedItemsAsync(
        AccountSnapshotRefreshMode refreshMode = AccountSnapshotRefreshMode.UseCache,
        CancellationToken cancellationToken = default) =>
        GetFeatureAsync(
            OwnedItemsFeature,
            refreshMode,
            _ownedItemsCache,
            LoadOwnedItemsAsync,
            cancellationToken);

    public Task<AccountSnapshotLoadResult<AccountCraftingSnapshot>> GetCraftingAsync(
        AccountSnapshotRefreshMode refreshMode = AccountSnapshotRefreshMode.UseCache,
        CancellationToken cancellationToken = default) =>
        GetFeatureAsync(
            CraftingFeature,
            refreshMode,
            _craftingCache,
            LoadCraftingAsync,
            cancellationToken);

    private async Task<AccountSnapshotLoadResult<TSnapshot>> GetFeatureAsync<TSnapshot>(
        string featureName,
        AccountSnapshotRefreshMode refreshMode,
        ExpiringSnapshotCache<TSnapshot> cache,
        Func<string, string, CancellationToken, Task<AccountSnapshotLoadResult<TSnapshot>>> loadAsync,
        CancellationToken cancellationToken)
        where TSnapshot : class
    {
        if (!Enum.IsDefined(refreshMode))
        {
            throw new ArgumentOutOfRangeException(nameof(refreshMode), refreshMode, "Unknown refresh mode.");
        }

        AccountAccessStatus accessStatus;
        try
        {
            accessStatus = await _accountAccessService.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return CreateFailure<TSnapshot>(AccountSnapshotLoadStatus.AccessUnavailable);
        }

        if (accessStatus.ValidationStatus != AccountAccessValidationStatus.Valid)
        {
            return CreateFailure<TSnapshot>(MapAccessStatus(accessStatus.ValidationStatus));
        }

        var feature = accessStatus.Features.SingleOrDefault(candidate =>
            string.Equals(candidate.Feature, featureName, StringComparison.Ordinal));
        if (feature is null)
        {
            return CreateFailure<TSnapshot>(AccountSnapshotLoadStatus.AccessUnavailable);
        }

        if (!feature.IsAvailable)
        {
            return CreateFailure<TSnapshot>(
                AccountSnapshotLoadStatus.MissingPermission,
                feature.MissingPermissions);
        }

        // KeyId is the validator's safe, non-secret identifier. Refuse to
        // fetch data that could not be scoped to an account cache profile.
        if (string.IsNullOrWhiteSpace(accessStatus.KeyId))
        {
            return CreateFailure<TSnapshot>(AccountSnapshotLoadStatus.AccessUnavailable);
        }

        string? credential;
        try
        {
            credential = await _credentialReader.ReadGw2ApiCredentialAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return CreateFailure<TSnapshot>(AccountSnapshotLoadStatus.AccessUnavailable);
        }

        if (credential is null)
        {
            return CreateFailure<TSnapshot>(AccountSnapshotLoadStatus.NotConfigured);
        }

        return await cache.GetOrLoadAsync(
            accessStatus.KeyId,
            refreshMode,
            cancellationToken,
            operationCancellationToken => loadAsync(
                accessStatus.KeyId,
                credential,
                operationCancellationToken)).ConfigureAwait(false);
    }

    private async Task<AccountSnapshotLoadResult<AccountOwnedItemsSnapshot>> LoadOwnedItemsAsync(
        string profileId,
        string credential,
        CancellationToken cancellationToken)
    {
        var bankTask = SendAsync<List<AccountBankSlotDto?>, IReadOnlyList<AccountBankItem>>(
            "account/bank",
            profileId,
            credential,
            MapBankItems,
            cancellationToken);
        var materialsTask = SendAsync<List<AccountMaterialDto>, IReadOnlyList<AccountMaterial>>(
            "account/materials",
            profileId,
            credential,
            MapMaterials,
            cancellationToken);

        await Task.WhenAll(bankTask, materialsTask).ConfigureAwait(false);
        var bank = await bankTask.ConfigureAwait(false);
        var materials = await materialsTask.ConfigureAwait(false);
        var failures = new List<AccountSnapshotComponentFailure>();
        AddFailure(failures, AccountSnapshotComponent.Bank, bank.ErrorCategory);
        AddFailure(failures, AccountSnapshotComponent.Materials, materials.ErrorCategory);

        if (!bank.IsSuccess && !materials.IsSuccess)
        {
            return CreateEndpointFailure<AccountOwnedItemsSnapshot>(failures);
        }

        var snapshot = new AccountOwnedItemsSnapshot(
            profileId,
            bank.Value ?? [],
            materials.Value ?? []);
        return failures.Count == 0
            ? CreateSuccess(snapshot)
            : CreatePartial(snapshot, failures);
    }

    private async Task<AccountSnapshotLoadResult<AccountCraftingSnapshot>> LoadCraftingAsync(
        string profileId,
        string credential,
        CancellationToken cancellationToken)
    {
        var recipesTask = SendAsync<List<int?>, IReadOnlyList<int>>(
            "account/recipes",
            profileId,
            credential,
            MapRecipeIds,
            cancellationToken);
        var charactersTask = SendAsync<List<string>, IReadOnlyList<string>>(
            "characters",
            profileId,
            credential,
            MapCharacterNames,
            cancellationToken);

        await Task.WhenAll(recipesTask, charactersTask).ConfigureAwait(false);
        var recipes = await recipesTask.ConfigureAwait(false);
        var characterNames = await charactersTask.ConfigureAwait(false);
        var failures = new List<AccountSnapshotComponentFailure>();
        AddFailure(failures, AccountSnapshotComponent.UnlockedRecipes, recipes.ErrorCategory);
        AddFailure(failures, AccountSnapshotComponent.CharacterList, characterNames.ErrorCategory);
        var characters = new List<AccountCharacterCrafting>();

        if (characterNames.IsSuccess)
        {
            var knownCharacterNames = characterNames.Value!;
            var characterTasks = knownCharacterNames
                .Select(characterName => SendAsync<
                    List<CharacterCraftingDisciplineDto>,
                    IReadOnlyList<AccountCraftingDiscipline>>(
                    $"characters/{Uri.EscapeDataString(characterName)}/crafting",
                    profileId,
                    credential,
                    MapCraftingDisciplines,
                    cancellationToken))
                .ToArray();
            var characterResults = await Task.WhenAll(characterTasks).ConfigureAwait(false);

            for (var index = 0; index < characterResults.Length; index++)
            {
                var result = characterResults[index];
                if (result.IsSuccess)
                {
                    characters.Add(new AccountCharacterCrafting(
                        knownCharacterNames[index],
                        result.Value!));
                }
                else
                {
                    AddFailure(
                        failures,
                        AccountSnapshotComponent.CharacterCrafting,
                        result.ErrorCategory,
                        knownCharacterNames[index]);
                }
            }
        }

        if (!recipes.IsSuccess && !characterNames.IsSuccess)
        {
            return CreateEndpointFailure<AccountCraftingSnapshot>(failures);
        }

        var snapshot = new AccountCraftingSnapshot(
            profileId,
            recipes.Value ?? [],
            characters);
        return failures.Count == 0
            ? CreateSuccess(snapshot)
            : CreatePartial(snapshot, failures);
    }

    private async Task<AccountEndpointResult<TValue>> SendAsync<TDto, TValue>(
        string resourcePath,
        string profileId,
        string credential,
        Func<TDto, TValue> map,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _requestScheduler.ScheduleAsync(
                // Scheduler deduplication must include a non-secret account
                // profile key; otherwise simultaneous account requests could
                // share authenticated data across profiles.
                new Gw2RequestKey($"account-snapshot:{profileId}:{resourcePath}"),
                operationCancellationToken => SendAttemptAsync(
                    resourcePath,
                    credential,
                    map,
                    operationCancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Gw2RequestSchedulerCapacityExceededException)
        {
            return AccountEndpointResult<TValue>.Failure(Gw2ApiErrorCategory.UpstreamUnavailable);
        }
    }

    private async Task<Gw2ScheduledResult<AccountEndpointResult<TValue>>> SendAttemptAsync<TDto, TValue>(
        string resourcePath,
        string credential,
        Func<TDto, TValue> map,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri($"{resourcePath}?v={Gw2ApiClient.SchemaVersion}", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);

        try
        {
            using var response = await _createHttpClient().SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return new Gw2ScheduledResult<AccountEndpointResult<TValue>>(
                    AccountEndpointResult<TValue>.Failure(MapErrorCategory(response.StatusCode)),
                    GetRetryKind(response.StatusCode),
                    GetRetryAfter(response));
            }

            await using var responseStream = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var payload = await JsonSerializer.DeserializeAsync<TDto>(
                responseStream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);

            if (payload is null)
            {
                return Complete(AccountEndpointResult<TValue>.Failure(Gw2ApiErrorCategory.InvalidPayload));
            }

            return Complete(AccountEndpointResult<TValue>.Success(map(payload)));
        }
        catch (JsonException)
        {
            return Complete(AccountEndpointResult<TValue>.Failure(Gw2ApiErrorCategory.InvalidPayload));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return Complete(AccountEndpointResult<TValue>.Failure(Gw2ApiErrorCategory.TransportFailure));
        }
        catch (IOException)
        {
            return Complete(AccountEndpointResult<TValue>.Failure(Gw2ApiErrorCategory.TransportFailure));
        }
        catch (TaskCanceledException)
        {
            return Complete(AccountEndpointResult<TValue>.Failure(Gw2ApiErrorCategory.TransportFailure));
        }
    }

    private static IReadOnlyList<AccountBankItem> MapBankItems(List<AccountBankSlotDto?> payload)
    {
        var items = new List<AccountBankItem>();
        foreach (var slot in payload)
        {
            if (slot is null)
            {
                continue;
            }

            if (slot.Id is not > 0 || slot.Count is not > 0)
            {
                throw new JsonException("The account bank payload contains an invalid item slot.");
            }

            items.Add(new AccountBankItem(
                slot.Id.Value,
                slot.Count.Value,
                string.IsNullOrWhiteSpace(slot.Binding) ? null : slot.Binding));
        }

        return items;
    }

    private static IReadOnlyList<AccountMaterial> MapMaterials(List<AccountMaterialDto> payload)
    {
        var materials = new List<AccountMaterial>(payload.Count);
        foreach (var material in payload)
        {
            if (material is null || material.Id is not > 0 || material.Category is null or < 0 ||
                material.Count is null or < 0)
            {
                throw new JsonException("The account materials payload contains an invalid material entry.");
            }

            materials.Add(new AccountMaterial(
                material.Id.Value,
                material.Category.Value,
                material.Count.Value));
        }

        return materials;
    }

    private static IReadOnlyList<int> MapRecipeIds(List<int?> payload)
    {
        if (payload.Any(recipeId => recipeId is not > 0))
        {
            throw new JsonException("The account recipes payload contains an invalid recipe ID.");
        }

        return payload.Select(recipeId => recipeId!.Value).Distinct().Order().ToArray();
    }

    private static IReadOnlyList<string> MapCharacterNames(List<string> payload)
    {
        if (payload.Any(string.IsNullOrWhiteSpace))
        {
            throw new JsonException("The character list contains an invalid character name.");
        }

        return payload.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<AccountCraftingDiscipline> MapCraftingDisciplines(
        List<CharacterCraftingDisciplineDto> payload)
    {
        var disciplines = new List<AccountCraftingDiscipline>(payload.Count);
        foreach (var discipline in payload)
        {
            if (discipline is null || string.IsNullOrWhiteSpace(discipline.Discipline) ||
                discipline.Rating is null or < 0 || discipline.Active is null)
            {
                throw new JsonException("The character crafting payload contains an invalid discipline.");
            }

            disciplines.Add(new AccountCraftingDiscipline(
                discipline.Discipline,
                discipline.Rating.Value,
                discipline.Active.Value));
        }

        return disciplines;
    }

    private static void AddFailure(
        ICollection<AccountSnapshotComponentFailure> failures,
        AccountSnapshotComponent component,
        Gw2ApiErrorCategory? errorCategory,
        string? characterName = null)
    {
        if (errorCategory is { } category)
        {
            failures.Add(new AccountSnapshotComponentFailure(component, category, characterName));
        }
    }

    private static AccountSnapshotLoadResult<TSnapshot> CreateSuccess<TSnapshot>(TSnapshot snapshot)
        where TSnapshot : class =>
        new(AccountSnapshotLoadStatus.Available, snapshot, null, [], []);

    private static AccountSnapshotLoadResult<TSnapshot> CreatePartial<TSnapshot>(
        TSnapshot snapshot,
        IReadOnlyList<AccountSnapshotComponentFailure> failures)
        where TSnapshot : class =>
        new(AccountSnapshotLoadStatus.PartialData, snapshot, null, [], failures);

    private static AccountSnapshotLoadResult<TSnapshot> CreateFailure<TSnapshot>(
        AccountSnapshotLoadStatus status,
        IReadOnlyList<string>? missingPermissions = null)
        where TSnapshot : class =>
        new(status, null, null, missingPermissions ?? [], []);

    private static AccountSnapshotLoadResult<TSnapshot> CreateEndpointFailure<TSnapshot>(
        IReadOnlyList<AccountSnapshotComponentFailure> failures)
        where TSnapshot : class =>
        new(GetFailureStatus(failures), null, null, [], failures);

    private static AccountSnapshotLoadStatus MapAccessStatus(AccountAccessValidationStatus status) => status switch
    {
        AccountAccessValidationStatus.NotConfigured => AccountSnapshotLoadStatus.NotConfigured,
        AccountAccessValidationStatus.Invalid => AccountSnapshotLoadStatus.InvalidCredential,
        AccountAccessValidationStatus.Unavailable => AccountSnapshotLoadStatus.AccessUnavailable,
        AccountAccessValidationStatus.Valid => AccountSnapshotLoadStatus.AccessUnavailable,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown account access status."),
    };

    private static AccountSnapshotLoadStatus GetFailureStatus(
        IReadOnlyList<AccountSnapshotComponentFailure> failures)
    {
        if (failures.All(failure => failure.ErrorCategory == Gw2ApiErrorCategory.Unauthorized))
        {
            return AccountSnapshotLoadStatus.AuthenticationFailed;
        }

        if (failures.All(failure => failure.ErrorCategory == Gw2ApiErrorCategory.Forbidden))
        {
            return AccountSnapshotLoadStatus.PermissionDenied;
        }

        if (failures.All(failure => failure.ErrorCategory == Gw2ApiErrorCategory.InvalidPayload))
        {
            return AccountSnapshotLoadStatus.InvalidRemoteData;
        }

        return AccountSnapshotLoadStatus.SourceUnavailable;
    }

    private static Gw2ScheduledResult<AccountEndpointResult<TValue>> Complete<TValue>(
        AccountEndpointResult<TValue> result) =>
        new(result, Gw2RetryKind.None, RetryAfter: null);

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

    private sealed record AccountEndpointResult<TValue>(TValue? Value, Gw2ApiErrorCategory? ErrorCategory)
    {
        public bool IsSuccess => ErrorCategory is null;

        public static AccountEndpointResult<TValue> Success(TValue value) => new(value, null);

        public static AccountEndpointResult<TValue> Failure(Gw2ApiErrorCategory errorCategory) =>
            new(default, errorCategory);
    }

    private sealed class ExpiringSnapshotCache<TSnapshot>
        where TSnapshot : class
    {
        private readonly string _featureName;
        private readonly TimeSpan _timeToLive;
        private readonly IClock _clock;
        private readonly ConcurrentDictionary<AccountSnapshotCacheKey, CacheEntry> _entries = new();
        private readonly ConcurrentDictionary<
            AccountSnapshotCacheKey,
            Lazy<Task<AccountSnapshotLoadResult<TSnapshot>>>> _inFlight = new();

        public ExpiringSnapshotCache(string featureName, TimeSpan timeToLive, IClock clock)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(featureName);
            ArgumentNullException.ThrowIfNull(clock);
            _featureName = featureName;
            _timeToLive = timeToLive;
            _clock = clock;
        }

        public async Task<AccountSnapshotLoadResult<TSnapshot>> GetOrLoadAsync(
            string profileId,
            AccountSnapshotRefreshMode refreshMode,
            CancellationToken cancellationToken,
            Func<CancellationToken, Task<AccountSnapshotLoadResult<TSnapshot>>> loadAsync)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
            ArgumentNullException.ThrowIfNull(loadAsync);
            cancellationToken.ThrowIfCancellationRequested();

            var key = new AccountSnapshotCacheKey(profileId, _featureName);
            if (refreshMode == AccountSnapshotRefreshMode.UseCache &&
                _entries.TryGetValue(key, out var cached) &&
                _clock.UtcNow < cached.Result.Freshness!.ExpiresAtUtc)
            {
                return cached.Result;
            }

            var candidate = new Lazy<Task<AccountSnapshotLoadResult<TSnapshot>>>(
                () => FillAsync(key, loadAsync),
                LazyThreadSafetyMode.ExecutionAndPublication);
            var inFlight = _inFlight.GetOrAdd(key, candidate);

            return await inFlight.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task<AccountSnapshotLoadResult<TSnapshot>> FillAsync(
            AccountSnapshotCacheKey key,
            Func<CancellationToken, Task<AccountSnapshotLoadResult<TSnapshot>>> loadAsync)
        {
            try
            {
                var result = await loadAsync(CancellationToken.None).ConfigureAwait(false);
                if (result.Status != AccountSnapshotLoadStatus.Available || result.Snapshot is null)
                {
                    return result;
                }

                var capturedAtUtc = _clock.UtcNow;
                var cachedResult = result with
                {
                    Freshness = new DataFreshness(capturedAtUtc, capturedAtUtc + _timeToLive),
                };
                _entries[key] = new CacheEntry(cachedResult);
                return cachedResult;
            }
            finally
            {
                _inFlight.TryRemove(key, out _);
            }
        }

        private sealed record CacheEntry(AccountSnapshotLoadResult<TSnapshot> Result);
    }

    private sealed record AccountSnapshotCacheKey(string ProfileId, string FeatureName);
}

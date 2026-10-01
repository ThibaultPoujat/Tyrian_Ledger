using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gw2Tp.Application.AccountEvidence;
using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Application.Time;
using Gw2Tp.Infrastructure.Gw2Api;
using Gw2Tp.Infrastructure.PersonalTradingPost;
using Gw2Tp.Infrastructure.Secrets;

namespace Gw2Tp.Infrastructure.AccountEvidence;

/// <summary>Disconnected typed producer. One captured credential and account identity per invocation.</summary>
internal sealed class AccountHoldingsGateway : IAccountHoldingsCollector
{
    internal const string HttpClientName = "TyrianLedger.AccountHoldings";
    internal const string SchemaVersion = PersonalTradingPostGateway.SchemaVersion;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IGw2ApiKeySource keySource;
    private readonly HttpClient client;
    private readonly IGw2RequestScheduler scheduler;
    private readonly IClock clock;
    private readonly TimeSpan requestTimeout;

    public AccountHoldingsGateway(IGw2ApiKeySource keySource, HttpClient client,
        IGw2RequestScheduler scheduler, IClock clock, TimeSpan? requestTimeout = null)
    {
        this.keySource = keySource ?? throw new ArgumentNullException(nameof(keySource));
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(10);
        if (this.requestTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(requestTimeout));
    }

    public async Task<Gw2ApiResult<AccountHoldingsCapture>> CollectAsync(
        DateTimeOffset evaluatedAtUtc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Gw2ApiKeyReadResult credential;
        try { credential = await keySource.ReadAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return Gw2ApiResult<AccountHoldingsCapture>.Failure(Gw2ApiErrorCategory.CredentialUnavailable); }
        if (credential.State != Gw2ApiKeyReadState.Available || string.IsNullOrWhiteSpace(credential.Value))
            return Gw2ApiResult<AccountHoldingsCapture>.Failure(credential.State == Gw2ApiKeyReadState.NotConfigured
                ? Gw2ApiErrorCategory.CredentialNotConfigured : Gw2ApiErrorCategory.CredentialUnavailable);

        var refreshId = Guid.NewGuid();
        // Session never exposes or hashes the credential. Unique capture keys prevent mixed captures/clock reuse.
        var session = new ReadSession(credential.Value, refreshId, client, scheduler, clock, requestTimeout);
        var identity = await session.ReadAsync(AccountHoldingsSource.AccountIdentity, null, "account", "identity",
            async (response, token) =>
            {
                var dto = await DeserializeAsync<AccountScopeDto>(response, token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(dto.Id)) throw new JsonException();
                return new Mapped<AccountScope>(new(dto.Id), 1);
            }, cancellationToken).ConfigureAwait(false);
        if (identity.Value is not { } account)
            return Gw2ApiResult<AccountHoldingsCapture>.Failure(identity.ErrorCategory ?? Gw2ApiErrorCategory.IncompleteData);

        var rosterTask = session.ReadAsync(AccountHoldingsSource.CharacterRoster, null, "characters", "roster",
            (response, token) => MapRosterAsync(account, response, token), cancellationToken);
        var bankTask = session.ReadAsync(AccountHoldingsSource.Bank, null, "account/bank", "bank",
            (response, token) => MapSlotsAsync(account, AccountHoldingsSource.Bank, response, token), cancellationToken);
        var sharedTask = session.ReadAsync(AccountHoldingsSource.SharedInventory, null, "account/inventory", "shared",
            (response, token) => MapSlotsAsync(account, AccountHoldingsSource.SharedInventory, response, token), cancellationToken);
        var materialsTask = session.ReadAsync(AccountHoldingsSource.MaterialStorage, null, "account/materials", "materials",
            MapMaterialsAsync, cancellationToken);
        var deliveryTask = session.ReadAsync(AccountHoldingsSource.TradingPostDelivery, null, "commerce/delivery", "delivery",
            MapDeliveryAsync, cancellationToken);
        var roster = await rosterTask.ConfigureAwait(false);
        var actors = roster.Value;
        var characterTask = ReadCharactersAsync(session, account, actors, cancellationToken);
        await Task.WhenAll(bankTask, sharedTask, materialsTask, deliveryTask, characterTask).ConfigureAwait(false);
        var characters = await characterTask.ConfigureAwait(false);
        var successful = characters.Count(value => value.Inventory.Completeness == EvidenceCompleteness.Complete);
        var capture = new AccountHoldingsCapture(account, refreshId, evaluatedAtUtc.ToUniversalTime(),
            identity.Fetch, roster, await bankTask.ConfigureAwait(false), await sharedTask.ConfigureAwait(false),
            await materialsTask.ConfigureAwait(false), await deliveryTask.ConfigureAwait(false), characters,
            new(actors is null ? EvidenceCompleteness.Unknown
                : successful == actors.Count ? EvidenceCompleteness.Complete : EvidenceCompleteness.Partial,
                actors?.Count, successful));
        return Gw2ApiResult<AccountHoldingsCapture>.Success(capture, isPartialData:
            capture.Roster.Completeness != EvidenceCompleteness.Complete ||
            capture.Bank.Completeness != EvidenceCompleteness.Complete ||
            capture.SharedInventory.Completeness != EvidenceCompleteness.Complete ||
            capture.MaterialStorage.Completeness != EvidenceCompleteness.Complete ||
            capture.Delivery.Completeness != EvidenceCompleteness.Complete ||
            capture.CharacterCoverage.Completeness != EvidenceCompleteness.Complete);
    }

    private static async Task<IReadOnlyList<ActorHoldingsEvidence>> ReadCharactersAsync(
        ReadSession session, AccountScope account, IReadOnlyList<AccountActor>? actors, CancellationToken cancellationToken)
    {
        if (actors is null) return [];
        var results = new ActorHoldingsEvidence[actors.Count];
        var next = 0;
        // Four sequential workers, indexed deterministic queue; never materialize one task per character.
        async Task WorkerAsync()
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var index = Interlocked.Increment(ref next) - 1;
                if (index >= actors.Count) return;
                var actor = actors[index];
                var inventory = await session.ReadAsync(AccountHoldingsSource.CharacterInventory, actor.ActorId,
                    $"characters/{Uri.EscapeDataString(actor.DisplayName)}/inventory",
                    $"character-{index.ToString(CultureInfo.InvariantCulture)}",
                    (response, token) => MapCharacterInventoryAsync(account, actor, response, token),
                    cancellationToken).ConfigureAwait(false);
                results[index] = new(actor, inventory);
            }
        }
        var workers = Enumerable.Range(0, Math.Min(4, actors.Count)).Select(_ => WorkerAsync()).ToArray();
        await Task.WhenAll(workers).ConfigureAwait(false);
        return results;
    }

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken token)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, token).ConfigureAwait(false)
            ?? throw new JsonException();
    }

    private static AccountActor Actor(AccountScope account, string name)
    {
        // Private account/name input, NOT a credential fingerprint. Length prefix prevents concatenation ambiguity.
        var input = $"{account.AccountId.Length.ToString(CultureInfo.InvariantCulture)}:{account.AccountId}{name}";
        return new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))), name);
    }

    private static async Task<Mapped<IReadOnlyList<AccountActor>>> MapRosterAsync(
        AccountScope account, HttpResponseMessage response, CancellationToken token)
    {
        var names = await DeserializeAsync<string[]>(response, token).ConfigureAwait(false);
        if (names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.Ordinal).Count() != names.Length)
            throw new JsonException();
        return new(names.OrderBy(value => value, StringComparer.Ordinal).Select(name => Actor(account, name)).ToArray(), names.Length);
    }

    private static async Task<Mapped<HoldingsInventoryObservation>> MapSlotsAsync(
        AccountScope account, AccountHoldingsSource source, HttpResponseMessage response, CancellationToken token)
    {
        var slots = await DeserializeAsync<HoldingsSlotDto?[]>(response, token).ConfigureAwait(false);
        var items = new List<HoldingsItemObservation>();
        for (var index = 0; index < slots.Length; index++)
        {
            if (slots[index] is not { } slot) continue;
            items.Add(MapSlot(account, slot, new(source, null, null, index, [], [index]),
                allowCharacterBinding: source == AccountHoldingsSource.Bank));
        }
        ValidateTotals(items.Select(item => (item.ItemId, item.Quantity)));
        return new(new(items.ToArray(), []), slots.Length);
    }

    private static async Task<Mapped<HoldingsInventoryObservation>> MapCharacterInventoryAsync(
        AccountScope account, AccountActor actor, HttpResponseMessage response, CancellationToken token)
    {
        var dto = await DeserializeAsync<HoldingsCharacterInventoryDto>(response, token).ConfigureAwait(false);
        if (dto.Bags is null) throw new JsonException();
        var bags = new List<InstalledBagObservation>();
        var items = new List<HoldingsItemObservation>();
        var locations = 0;
        for (var bagIndex = 0; bagIndex < dto.Bags.Length; bagIndex++)
        {
            if (dto.Bags[bagIndex] is not { } bag) continue;
            if (bag.ItemId is not > 0 || bag.Size is not >= 0 || bag.Inventory is null || bag.Inventory.Length != bag.Size)
                throw new JsonException();
            locations = checked(locations + bag.Inventory.Length);
            bags.Add(new(bagIndex, bag.ItemId.Value, bag.Size.Value));
            for (var slotIndex = 0; slotIndex < bag.Inventory.Length; slotIndex++)
            {
                if (bag.Inventory[slotIndex] is not { } slot) continue;
                items.Add(MapSlot(account, slot, new(AccountHoldingsSource.CharacterInventory, actor.ActorId,
                    bagIndex, slotIndex, [], [slotIndex]), allowCharacterBinding: true));
            }
        }
        ValidateTotals(items.Select(item => (item.ItemId, item.Quantity)));
        return new(new(items.ToArray(), bags.ToArray()), locations);
    }

    private static HoldingsItemObservation MapSlot(AccountScope account, HoldingsSlotDto slot,
        HoldingsLocation location, bool allowCharacterBinding)
    {
        if (slot.ItemId is not > 0 || slot.Count is not > 0) throw new JsonException();
        var binding = MapBinding(slot.Binding, allowCharacterBinding);
        if (binding == AccountItemBinding.CharacterBound ? string.IsNullOrWhiteSpace(slot.BoundTo) : slot.BoundTo is not null)
            throw new JsonException();
        var components = new List<HoldingsAttachedComponent>();
        void Attach(int[]? ids, HoldingsComponentKind kind)
        {
            if (ids is null) return;
            if (ids.Any(id => id <= 0)) throw new JsonException();
            components.AddRange(ids.Select((id, index) => new HoldingsAttachedComponent(kind, index, id)));
        }
        Attach(slot.Upgrades, HoldingsComponentKind.Upgrade);
        Attach(slot.Infusions, HoldingsComponentKind.Infusion);
        return new(location, slot.ItemId.Value, slot.Count.Value, binding,
            slot.BoundTo is null ? null : Actor(account, slot.BoundTo), components.ToArray());
    }

    private static AccountItemBinding MapBinding(string? binding, bool allowCharacterBinding = false) => binding switch
    {
        null => AccountItemBinding.Unspecified,
        "Account" => AccountItemBinding.AccountBound,
        "Character" when allowCharacterBinding => AccountItemBinding.CharacterBound,
        _ => throw new JsonException(),
    };

    private static async Task<Mapped<HoldingsInventoryObservation>> MapMaterialsAsync(HttpResponseMessage response, CancellationToken token)
    {
        var rows = await DeserializeAsync<HoldingsMaterialDto?[]>(response, token).ConfigureAwait(false);
        if (rows.Any(row => row is null || row.ItemId is not > 0 || row.Count is not >= 0 || row.CategoryId is not > 0))
            throw new JsonException();
        var items = rows.Select((row, index) => (Row: row!, Index: index)).GroupBy(value => value.Row.ItemId!.Value)
            .OrderBy(group => group.Key).Select(group =>
            {
                var quantities = group.Select(value => value.Row.Count!.Value).Distinct().ToArray();
                var bindings = group.Select(value => MapBinding(value.Row.Binding)).Distinct().ToArray();
                if (quantities.Length != 1 || bindings.Length != 1) throw new JsonException();
                var location = new HoldingsLocation(AccountHoldingsSource.MaterialStorage, null, null, null,
                    group.Select(value => value.Row.CategoryId!.Value).Distinct().Order().ToArray(),
                    group.Select(value => value.Index).ToArray());
                return new HoldingsItemObservation(location, group.Key, quantities[0], bindings[0], null, []);
            }).ToArray();
        return new(new(items, []), rows.Length);
    }

    private static async Task<Mapped<HoldingsDeliveryObservation>> MapDeliveryAsync(HttpResponseMessage response, CancellationToken token)
    {
        var dto = await DeserializeAsync<HoldingsDeliveryDto>(response, token).ConfigureAwait(false);
        if (dto.Coins is not >= 0 || dto.Items is null || dto.Items.Any(row => row is null || row.ItemId is not > 0 || row.Count is not > 0))
            throw new JsonException();
        var items = dto.Items.Select((row, index) => new DeliveryItemObservation(index, row!.ItemId!.Value, row.Count!.Value)).ToArray();
        var totals = ValidateTotals(items.Select(item => (item.ItemId, item.Quantity)))
            .Select(pair => new ObservedDeliveryItemTotal(pair.Key, pair.Value)).ToArray();
        return new(new(dto.Coins.Value, items, totals), items.Length);
    }

    private static IReadOnlyDictionary<int, int> ValidateTotals(IEnumerable<(int ItemId, int Quantity)> rows)
    {
        var totals = new SortedDictionary<int, int>();
        foreach (var row in rows) totals[row.ItemId] = checked(totals.GetValueOrDefault(row.ItemId) + row.Quantity);
        return totals;
    }

    private sealed record Mapped<T>(T Value, int LocationCount);

    private sealed class ReadSession(string credential, Guid refreshId, HttpClient client,
        IGw2RequestScheduler scheduler, IClock clock, TimeSpan requestTimeout)
    {
        public async Task<AccountSourceEvidence<T>> ReadAsync<T>(AccountHoldingsSource source, string? actorId,
            string path, string operation, Func<HttpResponseMessage, CancellationToken, Task<Mapped<T>>> map,
            CancellationToken token)
        {
            var started = clock.UtcNow;
            Gw2ApiResult<Mapped<T>> result;
            try
            {
                result = await scheduler.ScheduleAsync(new Gw2RequestKey($"holdings/{refreshId:N}/{operation}", "account-holdings"),
                    cancellation => SendAsync(path, map, cancellation), token).ConfigureAwait(false);
            }
            catch (Gw2RequestSchedulerCapacityExceededException)
            {
                result = Gw2ApiResult<Mapped<T>>.Failure(Gw2ApiErrorCategory.UpstreamUnavailable);
            }
            var fetch = new EvidenceFetchProvenance(started, clock.UtcNow, null);
            if (result.IsSuccess && result.Value is { } mapped)
                return new(source, actorId, EvidenceAvailability.Available, EvidenceCompleteness.Complete,
                    new(mapped.LocationCount, mapped.LocationCount), fetch, mapped.Value, null);
            var error = result.ErrorCategory ?? Gw2ApiErrorCategory.IncompleteData;
            return new(source, actorId,
                error is Gw2ApiErrorCategory.Forbidden or Gw2ApiErrorCategory.Unauthorized
                    ? EvidenceAvailability.MissingPermission : EvidenceAvailability.Unavailable,
                error is Gw2ApiErrorCategory.InvalidPayload or Gw2ApiErrorCategory.IncompleteData
                    ? EvidenceCompleteness.Partial : EvidenceCompleteness.Unknown,
                new(null, null), fetch, default, error);
        }

        private async Task<Gw2ScheduledResult<Gw2ApiResult<Mapped<T>>>> SendAsync<T>(string path,
            Func<HttpResponseMessage, CancellationToken, Task<Mapped<T>>> map, CancellationToken token)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(requestTimeout);
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{path}?v={SchemaVersion}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.PartialContent)
                    return new(Gw2ApiResult<Mapped<T>>.Failure(Gw2ApiErrorCategory.IncompleteData));
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    var error = (int)response.StatusCode >= 500 ? Gw2ApiErrorCategory.UpstreamUnavailable : response.StatusCode switch
                    {
                        HttpStatusCode.BadRequest => Gw2ApiErrorCategory.InvalidRequest,
                        HttpStatusCode.Unauthorized => Gw2ApiErrorCategory.Unauthorized,
                        HttpStatusCode.Forbidden => Gw2ApiErrorCategory.Forbidden,
                        HttpStatusCode.NotFound => Gw2ApiErrorCategory.NotFound,
                        HttpStatusCode.TooManyRequests => Gw2ApiErrorCategory.RateLimited,
                        _ => Gw2ApiErrorCategory.UnexpectedResponse,
                    };
                    var retry = response.StatusCode switch
                    {
                        HttpStatusCode.TooManyRequests => Gw2RetryKind.RateLimited,
                        HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout => Gw2RetryKind.UpstreamUnavailable,
                        _ => Gw2RetryKind.None,
                    };
                    return new(Gw2ApiResult<Mapped<T>>.Failure(error), retry,
                        response.Headers.RetryAfter?.Delta is { } delay && delay > TimeSpan.Zero ? delay : null);
                }
                return new(Gw2ApiResult<Mapped<T>>.Success(await map(response, timeout.Token).ConfigureAwait(false)));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (OperationCanceledException) { return new(Gw2ApiResult<Mapped<T>>.Failure(Gw2ApiErrorCategory.TransportFailure)); }
            catch (HttpRequestException) { return new(Gw2ApiResult<Mapped<T>>.Failure(Gw2ApiErrorCategory.TransportFailure)); }
            catch (IOException) { return new(Gw2ApiResult<Mapped<T>>.Failure(Gw2ApiErrorCategory.TransportFailure)); }
            catch (JsonException) { return new(Gw2ApiResult<Mapped<T>>.Failure(Gw2ApiErrorCategory.InvalidPayload)); }
            catch (OverflowException) { return new(Gw2ApiResult<Mapped<T>>.Failure(Gw2ApiErrorCategory.InvalidPayload)); }
        }
    }
}

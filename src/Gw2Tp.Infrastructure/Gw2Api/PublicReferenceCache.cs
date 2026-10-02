using System.Diagnostics.Metrics;
using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Time;
using Microsoft.Extensions.Options;

namespace Gw2Tp.Infrastructure.Gw2Api;

/// <summary>Local process reuse policy; it makes no claim about upstream cache coherence.</summary>
internal sealed class PublicReferenceCacheOptions
{
    public const string ConfigurationSectionName = "Gw2Api:PublicReferences";
    public int TimeToLiveSeconds { get; set; } = 3600;
    public int MaximumEntries { get; set; } = 10000;
}

internal sealed class PublicReferenceCacheOptionsValidator : IValidateOptions<PublicReferenceCacheOptions>
{
    public ValidateOptionsResult Validate(string? name, PublicReferenceCacheOptions options) =>
        options.TimeToLiveSeconds > 0 && options.MaximumEntries > 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Gw2Api:PublicReferences TTL and capacity must be positive.");
}

internal sealed record PublicReferenceCacheDiagnostics(long Hits, long Misses, long Evictions, int Entries);

/// <summary>
/// One bounded public-only cache. No credentials or account/market observations enter this type.
/// Successful complete gateway fills are retained per ID; the existing scheduler owns in-flight work.
/// Deterministic FIFO eviction never extends an entry's age. Returned nested recipe lists are detached.
/// </summary>
internal sealed class PublicReferenceCache
{
    private static readonly Meter Meter = new("TyrianLedger.PublicReferences");
    private static readonly Counter<long> HitCounter = Meter.CreateCounter<long>("gw2.references.hits");
    private static readonly Counter<long> MissCounter = Meter.CreateCounter<long>("gw2.references.misses");
    private static readonly Counter<long> EvictionCounter = Meter.CreateCounter<long>("gw2.references.evictions");
    private readonly object gate = new();
    private readonly Dictionary<Key, Entry> entries = [];
    private readonly IClock clock;
    private readonly TimeSpan ttl;
    private readonly int capacity;
    private DateTimeOffset? lastObserved;
    private long epoch;
    private long sequence;
    private long hits;
    private long misses;
    private long evictions;

    public PublicReferenceCache(IOptions<PublicReferenceCacheOptions> options, IClock clock)
        : this(options.Value, clock) { }

    internal PublicReferenceCache(PublicReferenceCacheOptions options, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (new PublicReferenceCacheOptionsValidator().Validate(null, options).Failed)
            throw new ArgumentOutOfRangeException(nameof(options), "Reference TTL and capacity must be positive.");
        ttl = TimeSpan.FromSeconds(options.TimeToLiveSeconds);
        capacity = options.MaximumEntries;
    }

    public PublicReferenceCacheDiagnostics GetDiagnostics()
    {
        lock (gate)
        {
            ObserveClock();
            return new(hits, misses, evictions, entries.Count);
        }
    }

    public Task<Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>> GetItemsAsync(
        string schema, string language, IReadOnlyCollection<int> ids,
        Func<IReadOnlyCollection<int>, CancellationToken, Task<Gw2ApiResult<IReadOnlyList<MarketItemMetadata>>>> fetch,
        CancellationToken token) =>
        GetAsync("items", schema, language, ids, fetch, item => item.ItemId, item => item with { }, token);

    public Task<Gw2ApiResult<IReadOnlyList<CraftingRecipe>>> GetRecipesAsync(
        string schema, IReadOnlyCollection<int> ids,
        Func<IReadOnlyCollection<int>, CancellationToken, Task<Gw2ApiResult<IReadOnlyList<CraftingRecipe>>>> fetch,
        CancellationToken token) =>
        GetAsync("recipes", schema, "", ids, fetch, recipe => recipe.RecipeId,
            recipe => recipe with { Disciplines = recipe.Disciplines.ToArray(), Flags = recipe.Flags.ToArray(),
                Ingredients = recipe.Ingredients.Select(ingredient => ingredient with { }).ToArray() }, token);

    private async Task<Gw2ApiResult<IReadOnlyList<T>>> GetAsync<T>(
        string endpoint, string schema, string language, IReadOnlyCollection<int> ids,
        Func<IReadOnlyCollection<int>, CancellationToken, Task<Gw2ApiResult<IReadOnlyList<T>>>> fetch,
        Func<T, int> idOf, Func<T, T> copy, CancellationToken token) where T : class
    {
        ArgumentNullException.ThrowIfNull(ids);
        token.ThrowIfCancellationRequested();
        if (ids.Any(id => id <= 0)) throw new ArgumentOutOfRangeException(nameof(ids));
        var ordered = ids.Distinct().Order().ToArray();
        var values = new Dictionary<int, Entry>();
        DateTimeOffset started;
        long startedEpoch;
        lock (gate)
        {
            started = ObserveClock();
            startedEpoch = epoch;
            foreach (var id in ordered)
            {
                if (entries.TryGetValue(new(endpoint, schema, language, id), out var entry))
                {
                    values.Add(id, entry);
                    Increment(ref hits);
                    HitCounter.Add(1);
                }
                else { Increment(ref misses); MissCounter.Add(1); }
            }
        }
        var missing = ordered.Where(id => !values.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            // Gateway batching, validation, retries and per-waiter cancellation stay below this seam.
            var result = await fetch(missing, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!result.IsSuccess) return Gw2ApiResult<IReadOnlyList<T>>.Failure(result.ErrorCategory!.Value);
            if (result.IsPartialData || result.Value is null || result.Value.Any(value => value is null) ||
                result.Value.Count != missing.Length || !result.Value.Select(idOf).ToHashSet().SetEquals(missing))
                return Gw2ApiResult<IReadOnlyList<T>>.Failure(Gw2ApiErrorCategory.IncompleteData);
            foreach (var value in result.Value) values.Add(idOf(value), new(copy(value), started, 0));
        }
        lock (gate)
        {
            var now = ObserveClock();
            // A slow fill cannot relabel expired hits fresh, and rollback invalidates in-flight reuse too.
            if (epoch != startedEpoch || values.Values.Any(entry => !Fresh(entry, now)))
                return Gw2ApiResult<IReadOnlyList<T>>.Failure(Gw2ApiErrorCategory.IncompleteData);
            foreach (var id in missing)
            {
                var key = new Key(endpoint, schema, language, id);
                if (entries.ContainsKey(key)) continue;
                while (entries.Count >= capacity)
                {
                    entries.Remove(entries.MinBy(pair => pair.Value.Sequence).Key);
                    Increment(ref evictions);
                    EvictionCounter.Add(1);
                }
                entries.Add(key, values[id] with { Sequence = ++sequence });
            }
            token.ThrowIfCancellationRequested();
            return Gw2ApiResult<IReadOnlyList<T>>.Success(ordered.Select(id => copy((T)values[id].Value)).ToArray());
        }
    }

    private DateTimeOffset ObserveClock()
    {
        var now = clock.UtcNow;
        if (lastObserved is { } previous && now < previous)
        {
            entries.Clear();
            epoch++;
        }
        lastObserved = now;
        foreach (var key in entries.Where(pair => !Fresh(pair.Value, now)).Select(pair => pair.Key).ToArray())
            entries.Remove(key);
        return now;
    }

    private bool Fresh(Entry entry, DateTimeOffset now) => now >= entry.CreatedAt && now - entry.CreatedAt < ttl;
    private static void Increment(ref long counter) { if (counter < long.MaxValue) counter++; }
    private sealed record Key(string Endpoint, string Schema, string Language, int Id);
    private sealed record Entry(object Value, DateTimeOffset CreatedAt, long Sequence);
}

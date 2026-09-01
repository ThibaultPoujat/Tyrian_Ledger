using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Recommendations;
using Gw2Tp.Application.Time;

namespace Gw2Tp.Application.MarketSnapshots;

/// <summary>
/// Collects the bounded, public data required for one recommendation snapshot.
/// It does not persist, publish, or calculate player-specific recommendations.
/// </summary>
public sealed class PublicMarketSnapshotCollector
{
    public const int MaximumFinalistCount = 200;

    private readonly IGw2ApiClient marketDataClient;
    private readonly IClock clock;

    public PublicMarketSnapshotCollector(IGw2ApiClient marketDataClient, IClock clock)
    {
        this.marketDataClient = marketDataClient ?? throw new ArgumentNullException(nameof(marketDataClient));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<PublicMarketSnapshotCollection> CollectAsync(
        Action<PublicMarketSnapshotCollectionProgress>? reportProgress = null,
        CancellationToken cancellationToken = default)
    {
        Report(reportProgress, PublicMarketSnapshotCollectionStage.DiscoveringPriceItemIds, finalistCount: null);
        var itemIds = await GetRequiredValueAsync(
            marketDataClient.GetPriceItemIdsAsync(cancellationToken),
            cancellationToken).ConfigureAwait(false);
        ValidatePriceItemIds(itemIds);

        Report(reportProgress, PublicMarketSnapshotCollectionStage.DiscoveringAggregatePrices, finalistCount: null);
        var prices = await GetRequiredValueAsync(
            marketDataClient.GetPricesAsync(itemIds, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        Report(reportProgress, PublicMarketSnapshotCollectionStage.ScreeningCandidates, finalistCount: null);
        var finalists = SelectFinalists(itemIds, prices);
        if (finalists.Length == 0)
        {
            return new PublicMarketSnapshotCollection(clock.UtcNow.ToUniversalTime(), []);
        }

        Report(reportProgress, PublicMarketSnapshotCollectionStage.ReadingFinalistListings, finalists.Length);
        var listings = await GetRequiredValueAsync(
            marketDataClient.GetListingsAsync(finalists, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        ValidateListings(finalists, listings);

        Report(reportProgress, PublicMarketSnapshotCollectionStage.ReadingFinalistMetadata, finalists.Length);
        var metadata = await GetRequiredValueAsync(
            marketDataClient.GetItemMetadataAsync(finalists, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        ValidateMetadata(finalists, metadata);

        var metadataByItemId = metadata.ToDictionary(item => item.ItemId);
        var candidates = listings
            .OrderBy(listing => listing.ItemId)
            .Select(listing => new MarketSnapshotCandidate(
                listing.ItemId,
                metadataByItemId[listing.ItemId].Name,
                ToSnapshotLevels(listing.Buys),
                ToSnapshotLevels(listing.Sells)))
            .ToArray();
        return new PublicMarketSnapshotCollection(clock.UtcNow.ToUniversalTime(), candidates);
    }

    private static IReadOnlyList<MarketSnapshotOrderLevel> ToSnapshotLevels(
        IReadOnlyList<MarketOrderLevel> levels) => levels
        .Select(level => new MarketSnapshotOrderLevel(
            level.Listings,
            level.Quantity,
            level.UnitPriceInCopper))
        .OrderBy(level => level.UnitPriceInCopper)
        .ThenBy(level => level.Quantity)
        .ThenBy(level => level.ListingCount)
        .ToArray();

    private static void Report(
        Action<PublicMarketSnapshotCollectionProgress>? reportProgress,
        PublicMarketSnapshotCollectionStage stage,
        int? finalistCount) => reportProgress?.Invoke(new PublicMarketSnapshotCollectionProgress(stage, finalistCount));

    private static async Task<T> GetRequiredValueAsync<T>(
        Task<Gw2ApiResult<T>> responseTask,
        CancellationToken cancellationToken)
    {
        var response = await responseTask.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!response.IsSuccess || response.IsPartialData || response.Value is null)
        {
            throw new PublicMarketSnapshotCollectionException(
                response.ErrorCategory ?? Gw2ApiErrorCategory.IncompleteData);
        }

        return response.Value;
    }

    private static int[] SelectFinalists(
        IReadOnlyList<int> itemIds,
        IReadOnlyList<MarketPrice> prices)
    {
        ValidatePrices(itemIds, prices);
        return prices
            .Where(HasPotentialAggregateSpread)
            .Select(price => new Finalist(
                price.ItemId,
                Math.Min(price.Buys.Quantity, price.Sells.Quantity),
                (long)price.Sells.UnitPriceInCopper - price.Buys.UnitPriceInCopper))
            .OrderByDescending(finalist => finalist.MinimumAggregateSideQuantity)
            .ThenByDescending(finalist => finalist.AggregatePriceGap)
            .ThenBy(finalist => finalist.ItemId)
            .Take(MaximumFinalistCount)
            .Select(finalist => finalist.ItemId)
            .ToArray();
    }

    private static bool HasPotentialAggregateSpread(MarketPrice price)
    {
        if (price.Buys.Quantity < BeginnerRecommendationPolicy.MinimumAggregateSideQuantity ||
            price.Sells.Quantity < BeginnerRecommendationPolicy.MinimumAggregateSideQuantity ||
            price.Buys.UnitPriceInCopper <= 0 || price.Sells.UnitPriceInCopper <= 1)
        {
            return false;
        }

        var plannedBuyPrice = checked((long)price.Buys.UnitPriceInCopper + 1);
        var plannedSalePrice = checked((long)price.Sells.UnitPriceInCopper - 1);
        return plannedSalePrice >= plannedBuyPrice &&
            plannedSalePrice <= checked(plannedBuyPrice * BeginnerRecommendationPolicy.MaximumPlannedPriceSpreadMultiple);
    }

    private static void ValidatePriceItemIds(IReadOnlyList<int>? itemIds)
    {
        if (itemIds is null || itemIds.Count == 0 || itemIds.Any(itemId => itemId <= 0) ||
            itemIds.Distinct().Count() != itemIds.Count)
        {
            throw new PublicMarketSnapshotCollectionException(Gw2ApiErrorCategory.IncompleteData);
        }
    }

    private static void ValidatePrices(
        IReadOnlyList<int> expectedItemIds,
        IReadOnlyList<MarketPrice>? prices)
    {
        ValidateExactItemSet(expectedItemIds, prices, price => price.ItemId);
        if (prices!.Any(price => price is null || price.Buys is null || price.Sells is null ||
            price.Buys.Quantity < 0 || price.Sells.Quantity < 0 ||
            price.Buys.UnitPriceInCopper < 0 || price.Sells.UnitPriceInCopper < 0))
        {
            throw new PublicMarketSnapshotCollectionException(Gw2ApiErrorCategory.InvalidPayload);
        }
    }

    private static void ValidateListings(
        IReadOnlyList<int> expectedItemIds,
        IReadOnlyList<MarketListing>? listings)
    {
        ValidateExactItemSet(expectedItemIds, listings, listing => listing.ItemId);
        if (listings!.Any(listing => listing is null || listing.Buys is null || listing.Sells is null ||
            listing.Buys.Any(level => level is null || level.Listings <= 0 || level.Quantity <= 0 || level.UnitPriceInCopper <= 0) ||
            listing.Sells.Any(level => level is null || level.Listings <= 0 || level.Quantity <= 0 || level.UnitPriceInCopper <= 0)))
        {
            throw new PublicMarketSnapshotCollectionException(Gw2ApiErrorCategory.InvalidPayload);
        }
    }

    private static void ValidateMetadata(
        IReadOnlyList<int> expectedItemIds,
        IReadOnlyList<MarketItemMetadata>? metadata)
    {
        ValidateExactItemSet(expectedItemIds, metadata, item => item.ItemId);
        if (metadata!.Any(item => item is null || string.IsNullOrWhiteSpace(item.Name) ||
            item.NormalStackLimit != MarketItemStackPolicy.NormalStackLimit))
        {
            throw new PublicMarketSnapshotCollectionException(Gw2ApiErrorCategory.InvalidPayload);
        }
    }

    private static void ValidateExactItemSet<T>(
        IReadOnlyList<int> expectedItemIds,
        IReadOnlyList<T>? values,
        Func<T, int> getItemId)
    {
        if (values is null || values.Count != expectedItemIds.Count)
        {
            throw new PublicMarketSnapshotCollectionException(Gw2ApiErrorCategory.IncompleteData);
        }

        var expected = expectedItemIds.ToHashSet();
        var received = new HashSet<int>();
        foreach (var value in values)
        {
            if (value is null || !received.Add(getItemId(value)) || !expected.Contains(getItemId(value)))
            {
                throw new PublicMarketSnapshotCollectionException(Gw2ApiErrorCategory.IncompleteData);
            }
        }
    }

    private sealed record Finalist(int ItemId, int MinimumAggregateSideQuantity, long AggregatePriceGap);
}

/// <summary>
/// Complete, in-memory public data gathered for one artifact. The collection
/// is valid only when every requested finalist has its listing and metadata.
/// </summary>
public sealed record PublicMarketSnapshotCollection(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<MarketSnapshotCandidate> Candidates);

/// <summary>
/// Progress exposed to the existing player scan without coupling the collector
/// to the local scan lifecycle.
/// </summary>
public sealed record PublicMarketSnapshotCollectionProgress(
    PublicMarketSnapshotCollectionStage Stage,
    int? FinalistCount);

public enum PublicMarketSnapshotCollectionStage
{
    DiscoveringPriceItemIds,
    DiscoveringAggregatePrices,
    ScreeningCandidates,
    ReadingFinalistListings,
    ReadingFinalistMetadata,
}

/// <summary>
/// Stable failure category for a rejected public collection.
/// </summary>
public sealed class PublicMarketSnapshotCollectionException : Exception
{
    public PublicMarketSnapshotCollectionException(Gw2ApiErrorCategory errorCategory)
    {
        ErrorCategory = errorCategory;
    }

    public Gw2ApiErrorCategory ErrorCategory { get; }
}

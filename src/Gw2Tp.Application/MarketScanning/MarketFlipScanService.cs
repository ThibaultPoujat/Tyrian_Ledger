using Gw2Tp.Analytics.Finance;
using Gw2Tp.Analytics.FlipOpportunities;
using Gw2Tp.Analytics.OrderBooks;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Application.Preferences;
using Gw2Tp.Application.Time;
using Gw2Tp.Domain.Finance;
using Gw2Tp.Domain.MarketData;

namespace Gw2Tp.Application.MarketScanning;

/// <summary>
/// Performs a bounded, read-only market-flip scan over the user's locally tracked items.
/// Aggregate prices screen candidates before any detailed order-book request is made.
/// </summary>
public sealed class MarketFlipScanService
{
    private readonly IMarketWatchlistStore watchlistStore;
    private readonly IGw2ApiClient gw2ApiClient;
    private readonly IClock clock;

    public MarketFlipScanService(
        IMarketWatchlistStore watchlistStore,
        IGw2ApiClient gw2ApiClient,
        IClock clock)
    {
        this.watchlistStore = watchlistStore ?? throw new ArgumentNullException(nameof(watchlistStore));
        this.gw2ApiClient = gw2ApiClient ?? throw new ArgumentNullException(nameof(gw2ApiClient));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<MarketFlipScanResult> ScanAsync(
        UserSessionPreferences preferences,
        FlipOpportunityScoringConfiguration scoringConfiguration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(scoringConfiguration);

        var itemIds = (await watchlistStore.ListAsync(cancellationToken).ConfigureAwait(false))
            .Select(item => item.ItemId)
            .Distinct()
            .OrderBy(itemId => itemId)
            .ToArray();
        var scannedAtUtc = clock.UtcNow;

        if (itemIds.Length == 0)
        {
            return MarketFlipScanResult.NoTrackedItems(scannedAtUtc);
        }

        var prices = await gw2ApiClient.GetPricesAsync(itemIds, cancellationToken).ConfigureAwait(false);
        if (!prices.IsSuccess || prices.IsPartialData || prices.Value is null)
        {
            return MarketFlipScanResult.Unavailable(scannedAtUtc, prices.ErrorCategory, itemIds.Length);
        }

        var requestedItemIds = itemIds.ToHashSet();
        var pricesByItemId = new Dictionary<int, MarketPrice>();
        foreach (var price in prices.Value.Where(price => requestedItemIds.Contains(price.ItemId)))
        {
            if (!pricesByItemId.TryAdd(price.ItemId, price))
            {
                return MarketFlipScanResult.Unavailable(
                    scannedAtUtc,
                    Gw2ApiErrorCategory.InvalidPayload,
                    itemIds.Length);
            }
        }

        if (itemIds.Any(itemId => !pricesByItemId.ContainsKey(itemId)))
        {
            return MarketFlipScanResult.Unavailable(
                scannedAtUtc,
                Gw2ApiErrorCategory.InvalidPayload,
                itemIds.Length);
        }

        var screenedCandidates = itemIds
            .Select(itemId => pricesByItemId[itemId])
            .Where(IsPotentiallyProfitableAtTopOfBook)
            .Select(price => new MarketScreenedCandidate(
                price.ItemId,
                price.Buys.UnitPriceInCopper,
                price.Sells.UnitPriceInCopper))
            .ToArray();

        if (!preferences.TryCreateTransactionFeePolicy(out var feePolicy))
        {
            return MarketFlipScanResult.FeeConfigurationRequired(scannedAtUtc, itemIds.Length, screenedCandidates);
        }

        if (screenedCandidates.Length == 0)
        {
            return MarketFlipScanResult.Complete(scannedAtUtc, itemIds.Length, screenedCandidates, []);
        }

        var listings = await gw2ApiClient.GetListingsAsync(
            screenedCandidates.Select(candidate => candidate.ItemId).ToArray(),
            cancellationToken).ConfigureAwait(false);
        if (!listings.IsSuccess || listings.IsPartialData || listings.Value is null || listings.Freshness is null)
        {
            return MarketFlipScanResult.Unavailable(scannedAtUtc, listings.ErrorCategory, itemIds.Length, screenedCandidates);
        }

        var listingsByItemId = listings.Value
            .Where(listing => screenedCandidates.Any(candidate => candidate.ItemId == listing.ItemId))
            .GroupBy(listing => listing.ItemId)
            .ToDictionary(group => group.Key, group => group.First());
        if (screenedCandidates.Any(candidate => !listingsByItemId.ContainsKey(candidate.ItemId)) ||
            listingsByItemId.Values.Any(listing => !HasValidLevels(listing)))
        {
            return MarketFlipScanResult.Unavailable(
                scannedAtUtc,
                Gw2ApiErrorCategory.InvalidPayload,
                itemIds.Length,
                screenedCandidates);
        }

        var analyzer = new FlipOpportunityAnalyzer(feePolicy!);
        var analyses = screenedCandidates
            .Select(candidate => listingsByItemId[candidate.ItemId])
            .Select(listing => analyzer.Analyze(CreateRequest(listing, preferences, listings.Freshness, scannedAtUtc)))
            .ToArray();
        var scoresByItemId = new FlipOpportunityScorer(scoringConfiguration)
            .Rank(analyses)
            .ToDictionary(score => score.ItemId);
        var opportunities = analyses
            .Where(analysis => scoresByItemId.ContainsKey(analysis.Scenario.ItemId))
            .Select(analysis => new MarketFlipScanOpportunity(analysis, scoresByItemId[analysis.Scenario.ItemId]))
            .OrderByDescending(opportunity => opportunity.Score.ScoreBasisPoints)
            .ThenBy(opportunity => opportunity.Analysis.Scenario.ItemId)
            .ToArray();

        return MarketFlipScanResult.Complete(scannedAtUtc, itemIds.Length, screenedCandidates, opportunities);
    }

    private static bool IsPotentiallyProfitableAtTopOfBook(MarketPrice price) =>
        price.Buys.Quantity > 0 &&
        price.Sells.Quantity > 0 &&
        price.Buys.UnitPriceInCopper > 0 &&
        price.Sells.UnitPriceInCopper > 0 &&
        price.Buys.UnitPriceInCopper > price.Sells.UnitPriceInCopper;

    private static bool HasValidLevels(MarketListing listing) =>
        listing.Buys is not null &&
        listing.Sells is not null &&
        listing.Buys.All(HasValidLevel) &&
        listing.Sells.All(HasValidLevel);

    private static bool HasValidLevel(MarketOrderLevel level) =>
        level.Listings >= 0 &&
        level.Quantity > 0 &&
        level.UnitPriceInCopper >= 0;

    private static FlipOpportunityRequest CreateRequest(
        MarketListing listing,
        UserSessionPreferences preferences,
        DataFreshness freshness,
        DateTimeOffset analyzedAtUtc) =>
        new(
            listing.ItemId,
            preferences.AnalysisQuantity,
            new FlipOpportunityOrderBook(
                listing.Buys.Select(level => new OrderBookLevel(level.Quantity, new Money(level.UnitPriceInCopper))).ToArray(),
                listing.Sells.Select(level => new OrderBookLevel(level.Quantity, new Money(level.UnitPriceInCopper))).ToArray(),
                freshness,
                isPartialData: false),
            analyzedAtUtc,
            new FlipOpportunityConstraints(
                preferences.MinimumProfitCopper is { } minimumProfit ? new Money(minimumProfit) : Money.Zero,
                preferences.GetPerOpportunityCapitalLimitCopper() is { } maximumCapital ? new Money(maximumCapital) : null));
}

public enum MarketFlipScanStatus
{
    NoTrackedItems,
    FeeConfigurationRequired,
    Complete,
    Unavailable,
}

public sealed record MarketScreenedCandidate(int ItemId, int BestBidCopper, int BestAskCopper);

public sealed record MarketFlipScanOpportunity(
    FlipOpportunityAnalysis Analysis,
    FlipOpportunityScore Score);

public sealed record MarketFlipScanResult(
    MarketFlipScanStatus Status,
    DateTimeOffset ScannedAtUtc,
    int TrackedItemCount,
    IReadOnlyList<MarketScreenedCandidate> ScreenedCandidates,
    IReadOnlyList<MarketFlipScanOpportunity> Opportunities,
    Gw2ApiErrorCategory? ErrorCategory)
{
    public static MarketFlipScanResult NoTrackedItems(DateTimeOffset scannedAtUtc) =>
        new(MarketFlipScanStatus.NoTrackedItems, scannedAtUtc, 0, [], [], null);

    public static MarketFlipScanResult FeeConfigurationRequired(
        DateTimeOffset scannedAtUtc,
        int trackedItemCount,
        IReadOnlyList<MarketScreenedCandidate> screenedCandidates) =>
        new(MarketFlipScanStatus.FeeConfigurationRequired, scannedAtUtc, trackedItemCount, screenedCandidates, [], null);

    public static MarketFlipScanResult Complete(
        DateTimeOffset scannedAtUtc,
        int trackedItemCount,
        IReadOnlyList<MarketScreenedCandidate> screenedCandidates,
        IReadOnlyList<MarketFlipScanOpportunity> opportunities) =>
        new(MarketFlipScanStatus.Complete, scannedAtUtc, trackedItemCount, screenedCandidates, opportunities, null);

    public static MarketFlipScanResult Unavailable(
        DateTimeOffset scannedAtUtc,
        Gw2ApiErrorCategory? errorCategory,
        int trackedItemCount = 0,
        IReadOnlyList<MarketScreenedCandidate>? screenedCandidates = null) =>
        new(
            MarketFlipScanStatus.Unavailable,
            scannedAtUtc,
            trackedItemCount,
            screenedCandidates ?? [],
            [],
            errorCategory);
}

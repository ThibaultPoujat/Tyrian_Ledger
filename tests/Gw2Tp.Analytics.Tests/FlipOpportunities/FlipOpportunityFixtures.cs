using Gw2Tp.Analytics.FlipOpportunities;
using Gw2Tp.Analytics.OrderBooks;
using Gw2Tp.Domain.Finance;
using Gw2Tp.Domain.MarketData;

namespace Gw2Tp.Analytics.Tests.FlipOpportunities;

/// <summary>
/// Small, synthetic fixtures for deterministic flip-opportunity analysis tests.
/// </summary>
internal static class FlipOpportunityFixtures
{
    public static readonly DateTimeOffset AnalysisAtUtc = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    public static FlipOpportunityRequest KnownGood(FlipOpportunityConstraints? constraints = null) =>
        new(
            itemId: 900_001,
            requestedQuantity: 5,
            new FlipOpportunityOrderBook(
                buyLevels: [Level(5, 150), Level(2, 140)],
                sellLevels: [Level(3, 100), Level(4, 110)],
                Freshness(),
                isPartialData: false),
            AnalysisAtUtc,
            constraints ?? new FlipOpportunityConstraints(new Money(100), new Money(600)));

    public static FlipOpportunityRequest NegativeProfit() =>
        new(
            itemId: 900_002,
            requestedQuantity: 5,
            new FlipOpportunityOrderBook(
                buyLevels: [Level(5, 150)],
                sellLevels: [Level(5, 150)],
                Freshness(),
                isPartialData: false),
            AnalysisAtUtc,
            new FlipOpportunityConstraints(Money.Zero));

    public static FlipOpportunityRequest Stale(FlipOpportunityConstraints? constraints = null) =>
        new(
            itemId: 900_003,
            requestedQuantity: 5,
            new FlipOpportunityOrderBook(
                buyLevels: [Level(5, 150)],
                sellLevels: [Level(3, 100), Level(4, 110)],
                new DataFreshness(AnalysisAtUtc.AddMinutes(-2), AnalysisAtUtc),
                isPartialData: false),
            AnalysisAtUtc,
            constraints ?? new FlipOpportunityConstraints(new Money(100), new Money(600)));

    public static FlipOpportunityRequest InsufficientAcquisitionDepth() =>
        new(
            itemId: 900_004,
            requestedQuantity: 5,
            new FlipOpportunityOrderBook(
                buyLevels: [Level(5, 150)],
                sellLevels: [Level(3, 100)],
                Freshness(),
                isPartialData: false),
            AnalysisAtUtc,
            new FlipOpportunityConstraints(Money.Zero));

    public static FlipOpportunityRequest MissingOrderBook() =>
        new(
            itemId: 900_005,
            requestedQuantity: 5,
            orderBook: null,
            AnalysisAtUtc,
            new FlipOpportunityConstraints(Money.Zero));

    public static FlipOpportunityRequest PartialOrderBook() =>
        new(
            itemId: 900_006,
            requestedQuantity: 5,
            new FlipOpportunityOrderBook(
                buyLevels: [Level(5, 150)],
                sellLevels: [Level(5, 100)],
                Freshness(),
                isPartialData: true),
            AnalysisAtUtc,
            new FlipOpportunityConstraints(Money.Zero));

    public static FlipOpportunityRequest MissingFreshnessMetadata() =>
        new(
            itemId: 900_007,
            requestedQuantity: 5,
            new FlipOpportunityOrderBook(
                buyLevels: [Level(5, 150)],
                sellLevels: [Level(5, 100)],
                freshness: null,
                isPartialData: false),
            AnalysisAtUtc,
            new FlipOpportunityConstraints(Money.Zero));

    public static FlipOpportunityRequest ZeroCapital() =>
        new(
            itemId: 900_008,
            requestedQuantity: 5,
            new FlipOpportunityOrderBook(
                buyLevels: [Level(5, 0)],
                sellLevels: [Level(5, 0)],
                Freshness(),
                isPartialData: false),
            AnalysisAtUtc,
            new FlipOpportunityConstraints(Money.Zero));

    private static DataFreshness Freshness() =>
        new(AnalysisAtUtc.AddMinutes(-1), AnalysisAtUtc.AddMinutes(1));

    private static OrderBookLevel Level(int quantity, long copper) => new(quantity, new Money(copper));
}

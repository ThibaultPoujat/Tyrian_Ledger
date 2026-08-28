using Gw2Tp.Application.MarketData;
using Gw2Tp.Domain.MarketData;

namespace Gw2Tp.Application.MarketHistory;

/// <summary>
/// Versioned, immutable top-of-book observation retained for historical
/// analysis. Monetary values remain integer copper in <see cref="MarketPrice"/>.
/// </summary>
public sealed record MarketPriceSnapshot
{
    public const int CurrentFormatVersion = 1;

    public MarketPriceSnapshot(
        Guid id,
        MarketPrice price,
        DataFreshness freshness,
        int formatVersion = CurrentFormatVersion)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A snapshot ID is required.", nameof(id));
        }

        Price = ValidatePrice(price);
        Freshness = freshness ?? throw new ArgumentNullException(nameof(freshness));
        FormatVersion = ValidateFormatVersion(formatVersion);
        Id = id;
    }

    public Guid Id { get; }

    public MarketPrice Price { get; }

    public DataFreshness Freshness { get; }

    public int FormatVersion { get; }

    private static MarketPrice ValidatePrice(MarketPrice price)
    {
        ArgumentNullException.ThrowIfNull(price);
        MarketSnapshotValidation.ValidateItemId(price.ItemId, nameof(price));
        MarketSnapshotValidation.ValidateOrderSummary(price.Buys, nameof(price));
        MarketSnapshotValidation.ValidateOrderSummary(price.Sells, nameof(price));
        return price;
    }

    private static int ValidateFormatVersion(int formatVersion)
    {
        if (formatVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(formatVersion), "A snapshot format version must be positive.");
        }

        return formatVersion;
    }
}

/// <summary>
/// Versioned, immutable full order-book observation retained for historical
/// liquidity analysis. Source ordering is preserved on both book sides.
/// </summary>
public sealed record MarketOrderBookSnapshot
{
    public const int CurrentFormatVersion = 1;

    public MarketOrderBookSnapshot(
        Guid id,
        MarketListing orderBook,
        DataFreshness freshness,
        int formatVersion = CurrentFormatVersion)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A snapshot ID is required.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(orderBook);
        MarketSnapshotValidation.ValidateItemId(orderBook.ItemId, nameof(orderBook));
        Buys = MarketSnapshotValidation.CopyAndValidateLevels(orderBook.Buys, nameof(orderBook));
        Sells = MarketSnapshotValidation.CopyAndValidateLevels(orderBook.Sells, nameof(orderBook));
        Id = id;
        ItemId = orderBook.ItemId;
        Freshness = freshness ?? throw new ArgumentNullException(nameof(freshness));
        FormatVersion = ValidateFormatVersion(formatVersion);
    }

    public Guid Id { get; }

    public int ItemId { get; }

    public IReadOnlyList<MarketOrderLevel> Buys { get; }

    public IReadOnlyList<MarketOrderLevel> Sells { get; }

    public DataFreshness Freshness { get; }

    public int FormatVersion { get; }

    public MarketListing ToMarketListing() => new(ItemId, Buys, Sells);

    private static int ValidateFormatVersion(int formatVersion)
    {
        if (formatVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(formatVersion), "A snapshot format version must be positive.");
        }

        return formatVersion;
    }
}

internal static class MarketSnapshotValidation
{
    public static void ValidateItemId(int itemId, string parameterName)
    {
        if (itemId <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "A market snapshot item ID must be positive.");
        }
    }

    public static void ValidateOrderSummary(MarketOrderSummary summary, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(summary);
        if (summary.Quantity < 0 || summary.UnitPriceInCopper < 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Market order quantities and unit prices cannot be negative.");
        }
    }

    public static IReadOnlyList<MarketOrderLevel> CopyAndValidateLevels(
        IReadOnlyList<MarketOrderLevel> levels,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(levels);
        var copiedLevels = levels.ToArray();
        foreach (var level in copiedLevels)
        {
            ArgumentNullException.ThrowIfNull(level);
            if (level.Listings < 0 || level.Quantity < 0 || level.UnitPriceInCopper < 0)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Order-book listings, quantities, and unit prices cannot be negative.");
            }
        }

        return Array.AsReadOnly(copiedLevels);
    }
}

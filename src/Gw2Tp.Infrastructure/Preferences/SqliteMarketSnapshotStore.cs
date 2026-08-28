using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketHistory;
using Gw2Tp.Domain.MarketData;
using Microsoft.EntityFrameworkCore;

namespace Gw2Tp.Infrastructure.Preferences;

internal sealed class SqliteMarketSnapshotStore : IMarketSnapshotStore
{
    private readonly IDbContextFactory<UserSessionPreferencesDbContext> dbContextFactory;

    public SqliteMarketSnapshotStore(IDbContextFactory<UserSessionPreferencesDbContext> dbContextFactory)
    {
        this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    public async Task AppendAsync(MarketPriceSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await dbContext.MarketPriceSnapshots.AnyAsync(candidate => candidate.Id == snapshot.Id, cancellationToken))
        {
            throw new InvalidOperationException("A market price snapshot with the same ID already exists.");
        }

        dbContext.MarketPriceSnapshots.Add(ToEntity(snapshot));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AppendAsync(MarketOrderBookSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await dbContext.MarketOrderBookSnapshots.AnyAsync(candidate => candidate.Id == snapshot.Id, cancellationToken))
        {
            throw new InvalidOperationException("A market order-book snapshot with the same ID already exists.");
        }

        dbContext.MarketOrderBookSnapshots.Add(ToEntity(snapshot));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MarketPriceSnapshot>> ListPriceSnapshotsAsync(
        int itemId,
        CancellationToken cancellationToken)
    {
        ValidateItemId(itemId);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await dbContext.MarketPriceSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.ItemId == itemId)
            .OrderBy(snapshot => snapshot.CapturedAtUtcTicks)
            .ThenBy(snapshot => snapshot.Id)
            .ToArrayAsync(cancellationToken);

        return Array.AsReadOnly(entities.Select(ToModel).ToArray());
    }

    public async Task<IReadOnlyList<MarketOrderBookSnapshot>> ListOrderBookSnapshotsAsync(
        int itemId,
        CancellationToken cancellationToken)
    {
        ValidateItemId(itemId);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await dbContext.MarketOrderBookSnapshots
            .AsNoTracking()
            .Include(snapshot => snapshot.Levels)
            .Where(snapshot => snapshot.ItemId == itemId)
            .OrderBy(snapshot => snapshot.CapturedAtUtcTicks)
            .ThenBy(snapshot => snapshot.Id)
            .ToArrayAsync(cancellationToken);

        return Array.AsReadOnly(entities.Select(ToModel).ToArray());
    }

    public async Task<IReadOnlyDictionary<int, MarketSnapshotCollectionState>> GetCollectionStatesAsync(
        IReadOnlyCollection<int> itemIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        var distinctItemIds = itemIds
            .OrderBy(itemId => itemId)
            .Distinct()
            .ToArray();
        foreach (var itemId in distinctItemIds)
        {
            ValidateItemId(itemId);
        }

        if (distinctItemIds.Length == 0)
        {
            return new Dictionary<int, MarketSnapshotCollectionState>();
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var latestPriceTicks = await dbContext.MarketPriceSnapshots
            .AsNoTracking()
            .Where(snapshot => distinctItemIds.Contains(snapshot.ItemId))
            .GroupBy(snapshot => snapshot.ItemId)
            .Select(group => new LatestCaptureTicks(group.Key, group.Max(snapshot => snapshot.CapturedAtUtcTicks)))
            .ToDictionaryAsync(capture => capture.ItemId, capture => capture.CapturedAtUtcTicks, cancellationToken);
        var latestOrderBookTicks = await dbContext.MarketOrderBookSnapshots
            .AsNoTracking()
            .Where(snapshot => distinctItemIds.Contains(snapshot.ItemId))
            .GroupBy(snapshot => snapshot.ItemId)
            .Select(group => new LatestCaptureTicks(group.Key, group.Max(snapshot => snapshot.CapturedAtUtcTicks)))
            .ToDictionaryAsync(capture => capture.ItemId, capture => capture.CapturedAtUtcTicks, cancellationToken);

        var states = new Dictionary<int, MarketSnapshotCollectionState>(distinctItemIds.Length);
        foreach (var itemId in distinctItemIds)
        {
            states[itemId] = new MarketSnapshotCollectionState(
                latestPriceTicks.TryGetValue(itemId, out var priceTicks)
                    ? new DateTimeOffset(priceTicks, TimeSpan.Zero)
                    : null,
                latestOrderBookTicks.TryGetValue(itemId, out var orderBookTicks)
                    ? new DateTimeOffset(orderBookTicks, TimeSpan.Zero)
                    : null);
        }

        return states;
    }

    private static MarketPriceSnapshotEntity ToEntity(MarketPriceSnapshot snapshot) => new()
    {
        Id = snapshot.Id,
        ItemId = snapshot.Price.ItemId,
        CapturedAtUtc = snapshot.Freshness.CapturedAtUtc,
        CapturedAtUtcTicks = snapshot.Freshness.CapturedAtUtc.UtcDateTime.Ticks,
        ExpiresAtUtc = snapshot.Freshness.ExpiresAtUtc,
        FormatVersion = snapshot.FormatVersion,
        IsWhitelisted = snapshot.Price.IsWhitelisted,
        BuyQuantity = snapshot.Price.Buys.Quantity,
        BuyUnitPriceCopper = snapshot.Price.Buys.UnitPriceInCopper,
        SellQuantity = snapshot.Price.Sells.Quantity,
        SellUnitPriceCopper = snapshot.Price.Sells.UnitPriceInCopper,
    };

    private static MarketOrderBookSnapshotEntity ToEntity(MarketOrderBookSnapshot snapshot)
    {
        var entity = new MarketOrderBookSnapshotEntity
        {
            Id = snapshot.Id,
            ItemId = snapshot.ItemId,
            CapturedAtUtc = snapshot.Freshness.CapturedAtUtc,
            CapturedAtUtcTicks = snapshot.Freshness.CapturedAtUtc.UtcDateTime.Ticks,
            ExpiresAtUtc = snapshot.Freshness.ExpiresAtUtc,
            FormatVersion = snapshot.FormatVersion,
        };
        AddLevels(entity.Levels, snapshot.Id, "buy", snapshot.Buys);
        AddLevels(entity.Levels, snapshot.Id, "sell", snapshot.Sells);
        return entity;
    }

    private static void AddLevels(
        ICollection<MarketOrderBookLevelEntity> destination,
        Guid snapshotId,
        string side,
        IReadOnlyList<MarketOrderLevel> levels)
    {
        for (var position = 0; position < levels.Count; position++)
        {
            var level = levels[position];
            destination.Add(new MarketOrderBookLevelEntity
            {
                SnapshotId = snapshotId,
                Side = side,
                Position = position,
                Listings = level.Listings,
                Quantity = level.Quantity,
                UnitPriceCopper = level.UnitPriceInCopper,
            });
        }
    }

    private static MarketPriceSnapshot ToModel(MarketPriceSnapshotEntity entity) => new(
        entity.Id,
        new MarketPrice(
            entity.ItemId,
            entity.IsWhitelisted,
            new MarketOrderSummary(entity.BuyQuantity, entity.BuyUnitPriceCopper),
            new MarketOrderSummary(entity.SellQuantity, entity.SellUnitPriceCopper)),
        new DataFreshness(entity.CapturedAtUtc, entity.ExpiresAtUtc),
        entity.FormatVersion);

    private static MarketOrderBookSnapshot ToModel(MarketOrderBookSnapshotEntity entity)
    {
        var levels = entity.Levels ?? throw new InvalidOperationException("The stored order-book snapshot is missing its levels.");
        return new MarketOrderBookSnapshot(
            entity.Id,
            new MarketListing(
                entity.ItemId,
                levels.Where(level => level.Side == "buy")
                    .OrderBy(level => level.Position)
                    .Select(ToModel)
                    .ToArray(),
                levels.Where(level => level.Side == "sell")
                    .OrderBy(level => level.Position)
                    .Select(ToModel)
                    .ToArray()),
            new DataFreshness(entity.CapturedAtUtc, entity.ExpiresAtUtc),
            entity.FormatVersion);
    }

    private static MarketOrderLevel ToModel(MarketOrderBookLevelEntity entity) => new(
        entity.Listings,
        entity.Quantity,
        entity.UnitPriceCopper);

    private static void ValidateItemId(int itemId)
    {
        if (itemId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemId), "An item ID must be positive.");
        }
    }

    private sealed record LatestCaptureTicks(int ItemId, long CapturedAtUtcTicks);
}

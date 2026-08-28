using Gw2Tp.Application.MarketHistory;
using Microsoft.EntityFrameworkCore;

namespace Gw2Tp.Infrastructure.Preferences;

internal sealed class SqliteMarketWatchlistStore : IMarketWatchlistStore
{
    private readonly IDbContextFactory<UserSessionPreferencesDbContext> dbContextFactory;

    public SqliteMarketWatchlistStore(IDbContextFactory<UserSessionPreferencesDbContext> dbContextFactory)
    {
        this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    public async Task<IReadOnlyList<MarketTrackedItem>> ListAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await dbContext.MarketWatchlistItems
            .AsNoTracking()
            .OrderBy(item => item.ItemId)
            .ToArrayAsync(cancellationToken);

        return Array.AsReadOnly(entities.Select(ToModel).ToArray());
    }

    public async Task AddAsync(MarketTrackedItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await dbContext.MarketWatchlistItems.AnyAsync(candidate => candidate.ItemId == item.ItemId, cancellationToken))
        {
            throw new InvalidOperationException("The market item is already tracked locally.");
        }

        var trackedItemCount = await dbContext.MarketWatchlistItems.CountAsync(cancellationToken);
        if (trackedItemCount >= MarketSamplingPolicy.MaximumTrackedItemCount)
        {
            throw new InvalidOperationException(
                $"No more than {MarketSamplingPolicy.MaximumTrackedItemCount} items may be tracked locally.");
        }

        dbContext.MarketWatchlistItems.Add(new MarketWatchlistItemEntity
        {
            ItemId = item.ItemId,
            SamplingClass = ToStorageValue(item.SamplingClass),
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateSamplingClassAsync(
        int itemId,
        MarketSamplingClass samplingClass,
        CancellationToken cancellationToken)
    {
        ValidateItemId(itemId);
        var storedValue = ToStorageValue(samplingClass);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.MarketWatchlistItems
            .SingleOrDefaultAsync(candidate => candidate.ItemId == itemId, cancellationToken);
        if (entity is null)
        {
            throw new KeyNotFoundException("The market item is not tracked locally.");
        }

        entity.SamplingClass = storedValue;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(int itemId, CancellationToken cancellationToken)
    {
        ValidateItemId(itemId);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.MarketWatchlistItems
            .SingleOrDefaultAsync(candidate => candidate.ItemId == itemId, cancellationToken);
        if (entity is null)
        {
            throw new KeyNotFoundException("The market item is not tracked locally.");
        }

        dbContext.MarketWatchlistItems.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static MarketTrackedItem ToModel(MarketWatchlistItemEntity entity) => new(
        entity.ItemId,
        ParseStorageValue(entity.SamplingClass));

    private static string ToStorageValue(MarketSamplingClass samplingClass) => samplingClass switch
    {
        MarketSamplingClass.Watchlist => "watchlist",
        MarketSamplingClass.Background => "background",
        _ => throw new ArgumentOutOfRangeException(
            nameof(samplingClass),
            samplingClass,
            "Only watchlist and background items may be persisted for historical collection."),
    };

    private static MarketSamplingClass ParseStorageValue(string value) => value switch
    {
        "watchlist" => MarketSamplingClass.Watchlist,
        "background" => MarketSamplingClass.Background,
        _ => throw new InvalidOperationException("The stored market sampling class is unsupported."),
    };

    private static void ValidateItemId(int itemId)
    {
        if (itemId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemId), "An item ID must be positive.");
        }
    }
}

using Gw2Tp.Application.Persistence;
using Gw2Tp.Application.PersonalTradingPost;

namespace Gw2Tp.Application.MarketHistory;

/// <summary>
/// Supplies item IDs from the most recent complete local current-order snapshot.
/// It never creates an account profile or invents a candidate when account access
/// or local synchronized data is unavailable.
/// </summary>
public sealed class CurrentPersonalOrderMarketSamplingSource(
    IPersonalTradingPostGateway personalTradingPostGateway,
    IPersonalTradingPostRepository personalTradingPostRepository) : IMarketSamplingSource
{
    public string Name => "current-personal-orders";

    public async Task<IReadOnlyList<MarketSamplingCandidate>> GetCandidatesAsync(CancellationToken cancellationToken = default)
    {
        var account = await personalTradingPostGateway.GetAccountScopeAsync(cancellationToken).ConfigureAwait(false);
        if (!account.IsSuccess || account.Value is null || string.IsNullOrWhiteSpace(account.Value.AccountId))
        {
            return [];
        }

        var profile = await personalTradingPostRepository.FindAccountProfileAsync(account.Value.AccountId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return [];
        }

        var snapshot = await personalTradingPostRepository.GetLatestCurrentOrderSnapshotAsync(profile, cancellationToken).ConfigureAwait(false);
        return snapshot?.Orders
            .Select(order => order.ItemId)
            .Distinct()
            .OrderBy(itemId => itemId)
            .Select(itemId => new MarketSamplingCandidate(itemId, MarketSamplingTier.CurrentPersonalOrder, IncludeOrderBook: false))
            .ToArray()
            ?? [];
    }
}

/// <summary>
/// Supplies locally approved watchlist items. Watchlists remain best-price-only
/// unless a later shortlist source explicitly opts an item into detailed books.
/// </summary>
public sealed class WatchlistMarketSamplingSource(IWatchlistRepository watchlistRepository) : IMarketSamplingSource
{
    public string Name => "watchlist";

    public async Task<IReadOnlyList<MarketSamplingCandidate>> GetCandidatesAsync(CancellationToken cancellationToken = default) =>
        (await watchlistRepository.GetAllAsync(cancellationToken).ConfigureAwait(false))
            .Select(entry => entry.ItemId)
            .Distinct()
            .OrderBy(itemId => itemId)
            .Select(itemId => new MarketSamplingCandidate(itemId, MarketSamplingTier.Watchlist, IncludeOrderBook: false))
            .ToArray();
}

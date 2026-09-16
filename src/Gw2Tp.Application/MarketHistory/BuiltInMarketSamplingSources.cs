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

/// <summary>
/// Open manual investment positions need the same highest-priority evidence as
/// current personal orders. Closed positions contribute nothing, and the
/// composition policy preserves any overlapping reason (such as a watchlist).
/// </summary>
public sealed class OpenInvestmentPositionMarketSamplingSource(
    IPersonalTradingPostGateway personalTradingPostGateway,
    IPersonalTradingPostRepository personalTradingPostRepository,
    IInvestmentPositionRepository investmentPositions) : IMarketSamplingSource
{
    public string Name => "open-investment-positions";

    public async Task<IReadOnlyList<MarketSamplingCandidate>> GetCandidatesAsync(CancellationToken cancellationToken = default)
    {
        var account = await personalTradingPostGateway.GetAccountScopeAsync(cancellationToken).ConfigureAwait(false);
        if (!account.IsSuccess || account.Value is null || string.IsNullOrWhiteSpace(account.Value.AccountId)) return [];
        var profile = await personalTradingPostRepository.FindAccountProfileAsync(account.Value.AccountId, cancellationToken).ConfigureAwait(false);
        if (profile is null) return [];
        return (await investmentPositions.GetAllAsync(profile, cancellationToken).ConfigureAwait(false))
            .Where(position => !position.IsClosed && position.RemainingQuantity > 0)
            .Select(position => position.ItemId)
            .Distinct().OrderBy(itemId => itemId)
            .Select(itemId => new MarketSamplingCandidate(itemId, MarketSamplingTier.CurrentPersonalOrder, IncludeOrderBook: false))
            .ToArray();
    }
}

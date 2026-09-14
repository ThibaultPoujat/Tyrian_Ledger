using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.Recommendations;

internal sealed record ReserveRestorationBid(long OrderId, int ItemId, Money Capital, bool IsAlreadyCancelledByPolicy, decimal? Score);

internal static class ReserveRestorationSelector
{
    internal static IReadOnlySet<long> Select(IReadOnlyCollection<ReserveRestorationBid> bids, Money shortfall)
    {
        ArgumentNullException.ThrowIfNull(bids);
        if (shortfall.Copper <= 0) return new HashSet<long>();
        var remaining = Math.Max(0L, checked(shortfall.Copper - bids
            .Where(bid => bid.IsAlreadyCancelledByPolicy).Sum(bid => bid.Capital.Copper)));
        var selected = new HashSet<long>();
        foreach (var bid in bids.Where(bid => !bid.IsAlreadyCancelledByPolicy)
            .OrderBy(bid => bid.Score.HasValue ? 1 : 0)
            .ThenBy(bid => bid.Score ?? decimal.MinValue)
            .ThenBy(bid => bid.ItemId)
            .ThenBy(bid => bid.OrderId))
        {
            if (remaining <= 0) break;
            selected.Add(bid.OrderId);
            remaining = Math.Max(0L, checked(remaining - bid.Capital.Copper));
        }
        return selected;
    }
}

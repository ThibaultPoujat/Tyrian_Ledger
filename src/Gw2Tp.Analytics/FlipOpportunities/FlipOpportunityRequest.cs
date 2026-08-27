namespace Gw2Tp.Analytics.FlipOpportunities;

/// <summary>
/// Complete deterministic input for modeling a candidate flip at one UTC instant.
/// A null order book explicitly represents missing market data.
/// </summary>
public sealed record FlipOpportunityRequest
{
    public FlipOpportunityRequest(
        int itemId,
        int requestedQuantity,
        FlipOpportunityOrderBook? orderBook,
        DateTimeOffset analyzedAtUtc,
        FlipOpportunityConstraints constraints)
    {
        if (itemId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemId), "An item identifier must be positive.");
        }

        if (requestedQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedQuantity),
                "A requested quantity must be positive.");
        }

        ArgumentNullException.ThrowIfNull(constraints);

        ItemId = itemId;
        RequestedQuantity = requestedQuantity;
        OrderBook = orderBook;
        AnalyzedAtUtc = analyzedAtUtc.ToUniversalTime();
        Constraints = constraints;
    }

    public int ItemId { get; }

    public int RequestedQuantity { get; }

    public FlipOpportunityOrderBook? OrderBook { get; }

    public DateTimeOffset AnalyzedAtUtc { get; }

    public FlipOpportunityConstraints Constraints { get; }
}

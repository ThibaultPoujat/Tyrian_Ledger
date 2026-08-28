using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Analytics.OwnedItems;

/// <summary>
/// The exact economic cost of one allocation, or the stable reasons it could
/// not be valued from the supplied market evidence.
/// </summary>
public sealed record OwnedItemStrategyAnalysis
{
    public OwnedItemStrategyAnalysis(
        OwnedItemStrategy strategy,
        bool isAvailable,
        int ownedQuantity,
        int purchasedQuantity,
        Money? ownedOpportunityCost,
        Money? purchasedCost,
        Money? totalEconomicCost,
        IReadOnlyList<OwnedItemOpportunityCostReason> reasons)
    {
        if (!Enum.IsDefined(strategy))
        {
            throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "The owned-item strategy is unknown.");
        }

        if (ownedQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ownedQuantity), "An owned quantity cannot be negative.");
        }

        if (purchasedQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(purchasedQuantity), "A purchased quantity cannot be negative.");
        }

        ArgumentNullException.ThrowIfNull(reasons);

        if (reasons.Any(reason => !Enum.IsDefined(reason)))
        {
            throw new ArgumentException("Strategy reasons cannot contain unknown values.", nameof(reasons));
        }

        if (isAvailable)
        {
            if (reasons.Count != 0 || ownedOpportunityCost is null || purchasedCost is null || totalEconomicCost is null)
            {
                throw new ArgumentException("An available strategy must have complete costs and no failure reasons.");
            }

            if (totalEconomicCost.Value != ownedOpportunityCost.Value + purchasedCost.Value)
            {
                throw new ArgumentException("The total economic cost must equal owned opportunity cost plus purchased cost.");
            }
        }
        else if (reasons.Count == 0 || ownedOpportunityCost is not null || purchasedCost is not null || totalEconomicCost is not null)
        {
            throw new ArgumentException("An unavailable strategy must have failure reasons and no cost values.");
        }

        Strategy = strategy;
        IsAvailable = isAvailable;
        OwnedQuantity = ownedQuantity;
        PurchasedQuantity = purchasedQuantity;
        OwnedOpportunityCost = ownedOpportunityCost;
        PurchasedCost = purchasedCost;
        TotalEconomicCost = totalEconomicCost;
        Reasons = Array.AsReadOnly(reasons.Distinct().Order().ToArray());
    }

    public OwnedItemStrategy Strategy { get; }

    public bool IsAvailable { get; }

    public int OwnedQuantity { get; }

    public int PurchasedQuantity { get; }

    public Money? OwnedOpportunityCost { get; }

    public Money? PurchasedCost { get; }

    public Money? TotalEconomicCost { get; }

    public IReadOnlyList<OwnedItemOpportunityCostReason> Reasons { get; }
}

using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Analytics.Reconciliation;

/// <summary>
/// Immutable, locally recorded acquisition and sale evidence for one operation.
/// It contains actual values only; it never derives values from a modeled scenario.
/// </summary>
public sealed record OperationActualOutcome
{
    public OperationActualOutcome(
        IReadOnlyList<ActualAcquisitionLot> acquisitionLots,
        IReadOnlyList<ActualSaleSettlement> saleSettlements)
    {
        ArgumentNullException.ThrowIfNull(acquisitionLots);
        ArgumentNullException.ThrowIfNull(saleSettlements);

        if (acquisitionLots.Any(lot => lot is null) || saleSettlements.Any(settlement => settlement is null))
        {
            throw new ArgumentException("Actual outcome entries cannot contain null values.");
        }

        if (acquisitionLots.Count == 0)
        {
            throw new ArgumentException("An actual outcome requires at least one acquisition lot.", nameof(acquisitionLots));
        }

        if (acquisitionLots.Select(lot => lot.Id).Distinct().Count() != acquisitionLots.Count)
        {
            throw new ArgumentException("Acquisition lot IDs must be unique.", nameof(acquisitionLots));
        }

        if (saleSettlements.Select(settlement => settlement.Id).Distinct().Count() != saleSettlements.Count)
        {
            throw new ArgumentException("Sale settlement IDs must be unique.", nameof(saleSettlements));
        }

        var acquiredQuantity = acquisitionLots.Sum(lot => (long)lot.Quantity);
        var soldQuantity = saleSettlements.Sum(settlement => (long)settlement.Quantity);
        if (acquiredQuantity > int.MaxValue || soldQuantity > acquiredQuantity)
        {
            throw new ArgumentException("Actual sale quantities must not exceed recorded acquisitions.");
        }

        AcquisitionLots = Array.AsReadOnly(acquisitionLots
            .OrderBy(lot => lot.OccurredAtUtc)
            .ThenBy(lot => lot.Id)
            .ToArray());
        SaleSettlements = Array.AsReadOnly(saleSettlements
            .OrderBy(settlement => settlement.OccurredAtUtc)
            .ThenBy(settlement => settlement.Id)
            .ToArray());
    }

    /// <summary>
    /// Acquisition lots are consumed in this deterministic order for FIFO cost attribution.
    /// </summary>
    public IReadOnlyList<ActualAcquisitionLot> AcquisitionLots { get; }

    /// <summary>
    /// Sale settlements are reconciled in this deterministic order.
    /// </summary>
    public IReadOnlyList<ActualSaleSettlement> SaleSettlements { get; }
}

/// <summary>
/// A recorded acquisition quantity and its total actual cost in copper.
/// </summary>
public sealed record ActualAcquisitionLot
{
    public ActualAcquisitionLot(Guid id, DateTimeOffset occurredAtUtc, int quantity, Money totalCost)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An acquisition lot ID is required.", nameof(id));
        }

        if (quantity <= 0 || totalCost.Copper < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Acquisition quantity must be positive and cost must be non-negative.");
        }

        Id = id;
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
        Quantity = quantity;
        TotalCost = totalCost;
    }

    public Guid Id { get; }

    public DateTimeOffset OccurredAtUtc { get; }

    public int Quantity { get; }

    public Money TotalCost { get; }
}

/// <summary>
/// A recorded sale quantity, gross value, and the fees that actually applied to it.
/// </summary>
public sealed record ActualSaleSettlement
{
    public ActualSaleSettlement(
        Guid id,
        DateTimeOffset occurredAtUtc,
        int quantity,
        Money grossSaleValue,
        Money listingFee,
        Money exchangeFee)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A sale settlement ID is required.", nameof(id));
        }

        if (quantity <= 0 || grossSaleValue.Copper < 0 || listingFee.Copper < 0 || exchangeFee.Copper < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Sale quantity must be positive and value and fees must be non-negative.");
        }

        Id = id;
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
        Quantity = quantity;
        GrossSaleValue = grossSaleValue;
        ListingFee = listingFee;
        ExchangeFee = exchangeFee;
        _ = NetSaleProceeds;
    }

    public Guid Id { get; }

    public DateTimeOffset OccurredAtUtc { get; }

    public int Quantity { get; }

    public Money GrossSaleValue { get; }

    public Money ListingFee { get; }

    public Money ExchangeFee { get; }

    public Money NetSaleProceeds => GrossSaleValue - ListingFee - ExchangeFee;
}

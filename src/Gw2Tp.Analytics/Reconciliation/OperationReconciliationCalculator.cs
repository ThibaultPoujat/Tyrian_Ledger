using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Analytics.Reconciliation;

/// <summary>
/// Reconciles locally recorded actual outcomes without reading market data or
/// reusing modeled scenario values. Partial sales consume actual acquisition
/// lots FIFO, preserving every copper through proportional allocation.
/// </summary>
public sealed class OperationReconciliationCalculator
{
    public OperationReconciliation Calculate(
        OperationActualOutcome? actualOutcome,
        Money? currentModeledNetValue = null)
    {
        if (actualOutcome is null)
        {
            if (currentModeledNetValue is not null)
            {
                throw new ArgumentException(
                    "A current modeled value requires recorded acquisition evidence.",
                    nameof(currentModeledNetValue));
            }

            return new OperationReconciliation(
                OperationReconciliationStatus.NoRecordedActualOutcome,
                0,
                0,
                0,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        var acquiredQuantity = actualOutcome.AcquisitionLots.Sum(lot => lot.Quantity);
        var soldQuantity = actualOutcome.SaleSettlements.Sum(settlement => settlement.Quantity);
        var remainingQuantity = acquiredQuantity - soldQuantity;
        if (remainingQuantity == 0 && currentModeledNetValue is not null)
        {
            throw new ArgumentException(
                "A current modeled value is only applicable to remaining quantity.",
                nameof(currentModeledNetValue));
        }

        var acquisitionLots = actualOutcome.AcquisitionLots
            .Select(lot => new AcquisitionLotBalance(lot))
            .ToArray();
        var nextLotIndex = 0;
        var recognizedAcquisitionCost = Money.Zero;
        foreach (var settlement in actualOutcome.SaleSettlements)
        {
            var quantityToMatch = settlement.Quantity;
            while (quantityToMatch > 0)
            {
                var lot = acquisitionLots[nextLotIndex];
                var quantityFromLot = Math.Min(quantityToMatch, lot.RemainingQuantity);
                recognizedAcquisitionCost += lot.Consume(quantityFromLot);
                quantityToMatch -= quantityFromLot;

                if (lot.RemainingQuantity == 0)
                {
                    nextLotIndex++;
                }
            }
        }

        var remainingCostBasis = actualOutcome.AcquisitionLots
            .Aggregate(Money.Zero, (total, lot) => total + lot.TotalCost) - recognizedAcquisitionCost;
        var status = soldQuantity == 0
            ? OperationReconciliationStatus.UnrealizedOnly
            : remainingQuantity == 0
                ? OperationReconciliationStatus.FullyRealized
                : OperationReconciliationStatus.PartiallyRealized;

        if (soldQuantity == 0)
        {
            return new OperationReconciliation(
                status,
                acquiredQuantity,
                0,
                remainingQuantity,
                null,
                null,
                null,
                null,
                null,
                null,
                remainingCostBasis,
                currentModeledNetValue,
                currentModeledNetValue is null ? null : currentModeledNetValue.Value - remainingCostBasis);
        }

        var grossSaleValue = actualOutcome.SaleSettlements
            .Aggregate(Money.Zero, (total, settlement) => total + settlement.GrossSaleValue);
        var listingFee = actualOutcome.SaleSettlements
            .Aggregate(Money.Zero, (total, settlement) => total + settlement.ListingFee);
        var exchangeFee = actualOutcome.SaleSettlements
            .Aggregate(Money.Zero, (total, settlement) => total + settlement.ExchangeFee);
        var netSaleProceeds = grossSaleValue - listingFee - exchangeFee;

        return new OperationReconciliation(
            status,
            acquiredQuantity,
            soldQuantity,
            remainingQuantity,
            recognizedAcquisitionCost,
            grossSaleValue,
            listingFee,
            exchangeFee,
            netSaleProceeds,
            netSaleProceeds - recognizedAcquisitionCost,
            remainingCostBasis,
            currentModeledNetValue,
            currentModeledNetValue is null ? null : currentModeledNetValue.Value - remainingCostBasis);
    }

    private sealed class AcquisitionLotBalance
    {
        private readonly ActualAcquisitionLot lot;
        private int consumedQuantity;

        public AcquisitionLotBalance(ActualAcquisitionLot lot)
        {
            this.lot = lot;
        }

        public int RemainingQuantity => lot.Quantity - consumedQuantity;

        public Money Consume(int quantity)
        {
            if (quantity <= 0 || quantity > RemainingQuantity)
            {
                throw new ArgumentOutOfRangeException(nameof(quantity), "Consumed quantity must remain within the acquisition lot.");
            }

            var costBeforeConsumption = CalculateCumulativeCost(consumedQuantity);
            consumedQuantity += quantity;
            return CalculateCumulativeCost(consumedQuantity) - costBeforeConsumption;
        }

        private Money CalculateCumulativeCost(int quantity)
        {
            var wholeCopperPerUnit = lot.TotalCost.Copper / lot.Quantity;
            var remainderCopper = lot.TotalCost.Copper % lot.Quantity;
            var allocatedCopper = checked((wholeCopperPerUnit * quantity) + ((remainderCopper * quantity) / lot.Quantity));
            return new Money(allocatedCopper);
        }
    }
}

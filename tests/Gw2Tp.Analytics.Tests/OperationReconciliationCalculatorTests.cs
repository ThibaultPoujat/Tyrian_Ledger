using Gw2Tp.Analytics.Reconciliation;
using Gw2Tp.Domain.Finance;
using Xunit;

namespace Gw2Tp.Analytics.Tests;

public sealed class OperationReconciliationCalculatorTests
{
    private static readonly DateTimeOffset RecordedAtUtc = new(2026, 8, 28, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Reconciles_a_complete_trade_from_recorded_values_and_fees()
    {
        var outcome = new OperationActualOutcome(
        [
            Acquisition("11111111-1111-1111-1111-111111111111", 5, 1_000),
        ],
        [
            Sale("22222222-2222-2222-2222-222222222222", 5, 1_500, 75, 150),
        ]);

        var reconciliation = new OperationReconciliationCalculator().Calculate(outcome);

        Assert.Equal(OperationReconciliationStatus.FullyRealized, reconciliation.Status);
        Assert.Equal(5, reconciliation.AcquiredQuantity);
        Assert.Equal(5, reconciliation.SoldQuantity);
        Assert.Equal(0, reconciliation.RemainingQuantity);
        Assert.Equal(new Money(1_000), reconciliation.RecognizedAcquisitionCost);
        Assert.Equal(new Money(1_500), reconciliation.GrossSaleValue);
        Assert.Equal(new Money(75), reconciliation.ListingFee);
        Assert.Equal(new Money(150), reconciliation.ExchangeFee);
        Assert.Equal(new Money(1_275), reconciliation.NetSaleProceeds);
        Assert.Equal(new Money(275), reconciliation.RealizedProfit);
        Assert.Equal(Money.Zero, reconciliation.RemainingCostBasis);
        Assert.Null(reconciliation.UnrealizedProfitLoss);
    }

    [Fact]
    public void Reconciles_a_partial_trade_FIFO_and_keeps_the_remaining_value_unrealized()
    {
        var outcome = new OperationActualOutcome(
        [
            Acquisition("33333333-3333-3333-3333-333333333333", 3, 100, RecordedAtUtc.AddMinutes(1)),
            Acquisition("44444444-4444-4444-4444-444444444444", 2, 80),
        ],
        [
            Sale("55555555-5555-5555-5555-555555555555", 3, 150, 10, 20, RecordedAtUtc.AddMinutes(2)),
        ]);

        var reconciliation = new OperationReconciliationCalculator().Calculate(outcome, new Money(120));

        Assert.Equal(OperationReconciliationStatus.PartiallyRealized, reconciliation.Status);
        Assert.Equal(5, reconciliation.AcquiredQuantity);
        Assert.Equal(3, reconciliation.SoldQuantity);
        Assert.Equal(2, reconciliation.RemainingQuantity);
        Assert.Equal(new Money(113), reconciliation.RecognizedAcquisitionCost);
        Assert.Equal(new Money(120), reconciliation.NetSaleProceeds);
        Assert.Equal(new Money(7), reconciliation.RealizedProfit);
        Assert.Equal(new Money(67), reconciliation.RemainingCostBasis);
        Assert.Equal(new Money(120), reconciliation.CurrentModeledNetValue);
        Assert.Equal(new Money(53), reconciliation.UnrealizedProfitLoss);
    }

    [Fact]
    public void Uses_the_recorded_fee_variation_instead_of_a_fee_policy()
    {
        var outcome = new OperationActualOutcome(
        [
            Acquisition("66666666-6666-6666-6666-666666666666", 1, 70),
        ],
        [
            Sale("77777777-7777-7777-7777-777777777777", 1, 100, 1, 29),
        ]);

        var reconciliation = new OperationReconciliationCalculator().Calculate(outcome);

        Assert.Equal(new Money(1), reconciliation.ListingFee);
        Assert.Equal(new Money(29), reconciliation.ExchangeFee);
        Assert.Equal(new Money(70), reconciliation.NetSaleProceeds);
        Assert.Equal(Money.Zero, reconciliation.RealizedProfit);
    }

    [Fact]
    public void Reports_an_acquired_but_unsold_trade_as_unrealized_only()
    {
        var outcome = new OperationActualOutcome(
        [
            Acquisition("88888888-8888-8888-8888-888888888888", 2, 60),
        ],
        []);

        var reconciliation = new OperationReconciliationCalculator().Calculate(outcome, new Money(70));

        Assert.Equal(OperationReconciliationStatus.UnrealizedOnly, reconciliation.Status);
        Assert.Null(reconciliation.RealizedProfit);
        Assert.Equal(new Money(60), reconciliation.RemainingCostBasis);
        Assert.Equal(new Money(10), reconciliation.UnrealizedProfitLoss);
    }

    [Fact]
    public void Does_not_count_a_cancelled_trade_without_actual_outcome_as_realized()
    {
        var reconciliation = new OperationReconciliationCalculator().Calculate(null);

        Assert.Equal(OperationReconciliationStatus.NoRecordedActualOutcome, reconciliation.Status);
        Assert.Equal(0, reconciliation.SoldQuantity);
        Assert.Null(reconciliation.RealizedProfit);
        Assert.Null(reconciliation.UnrealizedProfitLoss);
    }

    [Fact]
    public void Rejects_sale_quantity_that_exceeds_recorded_acquisitions()
    {
        Assert.Throws<ArgumentException>(() => new OperationActualOutcome(
        [
            Acquisition("99999999-9999-9999-9999-999999999999", 1, 10),
        ],
        [
            Sale("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", 2, 20, 1, 1),
        ]));
    }

    private static ActualAcquisitionLot Acquisition(
        string id,
        int quantity,
        long totalCostCopper,
        DateTimeOffset? occurredAtUtc = null) => new(
        Guid.Parse(id),
        occurredAtUtc ?? RecordedAtUtc,
        quantity,
        new Money(totalCostCopper));

    private static ActualSaleSettlement Sale(
        string id,
        int quantity,
        long grossSaleValueCopper,
        long listingFeeCopper,
        long exchangeFeeCopper,
        DateTimeOffset? occurredAtUtc = null) => new(
        Guid.Parse(id),
        occurredAtUtc ?? RecordedAtUtc,
        quantity,
        new Money(grossSaleValueCopper),
        new Money(listingFeeCopper),
        new Money(exchangeFeeCopper));
}

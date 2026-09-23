using Gw2Tp.Analytics.Finance;
using Gw2Tp.Application.Finance;
using Gw2Tp.Domain.Finance;
using Xunit;

namespace Gw2Tp.Application.Tests;

public sealed class Gw2TradingPostFeePolicyTests
{
    [Fact]
    public void Declares_the_canonical_components_and_provisional_evidence_status()
    {
        var policy = Gw2TradingPostFeePolicy.Create();

        Assert.Equal(500, policy.ListingFeeRule.BasisPoints);
        Assert.Equal(1_000, policy.ExchangeFeeRule.BasisPoints);
        Assert.Equal(FeeRounding.Up, policy.ListingFeeRule.Rounding);
        Assert.Equal(FeeRounding.Up, policy.ExchangeFeeRule.Rounding);
        Assert.Equal(new Money(1), policy.ListingFeeRule.MinimumFee);
        Assert.Equal(new Money(1), policy.ExchangeFeeRule.MinimumFee);
        Assert.False(Gw2TradingPostFeePolicy.IsListingFeeRefundable);
        Assert.False(Gw2TradingPostFeePolicy.IsFractionalCopperRoundingExternallyVerified);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 1)]
    [InlineData(9, 1, 1)]
    [InlineData(10, 1, 1)]
    [InlineData(19, 1, 2)]
    [InlineData(20, 1, 2)]
    [InlineData(21, 2, 3)]
    [InlineData(99, 5, 10)]
    [InlineData(100, 5, 10)]
    [InlineData(101, 6, 11)]
    public void Produces_independently_derived_whole_copper_vectors(
        long grossSaleCopper,
        long expectedListingFeeCopper,
        long expectedExchangeFeeCopper)
    {
        var fees = Gw2TradingPostFeePolicy.Create().CalculateFees(new Money(grossSaleCopper));

        Assert.Equal(new Money(expectedListingFeeCopper), fees.ListingFee);
        Assert.Equal(new Money(expectedExchangeFeeCopper), fees.ExchangeFee);
    }

    [Fact]
    public void Applies_each_fee_once_to_the_total_multi_unit_sale_value()
    {
        const int quantity = 3;
        const long unitSaleCopper = 101;
        var grossSaleValue = new Money(quantity * unitSaleCopper);

        var fees = Gw2TradingPostFeePolicy.Create().CalculateFees(grossSaleValue);

        Assert.Equal(new Money(303), grossSaleValue);
        Assert.Equal(new Money(16), fees.ListingFee);
        Assert.Equal(new Money(31), fees.ExchangeFee);
    }

    [Fact]
    public void Centralizes_full_up_front_cost_and_exact_roi()
    {
        var totalCost = Gw2TradingPostFeePolicy.CalculateFullUpFrontCost(new Money(101), new Money(10));
        var roi = Gw2TradingPostFeePolicy.CalculateExactRoi(new Money(68), new Money(101), new Money(10));

        Assert.Equal(new Money(111), totalCost);
        Assert.Equal(totalCost, roi.TotalCost);
        Assert.True(roi.MeetsOrExceedsBasisPoints(6_000));
        Assert.False(roi.MeetsOrExceedsBasisPoints(6_127));
    }

    [Fact]
    public void Calculates_the_largest_supported_sale_without_overflow()
    {
        var calculator = new FlipProfitCalculator(Gw2TradingPostFeePolicy.Create());

        var scenario = calculator.Calculate(Money.Zero, new Money(long.MaxValue));

        Assert.Equal(new Money(461_168_601_842_738_791), scenario.ListingFee);
        Assert.Equal(new Money(922_337_203_685_477_581), scenario.ExchangeFee);
        Assert.Equal(new Money(7_839_866_231_326_559_435), scenario.NetSaleProceeds);
        Assert.Equal(scenario.NetSaleProceeds, scenario.NetProfit);
    }

    [Fact]
    public void Calculates_the_first_break_even_unit_price_despite_independent_fee_rounding_dips()
    {
        var price = Gw2TradingPostFeePolicy.TryCalculateBreakEvenUnitPrice(new Money(170), quantity: 1);

        Assert.Equal(new Money(200), price);
        var feePolicy = Gw2TradingPostFeePolicy.Create();
        var beforeFees = feePolicy.CalculateFees(new Money(199));
        var atFees = feePolicy.CalculateFees(new Money(200));
        Assert.Equal(new Money(169), new Money(199) - beforeFees.ListingFee - beforeFees.ExchangeFee);
        Assert.Equal(new Money(170), new Money(200) - atFees.ListingFee - atFees.ExchangeFee);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 3)]
    public void Starts_break_even_at_a_valid_positive_sale_price_when_fees_are_minimal(
        long requiredNetProceeds,
        long expectedUnitPrice)
    {
        var price = Gw2TradingPostFeePolicy.TryCalculateBreakEvenUnitPrice(new Money(requiredNetProceeds), quantity: 1);

        Assert.Equal(new Money(expectedUnitPrice), price);
        var grossSale = new Money(expectedUnitPrice);
        var fees = Gw2TradingPostFeePolicy.Create().CalculateFees(grossSale);
        Assert.True((grossSale - fees.ListingFee - fees.ExchangeFee).Copper >= requiredNetProceeds);
    }
}

using Gw2Tp.Analytics.Finance;
using Gw2Tp.Domain.Finance;
using Xunit;

namespace Gw2Tp.Analytics.Tests;

public sealed class FlipProfitCalculatorTests
{
    [Fact]
    public void Calculates_representative_fee_and_profit_breakdown()
    {
        var calculator = new FlipProfitCalculator(
            new TransactionFeePolicy(
                new FeeRule(500, FeeRounding.Down),
                new FeeRule(1_000, FeeRounding.Down)));

        var scenario = calculator.Calculate(new Money(80), new Money(100));

        Assert.Equal(new Money(5), scenario.ListingFee);
        Assert.Equal(new Money(10), scenario.ExchangeFee);
        Assert.Equal(new Money(85), scenario.NetSaleProceeds);
        Assert.Equal(new Money(5), scenario.NetProfit);
    }

    [Fact]
    public void Represents_a_negative_profit_without_losing_copper_precision()
    {
        var calculator = new FlipProfitCalculator(
            new TransactionFeePolicy(
                new FeeRule(500, FeeRounding.Down),
                new FeeRule(1_000, FeeRounding.Down)));

        var scenario = calculator.Calculate(new Money(90), new Money(100));

        Assert.Equal(new Money(-5), scenario.NetProfit);
    }

    [Fact]
    public void Rounds_each_configured_fee_independently()
    {
        var policy = new TransactionFeePolicy(
            new FeeRule(50, FeeRounding.Down),
            new FeeRule(50, FeeRounding.Up));

        var fees = policy.CalculateFees(new Money(101));

        Assert.Equal(new Money(0), fees.ListingFee);
        Assert.Equal(new Money(1), fees.ExchangeFee);
    }

    [Fact]
    public void Calculates_large_values_without_multiplication_overflow()
    {
        var policy = new TransactionFeePolicy(
            new FeeRule(500, FeeRounding.Up),
            new FeeRule(1_000, FeeRounding.Up));
        var calculator = new FlipProfitCalculator(policy);

        var scenario = calculator.Calculate(new Money(0), new Money(long.MaxValue));

        Assert.Equal(new Money(461_168_601_842_738_791), scenario.ListingFee);
        Assert.Equal(new Money(922_337_203_685_477_581), scenario.ExchangeFee);
        Assert.Equal(new Money(7_839_866_231_326_559_435), scenario.NetProfit);
    }

    [Fact]
    public void Rejects_invalid_rules_and_negative_transaction_inputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FeeRule(-1, FeeRounding.Down));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FeeRule(10_001, FeeRounding.Down));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FeeRule(1, (FeeRounding)42));

        var policy = new TransactionFeePolicy(
            new FeeRule(500, FeeRounding.Down),
            new FeeRule(1_000, FeeRounding.Down));
        var calculator = new FlipProfitCalculator(policy);

        Assert.Throws<ArgumentOutOfRangeException>(() => policy.CalculateFees(new Money(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => calculator.Calculate(new Money(-1), new Money(100)));
    }
}

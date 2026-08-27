using Gw2Tp.Domain.Finance;
using Xunit;

namespace Gw2Tp.Domain.Tests;

public sealed class MoneyTests
{
    [Fact]
    public void Stores_exact_signed_copper_amounts()
    {
        var money = new Money(-1_234_567_890_123_456_789);

        Assert.Equal(-1_234_567_890_123_456_789, money.Copper);
        Assert.Equal(new Money(0), Money.Zero);
    }

    [Fact]
    public void Adds_and_subtracts_using_exact_copper_arithmetic()
    {
        var left = new Money(987_654_321);
        var right = new Money(123_456_789);

        Assert.Equal(new Money(1_111_111_110), left + right);
        Assert.Equal(new Money(864_197_532), left - right);
        Assert.Equal(new Money(-987_654_321), -left);
    }

    [Fact]
    public void Throws_when_checked_money_arithmetic_overflows()
    {
        Assert.Throws<OverflowException>(() => _ = new Money(long.MaxValue) + new Money(1));
        Assert.Throws<OverflowException>(() => _ = new Money(long.MinValue) - new Money(1));
        Assert.Throws<OverflowException>(() => _ = -new Money(long.MinValue));
    }
}

namespace Gw2Tp.Domain.Finance;

/// <summary>
/// A signed monetary amount represented exactly in copper.
/// </summary>
public readonly record struct Money(long Copper)
{
    public static readonly Money Zero = new(0);

    public static Money operator +(Money left, Money right) =>
        new(checked(left.Copper + right.Copper));

    public static Money operator -(Money left, Money right) =>
        new(checked(left.Copper - right.Copper));

    public static Money operator -(Money value) =>
        new(checked(-value.Copper));
}

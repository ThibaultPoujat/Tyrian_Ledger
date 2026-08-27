using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Analytics.FlipOpportunities;

/// <summary>
/// An exact return-on-investment ratio: net profit divided by capital required.
/// No display rounding or floating-point conversion is applied.
/// </summary>
public readonly record struct ExactReturnOnInvestment
{
    public ExactReturnOnInvestment(Money netProfit, Money capitalRequired)
    {
        if (capitalRequired.Copper <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capitalRequired),
                "Capital required must be positive to calculate return on investment.");
        }

        NetProfit = netProfit;
        CapitalRequired = capitalRequired;
    }

    public Money NetProfit { get; }

    public Money CapitalRequired { get; }
}

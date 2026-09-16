using Gw2Tp.Application.Investments;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Persistence;
using Xunit;

namespace Gw2Tp.Application.Tests;

public sealed class InvestmentPortfolioServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Staged_action_requires_every_suggested_unit_to_have_target_price_depth()
    {
        var position = Position(remainingQuantity: 2, [new InvestmentTarget(0, 100, 2)]);
        var valuation = new InvestmentValuation(InvestmentValuationState.Available, new("101"), new("85"), new("0"), 0, 100);
        var mixedDepth = new MarketListing(42, [new MarketOrderLevel(1, 1, 100), new MarketOrderLevel(1, 9, 1)], []);

        var action = InvestmentPortfolioService.DecideAction(position, valuation, mixedDepth);

        Assert.Equal(InvestmentAction.Hold, action.Action);
        Assert.Equal(0, action.SuggestedQuantity);
        Assert.Contains("target price", action.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Supported_next_stage_is_suggested_without_skipping_its_configured_order()
    {
        var position = Position(remainingQuantity: 8, [new InvestmentTarget(1, 200, 8)]);
        var valuation = new InvestmentValuation(InvestmentValuationState.Available, new("1600"), new("1360"), new("0"), 0, 200);
        var book = new MarketListing(42, [new MarketOrderLevel(1, 8, 200)], []);

        var action = InvestmentPortfolioService.DecideAction(position, valuation, book);

        Assert.Equal(InvestmentAction.Sell, action.Action);
        Assert.Equal(8, action.SuggestedQuantity);
    }

    private static InvestmentPosition Position(int remainingQuantity, IReadOnlyList<InvestmentTarget> targets) => new(
        1, 1, 42, remainingQuantity, remainingQuantity, 1000, "Investment", "Test", Now,
        "A manual thesis.", null, false, Now, Now, null, [], targets);
}

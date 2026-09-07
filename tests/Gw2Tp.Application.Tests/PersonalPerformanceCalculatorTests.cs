using Gw2Tp.Application.Accounting;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Persistence;
using Gw2Tp.Domain.Finance;
using Xunit;

namespace Gw2Tp.Application.Tests;

public sealed class PersonalPerformanceCalculatorTests
{
    private static readonly DateTimeOffset AsOfUtc = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private readonly PersonalPerformanceCalculator calculator = new();

    [Fact]
    public void Calculates_profitable_realized_results_with_canonical_fees_and_exact_roi()
    {
        var result = calculator.Rebuild(Request(
        [
            Buy(101, itemId: 42, unitPrice: 100, quantity: 2, completedAtUtc: AsOfUtc.AddDays(-2)),
            Sell(102, itemId: 42, unitPrice: 200, quantity: 2, completedAtUtc: AsOfUtc.AddDays(-1)),
        ]));

        var realized = result.CoverageRealizedPerformance;
        Assert.Equal(2, realized.KnownBasisQuantity);
        Assert.Equal(new Money(400), realized.GrossSales);
        Assert.Equal(new Money(20), realized.ListingFees);
        Assert.Equal(new Money(40), realized.ExchangeFees);
        Assert.Equal(new Money(340), realized.NetSaleProceeds);
        Assert.Equal(new Money(200), realized.AcquisitionBasis);
        Assert.Equal(new Money(140), realized.NetProfit);
        Assert.Equal(new Money(140), realized.Roi!.Value.Profit);
        Assert.Equal(new Money(200), realized.Roi!.Value.TotalCost);
        Assert.False(result.IsFeeRoundingExternallyVerified);
        Assert.Empty(result.UnknownBasisSaleAllocations);
    }

    [Fact]
    public void Calculates_losing_partial_sale_and_separately_values_remaining_open_basis()
    {
        var result = calculator.Rebuild(Request(
        [
            Buy(101, itemId: 42, unitPrice: 200, quantity: 5, completedAtUtc: AsOfUtc.AddDays(-4)),
            Sell(102, itemId: 42, unitPrice: 150, quantity: 2, completedAtUtc: AsOfUtc.AddDays(-1)),
        ], Evidence(itemId: 42, (3, 250))));

        Assert.Equal(new Money(-145), result.CoverageRealizedPerformance.NetProfit);
        Assert.Equal(new Money(600), result.OpenPerformance.OpenAcquisitionBasis);
        Assert.True(result.OpenPerformance.IsFullyValued);
        Assert.Equal(new Money(637), result.OpenPerformance.NetLiquidationValue);
        Assert.Equal(new Money(37), result.OpenPerformance.UnrealizedProfit);
        Assert.Equal(new Money(750), Assert.Single(result.OpenPerformance.Items).GrossSaleValue);
        Assert.Equal(new Money(38), Assert.Single(result.OpenPerformance.Items).ListingFee);
        Assert.Equal(new Money(75), Assert.Single(result.OpenPerformance.Items).ExchangeFee);
    }

    [Fact]
    public void Allocates_whole_sale_fees_proportionally_and_reconciles_mixed_known_unknown_sale()
    {
        var result = calculator.Rebuild(Request(
        [
            Buy(101, itemId: 42, unitPrice: 100, quantity: 2, completedAtUtc: AsOfUtc.AddDays(-2)),
            Sell(102, itemId: 42, unitPrice: 101, quantity: 3, completedAtUtc: AsOfUtc.AddDays(-1)),
        ]));

        var known = Assert.Single(result.KnownBasisSaleAllocations);
        var unknown = Assert.Single(result.UnknownBasisSaleAllocations);
        Assert.Equal(new Money(202), known.GrossSale);
        Assert.Equal(new Money(11), known.ListingFee);
        Assert.Equal(new Money(21), known.ExchangeFee);
        Assert.Equal(new Money(170), known.NetSaleProceeds);
        Assert.Equal(new Money(-30), known.NetProfit);
        Assert.Equal(new Money(101), unknown.GrossSale);
        Assert.Equal(new Money(5), unknown.ListingFee);
        Assert.Equal(new Money(10), unknown.ExchangeFee);
        Assert.Equal(new Money(86), unknown.NetSaleProceeds);
        Assert.Equal(1, unknown.Sale.UnmatchedQuantity);
        Assert.Equal(new Money(16), known.ListingFee + unknown.ListingFee);
        Assert.Equal(new Money(31), known.ExchangeFee + unknown.ExchangeFee);
        Assert.Equal(new Money(170), result.CoverageRealizedPerformance.NetSaleProceeds);
        Assert.Equal(new Money(-30), result.CoverageRealizedPerformance.NetProfit);
    }

    [Fact]
    public void Aggregates_many_fifo_lots_into_one_completed_sale_without_mixing_open_performance()
    {
        var result = calculator.Rebuild(Request(
        [
            Buy(101, itemId: 42, unitPrice: 100, quantity: 1, completedAtUtc: AsOfUtc.AddDays(-3)),
            Buy(102, itemId: 42, unitPrice: 150, quantity: 2, completedAtUtc: AsOfUtc.AddDays(-2)),
            Sell(103, itemId: 42, unitPrice: 300, quantity: 3, completedAtUtc: AsOfUtc.AddDays(-1)),
        ]));

        Assert.Equal(2, result.KnownBasisSaleAllocations.Count);
        Assert.Equal(new Money(400), result.CoverageRealizedPerformance.AcquisitionBasis);
        Assert.Equal(new Money(900), result.CoverageRealizedPerformance.GrossSales);
        Assert.Equal(new Money(45), result.CoverageRealizedPerformance.ListingFees);
        Assert.Equal(new Money(90), result.CoverageRealizedPerformance.ExchangeFees);
        Assert.Equal(new Money(365), result.CoverageRealizedPerformance.NetProfit);
        Assert.Equal(0, result.OpenPerformance.OpenQuantity);
        Assert.Equal(Money.Zero, result.OpenPerformance.UnrealizedProfit);
    }

    [Fact]
    public void Does_not_create_realized_profit_or_roi_for_entirely_unknown_sales()
    {
        var result = calculator.Rebuild(Request(
        [Sell(101, itemId: 42, unitPrice: 200, quantity: 2, completedAtUtc: AsOfUtc.AddDays(-1))]));

        Assert.Equal(0, result.CoverageRealizedPerformance.KnownBasisQuantity);
        Assert.Equal(Money.Zero, result.CoverageRealizedPerformance.GrossSales);
        Assert.Null(result.CoverageRealizedPerformance.Roi);
        var unknown = Assert.Single(result.UnknownBasisSaleAllocations);
        Assert.Equal(2, unknown.Sale.UnmatchedQuantity);
        Assert.Equal(new Money(400), unknown.GrossSale);
        Assert.Equal(new Money(20), unknown.ListingFee);
        Assert.Equal(new Money(40), unknown.ExchangeFee);
    }

    [Fact]
    public void Uses_utc_half_open_windows_and_requires_full_coverage()
    {
        var result = calculator.Rebuild(new PersonalPerformanceRequest(
            AsOfUtc,
            new PerformanceHistoryCoverage(AsOfUtc.AddDays(-10), AsOfUtc),
            [
                Buy(101, itemId: 42, unitPrice: 50, quantity: 1, completedAtUtc: AsOfUtc.AddDays(-9)),
                Sell(102, itemId: 42, unitPrice: 100, quantity: 1, completedAtUtc: AsOfUtc.AddDays(-7)),
                Buy(103, itemId: 43, unitPrice: 50, quantity: 1, completedAtUtc: AsOfUtc.AddDays(-2)),
                Sell(104, itemId: 43, unitPrice: 100, quantity: 1, completedAtUtc: AsOfUtc),
            ],
            []));

        var sevenDays = result.Windows.Single(window => window.Window == RealizedPerformanceWindow.SevenDays);
        var thirtyDays = result.Windows.Single(window => window.Window == RealizedPerformanceWindow.ThirtyDays);
        Assert.Equal(RealizedPerformanceWindowStatus.Supported, sevenDays.Status);
        Assert.Equal(new Money(35), sevenDays.KnownBasisPerformance!.NetProfit);
        Assert.Equal(RealizedPerformanceWindowStatus.InsufficientCoverage, thirtyDays.Status);
        Assert.Null(thirtyDays.KnownBasisPerformance);
    }

    [Fact]
    public void Does_not_claim_unrealized_value_without_complete_current_buy_depth()
    {
        var result = calculator.Rebuild(Request(
            [Buy(101, itemId: 42, unitPrice: 100, quantity: 3, completedAtUtc: AsOfUtc.AddDays(-2))],
            Evidence(itemId: 42, (2, 200))));

        var item = Assert.Single(result.OpenPerformance.Items);
        Assert.Equal(CurrentLiquidationStatus.InsufficientBuyDepth, item.Status);
        Assert.Equal(1, item.UnliquidatedQuantity);
        Assert.Null(item.NetLiquidationValue);
        Assert.False(result.OpenPerformance.IsFullyValued);
        Assert.Null(result.OpenPerformance.NetLiquidationValue);
        Assert.Null(result.OpenPerformance.UnrealizedProfit);
    }

    [Fact]
    public void Discloses_missing_market_evidence_without_inventing_open_value()
    {
        var result = calculator.Rebuild(Request(
            [Buy(101, itemId: 42, unitPrice: 100, quantity: 3, completedAtUtc: AsOfUtc.AddDays(-2))]));

        var item = Assert.Single(result.OpenPerformance.Items);
        Assert.Equal(CurrentLiquidationStatus.EvidenceMissing, item.Status);
        Assert.Equal(3, item.UnliquidatedQuantity);
        Assert.Equal(new Money(300), result.OpenPerformance.OpenAcquisitionBasis);
        Assert.Null(result.OpenPerformance.NetLiquidationValue);
    }

    [Fact]
    public void Rebuilds_identically_for_reordered_evidence_and_isolates_accounts()
    {
        AccountScopedCompletedTransaction[] transactions =
        [
            Sell(2, 102, itemId: 42, unitPrice: 300, quantity: 1, completedAtUtc: AsOfUtc.AddDays(-1)),
            Buy(1, 101, itemId: 42, unitPrice: 100, quantity: 1, completedAtUtc: AsOfUtc.AddDays(-2)),
            Buy(2, 101, itemId: 42, unitPrice: 200, quantity: 1, completedAtUtc: AsOfUtc.AddDays(-2)),
            Sell(1, 102, itemId: 42, unitPrice: 300, quantity: 1, completedAtUtc: AsOfUtc.AddDays(-1)),
        ];
        var evidence = new[] { Evidence(accountId: 2, itemId: 42, (1, 300)), Evidence(accountId: 1, itemId: 42, (1, 300)) };

        var first = calculator.Rebuild(Request(transactions, evidence));
        var second = calculator.Rebuild(Request(transactions.Reverse().ToArray(), evidence.Reverse().ToArray()));

        Assert.Equal(first.CoverageRealizedPerformance, second.CoverageRealizedPerformance);
        Assert.Equal(first.KnownBasisSaleAllocations, second.KnownBasisSaleAllocations);
        Assert.Equal(first.UnknownBasisSaleAllocations, second.UnknownBasisSaleAllocations);
        Assert.Equal(first.Windows, second.Windows);
        Assert.Equal(first.OpenPerformance.OpenQuantity, second.OpenPerformance.OpenQuantity);
        Assert.Equal(first.OpenPerformance.OpenAcquisitionBasis, second.OpenPerformance.OpenAcquisitionBasis);
        Assert.Equal(first.OpenPerformance.IsFullyValued, second.OpenPerformance.IsFullyValued);
        Assert.Equal(first.OpenPerformance.NetLiquidationValue, second.OpenPerformance.NetLiquidationValue);
        Assert.Equal(first.OpenPerformance.UnrealizedProfit, second.OpenPerformance.UnrealizedProfit);
        Assert.Equal(first.OpenPerformance.Items, second.OpenPerformance.Items);
        Assert.Equal(new Money(210), first.CoverageRealizedPerformance.NetProfit);
    }

    [Fact]
    public void Rejects_non_utc_coverage_duplicate_market_evidence_and_non_positive_buy_levels()
    {
        var transaction = Buy(101, itemId: 42, unitPrice: 100, quantity: 1, completedAtUtc: AsOfUtc.AddDays(-1));
        Assert.Throws<ArgumentException>(() => calculator.Rebuild(new PersonalPerformanceRequest(
            AsOfUtc,
            new PerformanceHistoryCoverage(AsOfUtc.AddDays(-10).ToOffset(TimeSpan.FromHours(1)), AsOfUtc),
            [transaction],
            [])));
        Assert.Throws<ArgumentException>(() => calculator.Rebuild(Request(
            [transaction],
            Evidence(itemId: 42, (1, 100)),
            Evidence(itemId: 42, (1, 100)))));
        Assert.Throws<ArgumentException>(() => calculator.Rebuild(Request(
            [transaction],
            new CurrentMarketLiquidationEvidence(1, new MarketListing(42, [new MarketOrderLevel(1, 1, 0)], []), AsOfUtc))));
    }

    [Fact]
    public void Exposes_no_floating_point_financial_contracts()
    {
        var publicTypes = new[]
        {
            typeof(PersonalPerformanceRequest),
            typeof(PersonalPerformanceRebuild),
            typeof(RealizedPerformance),
            typeof(OpenPerformance),
            typeof(OpenInventoryLiquidation),
        };

        Assert.All(
            publicTypes.SelectMany(type => type.GetProperties()).Select(property => property.PropertyType),
            type => Assert.DoesNotContain(type, new[] { typeof(float), typeof(double), typeof(decimal) }));
    }

    private static PersonalPerformanceRequest Request(
        IReadOnlyList<AccountScopedCompletedTransaction> transactions,
        params CurrentMarketLiquidationEvidence[] evidence) => new(
        AsOfUtc,
        new PerformanceHistoryCoverage(AsOfUtc.AddDays(-100), AsOfUtc),
        transactions,
        evidence);

    private static CurrentMarketLiquidationEvidence Evidence(
        int itemId,
        params (int Quantity, int UnitPrice)[] buys) => Evidence(accountId: 1, itemId, buys);

    private static CurrentMarketLiquidationEvidence Evidence(
        long accountId,
        int itemId,
        params (int Quantity, int UnitPrice)[] buys) => new(
        accountId,
        new MarketListing(
            itemId,
            buys.Select(buy => new MarketOrderLevel(1, buy.Quantity, buy.UnitPrice)).ToArray(),
            []),
        AsOfUtc);

    private static AccountScopedCompletedTransaction Buy(
        long transactionId,
        int itemId,
        int unitPrice,
        int quantity,
        DateTimeOffset completedAtUtc) => Buy(1, transactionId, itemId, unitPrice, quantity, completedAtUtc);

    private static AccountScopedCompletedTransaction Buy(
        long accountId,
        long transactionId,
        int itemId,
        int unitPrice,
        int quantity,
        DateTimeOffset completedAtUtc) => Transaction(
        accountId, transactionId, PersonalTradingPostSide.Buy, itemId, unitPrice, quantity, completedAtUtc);

    private static AccountScopedCompletedTransaction Sell(
        long transactionId,
        int itemId,
        int unitPrice,
        int quantity,
        DateTimeOffset completedAtUtc) => Sell(1, transactionId, itemId, unitPrice, quantity, completedAtUtc);

    private static AccountScopedCompletedTransaction Sell(
        long accountId,
        long transactionId,
        int itemId,
        int unitPrice,
        int quantity,
        DateTimeOffset completedAtUtc) => Transaction(
        accountId, transactionId, PersonalTradingPostSide.Sell, itemId, unitPrice, quantity, completedAtUtc);

    private static AccountScopedCompletedTransaction Transaction(
        long accountId,
        long transactionId,
        PersonalTradingPostSide side,
        int itemId,
        int unitPrice,
        int quantity,
        DateTimeOffset completedAtUtc) => new(
        accountId,
        new CompletedPersonalTradingPostTransaction(
            transactionId,
            side,
            itemId,
            unitPrice,
            quantity,
            completedAtUtc.AddHours(-1),
            completedAtUtc));
}

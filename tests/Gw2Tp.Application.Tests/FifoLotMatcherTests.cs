using Gw2Tp.Application.Accounting;
using Gw2Tp.Application.Persistence;
using Gw2Tp.Domain.Finance;
using Xunit;

namespace Gw2Tp.Application.Tests;

public sealed class FifoLotMatcherTests
{
    private static readonly DateTimeOffset BaselineUtc = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly FifoLotMatcher matcher = new();

    [Fact]
    public void Matches_a_complete_trade_with_exact_integer_copper_basis()
    {
        var result = matcher.Rebuild(
        [
            Buy(1, 101, 42, 125, 3, minutes: 0),
            Sell(1, 102, 42, 200, 3, minutes: 1),
        ]);

        var match = Assert.Single(result.Matches);
        Assert.Equal(1, result.PolicyVersion);
        Assert.Equal(101, match.BuyTransactionId);
        Assert.Equal(102, match.SellTransactionId);
        Assert.Equal(3, match.MatchedQuantity);
        Assert.Equal(new Money(375), match.AllocatedAcquisitionBasis);
        Assert.Empty(result.OpenLots);
        Assert.Empty(result.UnknownBasisSales);
    }

    [Fact]
    public void Leaves_the_unconsumed_quantity_and_basis_in_a_partial_lot()
    {
        var result = matcher.Rebuild(
        [
            Buy(1, 101, 42, 125, 5, minutes: 0),
            Sell(1, 102, 42, 200, 2, minutes: 1),
        ]);

        Assert.Equal(new Money(250), Assert.Single(result.Matches).AllocatedAcquisitionBasis);
        var lot = Assert.Single(result.OpenLots);
        Assert.Equal(5, lot.OriginalQuantity);
        Assert.Equal(3, lot.RemainingQuantity);
        Assert.Equal(new Money(625), lot.OriginalAcquisitionBasis);
        Assert.Equal(new Money(375), lot.RemainingAcquisitionBasis);
    }

    [Fact]
    public void Consumes_many_buy_lots_for_one_sale_in_fifo_order()
    {
        var result = matcher.Rebuild(
        [
            Buy(1, 101, 42, 100, 2, minutes: 0),
            Buy(1, 102, 42, 150, 3, minutes: 1),
            Sell(1, 103, 42, 300, 4, minutes: 2),
        ]);

        Assert.Equal([101L, 102L], result.Matches.Select(match => match.BuyTransactionId));
        Assert.Equal([2, 2], result.Matches.Select(match => match.MatchedQuantity));
        Assert.Equal([new Money(200), new Money(300)], result.Matches.Select(match => match.AllocatedAcquisitionBasis));
        var remaining = Assert.Single(result.OpenLots);
        Assert.Equal(102, remaining.BuyTransactionId);
        Assert.Equal(1, remaining.RemainingQuantity);
        Assert.Equal(new Money(150), remaining.RemainingAcquisitionBasis);
    }

    [Fact]
    public void Splits_one_buy_lot_across_many_sales()
    {
        var result = matcher.Rebuild(
        [
            Buy(1, 101, 42, 125, 5, minutes: 0),
            Sell(1, 102, 42, 200, 2, minutes: 1),
            Sell(1, 103, 42, 210, 3, minutes: 2),
        ]);

        Assert.Equal([102L, 103L], result.Matches.Select(match => match.SellTransactionId));
        Assert.All(result.Matches, match => Assert.Equal(101, match.BuyTransactionId));
        Assert.Equal([2, 3], result.Matches.Select(match => match.MatchedQuantity));
        Assert.Equal([new Money(250), new Money(375)], result.Matches.Select(match => match.AllocatedAcquisitionBasis));
        Assert.Empty(result.OpenLots);
    }

    [Fact]
    public void Isolates_interleaved_items_and_accounts()
    {
        var result = matcher.Rebuild(
        [
            Buy(2, 101, 42, 900, 1, minutes: 0),
            Buy(1, 101, 42, 100, 1, minutes: 0),
            Buy(1, 102, 43, 500, 1, minutes: 1),
            Sell(1, 103, 42, 200, 1, minutes: 2),
            Sell(1, 104, 43, 600, 1, minutes: 3),
            Sell(2, 102, 42, 1_000, 1, minutes: 4),
        ]);

        Assert.Equal(3, result.Matches.Count);
        Assert.Contains(result.Matches, match => match.AccountProfileId == 1 && match.ItemId == 42 && match.AllocatedAcquisitionBasis == new Money(100));
        Assert.Contains(result.Matches, match => match.AccountProfileId == 1 && match.ItemId == 43 && match.AllocatedAcquisitionBasis == new Money(500));
        Assert.Contains(result.Matches, match => match.AccountProfileId == 2 && match.ItemId == 42 && match.AllocatedAcquisitionBasis == new Money(900));
        Assert.Empty(result.OpenLots);
        Assert.Empty(result.UnknownBasisSales);
    }

    [Fact]
    public void Uses_external_transaction_id_to_break_equal_completion_timestamps()
    {
        var sameTime = BaselineUtc.AddMinutes(5);
        var result = matcher.Rebuild(
        [
            Transaction(1, 300, PersonalTradingPostSide.Buy, 42, 300, 1, sameTime, BaselineUtc.AddMinutes(-20)),
            Transaction(1, 200, PersonalTradingPostSide.Sell, 42, 500, 1, sameTime, BaselineUtc.AddMinutes(-10)),
            Transaction(1, 100, PersonalTradingPostSide.Buy, 42, 100, 1, sameTime, BaselineUtc),
        ]);

        var match = Assert.Single(result.Matches);
        Assert.Equal(100, match.BuyTransactionId);
        Assert.Equal(200, match.SellTransactionId);
        Assert.Equal(300, Assert.Single(result.OpenLots).BuyTransactionId);
        Assert.Equal("external transaction ID ascending", FifoAccountingPolicy.EqualTimestampTieBreaker);
    }

    [Fact]
    public void Keeps_missing_prior_acquisition_explicit_and_does_not_retroactively_match_it()
    {
        var result = matcher.Rebuild(
        [
            Sell(1, 101, 42, 200, 3, minutes: 0),
            Buy(1, 102, 42, 100, 2, minutes: 1),
        ]);

        Assert.Empty(result.Matches);
        var unknown = Assert.Single(result.UnknownBasisSales);
        Assert.Equal(101, unknown.SellTransactionId);
        Assert.Equal(3, unknown.UnmatchedQuantity);
        Assert.DoesNotContain(
            typeof(FifoUnknownBasisSale).GetProperties(),
            property => property.PropertyType == typeof(Money) || property.PropertyType == typeof(Money?));
        Assert.Equal(102, Assert.Single(result.OpenLots).BuyTransactionId);
    }

    [Fact]
    public void Splits_a_sale_between_known_and_unknown_basis()
    {
        var result = matcher.Rebuild(
        [
            Buy(1, 101, 42, 100, 2, minutes: 0),
            Sell(1, 102, 42, 200, 5, minutes: 1),
        ]);

        Assert.Equal(2, Assert.Single(result.Matches).MatchedQuantity);
        Assert.Equal(new Money(200), Assert.Single(result.Matches).AllocatedAcquisitionBasis);
        Assert.Equal(3, Assert.Single(result.UnknownBasisSales).UnmatchedQuantity);
        Assert.Empty(result.OpenLots);
    }

    [Fact]
    public void Rebuild_is_identical_for_repeated_and_reordered_source_evidence()
    {
        AccountScopedCompletedTransaction[] evidence =
        [
            Sell(1, 104, 42, 220, 2, minutes: 3),
            Buy(1, 101, 42, 100, 2, minutes: 0),
            Sell(1, 103, 42, 200, 1, minutes: 2),
            Buy(1, 102, 42, 150, 3, minutes: 1),
        ];

        var first = matcher.Rebuild(evidence);
        var second = matcher.Rebuild(evidence.Reverse());

        Assert.Equal(first.PolicyVersion, second.PolicyVersion);
        Assert.Equal(first.Matches, second.Matches);
        Assert.Equal(first.OpenLots, second.OpenLots);
        Assert.Equal(first.UnknownBasisSales, second.UnknownBasisSales);
    }

    [Fact]
    public void Preserves_checked_integer_copper_at_supported_transaction_boundaries()
    {
        var result = matcher.Rebuild(
        [
            Buy(1, 101, 42, int.MaxValue, int.MaxValue, minutes: 0),
        ]);

        var lot = Assert.Single(result.OpenLots);
        Assert.Equal(new Money(4_611_686_014_132_420_609), lot.OriginalAcquisitionBasis);
        Assert.Equal(lot.OriginalAcquisitionBasis, lot.RemainingAcquisitionBasis);
    }

    [Theory]
    [MemberData(nameof(InvalidTransactions))]
    public void Rejects_invalid_completed_transaction_evidence(AccountScopedCompletedTransaction invalid)
    {
        Assert.ThrowsAny<ArgumentException>(() => matcher.Rebuild([invalid]));
    }

    [Fact]
    public void Rejects_duplicate_transaction_ids_within_an_account_but_allows_them_across_accounts()
    {
        var duplicate = new[]
        {
            Buy(1, 101, 42, 100, 1, minutes: 0),
            Buy(1, 101, 43, 100, 1, minutes: 1),
        };

        Assert.Throws<ArgumentException>(() => matcher.Rebuild(duplicate));

        var isolated = matcher.Rebuild(
        [
            Buy(1, 101, 42, 100, 1, minutes: 0),
            Buy(2, 101, 42, 200, 1, minutes: 0),
        ]);
        Assert.Equal(2, isolated.OpenLots.Count);
    }

    public static TheoryData<AccountScopedCompletedTransaction> InvalidTransactions() => new()
    {
        Transaction(0, 101, PersonalTradingPostSide.Buy, 42, 100, 1, BaselineUtc, BaselineUtc),
        Transaction(1, 0, PersonalTradingPostSide.Buy, 42, 100, 1, BaselineUtc, BaselineUtc),
        Transaction(1, 101, (PersonalTradingPostSide)0, 42, 100, 1, BaselineUtc, BaselineUtc),
        Transaction(1, 101, PersonalTradingPostSide.Buy, 0, 100, 1, BaselineUtc, BaselineUtc),
        Transaction(1, 101, PersonalTradingPostSide.Buy, 42, -1, 1, BaselineUtc, BaselineUtc),
        Transaction(1, 101, PersonalTradingPostSide.Buy, 42, 100, 0, BaselineUtc, BaselineUtc),
        Transaction(1, 101, PersonalTradingPostSide.Buy, 42, 100, 1, BaselineUtc.ToOffset(TimeSpan.FromHours(1)), BaselineUtc),
        Transaction(1, 101, PersonalTradingPostSide.Buy, 42, 100, 1, BaselineUtc, BaselineUtc.ToOffset(TimeSpan.FromHours(1))),
    };

    private static AccountScopedCompletedTransaction Buy(
        long accountProfileId,
        long transactionId,
        int itemId,
        int unitPriceInCopper,
        int quantity,
        int minutes) => Transaction(
            accountProfileId,
            transactionId,
            PersonalTradingPostSide.Buy,
            itemId,
            unitPriceInCopper,
            quantity,
            BaselineUtc.AddMinutes(minutes),
            BaselineUtc.AddMinutes(minutes - 10));

    private static AccountScopedCompletedTransaction Sell(
        long accountProfileId,
        long transactionId,
        int itemId,
        int unitPriceInCopper,
        int quantity,
        int minutes) => Transaction(
            accountProfileId,
            transactionId,
            PersonalTradingPostSide.Sell,
            itemId,
            unitPriceInCopper,
            quantity,
            BaselineUtc.AddMinutes(minutes),
            BaselineUtc.AddMinutes(minutes - 10));

    private static AccountScopedCompletedTransaction Transaction(
        long accountProfileId,
        long transactionId,
        PersonalTradingPostSide side,
        int itemId,
        int unitPriceInCopper,
        int quantity,
        DateTimeOffset completedAtUtc,
        DateTimeOffset createdAtUtc) => new(
            accountProfileId,
            new CompletedPersonalTradingPostTransaction(
                transactionId,
                side,
                itemId,
                unitPriceInCopper,
                quantity,
                createdAtUtc,
                completedAtUtc));
}

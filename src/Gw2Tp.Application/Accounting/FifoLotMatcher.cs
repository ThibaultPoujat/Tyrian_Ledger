using Gw2Tp.Application.Persistence;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.Accounting;

/// <summary>
/// Reconstructs known acquisition lots from completed personal Trading Post events.
/// Events are ordered by completion timestamp and then by ascending external
/// transaction ID. Account and item boundaries are never crossed.
/// </summary>
public sealed class FifoLotMatcher
{
    public FifoAccountingRebuild Rebuild(
        IEnumerable<AccountScopedCompletedTransaction> completedTransactions)
    {
        ArgumentNullException.ThrowIfNull(completedTransactions);

        var transactions = completedTransactions.ToArray();
        Validate(transactions);

        var orderedTransactions = transactions
            .OrderBy(item => item.Transaction.CompletedAtUtc)
            .ThenBy(item => item.Transaction.ExternalTransactionId)
            .ThenBy(item => item.AccountProfileId)
            .ThenBy(item => item.Transaction.ItemId)
            .ToArray();
        var openLots = new Dictionary<AccountItemKey, Queue<MutableLot>>();
        var matches = new List<FifoLotMatch>();
        var unknownBasisSales = new List<FifoUnknownBasisSale>();

        foreach (var scopedTransaction in orderedTransactions)
        {
            var transaction = scopedTransaction.Transaction;
            var key = new AccountItemKey(scopedTransaction.AccountProfileId, transaction.ItemId);
            if (!openLots.TryGetValue(key, out var itemLots))
            {
                itemLots = new Queue<MutableLot>();
                openLots.Add(key, itemLots);
            }

            if (transaction.Side is PersonalTradingPostSide.Buy)
            {
                itemLots.Enqueue(new MutableLot(scopedTransaction.AccountProfileId, transaction));
                continue;
            }

            var quantityToMatch = transaction.Quantity;
            while (quantityToMatch > 0 && itemLots.Count > 0)
            {
                var lot = itemLots.Peek();
                var matchedQuantity = Math.Min(quantityToMatch, lot.RemainingQuantity);
                matches.Add(new FifoLotMatch(
                    scopedTransaction.AccountProfileId,
                    transaction.ItemId,
                    transaction.ExternalTransactionId,
                    lot.Transaction.ExternalTransactionId,
                    matchedQuantity,
                    CalculateBasis(lot.Transaction.UnitPriceInCopper, matchedQuantity),
                    lot.Transaction.CompletedAtUtc,
                    transaction.CompletedAtUtc));

                lot.RemainingQuantity -= matchedQuantity;
                quantityToMatch -= matchedQuantity;
                if (lot.RemainingQuantity == 0)
                {
                    itemLots.Dequeue();
                }
            }

            if (quantityToMatch > 0)
            {
                unknownBasisSales.Add(new FifoUnknownBasisSale(
                    scopedTransaction.AccountProfileId,
                    transaction.ItemId,
                    transaction.ExternalTransactionId,
                    quantityToMatch,
                    transaction.CompletedAtUtc));
            }
        }

        var remainingLots = openLots.Values
            .SelectMany(lots => lots)
            .OrderBy(lot => lot.AccountProfileId)
            .ThenBy(lot => lot.Transaction.ItemId)
            .ThenBy(lot => lot.Transaction.CompletedAtUtc)
            .ThenBy(lot => lot.Transaction.ExternalTransactionId)
            .Select(lot => lot.ToInventoryLot())
            .ToArray();

        return new FifoAccountingRebuild(
            FifoAccountingPolicy.Version,
            Array.AsReadOnly(remainingLots),
            matches.AsReadOnly(),
            unknownBasisSales.AsReadOnly());
    }

    private static void Validate(IReadOnlyList<AccountScopedCompletedTransaction> transactions)
    {
        var transactionIds = new HashSet<(long AccountProfileId, long ExternalTransactionId)>();
        foreach (var scopedTransaction in transactions)
        {
            if (scopedTransaction is null)
            {
                throw new ArgumentException("Completed transactions cannot contain null entries.", nameof(transactions));
            }

            var transaction = scopedTransaction.Transaction;
            if (scopedTransaction.AccountProfileId <= 0 || transaction is null ||
                transaction.ExternalTransactionId <= 0 || transaction.ItemId <= 0 ||
                transaction.UnitPriceInCopper < 0 || transaction.Quantity <= 0)
            {
                throw new ArgumentException(
                    "A completed transaction contains an invalid account, identifier, copper value, or quantity.",
                    nameof(transactions));
            }

            if (transaction.Side is not PersonalTradingPostSide.Buy and not PersonalTradingPostSide.Sell)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(transactions),
                    transaction.Side,
                    "A completed transaction side must be buy or sell.");
            }

            if (transaction.CreatedAtUtc.Offset != TimeSpan.Zero ||
                transaction.CompletedAtUtc.Offset != TimeSpan.Zero)
            {
                throw new ArgumentException("Completed transaction timestamps must be UTC.", nameof(transactions));
            }

            if (!transactionIds.Add((scopedTransaction.AccountProfileId, transaction.ExternalTransactionId)))
            {
                throw new ArgumentException(
                    "A completed transaction ID must be unique within its account scope.",
                    nameof(transactions));
            }
        }
    }

    private static Money CalculateBasis(int unitPriceInCopper, int quantity) =>
        new(checked((long)unitPriceInCopper * quantity));

    private readonly record struct AccountItemKey(long AccountProfileId, int ItemId);

    private sealed class MutableLot(
        long accountProfileId,
        CompletedPersonalTradingPostTransaction transaction)
    {
        public long AccountProfileId { get; } = accountProfileId;

        public CompletedPersonalTradingPostTransaction Transaction { get; } = transaction;

        public int RemainingQuantity { get; set; } = transaction.Quantity;

        public FifoInventoryLot ToInventoryLot() => new(
            AccountProfileId,
            Transaction.ItemId,
            Transaction.ExternalTransactionId,
            Transaction.Quantity,
            RemainingQuantity,
            new Money(Transaction.UnitPriceInCopper),
            CalculateBasis(Transaction.UnitPriceInCopper, Transaction.Quantity),
            CalculateBasis(Transaction.UnitPriceInCopper, RemainingQuantity),
            Transaction.CreatedAtUtc,
            Transaction.CompletedAtUtc);
    }
}

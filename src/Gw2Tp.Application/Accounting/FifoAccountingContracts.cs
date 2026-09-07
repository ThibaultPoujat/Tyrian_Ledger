using Gw2Tp.Application.Persistence;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.Accounting;

/// <summary>
/// The accepted FIFO accounting policy. Increment this version if ordering or
/// matching semantics change in a way that can alter a rebuild result.
/// </summary>
public static class FifoAccountingPolicy
{
    public const int Version = 1;

    public const string EqualTimestampTieBreaker = "external transaction ID ascending";
}

/// <summary>
/// One authoritative completed transaction together with its local account scope.
/// Current Trading Post orders intentionally cannot be supplied through this contract.
/// </summary>
public sealed record AccountScopedCompletedTransaction(
    long AccountProfileId,
    CompletedPersonalTradingPostTransaction Transaction);

/// <summary>
/// The known-basis quantity left from one completed buy after FIFO matching.
/// </summary>
public sealed record FifoInventoryLot(
    long AccountProfileId,
    int ItemId,
    long BuyTransactionId,
    int OriginalQuantity,
    int RemainingQuantity,
    Money UnitAcquisitionPrice,
    Money OriginalAcquisitionBasis,
    Money RemainingAcquisitionBasis,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CompletedAtUtc);

/// <summary>
/// A known-basis allocation from a completed buy lot to a completed sale.
/// Sale fees and realized profit are deliberately calculated by the downstream
/// accounting policy rather than duplicated by FIFO matching.
/// </summary>
public sealed record FifoLotMatch(
    long AccountProfileId,
    int ItemId,
    long SellTransactionId,
    long BuyTransactionId,
    int MatchedQuantity,
    Money AllocatedAcquisitionBasis,
    DateTimeOffset BuyCompletedAtUtc,
    DateTimeOffset SellCompletedAtUtc);

/// <summary>
/// A completed sale quantity for which the retained transaction history contains
/// no prior completed acquisition. No zero-valued basis is attached to this state.
/// </summary>
public sealed record FifoUnknownBasisSale(
    long AccountProfileId,
    int ItemId,
    long SellTransactionId,
    int UnmatchedQuantity,
    DateTimeOffset SellCompletedAtUtc);

/// <summary>
/// A deterministic, rebuildable FIFO projection from completed transaction evidence.
/// </summary>
public sealed record FifoAccountingRebuild(
    int PolicyVersion,
    IReadOnlyList<FifoInventoryLot> OpenLots,
    IReadOnlyList<FifoLotMatch> Matches,
    IReadOnlyList<FifoUnknownBasisSale> UnknownBasisSales);

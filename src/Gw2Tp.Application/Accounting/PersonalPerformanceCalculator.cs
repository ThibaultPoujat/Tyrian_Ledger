using System.Numerics;
using Gw2Tp.Analytics.Finance;
using Gw2Tp.Analytics.OrderBooks;
using Gw2Tp.Application.Finance;
using Gw2Tp.Application.Persistence;
using Gw2Tp.Domain.Finance;

namespace Gw2Tp.Application.Accounting;

/// <summary>
/// Rebuilds personal realized and unrealized performance from explicit retained
/// transaction and market evidence. It deliberately owns no clock, gateway, or
/// persistence dependency.
/// </summary>
public sealed class PersonalPerformanceCalculator
{
    private readonly FifoLotMatcher fifoLotMatcher = new();
    private readonly FlipProfitCalculator saleCalculator = new(Gw2TradingPostFeePolicy.Create());
    private readonly OrderBookExecutionSimulator orderBookSimulator = new();

    public PersonalPerformanceRebuild Rebuild(PersonalPerformanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var fifo = fifoLotMatcher.Rebuild(request.CompletedTransactions);
        var transactionsByKey = request.CompletedTransactions.ToDictionary(
            scoped => new AccountTransactionKey(scoped.AccountProfileId, scoped.Transaction.ExternalTransactionId));
        var (knownAllocations, unknownAllocations) = BuildRealizedAllocations(fifo, transactionsByKey);
        var coverageRealized = Aggregate(knownAllocations);
        var windows = BuildWindows(request, knownAllocations, unknownAllocations);
        var openPerformance = BuildOpenPerformance(fifo.OpenLots, request.CurrentMarketEvidence);

        return new PersonalPerformanceRebuild(
            fifo.PolicyVersion,
            request.Coverage,
            Gw2TradingPostFeePolicy.IsFractionalCopperRoundingExternallyVerified,
            coverageRealized,
            knownAllocations.AsReadOnly(),
            unknownAllocations.AsReadOnly(),
            windows.AsReadOnly(),
            openPerformance);
    }

    private static void ValidateRequest(PersonalPerformanceRequest request)
    {
        if (request.AsOfUtc.Offset != TimeSpan.Zero || request.Coverage is null ||
            request.CompletedTransactions is null || request.CurrentMarketEvidence is null)
        {
            throw new ArgumentException("Performance evidence and timestamps must be present and UTC.", nameof(request));
        }

        if (request.Coverage.StartUtc.Offset != TimeSpan.Zero || request.Coverage.EndUtc.Offset != TimeSpan.Zero ||
            request.Coverage.StartUtc > request.Coverage.EndUtc || request.Coverage.EndUtc > request.AsOfUtc)
        {
            throw new ArgumentException("Coverage must be an ordered UTC interval ending no later than the as-of time.", nameof(request));
        }

        if (request.CompletedTransactions.Any(scoped => scoped is null || scoped.Transaction is null ||
                                                       scoped.Transaction.CompletedAtUtc < request.Coverage.StartUtc ||
                                                       scoped.Transaction.CompletedAtUtc > request.Coverage.EndUtc))
        {
            throw new ArgumentException("Completed transaction evidence must fall inside the declared coverage interval.", nameof(request));
        }

        var evidenceKeys = new HashSet<AccountItemKey>();
        foreach (var evidence in request.CurrentMarketEvidence)
        {
            if (evidence is null || evidence.AccountProfileId <= 0 || evidence.Listing is null ||
                evidence.Listing.ItemId <= 0 || evidence.ObservedAtUtc.Offset != TimeSpan.Zero ||
                evidence.ObservedAtUtc > request.AsOfUtc ||
                evidence.Listing.Buys is null || evidence.Listing.Sells is null ||
                !evidenceKeys.Add(new AccountItemKey(evidence.AccountProfileId, evidence.Listing.ItemId)) ||
                evidence.Listing.Buys.Any(level => level is null || level.Listings <= 0 || level.Quantity <= 0 || level.UnitPriceInCopper <= 0))
            {
                throw new ArgumentException("Current market evidence must contain one valid non-future UTC buy book per account/item.", nameof(request));
            }
        }
    }

    private (List<KnownBasisRealizedSaleAllocation> Known, List<UnknownBasisRealizedSaleAllocation> Unknown)
        BuildRealizedAllocations(
            FifoAccountingRebuild fifo,
            IReadOnlyDictionary<AccountTransactionKey, AccountScopedCompletedTransaction> transactionsByKey)
    {
        var matchesBySale = fifo.Matches
            .GroupBy(match => new AccountTransactionKey(match.AccountProfileId, match.SellTransactionId))
            .ToDictionary(group => group.Key, group => group
                .OrderBy(match => match.BuyCompletedAtUtc)
                .ThenBy(match => match.BuyTransactionId)
                .ToArray());
        var unknownBySale = fifo.UnknownBasisSales.ToDictionary(
            sale => new AccountTransactionKey(sale.AccountProfileId, sale.SellTransactionId));
        var known = new List<KnownBasisRealizedSaleAllocation>();
        var unknown = new List<UnknownBasisRealizedSaleAllocation>();

        foreach (var scopedSale in transactionsByKey.Values
                     .Where(scoped => scoped.Transaction.Side == PersonalTradingPostSide.Sell)
                     .OrderBy(scoped => scoped.Transaction.CompletedAtUtc)
                     .ThenBy(scoped => scoped.Transaction.ExternalTransactionId)
                     .ThenBy(scoped => scoped.AccountProfileId))
        {
            var key = new AccountTransactionKey(scopedSale.AccountProfileId, scopedSale.Transaction.ExternalTransactionId);
            var matches = matchesBySale.GetValueOrDefault(key, []);
            var unknownSale = unknownBySale.GetValueOrDefault(key);
            var fragments = matches
                .Select(match => SaleFragment.Known(match, match.MatchedQuantity))
                .ToList();
            if (unknownSale is not null)
            {
                fragments.Add(SaleFragment.Unknown(unknownSale, unknownSale.UnmatchedQuantity));
            }

            var allocatedQuantity = fragments.Sum(fragment => (long)fragment.Quantity);
            if (allocatedQuantity != scopedSale.Transaction.Quantity)
            {
                throw new InvalidOperationException("FIFO sale allocations must reconcile to the completed sale quantity.");
            }

            var grossSale = TotalValue(scopedSale.Transaction.UnitPriceInCopper, scopedSale.Transaction.Quantity);
            var wholeSale = saleCalculator.Calculate(Money.Zero, grossSale);
            var listingFees = AllocateFee(wholeSale.ListingFee, fragments);
            var exchangeFees = AllocateFee(wholeSale.ExchangeFee, fragments);

            for (var index = 0; index < fragments.Count; index++)
            {
                var fragment = fragments[index];
                var fragmentGrossSale = TotalValue(scopedSale.Transaction.UnitPriceInCopper, fragment.Quantity);
                var netSale = fragmentGrossSale - listingFees[index] - exchangeFees[index];
                if (fragment.Match is not null)
                {
                    var profit = netSale - fragment.Match.AllocatedAcquisitionBasis;
                    known.Add(new KnownBasisRealizedSaleAllocation(
                        fragment.Match,
                        fragmentGrossSale,
                        listingFees[index],
                        exchangeFees[index],
                        netSale,
                        profit,
                        CreateRoi(profit, FullUpFrontCost(fragment.Match.AllocatedAcquisitionBasis, listingFees[index]))));
                    continue;
                }

                unknown.Add(new UnknownBasisRealizedSaleAllocation(
                    fragment.UnknownSale!,
                    fragmentGrossSale,
                    listingFees[index],
                    exchangeFees[index],
                    netSale));
            }

            if (Sum(known.Where(allocation => allocation.Match.AccountProfileId == scopedSale.AccountProfileId &&
                                              allocation.Match.SellTransactionId == scopedSale.Transaction.ExternalTransactionId)
                    .Select(allocation => allocation.ListingFee)) +
                Sum(unknown.Where(allocation => allocation.Sale.AccountProfileId == scopedSale.AccountProfileId &&
                                                 allocation.Sale.SellTransactionId == scopedSale.Transaction.ExternalTransactionId)
                    .Select(allocation => allocation.ListingFee)) != wholeSale.ListingFee ||
                Sum(known.Where(allocation => allocation.Match.AccountProfileId == scopedSale.AccountProfileId &&
                                              allocation.Match.SellTransactionId == scopedSale.Transaction.ExternalTransactionId)
                    .Select(allocation => allocation.ExchangeFee)) +
                Sum(unknown.Where(allocation => allocation.Sale.AccountProfileId == scopedSale.AccountProfileId &&
                                                 allocation.Sale.SellTransactionId == scopedSale.Transaction.ExternalTransactionId)
                    .Select(allocation => allocation.ExchangeFee)) != wholeSale.ExchangeFee)
            {
                throw new InvalidOperationException("Allocated fees must reconcile to the canonical whole-sale fees.");
            }
        }

        return (known, unknown);
    }

    private static List<Money> AllocateFee(Money fee, IReadOnlyList<SaleFragment> fragments)
    {
        var totalQuantity = fragments.Sum(fragment => (long)fragment.Quantity);
        var allocations = new List<Money>(fragments.Count);
        var allocatedCopper = 0L;
        foreach (var fragment in fragments)
        {
            var allocation = (long)(new BigInteger(fee.Copper) * fragment.Quantity / totalQuantity);
            allocations.Add(new Money(allocation));
            allocatedCopper = checked(allocatedCopper + allocation);
        }

        var remainder = checked(fee.Copper - allocatedCopper);
        for (var index = 0; index < remainder; index++)
        {
            allocations[index] = new Money(checked(allocations[index].Copper + 1));
        }

        return allocations;
    }

    private static List<RealizedPerformanceWindowResult> BuildWindows(
        PersonalPerformanceRequest request,
        IReadOnlyList<KnownBasisRealizedSaleAllocation> knownAllocations,
        IReadOnlyList<UnknownBasisRealizedSaleAllocation> unknownAllocations)
    {
        var windows = new List<RealizedPerformanceWindowResult>();
        foreach (var window in new[]
                 {
                     RealizedPerformanceWindow.SevenDays,
                     RealizedPerformanceWindow.ThirtyDays,
                     RealizedPerformanceWindow.NinetyDays,
                 })
        {
            var start = request.AsOfUtc.AddDays(-(int)window);
            var isSupported = request.Coverage.StartUtc <= start && request.Coverage.EndUtc >= request.AsOfUtc;
            var known = knownAllocations.Where(allocation => IsWithin(allocation.Match.SellCompletedAtUtc, start, request.AsOfUtc)).ToArray();
            var unknownQuantity = checked((int)unknownAllocations
                .Where(allocation => IsWithin(allocation.Sale.SellCompletedAtUtc, start, request.AsOfUtc))
                .Sum(allocation => (long)allocation.Sale.UnmatchedQuantity));
            windows.Add(new RealizedPerformanceWindowResult(
                window,
                start,
                request.AsOfUtc,
                isSupported ? RealizedPerformanceWindowStatus.Supported : RealizedPerformanceWindowStatus.InsufficientCoverage,
                isSupported ? Aggregate(known) : null,
                unknownQuantity));
        }

        return windows;
    }

    private OpenPerformance BuildOpenPerformance(
        IReadOnlyList<FifoInventoryLot> openLots,
        IReadOnlyList<CurrentMarketLiquidationEvidence> marketEvidence)
    {
        var evidenceByKey = marketEvidence.ToDictionary(evidence => new AccountItemKey(evidence.AccountProfileId, evidence.Listing.ItemId));
        var items = new List<OpenInventoryLiquidation>();
        foreach (var group in openLots.GroupBy(lot => new AccountItemKey(lot.AccountProfileId, lot.ItemId))
                     .OrderBy(group => group.Key.AccountProfileId)
                     .ThenBy(group => group.Key.ItemId))
        {
            var quantity = checked((int)group.Sum(lot => (long)lot.RemainingQuantity));
            var basis = Sum(group.Select(lot => lot.RemainingAcquisitionBasis));
            if (!evidenceByKey.TryGetValue(group.Key, out var evidence))
            {
                items.Add(new OpenInventoryLiquidation(
                    group.Key.AccountProfileId, group.Key.ItemId, quantity, basis,
                    CurrentLiquidationStatus.EvidenceMissing, null, quantity,
                    null, null, null, null, null, null));
                continue;
            }

            var scenario = orderBookSimulator.SimulateLiquidation(
                evidence.Listing.Buys.Select(level => new OrderBookLevel(level.Quantity, new Money(level.UnitPriceInCopper))).ToArray(),
                quantity);
            if (!scenario.IsFullyFilled)
            {
                items.Add(new OpenInventoryLiquidation(
                    group.Key.AccountProfileId, group.Key.ItemId, quantity, basis,
                    CurrentLiquidationStatus.InsufficientBuyDepth, evidence.ObservedAtUtc, scenario.RemainingQuantity,
                    null, null, null, null, null, null));
                continue;
            }

            var liquidation = saleCalculator.Calculate(basis, scenario.TotalValue);
            items.Add(new OpenInventoryLiquidation(
                group.Key.AccountProfileId, group.Key.ItemId, quantity, basis,
                CurrentLiquidationStatus.FullyValued, evidence.ObservedAtUtc, 0,
                liquidation.GrossSaleValue, liquidation.ListingFee, liquidation.ExchangeFee,
                liquidation.NetSaleProceeds, liquidation.NetProfit,
                CreateRoi(liquidation.NetProfit, FullUpFrontCost(basis, liquidation.ListingFee))));
        }

        var openQuantity = checked((int)openLots.Sum(lot => (long)lot.RemainingQuantity));
        var openBasis = Sum(openLots.Select(lot => lot.RemainingAcquisitionBasis));
        var isFullyValued = items.All(item => item.Status == CurrentLiquidationStatus.FullyValued);
        Money? liquidationValue = isFullyValued ? Sum(items.Select(item => item.NetLiquidationValue!.Value)) : null;
        Money? unrealizedProfit = liquidationValue is { } netValue ? netValue - openBasis : null;
        Money? listingFees = isFullyValued ? Sum(items.Select(item => item.ListingFee!.Value)) : null;
        return new OpenPerformance(
            openQuantity,
            openBasis,
            isFullyValued,
            liquidationValue,
            unrealizedProfit,
            unrealizedProfit is { } profit ? CreateRoi(profit, FullUpFrontCost(openBasis, listingFees!.Value)) : null,
            items.AsReadOnly());
    }

    private static RealizedPerformance Aggregate(IEnumerable<KnownBasisRealizedSaleAllocation> allocations)
    {
        var values = allocations.ToArray();
        var acquisitionBasis = Sum(values.Select(allocation => allocation.Match.AllocatedAcquisitionBasis));
        var netProfit = Sum(values.Select(allocation => allocation.NetProfit));
        return new RealizedPerformance(
            checked((int)values.Sum(allocation => (long)allocation.Match.MatchedQuantity)),
            Sum(values.Select(allocation => allocation.GrossSale)),
            Sum(values.Select(allocation => allocation.ListingFee)),
            Sum(values.Select(allocation => allocation.ExchangeFee)),
            Sum(values.Select(allocation => allocation.NetSaleProceeds)),
            acquisitionBasis,
            netProfit,
            CreateRoi(netProfit, FullUpFrontCost(acquisitionBasis, Sum(values.Select(allocation => allocation.ListingFee)))));
    }

    private static bool IsWithin(DateTimeOffset timestamp, DateTimeOffset start, DateTimeOffset end) =>
        timestamp >= start && timestamp < end;

    private static Money TotalValue(int unitPriceInCopper, int quantity) =>
        new(checked((long)unitPriceInCopper * quantity));

    private static ExactRoi? CreateRoi(Money profit, Money cost) =>
        cost.Copper > 0 ? new ExactRoi(profit, cost) : null;

    // The listing fee is non-refundable and therefore part of the full up-front
    // cost denominator used consistently by current and historical ROI.
    private static Money FullUpFrontCost(Money acquisitionBasis, Money listingFee) => acquisitionBasis + listingFee;

    private static Money Sum(IEnumerable<Money> values)
    {
        var total = Money.Zero;
        foreach (var value in values)
        {
            total += value;
        }

        return total;
    }

    private readonly record struct AccountTransactionKey(long AccountProfileId, long TransactionId);

    private readonly record struct AccountItemKey(long AccountProfileId, int ItemId);

    private sealed record SaleFragment(
        int Quantity,
        FifoLotMatch? Match,
        FifoUnknownBasisSale? UnknownSale)
    {
        public static SaleFragment Known(FifoLotMatch match, int quantity) => new(quantity, match, null);

        public static SaleFragment Unknown(FifoUnknownBasisSale sale, int quantity) => new(quantity, null, sale);
    }
}

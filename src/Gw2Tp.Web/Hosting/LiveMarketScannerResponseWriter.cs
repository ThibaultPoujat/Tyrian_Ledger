using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.MarketScanning;
using Gw2Tp.Domain.Finance;
using Microsoft.AspNetCore.Http;

namespace Gw2Tp.Web.Hosting;

/// <summary>
/// Converts scanner results to browser-safe JSON. Copper is serialized as text
/// so browser consumers cannot silently lose 64-bit integer precision.
/// </summary>
internal static class LiveMarketScannerResponseWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    internal static bool TryReadSettings(IQueryCollection query, out LiveMarketScannerSettings settings)
    {
        var defaults = LiveMarketScannerSettings.Default;
        if (!TryReadInt(query, "minimumRoiBasisPoints", defaults.MinimumRoiBasisPoints, out var minimumRoiBasisPoints) ||
            !TryReadLong(query, "minimumNetProfitCopper", defaults.MinimumNetProfit.Copper, out var minimumNetProfitCopper) ||
            !TryReadInt(query, "bidIncrementCopper", defaults.BidIncrementCopper, out var bidIncrementCopper) ||
            !TryReadInt(query, "listUndercutCopper", defaults.ListUndercutCopper, out var listUndercutCopper) ||
            !TryReadInt(query, "intendedQuantity", defaults.IntendedQuantity, out var intendedQuantity))
        {
            settings = defaults;
            return false;
        }

        settings = new LiveMarketScannerSettings(
            minimumRoiBasisPoints,
            new Money(minimumNetProfitCopper),
            bidIncrementCopper,
            listUndercutCopper,
            intendedQuantity);
        try
        {
            settings.Validate();
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    internal static Task WriteInvalidSettingsAsync(HttpContext context)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsync("{\"error\":\"invalid_scanner_settings\"}");
    }

    internal static Task WriteAsync(HttpContext context, LiveMarketScannerResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsync(JsonSerializer.Serialize(ToResponse(result), SerializerOptions));
    }

    private static LiveMarketScannerResponse ToResponse(LiveMarketScannerResult result) => new(
        result.State,
        result.ErrorCategory,
        result.ObservedAtUtc,
        new ScannerSettingsResponse(
            result.Settings.MinimumRoiBasisPoints,
            MoneyResponse.From(result.Settings.MinimumNetProfit),
            result.Settings.BidIncrementCopper,
            result.Settings.ListUndercutCopper,
            result.Settings.IntendedQuantity),
        result.IsFeeRoundingExternallyVerified,
        result.QualifyingCandidateCount,
        result.IsTruncated,
        result.Candidates.Select(candidate => new ScannerCandidateResponse(
            candidate.Item.ItemId,
            candidate.Item.Name,
            new ScannerOrderSummaryResponse(candidate.BestBuy.Quantity, MoneyResponse.From(new Money(candidate.BestBuy.UnitPriceInCopper))),
            new ScannerOrderSummaryResponse(candidate.LowestSell.Quantity, MoneyResponse.From(new Money(candidate.LowestSell.UnitPriceInCopper))),
            MoneyResponse.From(candidate.PlannedBid),
            MoneyResponse.From(candidate.PlannedListPrice),
            MoneyResponse.From(candidate.ProfitScenario.ListingFee),
            MoneyResponse.From(candidate.ProfitScenario.ExchangeFee),
            MoneyResponse.From(candidate.ProfitScenario.NetSaleProceeds),
            MoneyResponse.From(candidate.ProfitScenario.NetProfit),
            MoneyResponse.From(candidate.TotalCost),
            new ScannerExactRoiResponse(
                MoneyResponse.From(candidate.ModeledRoi.Profit),
                MoneyResponse.From(candidate.ModeledRoi.TotalCost),
                FormatRoiPercent(candidate.ModeledRoi.Profit.Copper, candidate.ModeledRoi.TotalCost.Copper)),
            MoneyResponse.From(candidate.MaximumBid),
            candidate.InclusionReasons.ToArray(),
            ToLiquidityResponse(candidate.Liquidity))).ToArray(),
        result.Exclusions.Select(exclusion => new ScannerExclusionResponse(exclusion.Reason, exclusion.Count)).ToArray());

    private static ScannerLiquidityResponse ToLiquidityResponse(LiveMarketScannerLiquidityEvidence liquidity) => new(
        QuantityResponse.From(liquidity.TotalBuyQuantity),
        QuantityResponse.From(liquidity.TotalSellQuantity),
        QuantityResponse.From(liquidity.NearBestBuyQuantity),
        QuantityResponse.From(liquidity.NearBestSellQuantity),
        QuantityResponse.From(liquidity.NearBestBuyListings),
        QuantityResponse.From(liquidity.NearBestSellListings),
        liquidity.BuyNextLevelGap is { } buyGap ? MoneyResponse.From(buyGap) : null,
        liquidity.SellNextLevelGap is { } sellGap ? MoneyResponse.From(sellGap) : null,
        liquidity.HasBuyPriceCliff,
        liquidity.HasSellPriceCliff,
        ToExecutionResponse(liquidity.Acquisition),
        ToExecutionResponse(liquidity.Liquidation),
        liquidity.ParticipationCapQuantity,
        liquidity.Reasons.ToArray(),
        liquidity.TopBuyLevels.Select(ToOrderBookLevelResponse).ToArray(),
        liquidity.TopSellLevels.Select(ToOrderBookLevelResponse).ToArray());

    private static ScannerOrderBookLevelResponse ToOrderBookLevelResponse(MarketOrderLevel level) => new(
        level.Listings,
        level.Quantity,
        MoneyResponse.From(new Money(level.UnitPriceInCopper)));

    private static ScannerExecutionResponse ToExecutionResponse(Gw2Tp.Analytics.OrderBooks.OrderBookExecutionScenario scenario) => new(
        scenario.RequestedQuantity,
        scenario.FilledQuantity,
        scenario.RemainingQuantity,
        scenario.IsFullyFilled,
        MoneyResponse.From(scenario.TotalValue),
        MoneyResponse.From(scenario.PriceImpact));

    private static bool TryReadInt(IQueryCollection query, string key, int fallback, out int value)
    {
        if (!query.TryGetValue(key, out var raw))
        {
            value = fallback;
            return true;
        }

        if (raw.Count != 1 || string.IsNullOrWhiteSpace(raw[0]))
        {
            value = fallback;
            return false;
        }

        return int.TryParse(raw[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadLong(IQueryCollection query, string key, long fallback, out long value)
    {
        if (!query.TryGetValue(key, out var raw))
        {
            value = fallback;
            return true;
        }

        if (raw.Count != 1 || string.IsNullOrWhiteSpace(raw[0]))
        {
            value = fallback;
            return false;
        }

        return long.TryParse(raw[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private sealed record MoneyResponse(string Copper)
    {
        public static MoneyResponse From(Money money) => new(money.Copper.ToString(CultureInfo.InvariantCulture));
    }

    private sealed record QuantityResponse(string Value)
    {
        public static QuantityResponse From(long value) => new(value.ToString(CultureInfo.InvariantCulture));
    }

    private sealed record ScannerSettingsResponse(
        int MinimumRoiBasisPoints,
        MoneyResponse MinimumNetProfit,
        int BidIncrementCopper,
        int ListUndercutCopper,
        int IntendedQuantity);

    private sealed record ScannerOrderSummaryResponse(int Quantity, MoneyResponse UnitPrice);

    private static string FormatRoiPercent(long profit, long totalCost)
    {
        var scaled = new BigInteger(profit) * 10_000;
        var denominator = new BigInteger(totalCost);
        var absoluteQuotient = BigInteger.DivRem(BigInteger.Abs(scaled), denominator, out var remainder);
        if (remainder * 2 >= denominator) absoluteQuotient++;
        var sign = scaled.Sign < 0 ? "-" : string.Empty;
        return string.Create(CultureInfo.InvariantCulture, $"{sign}{absoluteQuotient / 100}.{absoluteQuotient % 100:D2}%");
    }

    private sealed record ScannerExactRoiResponse(MoneyResponse Profit, MoneyResponse TotalCost, string DisplayPercent);

    private sealed record ScannerCandidateResponse(
        int ItemId,
        string ItemName,
        ScannerOrderSummaryResponse BestBuy,
        ScannerOrderSummaryResponse LowestSell,
        MoneyResponse PlannedBid,
        MoneyResponse PlannedListPrice,
        MoneyResponse ListingFee,
        MoneyResponse ExchangeFee,
        MoneyResponse NetSaleProceeds,
        MoneyResponse NetProfit,
        MoneyResponse TotalCost,
        ScannerExactRoiResponse ModeledRoi,
        MoneyResponse MaximumBid,
        IReadOnlyList<LiveMarketScannerInclusionReason> InclusionReasons,
        ScannerLiquidityResponse Liquidity);

    private sealed record ScannerExecutionResponse(
        int RequestedQuantity,
        int FilledQuantity,
        int RemainingQuantity,
        bool IsFullyFilled,
        MoneyResponse TotalValue,
        MoneyResponse PriceImpact);

    private sealed record ScannerLiquidityResponse(
        QuantityResponse TotalBuyQuantity,
        QuantityResponse TotalSellQuantity,
        QuantityResponse NearBestBuyQuantity,
        QuantityResponse NearBestSellQuantity,
        QuantityResponse NearBestBuyListings,
        QuantityResponse NearBestSellListings,
        MoneyResponse? BuyNextLevelGap,
        MoneyResponse? SellNextLevelGap,
        bool HasBuyPriceCliff,
        bool HasSellPriceCliff,
        ScannerExecutionResponse Acquisition,
        ScannerExecutionResponse Liquidation,
        int ParticipationCapQuantity,
        IReadOnlyList<LiveMarketScannerLiquidityReason> Reasons,
        IReadOnlyList<ScannerOrderBookLevelResponse> TopBuyLevels,
        IReadOnlyList<ScannerOrderBookLevelResponse> TopSellLevels);

    private sealed record ScannerOrderBookLevelResponse(int Listings, int Quantity, MoneyResponse UnitPrice);

    private sealed record ScannerExclusionResponse(LiveMarketScannerExclusionReason Reason, int Count);

    private sealed record LiveMarketScannerResponse(
        LiveMarketScannerState State,
        Gw2Tp.Application.MarketData.Gw2ApiErrorCategory? Error,
        DateTimeOffset? ObservedAtUtc,
        ScannerSettingsResponse Settings,
        bool IsFeeRoundingExternallyVerified,
        int QualifyingCandidateCount,
        bool IsTruncated,
        IReadOnlyList<ScannerCandidateResponse> Candidates,
        IReadOnlyList<ScannerExclusionResponse> Exclusions);
}

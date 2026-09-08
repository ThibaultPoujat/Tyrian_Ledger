using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
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
            !TryReadInt(query, "listUndercutCopper", defaults.ListUndercutCopper, out var listUndercutCopper))
        {
            settings = defaults;
            return false;
        }

        settings = new LiveMarketScannerSettings(
            minimumRoiBasisPoints,
            new Money(minimumNetProfitCopper),
            bidIncrementCopper,
            listUndercutCopper);
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
            result.Settings.ListUndercutCopper),
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
                MoneyResponse.From(candidate.ModeledRoi.TotalCost)),
            MoneyResponse.From(candidate.MaximumBid),
            candidate.InclusionReasons.ToArray())).ToArray(),
        result.Exclusions.Select(exclusion => new ScannerExclusionResponse(exclusion.Reason, exclusion.Count)).ToArray());

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

    private sealed record ScannerSettingsResponse(
        int MinimumRoiBasisPoints,
        MoneyResponse MinimumNetProfit,
        int BidIncrementCopper,
        int ListUndercutCopper);

    private sealed record ScannerOrderSummaryResponse(int Quantity, MoneyResponse UnitPrice);

    private sealed record ScannerExactRoiResponse(MoneyResponse Profit, MoneyResponse TotalCost);

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
        IReadOnlyList<LiveMarketScannerInclusionReason> InclusionReasons);

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

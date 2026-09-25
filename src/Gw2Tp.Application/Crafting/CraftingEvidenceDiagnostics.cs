using Gw2Tp.Application.MarketData;

namespace Gw2Tp.Application.Crafting;

/// <summary>
/// Sanitized, account-fact-free evidence timing for the bounded crafting
/// market read.  Item IDs themselves are deliberately not included.
/// </summary>
public sealed record CraftingMarketEvidenceDiagnostic(
    int RequestedItemIdCount,
    long ListingsElapsedMilliseconds,
    Gw2ApiErrorCategory? ListingsErrorCategory,
    bool ListingsPartialResponse,
    int? ListingResponseItemCount,
    int MissingListingItemIdCount,
    long MetadataElapsedMilliseconds,
    Gw2ApiErrorCategory? MetadataErrorCategory,
    bool MetadataPartialResponse,
    int? MetadataResponseItemCount,
    string Outcome);

public interface ICraftingEvidenceDiagnostics
{
    void Record(CraftingMarketEvidenceDiagnostic diagnostic);
}

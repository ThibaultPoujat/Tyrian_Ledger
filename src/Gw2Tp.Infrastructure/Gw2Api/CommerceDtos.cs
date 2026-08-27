using System.Text.Json.Serialization;

namespace Gw2Tp.Infrastructure.Gw2Api;

internal sealed class CommercePriceDto
{
    [JsonPropertyName("id")]
    public required int Id { get; init; }

    [JsonPropertyName("whitelisted")]
    public required bool Whitelisted { get; init; }

    [JsonPropertyName("buys")]
    public required CommercePriceSideDto Buys { get; init; }

    [JsonPropertyName("sells")]
    public required CommercePriceSideDto Sells { get; init; }
}

internal sealed class CommercePriceSideDto
{
    [JsonPropertyName("quantity")]
    public required int Quantity { get; init; }

    [JsonPropertyName("unit_price")]
    public required int UnitPrice { get; init; }
}

internal sealed class CommerceListingDto
{
    [JsonPropertyName("id")]
    public required int Id { get; init; }

    [JsonPropertyName("buys")]
    public required List<CommerceListingLevelDto> Buys { get; init; }

    [JsonPropertyName("sells")]
    public required List<CommerceListingLevelDto> Sells { get; init; }
}

internal sealed class CommerceListingLevelDto
{
    [JsonPropertyName("listings")]
    public required int Listings { get; init; }

    [JsonPropertyName("quantity")]
    public required int Quantity { get; init; }

    [JsonPropertyName("unit_price")]
    public required int UnitPrice { get; init; }
}

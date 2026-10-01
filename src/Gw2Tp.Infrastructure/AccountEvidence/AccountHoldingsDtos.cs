using System.Text.Json.Serialization;

namespace Gw2Tp.Infrastructure.AccountEvidence;

internal sealed class HoldingsSlotDto
{
    [JsonPropertyName("id")] public int? ItemId { get; init; }
    [JsonPropertyName("count")] public int? Count { get; init; }
    [JsonPropertyName("binding")] public string? Binding { get; init; }
    [JsonPropertyName("bound_to")] public string? BoundTo { get; init; }
    [JsonPropertyName("upgrades")] public int[]? Upgrades { get; init; }
    [JsonPropertyName("infusions")] public int[]? Infusions { get; init; }
}

internal sealed class HoldingsMaterialDto
{
    [JsonPropertyName("id")] public int? ItemId { get; init; }
    [JsonPropertyName("category")] public int? CategoryId { get; init; }
    [JsonPropertyName("count")] public int? Count { get; init; }
    [JsonPropertyName("binding")] public string? Binding { get; init; }
}

internal sealed class HoldingsCharacterInventoryDto
{
    [JsonPropertyName("bags")] public HoldingsBagDto?[]? Bags { get; init; }
}

internal sealed class HoldingsBagDto
{
    [JsonPropertyName("id")] public int? ItemId { get; init; }
    [JsonPropertyName("size")] public int? Size { get; init; }
    [JsonPropertyName("inventory")] public HoldingsSlotDto?[]? Inventory { get; init; }
}

internal sealed class HoldingsDeliveryDto
{
    [JsonPropertyName("coins")] public long? Coins { get; init; }
    [JsonPropertyName("items")] public HoldingsDeliveryItemDto?[]? Items { get; init; }
}

internal sealed class HoldingsDeliveryItemDto
{
    [JsonPropertyName("id")] public int? ItemId { get; init; }
    [JsonPropertyName("count")] public int? Count { get; init; }
}

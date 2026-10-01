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

internal sealed class HoldingsCraftingDto
{
    [JsonPropertyName("crafting")] public HoldingsDisciplineDto?[]? Crafting { get; init; }
}
internal sealed class HoldingsDisciplineDto
{
    [JsonPropertyName("discipline")] public string? Discipline { get; init; }
    [JsonPropertyName("rating")] public int? Rating { get; init; }
    [JsonPropertyName("active")] public bool? Active { get; init; }
}
internal sealed class HoldingsEquipmentDto
{
    [JsonPropertyName("equipment")] public HoldingsEquipmentRowDto?[]? Equipment { get; init; }
}
internal sealed class HoldingsEquipmentRowDto
{
    [JsonPropertyName("id")] public int? ItemId { get; init; }
    [JsonPropertyName("count")] public int? UnlockCount { get; init; }
    [JsonPropertyName("slot")] public string? Slot { get; init; }
    [JsonPropertyName("location")] public string? Location { get; init; }
    [JsonPropertyName("tabs")] public int[]? Tabs { get; init; }
    [JsonPropertyName("binding")] public string? Binding { get; init; }
    [JsonPropertyName("bound_to")] public string? BoundTo { get; init; }
    [JsonPropertyName("upgrades")] public int[]? Upgrades { get; init; }
    [JsonPropertyName("infusions")] public int[]? Infusions { get; init; }
}
internal sealed class HoldingsEquipmentTabDto
{
    [JsonPropertyName("tab")] public int? TabId { get; init; }
    [JsonPropertyName("is_active")] public bool? IsActive { get; init; }
    [JsonPropertyName("equipment")] public HoldingsEquipmentRowDto?[]? Equipment { get; init; }
}

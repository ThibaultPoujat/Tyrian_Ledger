using System.Text.Json.Serialization;

namespace Gw2Tp.Infrastructure.Crafting;

internal sealed class AccountBankSlotDto
{
    [JsonPropertyName("id")]
    public int? ItemId { get; init; }

    [JsonPropertyName("count")]
    public int? Count { get; init; }

    [JsonPropertyName("binding")]
    public string? Binding { get; init; }
}

internal sealed class AccountMaterialDto
{
    [JsonPropertyName("id")]
    public int? ItemId { get; init; }

    [JsonPropertyName("category")]
    public int? CategoryId { get; init; }

    [JsonPropertyName("count")]
    public int? Count { get; init; }

    [JsonPropertyName("binding")]
    public string? Binding { get; init; }
}

internal sealed class CharacterCraftingDto
{
    [JsonPropertyName("crafting")]
    public CharacterCraftingDisciplineDto[]? Crafting { get; init; }
}

internal sealed class CharacterCraftingDisciplineDto
{
    [JsonPropertyName("discipline")]
    public string? Discipline { get; init; }

    [JsonPropertyName("rating")]
    public int? Rating { get; init; }

    [JsonPropertyName("active")]
    public bool? Active { get; init; }
}

internal sealed class CraftingRecipeDto
{
    [JsonPropertyName("id")]
    public int? Id { get; init; }

    [JsonPropertyName("output_item_id")]
    public int? OutputItemId { get; init; }

    [JsonPropertyName("output_item_count")]
    public int? OutputItemCount { get; init; }

    [JsonPropertyName("disciplines")]
    public string[]? Disciplines { get; init; }

    [JsonPropertyName("min_rating")]
    public int? MinRating { get; init; }

    [JsonPropertyName("flags")]
    public string[]? Flags { get; init; }

    [JsonPropertyName("ingredients")]
    public CraftingRecipeIngredientDto[]? Ingredients { get; init; }
}

internal sealed class CraftingRecipeIngredientDto
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("id")]
    public int? Id { get; init; }

    [JsonPropertyName("count")]
    public int? Count { get; init; }
}

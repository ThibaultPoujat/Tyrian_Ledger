using System.Text.Json.Serialization;

namespace Gw2Tp.Infrastructure.PersonalTradingPost;

internal sealed class AccountScopeDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }
}

internal sealed class PersonalTradingPostTransactionDto
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("item_id")]
    public int ItemId { get; init; }

    [JsonPropertyName("price")]
    public int Price { get; init; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; init; }

    [JsonPropertyName("created")]
    public string? Created { get; init; }

    [JsonPropertyName("purchased")]
    public string? Purchased { get; init; }
}

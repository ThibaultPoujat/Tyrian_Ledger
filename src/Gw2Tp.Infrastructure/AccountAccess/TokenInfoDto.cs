using System.Text.Json.Serialization;

namespace Gw2Tp.Infrastructure.AccountAccess;

internal sealed class TokenInfoDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("permissions")]
    public IReadOnlyList<string>? Permissions { get; init; }
}

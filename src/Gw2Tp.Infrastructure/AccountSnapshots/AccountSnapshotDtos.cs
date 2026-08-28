namespace Gw2Tp.Infrastructure.AccountSnapshots;

internal sealed class AccountBankSlotDto
{
    public int? Id { get; init; }

    public int? Count { get; init; }

    public string? Binding { get; init; }
}

internal sealed class AccountMaterialDto
{
    public int? Id { get; init; }

    public int? Category { get; init; }

    public int? Count { get; init; }
}

internal sealed class CharacterCraftingDisciplineDto
{
    public string? Discipline { get; init; }

    public int? Rating { get; init; }

    public bool? Active { get; init; }
}

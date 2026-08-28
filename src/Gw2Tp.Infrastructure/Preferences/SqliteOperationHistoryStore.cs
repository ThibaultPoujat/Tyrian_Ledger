using System.Text.Json;
using System.Text.Json.Serialization;
using Gw2Tp.Analytics.Reconciliation;
using Gw2Tp.Application.Operations;
using Microsoft.EntityFrameworkCore;

namespace Gw2Tp.Infrastructure.Preferences;

internal sealed class SqliteOperationHistoryStore : IOperationHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true),
        },
    };
    private readonly IDbContextFactory<UserSessionPreferencesDbContext> dbContextFactory;

    public SqliteOperationHistoryStore(IDbContextFactory<UserSessionPreferencesDbContext> dbContextFactory)
    {
        this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    public async Task CreateAsync(OperationRecord operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await dbContext.Operations.AnyAsync(storedOperation => storedOperation.Id == operation.Id, cancellationToken))
        {
            throw new InvalidOperationException("An operation with the same ID already exists.");
        }

        dbContext.Operations.Add(ToEntity(operation));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<OperationRecord?> GetAsync(Guid operationId, CancellationToken cancellationToken)
    {
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("An operation ID is required.", nameof(operationId));
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.Operations
            .AsNoTracking()
            .Include(operation => operation.Scenario)
            .SingleOrDefaultAsync(operation => operation.Id == operationId, cancellationToken);

        return entity is null ? null : ToModel(entity);
    }

    public async Task<IReadOnlyList<OperationRecord>> ListAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await dbContext.Operations
            .AsNoTracking()
            .Include(operation => operation.Scenario)
            .OrderBy(operation => operation.CreatedAtUtcTicks)
            .ThenBy(operation => operation.Id)
            .ToArrayAsync(cancellationToken);

        return Array.AsReadOnly(entities.Select(ToModel).ToArray());
    }

    public async Task UpdateStatusAsync(
        Guid operationId,
        OperationStatus status,
        DateTimeOffset lastModifiedAtUtc,
        CancellationToken cancellationToken)
    {
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("An operation ID is required.", nameof(operationId));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "The operation status is unknown.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.Operations
            .SingleOrDefaultAsync(operation => operation.Id == operationId, cancellationToken);
        if (entity is null)
        {
            throw new KeyNotFoundException("The requested operation does not exist.");
        }

        var normalizedLastModifiedAtUtc = lastModifiedAtUtc.ToUniversalTime();
        if (normalizedLastModifiedAtUtc < entity.LastModifiedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lastModifiedAtUtc),
                "The last-modified timestamp cannot precede the stored last-modified timestamp.");
        }

        entity.Status = ToStorageValue(status);
        entity.LastModifiedAtUtc = normalizedLastModifiedAtUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateActualOutcomeAsync(
        Guid operationId,
        OperationActualOutcome actualOutcome,
        DateTimeOffset lastModifiedAtUtc,
        CancellationToken cancellationToken)
    {
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("An operation ID is required.", nameof(operationId));
        }

        ArgumentNullException.ThrowIfNull(actualOutcome);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.Operations
            .SingleOrDefaultAsync(operation => operation.Id == operationId, cancellationToken);
        if (entity is null)
        {
            throw new KeyNotFoundException("The requested operation does not exist.");
        }

        var normalizedLastModifiedAtUtc = lastModifiedAtUtc.ToUniversalTime();
        if (normalizedLastModifiedAtUtc < entity.LastModifiedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lastModifiedAtUtc),
                "The last-modified timestamp cannot precede the stored last-modified timestamp.");
        }

        entity.ActualOutcomeJson = SerializeActualOutcome(actualOutcome);
        entity.LastModifiedAtUtc = normalizedLastModifiedAtUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static OperationHistoryEntity ToEntity(OperationRecord operation) => new()
    {
        Id = operation.Id,
        CreatedAtUtc = operation.CreatedAtUtc,
        CreatedAtUtcTicks = operation.CreatedAtUtc.UtcDateTime.Ticks,
        LastModifiedAtUtc = operation.LastModifiedAtUtc,
        Status = ToStorageValue(operation.Status),
        CalculationVersionId = operation.CalculationVersionId,
        ConfigurationVersionId = operation.ConfigurationVersionId,
        ActualOutcomeJson = operation.ActualOutcome is null ? null : SerializeActualOutcome(operation.ActualOutcome),
        Scenario = new OperationHistoryScenarioEntity
        {
            OperationId = operation.Id,
            Kind = ToStorageValue(operation.Scenario.Kind),
            PayloadJson = SerializeScenario(operation.Scenario),
        },
    };

    private static OperationRecord ToModel(OperationHistoryEntity entity)
    {
        var scenario = entity.Scenario ?? throw new InvalidOperationException("The stored operation is missing its scenario snapshot.");
        return new OperationRecord(
            entity.Id,
            entity.CreatedAtUtc,
            entity.LastModifiedAtUtc,
            ParseStatus(entity.Status),
            entity.CalculationVersionId,
            entity.ConfigurationVersionId,
            DeserializeScenario(scenario),
            DeserializeActualOutcome(entity.ActualOutcomeJson));
    }

    private static string SerializeScenario(OperationScenarioSnapshot scenario) => scenario switch
    {
        MarketFlipOperationScenarioSnapshot marketFlip => JsonSerializer.Serialize(marketFlip, JsonOptions),
        CraftingOperationScenarioSnapshot crafting => JsonSerializer.Serialize(crafting, JsonOptions),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario.Kind, "The operation scenario is not supported."),
    };

    private static OperationScenarioSnapshot DeserializeScenario(OperationHistoryScenarioEntity scenario) => scenario.Kind switch
    {
        "market-flip" => JsonSerializer.Deserialize<MarketFlipOperationScenarioSnapshot>(scenario.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("The stored market-flip scenario is invalid."),
        "crafting" => JsonSerializer.Deserialize<CraftingOperationScenarioSnapshot>(scenario.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("The stored crafting scenario is invalid."),
        _ => throw new InvalidOperationException("The stored operation scenario kind is unsupported."),
    };

    private static string SerializeActualOutcome(OperationActualOutcome actualOutcome) =>
        JsonSerializer.Serialize(actualOutcome, JsonOptions);

    private static OperationActualOutcome? DeserializeActualOutcome(string? actualOutcomeJson) =>
        actualOutcomeJson is null
            ? null
            : JsonSerializer.Deserialize<OperationActualOutcome>(actualOutcomeJson, JsonOptions)
                ?? throw new InvalidOperationException("The stored operation actual outcome is invalid.");

    private static OperationStatus ParseStatus(string value) => value switch
    {
        "planned" => OperationStatus.Planned,
        "in-progress" => OperationStatus.InProgress,
        "completed" => OperationStatus.Completed,
        "cancelled" => OperationStatus.Cancelled,
        _ => throw new InvalidOperationException("The stored operation status is unsupported."),
    };

    private static string ToStorageValue(OperationStatus status) => status switch
    {
        OperationStatus.Planned => "planned",
        OperationStatus.InProgress => "in-progress",
        OperationStatus.Completed => "completed",
        OperationStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "The operation status is unsupported."),
    };

    private static string ToStorageValue(OperationScenarioKind kind) => kind switch
    {
        OperationScenarioKind.MarketFlip => "market-flip",
        OperationScenarioKind.Crafting => "crafting",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The operation scenario kind is unsupported."),
    };
}

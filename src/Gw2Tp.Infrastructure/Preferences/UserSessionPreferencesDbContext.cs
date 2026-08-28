using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gw2Tp.Infrastructure.Preferences;

public sealed class UserSessionPreferencesDbContext : DbContext
{
    public UserSessionPreferencesDbContext(DbContextOptions<UserSessionPreferencesDbContext> options)
        : base(options)
    {
    }

    internal DbSet<UserSessionPreferencesEntity> UserSessionPreferences => Set<UserSessionPreferencesEntity>();

    internal DbSet<OperationHistoryEntity> Operations => Set<OperationHistoryEntity>();

    internal DbSet<OperationHistoryScenarioEntity> OperationScenarios => Set<OperationHistoryScenarioEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserSessionPreferencesEntity>(entity =>
        {
            entity.ToTable("UserSessionPreferences", table =>
            {
                table.HasCheckConstraint(
                    "CK_UserSessionPreferences_CapitalLimitCopper",
                    "CapitalLimitCopper IS NULL OR (CapitalLimitCopper >= 0 AND CapitalLimitCopper <= 9007199254740991)");
                table.HasCheckConstraint(
                    "CK_UserSessionPreferences_MinimumProfitCopper",
                    "MinimumProfitCopper IS NULL OR (MinimumProfitCopper >= 0 AND MinimumProfitCopper <= 9007199254740991)");
                table.HasCheckConstraint(
                    "CK_UserSessionPreferences_AllocationPercent",
                    "AllocationPercent BETWEEN 1 AND 100");
                table.HasCheckConstraint(
                    "CK_UserSessionPreferences_RiskPreference",
                    "RiskPreference IN ('all', 'normal', 'reduced')");
                table.HasCheckConstraint(
                    "CK_UserSessionPreferences_StrategyPreference",
                    "StrategyPreference IN ('all', 'market-flip')");
            });
            entity.HasKey(preferences => preferences.Id);
            entity.Property(preferences => preferences.Id).ValueGeneratedNever();
            entity.Property(preferences => preferences.RiskPreference).HasMaxLength(16).IsRequired();
            entity.Property(preferences => preferences.StrategyPreference).HasMaxLength(16).IsRequired();
        });

        modelBuilder.Entity<OperationHistoryEntity>(entity =>
        {
            entity.ToTable("Operations", table =>
            {
                table.HasCheckConstraint(
                    "CK_Operations_Status",
                    "Status IN ('planned', 'in-progress', 'completed', 'cancelled')");
            });
            entity.HasKey(operation => operation.Id);
            entity.Property(operation => operation.CalculationVersionId)
                .HasMaxLength(128)
                .IsRequired();
            entity.Property(operation => operation.ConfigurationVersionId)
                .HasMaxLength(128)
                .IsRequired();
            entity.Property(operation => operation.CreatedAtUtcTicks).IsRequired();
            entity.Property(operation => operation.Status)
                .HasMaxLength(16)
                .IsRequired();
            entity.HasOne(operation => operation.Scenario)
                .WithOne(scenario => scenario.Operation)
                .HasForeignKey<OperationHistoryScenarioEntity>(scenario => scenario.OperationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OperationHistoryScenarioEntity>(entity =>
        {
            entity.ToTable("OperationScenarios", table =>
            {
                table.HasCheckConstraint(
                    "CK_OperationScenarios_Kind",
                    "Kind IN ('market-flip', 'crafting')");
            });
            entity.HasKey(scenario => scenario.OperationId);
            entity.Property(scenario => scenario.Kind)
                .HasMaxLength(16)
                .IsRequired();
            entity.Property(scenario => scenario.PayloadJson).IsRequired();
        });
    }
}

internal sealed class OperationHistoryEntity
{
    public Guid Id { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public long CreatedAtUtcTicks { get; set; }

    public DateTimeOffset LastModifiedAtUtc { get; set; }

    public string Status { get; set; } = null!;

    public string CalculationVersionId { get; set; } = null!;

    public string ConfigurationVersionId { get; set; } = null!;

    public OperationHistoryScenarioEntity Scenario { get; set; } = null!;
}

internal sealed class OperationHistoryScenarioEntity
{
    public Guid OperationId { get; set; }

    public string Kind { get; set; } = null!;

    public string PayloadJson { get; set; } = null!;

    public OperationHistoryEntity Operation { get; set; } = null!;
}

internal sealed class UserSessionPreferencesEntity
{
    public const int SingletonId = 1;

    public int Id { get; set; }

    public long? CapitalLimitCopper { get; set; }

    public long? MinimumProfitCopper { get; set; }

    public string RiskPreference { get; set; } = null!;

    public string StrategyPreference { get; set; } = null!;

    public int AllocationPercent { get; set; }
}

public sealed class UserSessionPreferencesDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<UserSessionPreferencesDbContext>
{
    public UserSessionPreferencesDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<UserSessionPreferencesDbContext>()
            .UseSqlite("Data Source=tyrian-ledger-design-time.db")
            .Options;

        return new UserSessionPreferencesDbContext(options);
    }
}

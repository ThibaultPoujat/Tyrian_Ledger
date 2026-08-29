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

    internal DbSet<MarketPriceSnapshotEntity> MarketPriceSnapshots => Set<MarketPriceSnapshotEntity>();

    internal DbSet<MarketOrderBookSnapshotEntity> MarketOrderBookSnapshots => Set<MarketOrderBookSnapshotEntity>();

    internal DbSet<MarketOrderBookLevelEntity> MarketOrderBookLevels => Set<MarketOrderBookLevelEntity>();

    internal DbSet<MarketWatchlistItemEntity> MarketWatchlistItems => Set<MarketWatchlistItemEntity>();

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
            entity.Property(operation => operation.ActualOutcomeJson);
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

        modelBuilder.Entity<MarketPriceSnapshotEntity>(entity =>
        {
            entity.ToTable("MarketPriceSnapshots", table =>
            {
                table.HasCheckConstraint("CK_MarketPriceSnapshots_ItemId", "ItemId > 0");
                table.HasCheckConstraint("CK_MarketPriceSnapshots_FormatVersion", "FormatVersion > 0");
                table.HasCheckConstraint("CK_MarketPriceSnapshots_BuyQuantity", "BuyQuantity >= 0");
                table.HasCheckConstraint("CK_MarketPriceSnapshots_BuyUnitPriceCopper", "BuyUnitPriceCopper >= 0");
                table.HasCheckConstraint("CK_MarketPriceSnapshots_SellQuantity", "SellQuantity >= 0");
                table.HasCheckConstraint("CK_MarketPriceSnapshots_SellUnitPriceCopper", "SellUnitPriceCopper >= 0");
            });
            entity.HasKey(snapshot => snapshot.Id);
            entity.Property(snapshot => snapshot.CapturedAtUtcTicks).IsRequired();
            entity.HasIndex(snapshot => new { snapshot.ItemId, snapshot.CapturedAtUtcTicks, snapshot.Id });
        });

        modelBuilder.Entity<MarketOrderBookSnapshotEntity>(entity =>
        {
            entity.ToTable("MarketOrderBookSnapshots", table =>
            {
                table.HasCheckConstraint("CK_MarketOrderBookSnapshots_ItemId", "ItemId > 0");
                table.HasCheckConstraint("CK_MarketOrderBookSnapshots_FormatVersion", "FormatVersion > 0");
            });
            entity.HasKey(snapshot => snapshot.Id);
            entity.Property(snapshot => snapshot.CapturedAtUtcTicks).IsRequired();
            entity.HasIndex(snapshot => new { snapshot.ItemId, snapshot.CapturedAtUtcTicks, snapshot.Id });
            entity.HasMany(snapshot => snapshot.Levels)
                .WithOne(level => level.Snapshot)
                .HasForeignKey(level => level.SnapshotId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MarketOrderBookLevelEntity>(entity =>
        {
            entity.ToTable("MarketOrderBookLevels", table =>
            {
                table.HasCheckConstraint("CK_MarketOrderBookLevels_Side", "Side IN ('buy', 'sell')");
                table.HasCheckConstraint("CK_MarketOrderBookLevels_Position", "Position >= 0");
                table.HasCheckConstraint("CK_MarketOrderBookLevels_Listings", "Listings >= 0");
                table.HasCheckConstraint("CK_MarketOrderBookLevels_Quantity", "Quantity >= 0");
                table.HasCheckConstraint("CK_MarketOrderBookLevels_UnitPriceCopper", "UnitPriceCopper >= 0");
            });
            entity.HasKey(level => new { level.SnapshotId, level.Side, level.Position });
        });

        modelBuilder.Entity<MarketWatchlistItemEntity>(entity =>
        {
            entity.ToTable("MarketWatchlistItems", table =>
            {
                table.HasCheckConstraint("CK_MarketWatchlistItems_ItemId", "ItemId > 0");
                table.HasCheckConstraint(
                    "CK_MarketWatchlistItems_SamplingClass",
                    "SamplingClass IN ('watchlist', 'background')");
            });
            entity.HasKey(item => item.ItemId);
            entity.Property(item => item.ItemId).ValueGeneratedNever();
            entity.Property(item => item.SamplingClass)
                .HasMaxLength(16)
                .IsRequired();
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

    public string? ActualOutcomeJson { get; set; }

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

    public int AnalysisQuantity { get; set; }

    public int? ListingFeeBasisPoints { get; set; }

    public string? ListingFeeRounding { get; set; }

    public int? ExchangeFeeBasisPoints { get; set; }

    public string? ExchangeFeeRounding { get; set; }
}

internal sealed class MarketPriceSnapshotEntity
{
    public Guid Id { get; set; }

    public int ItemId { get; set; }

    public DateTimeOffset CapturedAtUtc { get; set; }

    public long CapturedAtUtcTicks { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public int FormatVersion { get; set; }

    public bool IsWhitelisted { get; set; }

    public int BuyQuantity { get; set; }

    public int BuyUnitPriceCopper { get; set; }

    public int SellQuantity { get; set; }

    public int SellUnitPriceCopper { get; set; }
}

internal sealed class MarketOrderBookSnapshotEntity
{
    public Guid Id { get; set; }

    public int ItemId { get; set; }

    public DateTimeOffset CapturedAtUtc { get; set; }

    public long CapturedAtUtcTicks { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public int FormatVersion { get; set; }

    public List<MarketOrderBookLevelEntity> Levels { get; } = [];
}

internal sealed class MarketOrderBookLevelEntity
{
    public Guid SnapshotId { get; set; }

    public string Side { get; set; } = null!;

    public int Position { get; set; }

    public int Listings { get; set; }

    public int Quantity { get; set; }

    public int UnitPriceCopper { get; set; }

    public MarketOrderBookSnapshotEntity Snapshot { get; set; } = null!;
}

internal sealed class MarketWatchlistItemEntity
{
    public int ItemId { get; set; }

    public string SamplingClass { get; set; } = null!;
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

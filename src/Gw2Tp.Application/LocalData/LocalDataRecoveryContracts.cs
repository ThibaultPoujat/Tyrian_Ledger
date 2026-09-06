namespace Gw2Tp.Application.LocalData;

/// <summary>
/// The local paths that hold Tyrian Ledger's durable database and its managed
/// backup copies. These paths are local-only and never contain credentials.
/// </summary>
public sealed record LocalDataLocation(string DatabasePath, string BackupDirectoryPath);

/// <summary>
/// A local SQLite backup created by the application.
/// </summary>
public sealed record LocalDataBackup(string FileName, DateTimeOffset CreatedAtUtc);

public enum LocalDataRestoreOutcome
{
    Restored = 1,
    InvalidBackup = 2,
    RestoreFailed = 3,
}

/// <summary>
/// The safe, non-secret outcome of restoring a local database backup.
/// </summary>
public sealed record LocalDataRestoreResult(
    LocalDataRestoreOutcome Outcome,
    string? PreRestoreBackupFileName = null);

/// <summary>
/// Application boundary for intentional local backup, recovery, and personal
/// account-data deletion. Implementations own SQLite and filesystem details.
/// </summary>
public interface ILocalDataRecoveryService
{
    LocalDataLocation GetLocation();

    Task<LocalDataBackup> CreateBackupAsync(CancellationToken cancellationToken = default);

    Task<LocalDataRestoreResult> RestoreAsync(
        Stream backupContents,
        CancellationToken cancellationToken = default);

    Task ClearPersonalDataAsync(CancellationToken cancellationToken = default);

    Task CleanupStaleRestoreArtifactsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Coordinates long-running personal-data operations so recovery controls are
/// not followed by an earlier in-flight synchronization commit.
/// </summary>
public interface IPersonalDataOperationGate
{
    ValueTask<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default);
}

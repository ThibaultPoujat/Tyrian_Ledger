using Gw2Tp.Application.LocalData;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace Gw2Tp.Infrastructure.Persistence;

/// <summary>
/// Local-only backup, restore, and clear operations for the Tyrian Ledger
/// database. Candidate restores are never allowed to touch the live database
/// until they have passed validation and completed migration in staging.
/// </summary>
internal sealed class SqliteLocalDataRecoveryService(
    SqliteConnectionFactory connectionFactory,
    ISqliteDatabaseGate databaseGate,
    IPersonalDataOperationGate? operationGate = null,
    ILocalDataFileOperations? fileOperations = null) : ILocalDataRecoveryService
{
    private const string BackupDirectoryName = "backups";
    private const string RestoreArtifactPrefix = ".tyrian-ledger-restore-";
    private const string BackupArtifactPrefix = "tyrian-ledger-";
    private readonly ILocalDataFileOperations files = fileOperations ?? new LocalDataFileOperations();
    private readonly IPersonalDataOperationGate recoveryOperationGate = operationGate ?? new PersonalDataOperationGate();

    public LocalDataLocation GetLocation() => new(connectionFactory.DatabasePath, GetBackupDirectoryPath());

    public async Task<LocalDataBackup> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        await using var operationLease = await recoveryOperationGate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var lease = await databaseGate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        CleanupStaleRestoreArtifactsCore();
        return await CreateBackupCoreAsync("backup", cancellationToken).ConfigureAwait(false);
    }

    public async Task<LocalDataRestoreResult> RestoreAsync(
        Stream backupContents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(backupContents);
        await using var operationLease = await recoveryOperationGate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var lease = await databaseGate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        CleanupStaleRestoreArtifactsCore();

        var stagingDirectory = Path.GetDirectoryName(connectionFactory.DatabasePath)
            ?? throw new InvalidOperationException("The local database path has no parent directory.");
        var incomingPath = Path.Combine(stagingDirectory, $"{RestoreArtifactPrefix}{Guid.NewGuid():N}.incoming");
        var stagedDatabasePath = Path.Combine(stagingDirectory, $"{RestoreArtifactPrefix}{Guid.NewGuid():N}.db");

        try
        {
            var uploadOutcome = await CopyUploadedBackupAsync(backupContents, incomingPath, cancellationToken).ConfigureAwait(false);
            if (uploadOutcome is not null)
            {
                return new LocalDataRestoreResult(uploadOutcome.Value);
            }

            try
            {
                await SqliteSchemaMigrator.ValidateBackupCandidateAsync(incomingPath, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return new LocalDataRestoreResult(LocalDataRestoreOutcome.InvalidBackup);
            }

            try
            {
                await CopyDatabaseAsync(incomingPath, stagedDatabasePath, cancellationToken).ConfigureAwait(false);
                var stagedMigrator = new SqliteSchemaMigrator(new SqliteConnectionFactory(stagedDatabasePath));
                await stagedMigrator.MigrateAndValidatePersistedDataAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (InvalidDataException)
            {
                return new LocalDataRestoreResult(LocalDataRestoreOutcome.InvalidBackup);
            }
            catch (InvalidOperationException)
            {
                return new LocalDataRestoreResult(LocalDataRestoreOutcome.InvalidBackup);
            }
            catch (SqliteException exception) when (IsLocalStorageFailure(exception))
            {
                return new LocalDataRestoreResult(LocalDataRestoreOutcome.RestoreFailed);
            }
            catch (SqliteException)
            {
                return new LocalDataRestoreResult(LocalDataRestoreOutcome.InvalidBackup);
            }
            catch
            {
                return new LocalDataRestoreResult(LocalDataRestoreOutcome.RestoreFailed);
            }

            try
            {
                var preRestoreBackup = await CreateBackupCoreAsync("pre-restore", cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                SqliteConnection.ClearAllPools();
                files.ReplaceDatabase(stagedDatabasePath, connectionFactory.DatabasePath);
                return new LocalDataRestoreResult(LocalDataRestoreOutcome.Restored, preRestoreBackup.FileName);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return new LocalDataRestoreResult(LocalDataRestoreOutcome.RestoreFailed);
            }
        }
        finally
        {
            DeleteIfPresent(incomingPath);
            DeleteIfPresent(stagedDatabasePath);
        }
    }

    public async Task ClearPersonalDataAsync(CancellationToken cancellationToken = default)
    {
        await using var operationLease = await recoveryOperationGate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var lease = await databaseGate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        CleanupStaleRestoreArtifactsCore();
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var tableName in new[]
                 {
                     "current_tp_orders",
                     "current_tp_order_observations",
                     "current_order_sync_batches",
                     "completed_tp_transactions",
                     "account_profiles",
                 })
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"DELETE FROM {tableName};";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CleanupStaleRestoreArtifactsAsync(CancellationToken cancellationToken = default)
    {
        await using var operationLease = await recoveryOperationGate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await using var lease = await databaseGate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        CleanupStaleRestoreArtifactsCore();
    }

    private async Task<LocalDataBackup> CreateBackupCoreAsync(string prefix, CancellationToken cancellationToken)
    {
        var createdAtUtc = DateTimeOffset.UtcNow;
        var backupDirectory = GetBackupDirectoryPath();
        Directory.CreateDirectory(backupDirectory);

        for (var attempt = 0; attempt < 100; attempt++)
        {
            var suffix = attempt == 0 ? string.Empty : $"-{attempt:D2}";
            var fileName = $"tyrian-ledger-{prefix}-{createdAtUtc:yyyyMMddTHHmmssfffZ}{suffix}.db";
            var backupPath = Path.Combine(backupDirectory, fileName);
            var stagingPath = $"{backupPath}.partial-{Guid.NewGuid():N}";
            try
            {
                await CopyDatabaseAsync(connectionFactory.DatabasePath, stagingPath, cancellationToken).ConfigureAwait(false);
                files.MoveFile(stagingPath, backupPath);
                return new LocalDataBackup(fileName, createdAtUtc);
            }
            catch (IOException) when (File.Exists(backupPath))
            {
                DeleteIfPresent(stagingPath);
                continue;
            }
            catch
            {
                DeleteIfPresent(stagingPath);
                throw;
            }
        }

        throw new IOException("A unique local backup filename could not be created.");
    }

    private string GetBackupDirectoryPath()
    {
        var databaseDirectory = Path.GetDirectoryName(connectionFactory.DatabasePath)
            ?? throw new InvalidOperationException("The local database path has no parent directory.");
        return Path.Combine(databaseDirectory, BackupDirectoryName);
    }

    private void CleanupStaleRestoreArtifactsCore()
    {
        var databaseDirectory = Path.GetDirectoryName(connectionFactory.DatabasePath)
            ?? throw new InvalidOperationException("The local database path has no parent directory.");
        var liveDatabasePath = Path.GetFullPath(connectionFactory.DatabasePath);
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        foreach (var path in Directory.EnumerateFiles(databaseDirectory, $"{RestoreArtifactPrefix}*", SearchOption.TopDirectoryOnly))
        {
            if (IsManagedRestoreArtifact(path) &&
                !string.Equals(Path.GetFullPath(path), liveDatabasePath, pathComparison))
            {
                DeleteIfPresent(path);
            }
        }

        var backupDirectory = GetBackupDirectoryPath();
        if (!Directory.Exists(backupDirectory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(backupDirectory, $"{BackupArtifactPrefix}*.db.partial-*", SearchOption.TopDirectoryOnly))
        {
            if (IsManagedBackupPartial(path))
            {
                DeleteIfPresent(path);
            }
        }
    }

    private static bool IsManagedRestoreArtifact(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension is not ".incoming" and not ".db")
        {
            return false;
        }

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
        return fileNameWithoutExtension.StartsWith(RestoreArtifactPrefix, StringComparison.Ordinal)
            && Guid.TryParseExact(fileNameWithoutExtension[RestoreArtifactPrefix.Length..], "N", out _);
    }

    private static bool IsManagedBackupPartial(string path)
    {
        var fileName = Path.GetFileName(path);
        var partialMarker = ".db.partial-";
        var markerIndex = fileName.LastIndexOf(partialMarker, StringComparison.Ordinal);
        if (markerIndex <= BackupArtifactPrefix.Length
            || !Guid.TryParseExact(fileName[(markerIndex + partialMarker.Length)..], "N", out _))
        {
            return false;
        }

        var backupStem = fileName[..markerIndex];
        const string backupPrefix = "tyrian-ledger-backup-";
        const string preRestorePrefix = "tyrian-ledger-pre-restore-";
        var timestampAndCollisionSuffix = backupStem.StartsWith(backupPrefix, StringComparison.Ordinal)
            ? backupStem[backupPrefix.Length..]
            : backupStem.StartsWith(preRestorePrefix, StringComparison.Ordinal)
                ? backupStem[preRestorePrefix.Length..]
                : null;
        return timestampAndCollisionSuffix is not null && IsManagedBackupTimestamp(timestampAndCollisionSuffix);
    }

    private static bool IsManagedBackupTimestamp(string timestampAndCollisionSuffix)
    {
        const int timestampLength = 19;
        if (timestampAndCollisionSuffix.Length is not timestampLength and not 22
            || timestampAndCollisionSuffix.Length == 22
                && (timestampAndCollisionSuffix[timestampLength] != '-'
                    || !char.IsAsciiDigit(timestampAndCollisionSuffix[timestampLength + 1])
                    || !char.IsAsciiDigit(timestampAndCollisionSuffix[timestampLength + 2])))
        {
            return false;
        }

        return DateTime.TryParseExact(
            timestampAndCollisionSuffix[..timestampLength],
            "yyyyMMdd'T'HHmmssfff'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _);
    }

    private static async Task CopyDatabaseAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var sourceConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly,
            ForeignKeys = true,
            Pooling = false,
        }.ToString();
        var destinationConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = false,
        }.ToString();

        await using var source = new SqliteConnection(sourceConnectionString);
        await using var destination = new SqliteConnection(destinationConnectionString);
        await source.OpenAsync(cancellationToken).ConfigureAwait(false);
        await destination.OpenAsync(cancellationToken).ConfigureAwait(false);
        source.BackupDatabase(destination);
    }

    private static async Task<LocalDataRestoreOutcome?> CopyUploadedBackupAsync(
        Stream source,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        FileStream destination;
        try
        {
            destination = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);
        }
        catch
        {
            return LocalDataRestoreOutcome.RestoreFailed;
        }

        await using (destination)
        {
            var buffer = new byte[81920];
            while (true)
            {
                int bytesRead;
                try
                {
                    bytesRead = await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    return LocalDataRestoreOutcome.InvalidBackup;
                }

                if (bytesRead == 0)
                {
                    break;
                }

                try
                {
                    await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    return LocalDataRestoreOutcome.RestoreFailed;
                }
            }

            try
            {
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return LocalDataRestoreOutcome.RestoreFailed;
            }
        }

        return null;
    }

    private static void DeleteIfPresent(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A failed cleanup must not hide the safety outcome of a restore.
        }
    }

    private static bool IsLocalStorageFailure(SqliteException exception) =>
        exception.SqliteErrorCode is 10 or 13 or 14;
}

internal interface ILocalDataFileOperations
{
    void MoveFile(string stagingPath, string backupPath);

    void ReplaceDatabase(string stagedDatabasePath, string liveDatabasePath);
}

internal sealed class LocalDataFileOperations : ILocalDataFileOperations
{
    public void MoveFile(string stagingPath, string backupPath) => File.Move(stagingPath, backupPath);

    public void ReplaceDatabase(string stagedDatabasePath, string liveDatabasePath) =>
        File.Replace(stagedDatabasePath, liveDatabasePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
}

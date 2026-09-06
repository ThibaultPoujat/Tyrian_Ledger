using Microsoft.Data.Sqlite;

namespace Gw2Tp.Infrastructure.Persistence;

internal interface ISqliteConnectionFactory
{
    Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
}

internal sealed class SqliteConnectionFactory : ISqliteConnectionFactory
{
    private readonly string connectionString;

    public SqliteConnectionFactory(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath) || !Path.IsPathFullyQualified(databasePath))
        {
            throw new ArgumentException("The database path must be an absolute file path.", nameof(databasePath));
        }

        DatabasePath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(DatabasePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("The database path must include a parent directory.", nameof(databasePath));
        }

        Directory.CreateDirectory(directory);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = true,
        }.ToString();
    }

    internal string DatabasePath { get; }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}

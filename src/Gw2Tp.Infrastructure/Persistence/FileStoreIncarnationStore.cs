using Gw2Tp.Infrastructure.Secrets;

namespace Gw2Tp.Infrastructure.Persistence;

/// <summary>Nonsecret, durable identity independent of any imported database.</summary>
internal sealed class FileStoreIncarnationStore(SqliteConnectionFactory connections) : IStoreIncarnationStore
{
    private readonly string path = connections.DatabasePath + ".incarnation";
    public async Task<string> ReadOrCreateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return await RotateAsync(cancellationToken).ConfigureAwait(false);
        var value = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (!Guid.TryParseExact(value, "N", out _)) throw new InvalidDataException("Invalid store incarnation.");
        return value;
    }

    public async Task<string> RotateAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var value = Guid.NewGuid().ToString("N");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(value), cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
            return value;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

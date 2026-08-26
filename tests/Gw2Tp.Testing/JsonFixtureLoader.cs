using System.Text.Json;

namespace Gw2Tp.Testing;

public sealed class JsonFixtureLoader
{
    private readonly string _fixtureRoot;

    public JsonFixtureLoader(string fixtureRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixtureRoot);
        _fixtureRoot = Path.GetFullPath(fixtureRoot);
    }

    public async Task<JsonDocument> LoadAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (Path.IsPathRooted(relativePath) ||
            !relativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Fixture paths must be relative JSON file paths.",
                nameof(relativePath));
        }

        var fixturePath = Path.GetFullPath(Path.Combine(_fixtureRoot, relativePath));
        var pathFromRoot = Path.GetRelativePath(_fixtureRoot, fixturePath);

        if (pathFromRoot.Equals("..", StringComparison.Ordinal) ||
            pathFromRoot.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            pathFromRoot.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Fixture paths must remain within the configured fixture root.",
                nameof(relativePath));
        }

        await using var fixtureStream = File.OpenRead(fixturePath);
        return await JsonDocument.ParseAsync(fixtureStream, cancellationToken: cancellationToken);
    }
}

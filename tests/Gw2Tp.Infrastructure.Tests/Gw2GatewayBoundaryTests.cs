using Xunit;

namespace Gw2Tp.Infrastructure.Tests;

public sealed class Gw2GatewayBoundaryTests
{
    [Fact]
    public void Feature_layers_do_not_construct_arena_net_urls()
    {
        var repositoryRoot = FindRepositoryRoot();
        var featureSourceRoots = new[]
        {
            Path.Combine(repositoryRoot, "src", "Gw2Tp.Application"),
            Path.Combine(repositoryRoot, "src", "Gw2Tp.Analytics"),
            Path.Combine(repositoryRoot, "src", "Gw2Tp.Domain"),
        };

        var offendingFiles = featureSourceRoots
            .SelectMany(root => Directory
                .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(path => !IsBuildArtifact(path, root)))
            .Where(path => File.ReadAllText(path).Contains("api.guildwars2.com", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .ToArray();

        Assert.Empty(offendingFiles);
    }

    private static bool IsBuildArtifact(string path, string sourceRoot) =>
        Path.GetRelativePath(sourceRoot, path)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar])
            .Any(segment => segment is "bin" or "obj");

    private static string FindRepositoryRoot()
    {
        foreach (var candidate in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var directory = new DirectoryInfo(candidate); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "TyrianLedger.slnx")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("Could not locate the Tyrian Ledger repository root.");
    }
}

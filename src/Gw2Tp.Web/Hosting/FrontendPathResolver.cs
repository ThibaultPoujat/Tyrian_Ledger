namespace Gw2Tp.Web.Hosting;

internal static class FrontendPathResolver
{
    internal static string? Resolve(string contentRootPath, IConfiguration configuration)
    {
        var configuredPath = configuration["TyrianLedger:Frontend:Path"];
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var absoluteConfiguredPath = Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(contentRootPath, configuredPath);
            return HasIndex(absoluteConfiguredPath)
                ? Path.GetFullPath(absoluteConfiguredPath)
                : null;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            Path.Combine(contentRootPath, "frontend/dist"),
            Path.Combine(contentRootPath, "../../frontend/dist"),
        };

        return candidates
            .Select(Path.GetFullPath)
            .FirstOrDefault(HasIndex);
    }

    private static bool HasIndex(string path)
    {
        return File.Exists(Path.Combine(path, "index.html"));
    }
}

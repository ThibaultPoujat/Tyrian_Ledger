using Gw2Tp.Application.Preferences;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Gw2Tp.Infrastructure.Preferences;

public static class UserSessionPreferencesServiceCollectionExtensions
{
    public const string DatabasePathConfigurationKey = "UserSessionPreferences:DatabasePath";

    public static IServiceCollection AddTyrianLedgerUserSessionPreferences(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var databasePath = configuration[DatabasePathConfigurationKey];
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            databasePath = GetDefaultDatabasePath(environment);
        }

        var databaseDirectory = Path.GetDirectoryName(databasePath);
        if (string.IsNullOrWhiteSpace(databaseDirectory))
        {
            throw new InvalidOperationException("The local user-session preference database path must include a directory.");
        }

        Directory.CreateDirectory(databaseDirectory);
        var connectionString = $"Data Source={databasePath}";

        services.AddDbContextFactory<UserSessionPreferencesDbContext>(options => options.UseSqlite(connectionString));
        services.AddSingleton<IUserSessionPreferencesStore, SqliteUserSessionPreferencesStore>();

        return services;
    }

    public static async Task MigrateTyrianLedgerUserSessionPreferencesAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<UserSessionPreferencesDbContext>>();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    private static string GetDefaultDatabasePath(IHostEnvironment environment)
    {
        var baseDirectory = environment.IsEnvironment("Testing")
            ? Path.Combine(Path.GetTempPath(), "TyrianLedger", "Testing", Guid.NewGuid().ToString("N"))
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TyrianLedger");

        return Path.Combine(baseDirectory, "user-session-preferences.db");
    }
}

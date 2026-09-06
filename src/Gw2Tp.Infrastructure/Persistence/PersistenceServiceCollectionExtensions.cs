using Gw2Tp.Application.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Gw2Tp.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddTyrianLedgerPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<SqliteDatabasePathResolver>();
        services.AddSingleton<ISqliteConnectionFactory>(serviceProvider => new SqliteConnectionFactory(
            serviceProvider.GetRequiredService<SqliteDatabasePathResolver>().Resolve()));
        services.AddSingleton<SqliteSchemaMigrator>();
        services.AddSingleton<SqlitePersonalTradingPostRepository>();
        services.AddSingleton<IPersonalTradingPostRepository>(serviceProvider =>
            serviceProvider.GetRequiredService<SqlitePersonalTradingPostRepository>());
        services.AddSingleton<IItemMetadataRepository, SqliteItemMetadataRepository>();
        services.AddSingleton<IUserSettingsRepository, SqliteUserSettingsRepository>();
        services.AddHostedService<SqliteDatabaseInitializationService>();

        return services;
    }
}

internal sealed class SqliteDatabaseInitializationService(SqliteSchemaMigrator schemaMigrator) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => schemaMigrator.MigrateAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

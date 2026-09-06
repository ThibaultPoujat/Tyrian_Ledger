using Gw2Tp.Application.Persistence;
using Gw2Tp.Application.LocalData;
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
        services.AddSingleton<SqliteConnectionFactory>(serviceProvider => (SqliteConnectionFactory)serviceProvider
            .GetRequiredService<ISqliteConnectionFactory>());
        services.AddSingleton<ISqliteDatabaseGate, SqliteDatabaseGate>();
        services.AddSingleton<IPersonalDataOperationGate, PersonalDataOperationGate>();
        services.AddSingleton<SqliteSchemaMigrator>();
        services.AddSingleton<SqlitePersonalTradingPostRepository>();
        services.AddSingleton<IPersonalTradingPostRepository>(serviceProvider =>
            serviceProvider.GetRequiredService<SqlitePersonalTradingPostRepository>());
        services.AddSingleton<IPersonalTradingPostSynchronizationStore, SqlitePersonalTradingPostSynchronizationStore>();
        services.AddSingleton<IItemMetadataRepository, SqliteItemMetadataRepository>();
        services.AddSingleton<IUserSettingsRepository, SqliteUserSettingsRepository>();
        services.AddSingleton<ILocalDataRecoveryService, SqliteLocalDataRecoveryService>();
        services.AddHostedService<SqliteDatabaseInitializationService>();

        return services;
    }
}

internal sealed class SqliteDatabaseInitializationService(
    SqliteSchemaMigrator schemaMigrator,
    ISqliteDatabaseGate databaseGate,
    ILocalDataRecoveryService recoveryService) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using (var lease = await databaseGate.AcquireAsync(cancellationToken).ConfigureAwait(false))
        {
            await schemaMigrator.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }

        await recoveryService.CleanupStaleRestoreArtifactsAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

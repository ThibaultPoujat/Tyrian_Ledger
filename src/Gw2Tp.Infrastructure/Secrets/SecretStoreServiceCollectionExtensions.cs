using Gw2Tp.Application.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Gw2Tp.Infrastructure.Secrets;

public static class SecretStoreServiceCollectionExtensions
{
    public static IServiceCollection AddTyrianLedgerSecretStore(
        this IServiceCollection services,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddSingleton<MacOsKeychainSecretStore>();

        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            services.AddSingleton<ISecretStore>(serviceProvider =>
                serviceProvider.GetRequiredService<MacOsKeychainSecretStore>());
            return services;
        }

        services.AddSingleton<EnvironmentSecretStore>();
        services.AddSingleton<ISecretStore>(serviceProvider =>
            new DevelopmentSecretStore(
                serviceProvider.GetRequiredService<EnvironmentSecretStore>(),
                serviceProvider.GetRequiredService<MacOsKeychainSecretStore>()));

        return services;
    }
}

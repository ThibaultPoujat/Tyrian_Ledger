using Gw2Tp.Application.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gw2Tp.Infrastructure.Secrets;

public static class SecretStoreServiceCollectionExtensions
{
    public static IServiceCollection AddTyrianLedgerSecretStore(
        this IServiceCollection services,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddSingleton<IPlatformSecretReader>(_ =>
            PlatformSecretReaderFactory.CreateForCurrentOperatingSystem());
        services.AddSingleton<PlatformGw2ApiCredentialReader>();
        services.AddSingleton<OperatingSystemSecretStore>(serviceProvider =>
            new OperatingSystemSecretStore(
                serviceProvider.GetRequiredService<IPlatformSecretReader>(),
                serviceProvider.GetRequiredService<ILogger<OperatingSystemSecretStore>>()));

        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            services.AddSingleton<IGw2ApiCredentialReader>(serviceProvider =>
                serviceProvider.GetRequiredService<PlatformGw2ApiCredentialReader>());
            services.AddSingleton<ISecretStore>(serviceProvider =>
                serviceProvider.GetRequiredService<OperatingSystemSecretStore>());
            return services;
        }

        services.AddSingleton<EnvironmentGw2ApiCredentialReader>();
        services.AddSingleton<PreferredGw2ApiCredentialReader>(serviceProvider =>
            new PreferredGw2ApiCredentialReader(
                serviceProvider.GetRequiredService<EnvironmentGw2ApiCredentialReader>(),
                serviceProvider.GetRequiredService<PlatformGw2ApiCredentialReader>()));
        services.AddSingleton<IGw2ApiCredentialReader>(serviceProvider =>
            serviceProvider.GetRequiredService<PreferredGw2ApiCredentialReader>());
        services.AddSingleton<EnvironmentSecretStore>();
        services.AddSingleton<ISecretStore>(serviceProvider =>
            new DevelopmentSecretStore(
                serviceProvider.GetRequiredService<EnvironmentSecretStore>(),
                serviceProvider.GetRequiredService<OperatingSystemSecretStore>()));

        return services;
    }
}

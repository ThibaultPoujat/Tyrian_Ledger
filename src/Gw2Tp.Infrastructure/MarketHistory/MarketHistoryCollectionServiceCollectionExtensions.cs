using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Gw2Tp.Infrastructure.MarketHistory;

public static class MarketHistoryCollectionServiceCollectionExtensions
{
    public static IServiceCollection AddTyrianLedgerMarketHistoryCollection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IValidateOptions<MarketHistoryCollectionOptions>, MarketHistoryCollectionOptionsValidator>();
        services
            .AddOptions<MarketHistoryCollectionOptions>()
            .Bind(configuration.GetSection(MarketHistoryCollectionOptions.ConfigurationSectionName))
            .ValidateOnStart();
        services.AddSingleton<MarketHistoryCollector>();
        services.AddSingleton<IMarketHistoryCollectionDelay, SystemMarketHistoryCollectionDelay>();
        services.AddHostedService<MarketHistoryCollectionHostedService>();

        return services;
    }
}

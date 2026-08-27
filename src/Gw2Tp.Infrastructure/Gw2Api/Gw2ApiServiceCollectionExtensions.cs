using Gw2Tp.Application.MarketData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Gw2Tp.Infrastructure.Gw2Api;

public static class Gw2ApiServiceCollectionExtensions
{
    private static readonly Uri Gw2ApiBaseAddress = new("https://api.guildwars2.com/v2/");

    public static IServiceCollection AddTyrianLedgerGw2ApiClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<Gw2ApiSchedulerOptions>()
            .Bind(configuration.GetSection(Gw2ApiSchedulerOptions.ConfigurationSectionName))
            .Validate(
                options => options.TryValidate(out _),
                "GW2 API scheduler configuration is invalid.")
            .ValidateOnStart();
        services.AddSingleton<IGw2RequestScheduler, Gw2RequestScheduler>();

        services.AddHttpClient<IGw2ApiClient, Gw2ApiClient>((serviceProvider, httpClient) =>
        {
            httpClient.BaseAddress = Gw2ApiBaseAddress;
            var options = serviceProvider
                .GetRequiredService<IOptions<Gw2ApiSchedulerOptions>>()
                .Value;
            httpClient.Timeout = TimeSpan.FromMilliseconds(options.RequestTimeoutMs);
        });

        return services;
    }
}

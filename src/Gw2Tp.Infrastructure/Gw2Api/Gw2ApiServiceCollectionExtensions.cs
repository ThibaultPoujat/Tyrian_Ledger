using Gw2Tp.Application.MarketData;
using Microsoft.Extensions.DependencyInjection;

namespace Gw2Tp.Infrastructure.Gw2Api;

public static class Gw2ApiServiceCollectionExtensions
{
    private static readonly Uri Gw2ApiBaseAddress = new("https://api.guildwars2.com/v2/");

    public static IServiceCollection AddTyrianLedgerGw2ApiClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient<IGw2ApiClient, Gw2ApiClient>(httpClient =>
        {
            httpClient.BaseAddress = Gw2ApiBaseAddress;
        });

        return services;
    }
}

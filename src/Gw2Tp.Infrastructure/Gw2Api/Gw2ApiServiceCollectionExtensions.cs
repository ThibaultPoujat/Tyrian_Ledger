using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
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

        services.AddSingleton<IValidateOptions<Gw2ApiSchedulerOptions>, Gw2ApiSchedulerOptionsValidator>();
        services.AddSingleton<IValidateOptions<Gw2MarketCacheOptions>, Gw2MarketCacheOptionsValidator>();
        services
            .AddOptions<Gw2ApiSchedulerOptions>()
            .Bind(configuration.GetSection(Gw2ApiSchedulerOptions.ConfigurationSectionName))
            .ValidateOnStart();
        services
            .AddOptions<Gw2MarketCacheOptions>()
            .Bind(configuration
                .GetSection(Gw2ApiSchedulerOptions.ConfigurationSectionName)
                .GetSection(Gw2MarketCacheOptions.ConfigurationSectionName))
            .ValidateOnStart();
        services.AddSingleton<IGw2RequestScheduler, Gw2RequestScheduler>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<MarketDataDiagnostics>();
        services.AddSingleton<IMarketDataDiagnostics>(serviceProvider =>
            serviceProvider.GetRequiredService<MarketDataDiagnostics>());
        services.AddSingleton<IMarketDataDiagnosticsRecorder>(serviceProvider =>
            serviceProvider.GetRequiredService<MarketDataDiagnostics>());

        services.AddHttpClient(Gw2ApiClient.HttpClientName, (serviceProvider, httpClient) =>
        {
            httpClient.BaseAddress = Gw2ApiBaseAddress;
            var options = serviceProvider
                .GetRequiredService<IOptions<Gw2ApiSchedulerOptions>>()
                .Value;
            httpClient.Timeout = TimeSpan.FromMilliseconds(options.RequestTimeoutMs);
        });
        services.Configure<HttpClientFactoryOptions>(Gw2ApiClient.HttpClientName, options =>
            options.ShouldRedactHeaderValue = static _ => true);
        services.AddSingleton<IGw2ApiTransport>(serviceProvider => new Gw2ApiClient(
            serviceProvider.GetRequiredService<IHttpClientFactory>(),
            serviceProvider.GetRequiredService<IGw2RequestScheduler>(),
            serviceProvider.GetRequiredService<IMarketDataDiagnosticsRecorder>()));
        services.AddSingleton<IGw2ApiClient, CachingGw2ApiClient>();
        return services;
    }
}

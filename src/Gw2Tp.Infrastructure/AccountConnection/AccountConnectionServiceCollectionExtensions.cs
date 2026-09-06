using Gw2Tp.Application.AccountConnection;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Infrastructure.Secrets;
using Gw2Tp.Infrastructure.Gw2Api;
using Gw2Tp.Infrastructure.PersonalTradingPost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Gw2Tp.Infrastructure.AccountConnection;

public static class AccountConnectionServiceCollectionExtensions
{
    private static readonly Uri Gw2ApiBaseAddress = new("https://api.guildwars2.com/v2/");

    public static IServiceCollection AddTyrianLedgerAccountConnection(
        this IServiceCollection services,
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddTyrianLedgerGw2ApiClient(configuration);
        services.AddSingleton<OperatingSystemGw2ApiKeySource>();
        services.AddSingleton<EnvironmentGw2ApiKeySource>();
        services.AddSingleton<IGw2ApiKeySource>(serviceProvider =>
        {
            var environmentSource = serviceProvider.GetRequiredService<EnvironmentGw2ApiKeySource>();
            if (environment.IsEnvironment("Testing"))
            {
                return environmentSource;
            }

            var operatingSystemSource = serviceProvider.GetRequiredService<OperatingSystemGw2ApiKeySource>();
            return environment.IsDevelopment()
                ? new DevelopmentGw2ApiKeySource(environmentSource, operatingSystemSource)
                : operatingSystemSource;
        });
        services.AddHttpClient(AccountConnectionStatusService.HttpClientName, (serviceProvider, httpClient) =>
        {
            httpClient.BaseAddress = Gw2ApiBaseAddress;
            var options = serviceProvider
                .GetRequiredService<IOptions<Gw2ApiSchedulerOptions>>()
                .Value;
            httpClient.Timeout = TimeSpan.FromMilliseconds(options.RequestTimeoutMs);
        });
        services.Configure<HttpClientFactoryOptions>(AccountConnectionStatusService.HttpClientName, options =>
            options.ShouldRedactHeaderValue = static _ => true);
        services.AddSingleton<AccountConnectionStatusService>(serviceProvider => new AccountConnectionStatusService(
            serviceProvider.GetRequiredService<IGw2ApiKeySource>(),
            serviceProvider.GetRequiredService<IHttpClientFactory>()
                .CreateClient(AccountConnectionStatusService.HttpClientName),
            serviceProvider.GetRequiredService<IGw2RequestScheduler>(),
            TimeSpan.FromMilliseconds(serviceProvider
                .GetRequiredService<IOptions<Gw2ApiSchedulerOptions>>()
                .Value
                .RequestTimeoutMs)));
        services.AddSingleton<IAccountConnectionStatusService>(serviceProvider =>
            new CachedAccountConnectionStatusService(
                serviceProvider.GetRequiredService<AccountConnectionStatusService>()));
        services.AddHttpClient(PersonalTradingPostGateway.HttpClientName, (serviceProvider, httpClient) =>
        {
            httpClient.BaseAddress = Gw2ApiBaseAddress;
            var options = serviceProvider
                .GetRequiredService<IOptions<Gw2ApiSchedulerOptions>>()
                .Value;
            httpClient.Timeout = TimeSpan.FromMilliseconds(options.RequestTimeoutMs);
        });
        services.Configure<HttpClientFactoryOptions>(PersonalTradingPostGateway.HttpClientName, options =>
            options.ShouldRedactHeaderValue = static _ => true);
        services.AddSingleton<IPersonalTradingPostGateway>(serviceProvider => new PersonalTradingPostGateway(
            serviceProvider.GetRequiredService<IGw2ApiKeySource>(),
            serviceProvider.GetRequiredService<IHttpClientFactory>()
                .CreateClient(PersonalTradingPostGateway.HttpClientName),
            serviceProvider.GetRequiredService<IGw2RequestScheduler>(),
            TimeSpan.FromMilliseconds(serviceProvider
                .GetRequiredService<IOptions<Gw2ApiSchedulerOptions>>()
                .Value
                .RequestTimeoutMs)));

        return services;
    }
}

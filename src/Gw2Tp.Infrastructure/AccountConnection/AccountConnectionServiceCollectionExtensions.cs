using Gw2Tp.Application.AccountConnection;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Infrastructure.Secrets;
using Gw2Tp.Infrastructure.Gw2Api;
using Gw2Tp.Infrastructure.Diagnostics;
using Gw2Tp.Infrastructure.PersonalTradingPost;
using Gw2Tp.Infrastructure.Crafting;
using Gw2Tp.Application.Crafting;
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
        services.AddSingleton<SafeTransportDiagnosticBuffer>();
        services.AddSingleton<ICraftingEvidenceDiagnostics>(serviceProvider =>
            serviceProvider.GetRequiredService<SafeTransportDiagnosticBuffer>());
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
        }).RemoveAllLoggers();
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
        }).RemoveAllLoggers();
        services.Configure<HttpClientFactoryOptions>(PersonalTradingPostGateway.HttpClientName, options =>
            options.ShouldRedactHeaderValue = static _ => true);
        services.AddSingleton<PersonalTradingPostGateway>(serviceProvider => new PersonalTradingPostGateway(
            serviceProvider.GetRequiredService<IGw2ApiKeySource>(),
            serviceProvider.GetRequiredService<IHttpClientFactory>()
                .CreateClient(PersonalTradingPostGateway.HttpClientName),
            serviceProvider.GetRequiredService<IGw2RequestScheduler>(),
            TimeSpan.FromMilliseconds(serviceProvider
                .GetRequiredService<IOptions<Gw2ApiSchedulerOptions>>()
                .Value
                .RequestTimeoutMs),
            serviceProvider.GetRequiredService<SafeTransportDiagnosticBuffer>()));
        services.AddSingleton<IPersonalTradingPostGateway>(serviceProvider =>
            serviceProvider.GetRequiredService<PersonalTradingPostGateway>());
        services.AddSingleton<IAccountPortfolioGateway>(serviceProvider =>
            serviceProvider.GetRequiredService<PersonalTradingPostGateway>());
        services.AddHttpClient(AccountCraftingGateway.HttpClientName, (serviceProvider, httpClient) =>
        {
            httpClient.BaseAddress = Gw2ApiBaseAddress;
            httpClient.Timeout = TimeSpan.FromMilliseconds(serviceProvider
                .GetRequiredService<IOptions<Gw2ApiSchedulerOptions>>().Value.RequestTimeoutMs);
        }).RemoveAllLoggers();
        services.Configure<HttpClientFactoryOptions>(AccountCraftingGateway.HttpClientName, options =>
            options.ShouldRedactHeaderValue = static _ => true);
        services.AddSingleton<IAccountCraftingGateway>(serviceProvider => new AccountCraftingGateway(
            serviceProvider.GetRequiredService<IGw2ApiKeySource>(),
            serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(AccountCraftingGateway.HttpClientName),
            serviceProvider.GetRequiredService<IGw2RequestScheduler>(),
            TimeSpan.FromMilliseconds(serviceProvider.GetRequiredService<IOptions<Gw2ApiSchedulerOptions>>().Value.RequestTimeoutMs)));
        services.AddHttpClient(CraftingReferenceGateway.HttpClientName, (serviceProvider, httpClient) =>
        {
            httpClient.BaseAddress = Gw2ApiBaseAddress;
            httpClient.Timeout = TimeSpan.FromMilliseconds(serviceProvider
                .GetRequiredService<IOptions<Gw2ApiSchedulerOptions>>().Value.RequestTimeoutMs);
        }).RemoveAllLoggers();
        services.AddSingleton<ICraftingReferenceGateway>(serviceProvider => new CraftingReferenceGateway(
            serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(CraftingReferenceGateway.HttpClientName),
            serviceProvider.GetRequiredService<IGw2RequestScheduler>()));
        services.AddSingleton<IPersonalTradingPostSynchronizationService, PersonalTradingPostSynchronizationService>();

        return services;
    }
}

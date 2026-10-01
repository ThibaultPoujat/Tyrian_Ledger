using Gw2Tp.Application.AccountEvidence;
using Gw2Tp.Application.LocalData;
using Gw2Tp.Application.Time;
using Gw2Tp.Infrastructure.Gw2Api;
using Gw2Tp.Infrastructure.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Gw2Tp.Infrastructure.AccountEvidence;

/// <summary>Typed collector used by the guarded production account refresh; no independent polling timer.</summary>
public static class AccountHoldingsServiceCollectionExtensions
{
    public static IServiceCollection AddTyrianLedgerAccountHoldingsCollector(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        // Requires the existing account connection's credential source, scheduler and clock.
        services.AddHttpClient(AccountHoldingsGateway.HttpClientName, (provider, client) =>
        {
            client.BaseAddress = new Uri("https://api.guildwars2.com/v2/");
            client.Timeout = TimeSpan.FromMilliseconds(provider.GetRequiredService<IOptions<Gw2ApiSchedulerOptions>>().Value.RequestTimeoutMs);
        }).RemoveAllLoggers();
        services.Configure<HttpClientFactoryOptions>(AccountHoldingsGateway.HttpClientName,
            options => options.ShouldRedactHeaderValue = static _ => true);
        services.AddSingleton<IAccountHoldingsCollector>(provider => new AccountHoldingsGateway(
            provider.GetRequiredService<IGw2ApiKeySource>(),
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(AccountHoldingsGateway.HttpClientName),
            provider.GetRequiredService<IGw2RequestScheduler>(), provider.GetRequiredService<IClock>(),
            TimeSpan.FromMilliseconds(provider.GetRequiredService<IOptions<Gw2ApiSchedulerOptions>>().Value.RequestTimeoutMs),
            provider.GetService<IAccountWorkFence>()));
        return services;
    }
}

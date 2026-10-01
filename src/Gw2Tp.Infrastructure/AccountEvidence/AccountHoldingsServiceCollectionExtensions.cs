using Gw2Tp.Application.AccountEvidence;
using Gw2Tp.Application.Time;
using Gw2Tp.Infrastructure.Gw2Api;
using Gw2Tp.Infrastructure.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Gw2Tp.Infrastructure.AccountEvidence;

/// <summary>Explicit opt-in seam for later tickets/tests. The production host does not invoke this registration.</summary>
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
            TimeSpan.FromMilliseconds(provider.GetRequiredService<IOptions<Gw2ApiSchedulerOptions>>().Value.RequestTimeoutMs)));
        return services;
    }
}

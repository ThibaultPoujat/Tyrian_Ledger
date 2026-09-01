using Gw2Tp.Application.MarketSnapshots;
using Gw2Tp.Infrastructure.Gw2Api;
using Gw2Tp.MarketSnapshotGenerator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

return await MarketSnapshotGeneratorHost.RunAsync(args);

public static class MarketSnapshotGeneratorHost
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter? standardOutput = null,
        TextWriter? standardError = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);

        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddTyrianLedgerMarketSnapshotGateway(builder.Configuration);
        builder.Services.AddSingleton<PublicMarketSnapshotCollector>();
        builder.Services.AddSingleton<MarketSnapshotJsonWriter>();
        builder.Services.AddSingleton<MarketSnapshotGeneratorCommand>();

        await using var serviceProvider = builder.Services.BuildServiceProvider();
        var command = serviceProvider.GetRequiredService<MarketSnapshotGeneratorCommand>();
        return await command.RunAsync(
            args,
            standardOutput ?? Console.Out,
            standardError ?? Console.Error,
            cancellationToken).ConfigureAwait(false);
    }
}

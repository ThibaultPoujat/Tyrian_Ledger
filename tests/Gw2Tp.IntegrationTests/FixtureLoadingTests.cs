using System.Text.Json;
using Gw2Tp.Testing;
using Xunit;

namespace Gw2Tp.IntegrationTests;

public sealed class FixtureLoadingTests
{
    [Fact]
    public async Task Prices_fixture_loads_as_the_expected_synthetic_market_payload()
    {
        var fixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var fixtureLoader = new JsonFixtureLoader(fixtureRoot);

        using var document = await fixtureLoader.LoadAsync("gw2/commerce/prices.json");

        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Equal(3, document.RootElement.GetArrayLength());
        Assert.Equal(900001, document.RootElement[0].GetProperty("id").GetInt32());
        Assert.Equal(850, document.RootElement[0].GetProperty("buys").GetProperty("unit_price").GetInt32());
    }

    [Fact]
    public async Task Fixture_loader_rejects_paths_outside_the_fixture_root()
    {
        var fixtureLoader = new JsonFixtureLoader(AppContext.BaseDirectory);

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => fixtureLoader.LoadAsync("../outside.json"));

        Assert.Equal("relativePath", exception.ParamName);
    }
}

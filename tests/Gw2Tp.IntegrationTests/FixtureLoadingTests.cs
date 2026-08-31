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
    public async Task Whole_market_discovery_fixtures_are_synthetic_and_minimal()
    {
        var fixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var fixtureLoader = new JsonFixtureLoader(fixtureRoot);

        using var index = await fixtureLoader.LoadAsync("gw2/commerce/price-item-ids.json");
        using var items = await fixtureLoader.LoadAsync("gw2/items/metadata.json");

        Assert.Equal(JsonValueKind.Array, index.RootElement.ValueKind);
        Assert.Equal(900001, index.RootElement[0].GetInt32());
        Assert.Equal("Synthetic Mithril Widget", items.RootElement[0].GetProperty("name").GetString());
        Assert.False(items.RootElement[0].TryGetProperty("max_stack", out _));
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

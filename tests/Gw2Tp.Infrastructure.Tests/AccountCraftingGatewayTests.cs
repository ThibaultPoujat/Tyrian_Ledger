using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Infrastructure.Crafting;
using Gw2Tp.Infrastructure.Gw2Api;
using Gw2Tp.Infrastructure.Secrets;
using Gw2Tp.Testing;
using Xunit;

namespace Gw2Tp.Infrastructure.Tests;

public sealed class AccountCraftingGatewayTests
{
    private const string SyntheticKey = "synthetic-crafting-key-never-persisted-or-returned";

    [Fact]
    public async Task Snapshot_normalizes_account_sources_discards_character_names_and_keeps_binding_evidence()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/v2/account" => JsonFixture("account.json"),
            "/v2/account/bank" => JsonFixture("bank.json"),
            "/v2/account/materials" => JsonFixture("materials.json"),
            "/v2/account/recipes" => JsonFixture("recipes.json"),
            "/v2/characters" => JsonFixture("characters.json"),
            "/v2/characters/Synthetic%20Crafter%20One/crafting" => JsonFixture("character-one.json"),
            "/v2/characters/Synthetic%20Crafter%20Two/crafting" => JsonFixture("character-two.json"),
            _ => throw new InvalidOperationException(request.RequestUri!.AbsolutePath),
        });
        using var client = Client(handler);
        var gateway = new AccountCraftingGateway(new FixedKeySource(SyntheticKey), client, new ImmediateScheduler());

        var result = await gateway.GetSnapshotAsync();

        Assert.True(result.IsSuccess);
        var snapshot = Assert.IsType<AccountCraftingSnapshot>(result.Value);
        Assert.Equal("synthetic-crafting-account-920001", snapshot.AccountScope.AccountId);
        Assert.Equal(CraftingFeatureAvailability.Available, snapshot.BankInventory.Availability);
        Assert.Contains(snapshot.BankInventory.Value!, entry => entry.ItemId == 920101 && entry.Binding == AccountItemBinding.AccountBound);
        Assert.Equal(0, Assert.Single(snapshot.MaterialStorage.Value!, entry => entry.ItemId == 920201).Quantity);
        Assert.Equal(AccountItemBinding.AccountBound, Assert.Single(snapshot.MaterialStorage.Value!, entry => entry.ItemId == 920202).Binding);
        Assert.Equal([920301, 920302], snapshot.RecipeUnlocks.Value);
        var artificer = Assert.Single(snapshot.CharacterCrafting.Value!, capability => capability.Discipline == "Artificer");
        Assert.Equal(500, artificer.Rating);
        Assert.True(artificer.IsActive);
        Assert.DoesNotContain("Synthetic Crafter", snapshot.ToString(), StringComparison.Ordinal);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(SyntheticKey, request.Headers.Authorization?.Parameter);
            Assert.Equal(AccountCraftingGateway.SchemaVersion, Query(request.RequestUri!)["v"]);
        });
        Assert.DoesNotContain(handler.Requests.Select(request => request.RequestUri!.Query), query => query.Contains("access_token", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Missing_permissions_disable_only_affected_features_and_malformed_or_partial_sources_fail_closed()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/v2/account" => JsonFixture("account.json"),
            "/v2/account/bank" => new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new StringContent("[]", Encoding.UTF8, "application/json") },
            "/v2/account/materials" => new HttpResponseMessage(HttpStatusCode.Forbidden),
            "/v2/account/recipes" => new HttpResponseMessage(HttpStatusCode.Forbidden),
            "/v2/characters" => JsonFixture("characters.json"),
            "/v2/characters/Synthetic%20Crafter%20One/crafting" => Json("{\"crafting\":[{\"discipline\":\"Chef\",\"rating\":-1,\"active\":true}]}"),
            "/v2/characters/Synthetic%20Crafter%20Two/crafting" => JsonFixture("character-two.json"),
            _ => throw new InvalidOperationException(request.RequestUri!.AbsolutePath),
        });
        using var client = Client(handler);
        var gateway = new AccountCraftingGateway(new FixedKeySource(SyntheticKey), client, new ImmediateScheduler());

        var result = await gateway.GetSnapshotAsync();

        Assert.True(result.IsSuccess);
        var snapshot = Assert.IsType<AccountCraftingSnapshot>(result.Value);
        Assert.Equal(CraftingFeatureAvailability.Unavailable, snapshot.BankInventory.Availability);
        Assert.Equal(Gw2ApiErrorCategory.IncompleteData, snapshot.BankInventory.ErrorCategory);
        Assert.Equal(CraftingFeatureAvailability.MissingPermission, snapshot.MaterialStorage.Availability);
        Assert.Equal(CraftingFeatureAvailability.MissingPermission, snapshot.RecipeUnlocks.Availability);
        Assert.Equal(CraftingFeatureAvailability.Unavailable, snapshot.CharacterCrafting.Availability);
        Assert.Equal(Gw2ApiErrorCategory.InvalidPayload, snapshot.CharacterCrafting.ErrorCategory);
        Assert.Null(snapshot.MaterialStorage.Value);
        Assert.Null(snapshot.CharacterCrafting.Value);
    }

    [Fact]
    public async Task Recipe_definitions_are_bounded_normalized_and_reject_incomplete_or_malformed_responses()
    {
        var handler = new RecordingHandler(_ => JsonFixture("recipe-definitions.json"));
        using var client = Client(handler);
        var gateway = new CraftingReferenceGateway(client, new ImmediateScheduler());

        var result = await gateway.GetRecipesAsync([920301, 920302]);

        Assert.True(result.IsSuccess);
        var recipe = Assert.Single(result.Value!, recipe => recipe.RecipeId == 920301);
        Assert.Contains(recipe.Ingredients, ingredient => ingredient.Type == "Currency" && ingredient.Id == 1);
        Assert.Equal("recipes", handler.Requests.Single().RequestUri!.AbsolutePath.Trim('/').Split('/').Last());
        Assert.Equal(CraftingReferenceGateway.SchemaVersion, Query(handler.Requests.Single().RequestUri!)["v"]);
        Assert.Equal("920301,920302", Query(handler.Requests.Single().RequestUri!)["ids"]);

        var incomplete = new CraftingReferenceGateway(Client(new RecordingHandler(_ => Json("[{}]"))), new ImmediateScheduler());
        var failed = await incomplete.GetRecipesAsync([920301]);
        Assert.False(failed.IsSuccess);
        Assert.Equal(Gw2ApiErrorCategory.InvalidPayload, failed.ErrorCategory);
    }

    private static HttpClient Client(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("https://api.guildwars2.com/v2/") };
    private static HttpResponseMessage JsonFixture(string fixture) => Json(Load(fixture));
    private static HttpResponseMessage Json(string payload) => new(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
    private static string Load(string fixture)
    {
        var loader = new JsonFixtureLoader(Path.Combine(AppContext.BaseDirectory, "Fixtures"));
        using var document = loader.LoadAsync($"gw2/crafting/{fixture}").GetAwaiter().GetResult();
        return document.RootElement.GetRawText();
    }
    private static IReadOnlyDictionary<string, string> Query(Uri uri) => uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(pair => pair.Split('=', 2)).ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]), StringComparer.Ordinal);

    private sealed class FixedKeySource(string value) : IGw2ApiKeySource
    {
        public ValueTask<Gw2ApiKeyReadResult> ReadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Gw2ApiKeyReadResult.FromValue(value));
    }
    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(response(request));
        }
    }
    private sealed class ImmediateScheduler : IGw2RequestScheduler
    {
        public async Task<T> ScheduleAsync<T>(Gw2RequestKey requestKey, Func<CancellationToken, Task<Gw2ScheduledResult<T>>> sendAsync, CancellationToken cancellationToken) => (await sendAsync(cancellationToken).ConfigureAwait(false)).Result;
    }
}

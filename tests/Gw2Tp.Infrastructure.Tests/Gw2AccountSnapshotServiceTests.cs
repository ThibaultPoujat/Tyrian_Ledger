using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Gw2Tp.Application.AccountAccess;
using Gw2Tp.Application.AccountSnapshots;
using Gw2Tp.Application.Time;
using Gw2Tp.Infrastructure.AccountSnapshots;
using Gw2Tp.Infrastructure.Gw2Api;
using Gw2Tp.Infrastructure.Secrets;
using Gw2Tp.Testing;
using Xunit;

namespace Gw2Tp.Infrastructure.Tests;

public sealed class Gw2AccountSnapshotServiceTests
{
    private const string SyntheticCredential = "synthetic-gw2-api-credential-for-account-snapshot-tests";
    private static readonly DateTimeOffset CapturedAtUtc =
        new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Owned_item_fixtures_are_mapped_to_minimized_data_and_cached_per_feature()
    {
        var bank = await LoadFixturePayloadAsync("gw2/account/bank.json");
        var materials = await LoadFixturePayloadAsync("gw2/account/materials.json");
        var handler = new StubHttpMessageHandler((request, _) => Task.FromResult(
            CreateJsonResponse(request.RequestUri!.AbsolutePath switch
            {
                "/v2/account/bank" => bank,
                "/v2/account/materials" => materials,
                _ => throw new Xunit.Sdk.XunitException("Unexpected account endpoint."),
            })));
        using var httpClient = CreateHttpClient(handler);
        var service = CreateService(httpClient, CreateAccessService("profile-alpha"));

        var first = await service.GetOwnedItemsAsync();
        var cached = await service.GetOwnedItemsAsync();
        var refreshed = await service.GetOwnedItemsAsync(AccountSnapshotRefreshMode.ForceRefresh);

        Assert.Equal(AccountSnapshotLoadStatus.Available, first.Status);
        var snapshot = Assert.IsType<AccountOwnedItemsSnapshot>(first.Snapshot);
        Assert.Equal("profile-alpha", snapshot.ProfileId);
        var boundBankItem = Assert.Single(snapshot.BankItems, item => item.ItemId == 910001);
        Assert.Equal(7, boundBankItem.Count);
        Assert.Equal("Account", boundBankItem.Binding);
        Assert.Equal(2, snapshot.Materials.Count);
        Assert.Equal(0, snapshot.Materials[0].Count);
        Assert.Equal(250, snapshot.Materials[1].Count);
        Assert.Equal(CapturedAtUtc, first.Freshness!.CapturedAtUtc);
        Assert.Equal(CapturedAtUtc.AddMinutes(5), first.Freshness.ExpiresAtUtc);
        Assert.Equal(first, cached);
        Assert.Equal(AccountSnapshotLoadStatus.Available, refreshed.Status);
        Assert.Equal(4, handler.Requests.Count);
        Assert.All(handler.Requests, AssertAuthenticatedSchemaPinnedGet);
        Assert.DoesNotContain(SyntheticCredential, JsonSerializer.Serialize(first));
    }

    [Fact]
    public async Task Crafting_fixtures_fetch_only_the_enabled_crafting_endpoints()
    {
        var recipes = await LoadFixturePayloadAsync("gw2/account/recipes.json");
        var characters = await LoadFixturePayloadAsync("gw2/account/characters.json");
        var liraCrafting = await LoadFixturePayloadAsync("gw2/account/lira-mirage-crafting.json");
        var zojjaCrafting = await LoadFixturePayloadAsync("gw2/account/zojja-test-crafting.json");
        var handler = new StubHttpMessageHandler((request, _) => Task.FromResult(CreateJsonResponse(
            Uri.UnescapeDataString(request.RequestUri!.AbsolutePath) switch
            {
                "/v2/account/recipes" => recipes,
                "/v2/characters" => characters,
                "/v2/characters/Lira Mirage/crafting" => liraCrafting,
                "/v2/characters/Zojja Test/crafting" => zojjaCrafting,
                _ => throw new Xunit.Sdk.XunitException("Unexpected account endpoint."),
            })));
        using var httpClient = CreateHttpClient(handler);
        var service = CreateService(httpClient, CreateAccessService("profile-alpha"));

        var result = await service.GetCraftingAsync();

        Assert.Equal(AccountSnapshotLoadStatus.Available, result.Status);
        var snapshot = Assert.IsType<AccountCraftingSnapshot>(result.Snapshot);
        Assert.Equal([930001, 930002], snapshot.UnlockedRecipeIds);
        Assert.Equal(["Lira Mirage", "Zojja Test"], snapshot.Characters.Select(character => character.CharacterName));
        Assert.Equal("Artificer", snapshot.Characters[0].Disciplines[0].Discipline);
        Assert.Equal(500, snapshot.Characters[0].Disciplines[0].Rating);
        Assert.True(snapshot.Characters[0].Disciplines[0].IsActive);
        Assert.Equal("Weaponsmith", snapshot.Characters[1].Disciplines[0].Discipline);
        Assert.False(snapshot.Characters[1].Disciplines[0].IsActive);
        Assert.Equal(4, handler.Requests.Count);
        Assert.DoesNotContain(handler.Requests, request => request.Uri.AbsolutePath.StartsWith("/v2/account/bank", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.Requests, request => request.Uri.AbsolutePath.StartsWith("/v2/account/materials", StringComparison.Ordinal));
        Assert.All(handler.Requests, AssertAuthenticatedSchemaPinnedGet);
    }

    [Fact]
    public async Task Missing_permissions_skip_the_feature_without_sending_a_protected_request()
    {
        var status = new AccountAccessStatus(
            AccountAccessValidationStatus.Valid,
            "profile-alpha",
            "Synthetic key",
            ["account"],
            [
                new AccountFeatureAccess("account-materials", false, ["inventories"]),
                new AccountFeatureAccess("account-crafting", false, ["characters", "unlocks"]),
            ]);
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new Xunit.Sdk.XunitException("No HTTP request should be sent."));
        using var httpClient = CreateHttpClient(handler);
        var service = CreateService(httpClient, new FixedAccountAccessService(status));

        var result = await service.GetOwnedItemsAsync();

        Assert.Equal(AccountSnapshotLoadStatus.MissingPermission, result.Status);
        Assert.Null(result.Snapshot);
        Assert.Equal(["inventories"], result.MissingPermissions);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Upstream_permission_failures_are_safe_and_are_not_cached()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.Forbidden)));
        using var httpClient = CreateHttpClient(handler);
        var service = CreateService(httpClient, CreateAccessService("profile-alpha"));

        var first = await service.GetOwnedItemsAsync();
        var second = await service.GetOwnedItemsAsync();

        Assert.Equal(AccountSnapshotLoadStatus.PermissionDenied, first.Status);
        Assert.Null(first.Snapshot);
        Assert.Equal(AccountSnapshotLoadStatus.PermissionDenied, second.Status);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task Account_cache_never_shares_data_between_safe_profile_identifiers()
    {
        var bank = await LoadFixturePayloadAsync("gw2/account/bank.json");
        var materials = await LoadFixturePayloadAsync("gw2/account/materials.json");
        var handler = new StubHttpMessageHandler((request, _) => Task.FromResult(
            CreateJsonResponse(request.RequestUri!.AbsolutePath == "/v2/account/bank" ? bank : materials)));
        using var httpClient = CreateHttpClient(handler);
        var service = CreateService(
            httpClient,
            new SequencedAccountAccessService(
            [
                CreateValidAccessStatus("profile-alpha"),
                CreateValidAccessStatus("profile-beta"),
                CreateValidAccessStatus("profile-alpha"),
            ]));

        var alpha = await service.GetOwnedItemsAsync();
        var beta = await service.GetOwnedItemsAsync();
        var cachedAlpha = await service.GetOwnedItemsAsync();

        Assert.Equal("profile-alpha", alpha.Snapshot!.ProfileId);
        Assert.Equal("profile-beta", beta.Snapshot!.ProfileId);
        Assert.Equal("profile-alpha", cachedAlpha.Snapshot!.ProfileId);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task Failed_character_ingestion_preserves_other_crafting_facts_without_caching_the_partial_snapshot()
    {
        var recipes = await LoadFixturePayloadAsync("gw2/account/recipes.json");
        var characters = await LoadFixturePayloadAsync("gw2/account/characters.json");
        var liraCrafting = await LoadFixturePayloadAsync("gw2/account/lira-mirage-crafting.json");
        var handler = new StubHttpMessageHandler((request, _) => Task.FromResult(
            Uri.UnescapeDataString(request.RequestUri!.AbsolutePath) switch
            {
                "/v2/account/recipes" => CreateJsonResponse(recipes),
                "/v2/characters" => CreateJsonResponse(characters),
                "/v2/characters/Lira Mirage/crafting" => CreateJsonResponse(liraCrafting),
                "/v2/characters/Zojja Test/crafting" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                _ => throw new Xunit.Sdk.XunitException("Unexpected account endpoint."),
            }));
        using var httpClient = CreateHttpClient(handler);
        var service = CreateService(httpClient, CreateAccessService("profile-alpha"));

        var result = await service.GetCraftingAsync();

        Assert.Equal(AccountSnapshotLoadStatus.PartialData, result.Status);
        Assert.NotNull(result.Snapshot);
        Assert.Equal([930001, 930002], result.Snapshot.UnlockedRecipeIds);
        Assert.Equal(["Lira Mirage"], result.Snapshot.Characters.Select(character => character.CharacterName));
        var failure = Assert.Single(result.Failures);
        Assert.Equal(AccountSnapshotComponent.CharacterCrafting, failure.Component);
        Assert.Equal("Zojja Test", failure.CharacterName);
        Assert.Null(result.Freshness);
    }

    private static Gw2AccountSnapshotService CreateService(
        HttpClient httpClient,
        IAccountAccessService accountAccessService) =>
        new(
            httpClient,
            PassthroughRequestScheduler.Instance,
            new EnvironmentGw2ApiCredentialReader(_ => SyntheticCredential),
            accountAccessService,
            new Gw2AccountSnapshotCacheOptions { TimeToLiveSeconds = 300 },
            new FrozenClock(CapturedAtUtc));

    private static IAccountAccessService CreateAccessService(string profileId) =>
        new FixedAccountAccessService(CreateValidAccessStatus(profileId));

    private static AccountAccessStatus CreateValidAccessStatus(string profileId) =>
        new(
            AccountAccessValidationStatus.Valid,
            profileId,
            "Synthetic key",
            ["account", "characters", "inventories", "unlocks"],
            [
                new AccountFeatureAccess("account-materials", true, []),
                new AccountFeatureAccess("account-crafting", true, []),
            ]);

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri("https://api.guildwars2.com/v2/"),
    };

    private static HttpResponseMessage CreateJsonResponse(string payload) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(payload, Encoding.UTF8, "application/json"),
    };

    private static void AssertAuthenticatedSchemaPinnedGet(CapturedRequest request)
    {
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("Bearer", request.Authorization?.Scheme);
        Assert.Equal(SyntheticCredential, request.Authorization?.Parameter);
        Assert.Equal($"?v={Gw2ApiClient.SchemaVersion}", request.Uri.Query);
    }

    private static async Task<string> LoadFixturePayloadAsync(string relativePath)
    {
        var loader = new JsonFixtureLoader(Path.Combine(AppContext.BaseDirectory, "Fixtures"));
        using var document = await loader.LoadAsync(relativePath);
        return document.RootElement.GetRawText();
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responseFactory;

        public StubHttpMessageHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public List<CapturedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request.RequestUri);
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri,
                request.Headers.Authorization));
            return _responseFactory(request, cancellationToken);
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri Uri,
        AuthenticationHeaderValue? Authorization);

    private sealed class FixedAccountAccessService : IAccountAccessService
    {
        private readonly AccountAccessStatus _status;

        public FixedAccountAccessService(AccountAccessStatus status)
        {
            _status = status;
        }

        public Task<AccountAccessStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_status);
    }

    private sealed class SequencedAccountAccessService : IAccountAccessService
    {
        private readonly Queue<AccountAccessStatus> _statuses;

        public SequencedAccountAccessService(IEnumerable<AccountAccessStatus> statuses)
        {
            _statuses = new Queue<AccountAccessStatus>(statuses);
        }

        public Task<AccountAccessStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_statuses.Dequeue());
    }

    private sealed class PassthroughRequestScheduler : IGw2RequestScheduler
    {
        public static readonly PassthroughRequestScheduler Instance = new();

        public async Task<T> ScheduleAsync<T>(
            Gw2RequestKey requestKey,
            Func<CancellationToken, Task<Gw2ScheduledResult<T>>> sendAsync,
            CancellationToken cancellationToken) =>
            (await sendAsync(cancellationToken)).Result;
    }
}

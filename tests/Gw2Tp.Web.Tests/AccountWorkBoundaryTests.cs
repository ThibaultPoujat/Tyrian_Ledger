using Gw2Tp.Application.LocalData;
using Gw2Tp.Application.Crafting;
using Gw2Tp.Application.MarketData;
using Gw2Tp.Application.PersonalTradingPost;
using Gw2Tp.Application.Plans;
using Gw2Tp.Web.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.TestHost;
using System.Net.Http.Json;
using System.Text.Json;
using Gw2Tp.Application.Time;
using Xunit;

namespace Gw2Tp.Web.Tests;

public sealed class AccountWorkBoundaryTests
{
    [Fact]
    public async Task Real_clear_restore_and_restart_rotate_browser_scope_before_command_lookup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TyrianLedger.Scope.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "test.db");
        string restoredScope;
        try
        {
            await using (var app = CreateHost(path))
            {
                await app.StartAsync(); using var client = app.GetTestClient();
                var initial = await ReadScope(client);
                using var backupResponse = await Post(client, "/api/local-data/backup", new { });
                backupResponse.EnsureSuccessStatusCode();
                var backup = JsonDocument.Parse(await backupResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("fileName").GetString();
                using var clearResponse = await Post(client, "/api/local-data/clear-personal", new { confirmation = LocalDataEndpoints.ClearConfirmation });
                clearResponse.EnsureSuccessStatusCode();
                var cleared = await ReadScope(client); Assert.NotEqual(initial, cleared);
                await RejectOldCommand(client, initial);
                using var restoreResponse = await Post(client, "/api/local-data/restore-managed", new { confirmation = LocalDataEndpoints.RestoreConfirmation, backupFileName = backup });
                restoreResponse.EnsureSuccessStatusCode();
                restoredScope = await ReadScope(client); Assert.NotEqual(cleared, restoredScope);
                await RejectOldCommand(client, cleared);
                await app.StopAsync();
            }
            await using (var restarted = CreateHost(path))
            {
                await restarted.StartAsync(); using var client = restarted.GetTestClient();
                Assert.NotEqual(restoredScope, await ReadScope(client));
                await RejectOldCommand(client, restoredScope);
                await restarted.StopAsync();
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static Microsoft.AspNetCore.Builder.WebApplication CreateHost(string path) => Program.CreateApplication([], builder =>
    {
        builder.Environment.EnvironmentName = "Testing";
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["TyrianLedger:Database:Path"] = path });
    }, services =>
    {
        services.AddSingleton<IPersonalTradingPostGateway>(new ScopeGateway());
        foreach (var worker in services.Where(value => value.ServiceType == typeof(IHostedService) &&
            value.ImplementationType is { } type && (type == typeof(ContinuousDecisionLoopHostedService) || type == typeof(MarketHistoryCollectorHostedService))).ToArray())
            services.Remove(worker);
    });
    private static async Task<string> ReadScope(HttpClient client)
    {
        using var response = await client.GetAsync("/api/plans/context"); response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("accountCacheScope").GetString()!;
    }
    private static Task<HttpResponseMessage> Post(HttpClient client, string path, object body, string? scope = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Origin", "http://localhost");
        request.Headers.Add(LocalRequestOriginProtectionMiddleware.RequestHeader, LocalRequestOriginProtectionMiddleware.RequestHeaderValue);
        if (scope is not null) request.Headers.Add(PlanEndpointService.AccountViewScopeHeader, scope);
        return client.SendAsync(request);
    }
    private static async Task RejectOldCommand(HttpClient client, string scope)
    {
        using var response = await Post(client, "/api/plans/plan/complete", new
        { stepId = "step", expectedRevision = "1", commandId = "known-command", operation = "ReportPerformed", quantity = 1, unitPriceCopper = "100" }, scope);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("account_scope_changed", body); Assert.DoesNotContain("acknowledgement", body); Assert.DoesNotContain("receiptId", body);
    }

    [Fact]
    public async Task ABA_old_browser_scope_is_rejected_before_known_command_receipt_lookup()
    {
        var fence = new BoundaryFence();
        var scopes = new AccountViewScopeTokenService(fence);
        var oldA = scopes.GetToken("A");
        fence.Rotate(); _ = scopes.GetToken("B"); fence.Rotate();
        var commands = new CompletionSpy();
        // Null repositories deliberately prove rejection precedes even the profile lookup.
        var service = new PlanEndpointService(null!, null!, new ScopeGateway(), null!, null!, null!, null!,
            new PlanOrchestrationService(), null!, scopes, commands);
        var result = await service.CompleteAsync("plan", new("step", "1", "known-command", "ReportPerformed", 1, "100"),
            oldA, CancellationToken.None);
        Assert.Equal(StatusCodes.Status409Conflict, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Equal(0, commands.Calls);
        Assert.NotEqual(oldA, scopes.GetToken("A"));
    }

    [Fact]
    public async Task Late_private_response_cannot_publish_after_generation_boundary()
    {
        var fence = new BoundaryFence();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new DefaultHttpContext(); context.Request.Path = "/api/plans";
        context.Response.Body = new MemoryStream();
        var middleware = new AccountWorkMiddleware(async request =>
        {
            entered.SetResult(); await release.Task;
            await request.Response.WriteAsJsonAsync(new { receiptId = "private-old-command", planId = "private-old-plan" });
        });
        var run = middleware.InvokeAsync(context, fence);
        await entered.Task; fence.Rotate(); release.SetResult(); await run;
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("account_scope_changed", body);
        Assert.DoesNotContain("private-old", body);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
    }

    [Fact]
    public async Task Obsolete_loop_failure_does_not_overwrite_reset_readiness_or_recommendations()
    {
        var fence = new BoundaryFence();
        var synchronization = new BlockedSynchronization();
        var projections = new PlanDecisionProjectionStore(DecisionLoopSchedulerSettings.Default);
        var plans = new PlanEndpointService(null!, null!, null!, null!, null!, null!, null!,
            new PlanOrchestrationService(), projections);
        var loop = new ContinuousDecisionLoopService(synchronization, new NoCrafting(), plans, new FixedClock(), new Lifetime(), fence);
        var run = loop.RunNowAsync(); await synchronization.Entered.Task;
        fence.Rotate(); synchronization.Release.SetResult();
        Assert.False((await run).IsSuccess);
        var state = loop.GetStatus();
        Assert.Equal(DecisionLoopRunState.NeverRun, state.State);
        Assert.Null(state.AccountScopeId); Assert.Null(state.Recommendations); Assert.Empty(state.Notifications);
        Assert.Null(state.LastErrorCode);
        // A new-context failure is observable, rather than a permanently blocked fence.
        Assert.False((await loop.RunNowAsync()).IsSuccess);
        Assert.Equal(DecisionLoopRunState.Degraded, loop.GetStatus().State);
    }

    private sealed class BoundaryFence : IAccountWorkFence
    {
        private readonly AsyncLocal<AccountWorkContext?> current = new();
        public AccountWorkContext? Current => current.Value;
        public string Generation { get; private set; } = Guid.NewGuid().ToString("N");
        public event Action? Invalidated;
        internal void Rotate() { Generation = Guid.NewGuid().ToString("N"); Invalidated?.Invoke(); }
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default)
        {
            if (Current is not null) return await work(cancellationToken);
            current.Value = new(new("A"), "synthetic-session", "synthetic-incarnation", Generation);
            try { return await work(cancellationToken); } finally { current.Value = null; }
        }
        public Task BindAccountAsync(AccountScope account, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask<IAsyncDisposable> AcquireCommitAsync(CancellationToken cancellationToken = default)
        {
            if (Current?.Generation != Generation) throw new AccountWorkRejectedException();
            return ValueTask.FromResult<IAsyncDisposable>(new EmptyLease());
        }
        public ValueTask<IAccountWorkTransition> QuiesceAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        private sealed class EmptyLease : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }
    private sealed class CompletionSpy : IPlanCompletionCommandService
    {
        internal int Calls { get; private set; }
        public Task<PlanCompletionResult> CompleteAsync(long accountProfileId, PlanCompletionCommand command, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(new PlanCompletionResult(PlanCompletionStatus.Invalid)); }
    }
    private sealed class ScopeGateway : IPersonalTradingPostGateway
    {
        public Task<Gw2ApiResult<AccountScope>> GetAccountScopeAsync(CancellationToken cancellationToken = default) => Task.FromResult(Gw2ApiResult<AccountScope>.Success(new("A")));
        public Task<Gw2ApiResult<PersonalTransactionPage>> GetCurrentBuyOrdersAsync(int page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<PersonalTransactionPage>> GetCurrentSellListingsAsync(int page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<PersonalTransactionPage>> GetCompletedBuyHistoryAsync(int page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Gw2ApiResult<PersonalTransactionPage>> GetCompletedSellHistoryAsync(int page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class BlockedSynchronization : IPersonalTradingPostSynchronizationService
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<PersonalTradingPostSynchronizationResult> SynchronizeAsync(CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult(); await Release.Task;
            return PersonalTradingPostSynchronizationResult.GatewayFailed(DateTimeOffset.UtcNow, Gw2ApiErrorCategory.Forbidden);
        }
    }
    private sealed class NoCrafting : IAccountCraftingSnapshotService
    {
        public Task<Gw2ApiResult<AccountCraftingSnapshot>> RefreshAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountCraftingSnapshot?> GetLatestAsync(AccountScope accountScope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class Lifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }
}

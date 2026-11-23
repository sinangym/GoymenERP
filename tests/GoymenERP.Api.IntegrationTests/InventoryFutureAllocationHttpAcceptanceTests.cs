using System.Net;
using System.Net.Http.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class InventoryFutureAllocationHttpAcceptanceTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid Allocation = Guid.Parse("8a200000-0000-4000-8000-000000000001");

    [Fact]
    public async Task Create_binds_the_command_key_and_returns_a_lossless_view_with_replay_headers()
    {
        var command = new Commands();
        using var host = Host(command);
        using var client = Client(host);
        using var request = Request(JsonContent.Create(new CreateFutureSupplyAllocationV1(Allocation, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), "1.001", Guid.NewGuid(), "SENTETIK gerekçe")));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal("SENTETIK-original", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        Assert.Equal("SENTETIK-key", command.Transport!.IdempotencyKey);
        Assert.Equal(ErpPermissions.InventoryFutureAllocationWrite, command.Transport.PermissionCode);
        var view = await response.Content.ReadFromJsonAsync<InventoryFutureAllocationViewV1>();
        Assert.Equal("1.001", view!.RemainingQuantity);
        Assert.Equal(Allocation, view.AllocationId);
    }

    [Fact]
    public async Task Unknown_fields_are_rejected_before_the_command_and_terminal_refusal_replays_preserve_the_original_correlation()
    {
        var command = new Commands { Refusal = new(409, InventoryFutureAllocationCodesV1.VersionConflict, true, "SENTETIK-first") };
        using var host = Host(command);
        using var client = Client(host);
        using var badRequest = Request(new StringContent("{\"expectedAllocationVersion\":\"1\",\"reasonCode\":\"manual-release\",\"reasonText\":\"SENTETIK\",\"origin\":\"sales\"}",
            System.Text.Encoding.UTF8, "application/json"), release: true);
        using var bad = await client.SendAsync(badRequest);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Null(command.Transport);
        using var validRequest = Request(JsonContent.Create(new ReleaseFutureSupplyAllocationV1("1", null, "manual-release", "SENTETIK gerekçe")), release: true);
        using var conflict = await client.SendAsync(validRequest);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("true", Assert.Single(conflict.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal("SENTETIK-first", Assert.Single(conflict.Headers.GetValues("Idempotency-Original-Correlation-Id")));
    }

    private WebApplicationFactory<Program> Host(Commands command) => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
    {
        services.RemoveAll<IInventoryFutureAllocationCommandV1>(); services.AddSingleton<IInventoryFutureAllocationCommandV1>(command);
        services.RemoveAll<IErpAuthorizationService>(); services.AddSingleton<IErpAuthorizationService>(new Authority());
    }));
    private static HttpClient Client(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        return client;
    }
    private static HttpRequestMessage Request(HttpContent content, bool release = false)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, InventoryFutureAllocationApiV1.Path + (release ? $"/{Allocation:D}/release" : string.Empty)) { Content = content };
        request.Headers.Add("Idempotency-Key", "SENTETIK-key"); return request;
    }
    private sealed class Commands : IInventoryFutureAllocationCommandV1
    {
        public InventoryAllocationException? Refusal { get; init; }
        public InventoryCommandMetadata? Transport { get; private set; }
        public Task<InventoryFutureAllocationResultV1> CreateAsync(CreateFutureSupplyAllocationV1 request, InventoryCommandMetadata transport,
            CancellationToken cancellationToken = default) => Answer(transport);
        public Task<InventoryFutureAllocationResultV1> ReleaseAsync(Guid allocationId, ReleaseFutureSupplyAllocationV1 request, InventoryCommandMetadata transport,
            CancellationToken cancellationToken = default) => Answer(transport);
        private Task<InventoryFutureAllocationResultV1> Answer(InventoryCommandMetadata transport)
        {
            Transport = transport;
            if (Refusal is not null) return Task.FromException<InventoryFutureAllocationResultV1>(Refusal);
            var id = Guid.NewGuid(); var at = DateTimeOffset.Parse("2026-11-16T09:00:00Z");
            return Task.FromResult(new InventoryFutureAllocationResultV1(new(Allocation, TestCompanyIds.CompanyA, "1", "Active",
                id, id, id, id, id, "1", id, "1.001", "1.001", "0", "0", null, "SENTETIK gerekçe", null, null, null, at, at, null), true, "SENTETIK-original"));
        }
    }
    private sealed class Authority : IErpAuthorizationService
    {
        public Task<bool> HasPermissionAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Scope(permission, companyId).IsGranted);
        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Scope(permission, companyId));
        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthorizationSnapshot("SENTETIK-future-http", new HashSet<Guid> { TestCompanyIds.CompanyA },
                permissions.ToDictionary(permission => permission, permission => Scope(permission, companyId), StringComparer.Ordinal)));
        private static PermissionScope Scope(string permission, Guid companyId) => new(companyId == TestCompanyIds.CompanyA
            && permission is ErpPermissions.InventoryFutureAllocationWrite or ErpPermissions.InventoryFutureAllocationRead,
            true, new HashSet<string> { "SENTETIK-http" }, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
    }
}

using System.Net;
using System.Text;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

// The central composition must mount the real endpoint extension. This stub proves HTTP shape, not SQL or authority acceptance.
public sealed class InventorySubcontractAllocationReleaseHttpContractTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    [Fact]
    public async Task Subcontract_release_HTTP_rejects_client_proof_quantity_actor_and_duplicate_fields_before_calling_the_owner()
    {
        var stub = new Stub(); using var host = Host(stub);
        using var client = host.CreateClient(); client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        foreach (var body in new[] { "{\"expectedAllocationVersion\":\"2\",\"reason\":\"SENTETIK\",\"quantityKg\":\"20\"}",
                     "{\"expectedAllocationVersion\":\"2\",\"reason\":\"SENTETIK\",\"actorId\":\"SENTETIK\"}",
                     "{\"expectedAllocationVersion\":\"2\",\"reason\":\"SENTETIK\",\"reason\":\"SENTETIK\"}",
                     "{\"expectedAllocationVersion\":2,\"reason\":\"SENTETIK\"}" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/inventory/subcontract-allocations/{Guid.NewGuid():D}/release-unused");
            request.Headers.Add("Idempotency-Key", "SENTETIK-release"); request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.Equal(0, stub.Calls);
    }
    [Fact]
    public async Task Subcontract_release_HTTP_replays_original_terminal_refusal_without_private_detail()
    {
        var stub = new Stub(); using var host = Host(stub);
        using var client = host.CreateClient(); client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/inventory/subcontract-allocations/{Guid.NewGuid():D}/release-unused");
        request.Headers.Add("Idempotency-Key", "SENTETIK-release"); request.Content = new StringContent("{\"expectedAllocationVersion\":\"2\",\"reason\":\"SENTETIK\"}", Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true }); Assert.Equal("true", response.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal("SENTETIK-original", response.Headers.GetValues("Idempotency-Original-Correlation-Id").Single());
        Assert.DoesNotContain("quantityKg", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(1, stub.Calls);
    }

    [Fact]
    public async Task Subcontract_release_HTTP_without_the_endpoint_permission_never_calls_the_owner()
    {
        var stub = new Stub(); using var host = Host(stub, granted: false);
        using var client = host.CreateClient(); client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/inventory/subcontract-allocations/{Guid.NewGuid():D}/release-unused");
        request.Headers.Add("Idempotency-Key", "SENTETIK-release"); request.Content = new StringContent("{\"expectedAllocationVersion\":\"2\",\"reason\":\"SENTETIK\"}", Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, stub.Calls);
    }

    private WebApplicationFactory<Program> Host(Stub stub, bool granted = true) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // Synthetic HTTP authority only; the registered permission is not a seeded role grant.
            services.RemoveAll<IErpAuthorizationService>();
            services.AddSingleton<IErpAuthorizationService>(new HttpAuthority(granted));
            services.RemoveAll<IInventorySubcontractAllocationReleaseServiceV1>();
            services.AddSingleton<IInventorySubcontractAllocationReleaseServiceV1>(stub);
        }));

    private sealed class HttpAuthority(bool granted) : IErpAuthorizationService
    {
        public Task<bool> HasPermissionAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Scope(permission, companyId).IsGranted);
        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Scope(permission, companyId));
        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId, IReadOnlyCollection<string> permissions,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthorizationSnapshot("SENTETIK-subcontract-release", new HashSet<Guid> { TestCompanyIds.CompanyA },
                permissions.ToDictionary(permission => permission, permission => Scope(permission, companyId), StringComparer.Ordinal)));
        private PermissionScope Scope(string permission, Guid companyId) =>
            granted && companyId == TestCompanyIds.CompanyA && permission == ErpPermissions.InventoryPhysicalAllocationWrite
                ? new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied;
    }

    private sealed class Stub : IInventorySubcontractAllocationReleaseServiceV1
    {
        public int Calls { get; private set; }
        public Task<InventorySubcontractAllocationReleaseCandidatesV1> ListAsync(string? cursor, CancellationToken cancellationToken = default) => Task.FromResult(new InventorySubcontractAllocationReleaseCandidatesV1([], null));
        public Task<InventorySubcontractAllocationReleaseResultV1> ReleaseAsync(Guid allocationId, ReleaseInventorySubcontractAllocationV1 request, InventoryCommandMetadata metadata, CancellationToken cancellationToken = default)
        { Calls++; throw new InventoryAllocationException(409, InventoryAllocationCodesV1.AllocationVersionConflict, true, "SENTETIK-original"); }
    }
}

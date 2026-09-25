using System.Net;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class InventoryPhysicalAllocationReadHttpAcceptanceTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private const string Path = "/api/v1/inventory/physical-allocations";
    private static readonly Guid SalesOrderId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid SalesOrderLineId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid AllocationId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid LabelId = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid ProductId = Guid.Parse("55555555-5555-4555-8555-555555555555");

    [Fact]
    public async Task Authorized_actor_gets_exact_allocations_with_private_no_store()
    {
        var authority = new ControlledAuthority();
        var owner = new StubReads(Reads());
        using var host = CreateHost(authority, owner);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(Query(limit: 12));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.Equal(new InventoryPhysicalAllocationReadQueryV1(SalesOrderId, SalesOrderLineId, 12),
            Assert.Single(owner.Queries));
        Assert.Contains(ErpPermissions.InventoryPhysicalAllocationWrite, authority.CheckedPermissions);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.GetProperty("searchWindowFull").GetBoolean());
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("9", item.GetProperty("labelVersion").GetString());
        Assert.Equal(AllocationId, item.GetProperty("allocation").GetProperty("allocationId").GetGuid());
    }

    [Fact]
    public async Task Missing_write_permission_refuses_before_owner_read()
    {
        var authority = new ControlledAuthority { MissingPermission = ErpPermissions.InventoryPhysicalAllocationWrite };
        var owner = new StubReads(Reads());
        using var host = CreateHost(authority, owner);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(Query());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.Empty(owner.Queries);
        Assert.DoesNotContain(AllocationId.ToString("D"), await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unauthenticated_request_is_private_no_store_and_never_reaches_the_owner()
    {
        var owner = new StubReads(Reads());
        using var host = CreateHost(new ControlledAuthority(), owner);
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));

        using var response = await client.GetAsync(Query());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.Empty(owner.Queries);
    }

    [Fact]
    public async Task Invalid_query_is_coded_400_before_owner_read()
    {
        var owner = new StubReads(Reads());
        using var host = CreateHost(new ControlledAuthority(), owner);
        using var client = CreateClient(host);

        foreach (var query in new[]
                 {
                     string.Empty,
                     $"?salesOrderId={SalesOrderId:D}&salesOrderLineId={SalesOrderLineId:D}&unknown=x",
                     $"?salesOrderId={SalesOrderId:D}&salesOrderId={SalesOrderId:D}&salesOrderLineId={SalesOrderLineId:D}",
                     $"?salesOrderId={Guid.Empty:D}&salesOrderLineId={SalesOrderLineId:D}",
                     $"?salesOrderId={SalesOrderId:D}&salesOrderLineId={SalesOrderLineId:D}&limit=101",
                 })
        {
            using var response = await client.GetAsync(Path + query);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(InventoryPhysicalAllocationReadCodesV1.InvalidQuery,
                document.RootElement.GetProperty("code").GetString());
        }

        Assert.Empty(owner.Queries);
    }

    [Theory]
    [InlineData(400, InventoryPhysicalAllocationReadCodesV1.InvalidQuery, true)]
    [InlineData(409, InventoryPhysicalAllocationReadCodesV1.ReadConflict, true)]
    [InlineData(503, InventoryPhysicalAllocationReadCodesV1.Unavailable, false)]
    [InlineData(401, InventoryPhysicalAllocationReadCodesV1.Unauthenticated, false)]
    [InlineData(403, InventoryPhysicalAllocationReadCodesV1.Forbidden, false)]
    public async Task Owner_refusals_preserve_only_the_public_error_contract(
        int status,
        string privateCode,
        bool exposesCode)
    {
        var owner = new StubReads(Reads())
        {
            Failure = new InventoryPhysicalAllocationReadException(status, privateCode),
        };
        using var host = CreateHost(new ControlledAuthority(), owner);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(Query());

        Assert.Equal(status, (int)response.StatusCode);
        AssertPrivateNoStore(response);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        if (exposesCode)
        {
            Assert.Equal(privateCode, document.RootElement.GetProperty("code").GetString());
        }
        else
        {
            Assert.DoesNotContain(privateCode, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Unexpected_owner_failure_is_code_less_503_without_allocation_disclosure()
    {
        var owner = new StubReads(Reads()) { Failure = new InvalidOperationException("private_owner_detail") };
        using var host = CreateHost(new ControlledAuthority(), owner);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(Query());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertPrivateNoStore(response);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private_owner_detail", body, StringComparison.Ordinal);
        Assert.DoesNotContain(AllocationId.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
    }

    private WebApplicationFactory<Program> CreateHost(ControlledAuthority authority, StubReads owner) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IErpAuthorizationService>();
            services.AddSingleton<IErpAuthorizationService>(authority);
            services.RemoveAll<IErpAuthorityRevalidationService>();
            services.AddSingleton<IErpAuthorityRevalidationService>(authority);
            services.RemoveAll<IInventoryPhysicalAllocationReadsV1>();
            services.AddSingleton<IInventoryPhysicalAllocationReadsV1>(owner);
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        return client;
    }

    private static string Query(int limit = 100) =>
        $"{Path}?salesOrderId={SalesOrderId:D}&salesOrderLineId={SalesOrderLineId:D}&limit={limit}";

    private static InventoryPhysicalAllocationReadsV1 Reads() => new([
        new InventoryPhysicalAllocationReadItemV1(
            new InventoryPhysicalAllocationViewV1(
                AllocationId,
                TestCompanyIds.CompanyA,
                "7",
                "Active",
                LabelId,
                ProductId,
                "SalesOrderLine",
                SalesOrderId,
                SalesOrderLineId,
                "product-v3",
                "5",
                "5",
                "sales-order-line",
                null,
                DateTimeOffset.Parse("2026-09-24T08:00:00+00:00"),
                DateTimeOffset.Parse("2026-09-24T08:00:00+00:00")),
            "9"),
    ], false);

    private static void AssertPrivateNoStore(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("no-cache", response.Headers.Pragma.ToString(), StringComparison.Ordinal);
    }

    private sealed class StubReads(InventoryPhysicalAllocationReadsV1 answer)
        : IInventoryPhysicalAllocationReadsV1
    {
        public List<InventoryPhysicalAllocationReadQueryV1> Queries { get; } = [];
        public Exception? Failure { get; init; }

        public Task<InventoryPhysicalAllocationReadsV1> SearchAsync(
            InventoryPhysicalAllocationReadQueryV1 query,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(answer);
        }
    }

    private sealed class ControlledAuthority : IErpAuthorizationService, IErpAuthorityRevalidationService
    {
        public string? MissingPermission { get; init; }
        public List<string> CheckedPermissions { get; } = [];

        public Task<bool> HasPermissionAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            CheckedPermissions.Add(permission);
            return Task.FromResult(companyId == TestCompanyIds.CompanyA && permission != MissingPermission);
        }

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            CheckedPermissions.Add(permission);
            var allowed = companyId == TestCompanyIds.CompanyA && permission != MissingPermission;
            return Task.FromResult(allowed
                ? new PermissionScope(true, true, new HashSet<string>(StringComparer.Ordinal),
                    new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
        }

        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId,
            IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

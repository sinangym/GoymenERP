using System.Net;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class InventoryStockPositionHttpAcceptanceTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid LabelId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid ProductId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid LotId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid LocationId = Guid.Parse("44444444-4444-4444-8444-444444444444");

    [Fact]
    public async Task Authorized_actor_gets_exact_positions_with_private_no_store()
    {
        var authority = new ControlledAuthority();
        var owner = new StubPositions(Positions());
        using var host = CreateHost(authority, owner);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(InventoryStockPositionApiV1.PositionsPath
            + "?q=%20ROLL-1%20&limit=12");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.Equal(new InventoryStockPositionQueryV1(" ROLL-1 ", 12), Assert.Single(owner.Queries));
        Assert.Contains(ErpPermissions.InventoryLabelsRead, authority.CheckedPermissions);
        Assert.Contains(ErpPermissions.InventoryBalancesRead, authority.CheckedPermissions);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.GetProperty("searchWindowFull").GetBoolean());
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(LabelId, item.GetProperty("labelId").GetGuid());
        Assert.Equal("3", item.GetProperty("labelVersion").GetString());
        Assert.Equal("7", item.GetProperty("availableKg").GetString());
    }

    [Fact]
    public async Task Either_missing_read_permission_refuses_before_owner_read()
    {
        foreach (var missing in new[] { ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead })
        {
            var authority = new ControlledAuthority { MissingPermission = missing };
            var owner = new StubPositions(Positions());
            using var host = CreateHost(authority, owner);
            using var client = CreateClient(host);

            using var response = await client.GetAsync(InventoryStockPositionApiV1.PositionsPath);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            AssertPrivateNoStore(response);
            Assert.Empty(owner.Queries);
            Assert.DoesNotContain("ROLL-1", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Unauthenticated_request_is_private_no_store_and_never_reaches_the_owner()
    {
        var authority = new ControlledAuthority();
        var owner = new StubPositions(Positions());
        using var host = CreateHost(authority, owner);
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));

        using var response = await client.GetAsync(InventoryStockPositionApiV1.PositionsPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.Empty(owner.Queries);
    }

    [Fact]
    public async Task Invalid_query_is_coded_400_before_owner_read()
    {
        var owner = new StubPositions(Positions());
        using var host = CreateHost(new ControlledAuthority(), owner);
        using var client = CreateClient(host);

        foreach (var query in new[] { "?unknown=x", "?q=a&q=b", "?limit=abc", "?limit=101" })
        {
            using var response = await client.GetAsync(InventoryStockPositionApiV1.PositionsPath + query);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(InventoryStockPositionCodesV1.InvalidQuery,
                document.RootElement.GetProperty("code").GetString());
        }

        Assert.Empty(owner.Queries);
    }

    [Theory]
    [InlineData(400, InventoryStockPositionCodesV1.InvalidQuery)]
    [InlineData(409, InventoryStockPositionCodesV1.ReadConflict)]
    [InlineData(503, InventoryStockPositionCodesV1.Unavailable)]
    [InlineData(401, InventoryStockPositionCodesV1.Unauthenticated)]
    [InlineData(403, InventoryStockPositionCodesV1.Forbidden)]
    public async Task Owner_refusals_preserve_only_the_public_error_contract(int status, string privateCode)
    {
        var owner = new StubPositions(Positions())
        {
            Failure = new InventoryStockPositionException(status, privateCode),
        };
        using var host = CreateHost(new ControlledAuthority(), owner);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(InventoryStockPositionApiV1.PositionsPath);

        Assert.Equal(status, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        if (status is 400 or 409)
        {
            Assert.Equal(privateCode, document.RootElement.GetProperty("code").GetString());
        }
        else
        {
            Assert.NotEqual(privateCode, document.RootElement.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Unexpected_owner_failure_is_code_less_503_without_position_disclosure()
    {
        var owner = new StubPositions(Positions()) { Failure = new InvalidOperationException("private_owner_detail") };
        using var host = CreateHost(new ControlledAuthority(), owner);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(InventoryStockPositionApiV1.PositionsPath);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private_owner_detail", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ROLL-1", body, StringComparison.Ordinal);
    }

    private WebApplicationFactory<Program> CreateHost(ControlledAuthority authority, StubPositions owner) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IErpAuthorizationService>();
            services.AddSingleton<IErpAuthorizationService>(authority);
            services.RemoveAll<IErpAuthorityRevalidationService>();
            services.AddSingleton<IErpAuthorityRevalidationService>(authority);
            services.RemoveAll<IInventoryStockPositionsV1>();
            services.AddSingleton<IInventoryStockPositionsV1>(owner);
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        return client;
    }

    private static InventoryStockPositionsV1 Positions() => new([
        new InventoryStockPositionV1(
            TestCompanyIds.CompanyA, LabelId, "3", "ROLL-1", ProductId,
            null, null,
            new InventoryLotV1(LotId, "HEAT-1", "COIL-1"), "HEAT-1", "COIL-1", "Complete",
            "Company", TestCompanyIds.CompanyA, "Company", TestCompanyIds.CompanyA,
            LocationId, "10", "8", "2", "1", "7"),
    ], false);

    private static void AssertPrivateNoStore(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("no-cache", response.Headers.Pragma.ToString(), StringComparison.Ordinal);
    }

    private sealed class StubPositions(InventoryStockPositionsV1 answer) : IInventoryStockPositionsV1
    {
        public List<InventoryStockPositionQueryV1> Queries { get; } = [];
        public Exception? Failure { get; init; }

        public Task<InventoryStockPositionsV1> SearchAsync(
            InventoryStockPositionQueryV1 query,
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

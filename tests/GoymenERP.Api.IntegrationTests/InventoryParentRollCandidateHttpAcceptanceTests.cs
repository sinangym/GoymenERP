using System.Net;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class InventoryParentRollCandidateHttpAcceptanceTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid LabelId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid ProductId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid LotId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid LocationId = Guid.Parse("44444444-4444-4444-8444-444444444444");

    [Fact]
    public async Task Authorized_actor_gets_exact_candidates_with_private_no_store()
    {
        var authority = new ControlledAuthority();
        var owner = new StubCandidates(Candidates());
        using var host = CreateHost(authority, owner);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(InventoryParentRollApiV1.CandidatesPath
            + "?q=%20ROLL-1%20&limit=12");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.Equal(new InventoryParentRollQueryV1(" ROLL-1 ", 12), Assert.Single(owner.Queries));
        Assert.Contains(ErpPermissions.InventoryLabelsRead, authority.CheckedPermissions);
        Assert.Contains(ErpPermissions.InventoryBalancesRead, authority.CheckedPermissions);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.GetProperty("searchWindowFull").GetBoolean());
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(LabelId, item.GetProperty("labelId").GetGuid());
        Assert.Equal("3", item.GetProperty("labelVersion").GetString());
        Assert.Equal("8", item.GetProperty("unblockedPhysicalKg").GetString());
        Assert.True(item.GetProperty("productionUseRequiresRevalidation").GetBoolean());
    }

    [Fact]
    public async Task Either_missing_read_permission_refuses_before_owner_read()
    {
        foreach (var missing in new[] { ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead })
        {
            var authority = new ControlledAuthority { MissingPermission = missing };
            var owner = new StubCandidates(Candidates());
            using var host = CreateHost(authority, owner);
            using var client = CreateClient(host);

            using var response = await client.GetAsync(InventoryParentRollApiV1.CandidatesPath);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Empty(owner.Queries);
            var body = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);
            Assert.Equal("forbidden", document.RootElement.GetProperty("code").GetString());
            Assert.DoesNotContain("ROLL-1", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Unknown_duplicate_malformed_and_out_of_range_query_is_coded_400_before_owner_read()
    {
        var authority = new ControlledAuthority();
        var owner = new StubCandidates(Candidates());
        using var host = CreateHost(authority, owner);
        using var client = CreateClient(host);
        var queries = new[] { "?unknown=x", "?q=a&q=b", "?limit=abc", "?limit=101" };

        foreach (var query in queries)
        {
            using var response = await client.GetAsync(InventoryParentRollApiV1.CandidatesPath + query);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            AssertPrivateNoStore(response);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(InventoryParentRollCodesV1.InvalidQuery,
                document.RootElement.GetProperty("code").GetString());
        }

        Assert.Empty(owner.Queries);
    }

    [Theory]
    [InlineData(400, InventoryParentRollCodesV1.InvalidQuery)]
    [InlineData(409, InventoryParentRollCodesV1.ReadConflict)]
    [InlineData(503, "service_unavailable")]
    [InlineData(401, "authentication_required")]
    [InlineData(403, "forbidden")]
    public async Task Owner_refusals_preserve_only_the_public_error_contract(int status, string? expectedCode)
    {
        var authority = new ControlledAuthority();
        var owner = new StubCandidates(Candidates())
        {
            Failure = new InventoryParentRollException(status,
                expectedCode ?? InventoryParentRollCodesV1.Forbidden),
        };
        using var host = CreateHost(authority, owner);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(InventoryParentRollApiV1.CandidatesPath);

        Assert.Equal(status, (int)response.StatusCode);
        AssertPrivateNoStore(response);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("private_owner_detail", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unexpected_owner_failure_is_code_less_503_without_candidate_disclosure()
    {
        var authority = new ControlledAuthority();
        var owner = new StubCandidates(Candidates()) { Failure = new InvalidOperationException("private_owner_detail") };
        using var host = CreateHost(authority, owner);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(InventoryParentRollApiV1.CandidatesPath);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertPrivateNoStore(response);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal("service_unavailable", document.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("private_owner_detail", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ROLL-1", body, StringComparison.Ordinal);
    }

    private WebApplicationFactory<Program> CreateHost(ControlledAuthority authority, StubCandidates owner) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IErpAuthorizationService>();
            services.AddSingleton<IErpAuthorizationService>(authority);
            services.RemoveAll<IErpAuthorityRevalidationService>();
            services.AddSingleton<IErpAuthorityRevalidationService>(authority);
            services.RemoveAll<IInventoryParentRollCandidatesV1>();
            services.AddSingleton<IInventoryParentRollCandidatesV1>(owner);
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        return client;
    }

    private static InventoryParentRollCandidatesV1 Candidates() => new([
        new InventoryParentRollReferenceV1(
            TestCompanyIds.CompanyA,
            LabelId,
            "3",
            "ROLL-1",
            ProductId,
            new InventoryLotV1(LotId, "HEAT-1", "COIL-1"),
            "HEAT-1",
            "COIL-1",
            "Complete",
            ["certificate"],
            ["certificate"],
            "10",
            "8",
            "2",
            [new InventoryPositionV1("Company", TestCompanyIds.CompanyA, "Company", TestCompanyIds.CompanyA,
                LocationId, ProductId, "Free", "8")],
            true,
            null,
            true),
    ], false);

    private static void AssertPrivateNoStore(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("no-cache", response.Headers.Pragma.ToString(), StringComparison.Ordinal);
    }

    private sealed class StubCandidates(InventoryParentRollCandidatesV1 answer)
        : IInventoryParentRollCandidatesV1
    {
        public List<InventoryParentRollQueryV1> Queries { get; } = [];
        public Exception? Failure { get; init; }

        public Task<InventoryParentRollCandidatesV1> SearchAsync(
            InventoryParentRollQueryV1 query,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Failure is null
                ? Task.FromResult(answer)
                : Task.FromException<InventoryParentRollCandidatesV1>(Failure);
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

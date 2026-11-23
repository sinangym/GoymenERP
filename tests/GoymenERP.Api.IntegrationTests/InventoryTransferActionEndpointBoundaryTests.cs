using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.AspNetCore.Http;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Synthetic HTTP owner-boundary cases; no runtime SQL or user-workflow acceptance claim.</summary>
public sealed class InventoryTransferActionEndpointBoundaryTests
{
    private static readonly Guid Company = TestCompanyIds.CompanyA;
    [Fact]
    public async Task Purpose_permission_denial_is_403_before_any_candidate_or_location_read_and_never_escapes_as_500()
    {
        var authority = new Authority { Deny = true }; var owner = new Owner(); var display = new Display();
        var http = Context("?purpose=overage-decision");
        var result = await Invoke(http, owner, display, authority);
        Assert.Equal(403, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.False(owner.Called); Assert.Empty(display.Batches); Assert.Equal("private, no-store", http.Response.Headers.CacheControl.ToString());
        Assert.All(authority.Seen, value => Assert.Equal(InventoryPermissionManifestV1.StockTransferOverageApprove, value));
    }
    [Fact]
    public async Task A_hundred_transfer_candidates_respect_the_actual_Organization_maximum_without_hiding_the_tail()
    {
        var owner = new Owner { Count = 100 }; var display = new Display(); var authority = new Authority();
        var result = await Invoke(Context("?purpose=overage-decision&limit=100"), owner, display, authority);
        var value = Assert.IsAssignableFrom<IValueHttpResult>(result).Value;
        var response = Assert.IsType<InventoryTransferReceiptWorkDisplayCandidatesV1>(value);
        Assert.Equal(100, response.Items.Count); Assert.Equal(2, display.Batches.Count);
        Assert.All(display.Batches, batch => Assert.InRange(batch.Count, 1, 100));
        Assert.Equal(200, display.Batches.SelectMany(x => x).Distinct().Count());
        Assert.All(authority.Seen, permission => Assert.Equal(InventoryPermissionManifestV1.StockTransferOverageApprove, permission));
    }
    [Fact]
    public async Task Authority_loss_after_location_display_returns_403_instead_of_the_previously_read_payload()
    {
        var authority = new Authority(); var display = new Display { AfterRead = () => authority.Deny = true };
        var result = await Invoke(Context("?purpose=overage-decision"), new Owner(), display, authority);
        Assert.Equal(403, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Single(display.Batches);
    }
    [Theory]
    [InlineData("?purpose=overage-decision&companyId=other")]
    [InlineData("?purpose=receive&purpose=loss-request")]
    [InlineData("?purpose=unknown")]
    [InlineData("?purpose=receive&limit=01")]
    public async Task Candidate_transport_rejects_ambiguous_scope_and_purpose_without_calling_an_owner(string query)
    {
        var owner = new Owner(); var display = new Display();
        var result = await Invoke(Context(query), owner, display, new Authority());
        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode); Assert.False(owner.Called);
    }
    private static Task<IResult> Invoke(HttpContext http, Owner owner, Display display, Authority authority)
    {
        var company = new CompanyContext(); company.Set(Company);
        return InventoryEndpoints.ReceiptWorkCandidatesAsync(http, owner, display, company, new User(), authority, default);
    }
    private static DefaultHttpContext Context(string query) => new() { Request = { QueryString = new QueryString(query) } };
    private sealed class Owner : IInventoryTransferReceiptWorkV1
    {
        public int Count { get; init; } = 1; public bool Called { get; private set; }
        public Task<InventoryTransferReceiptWorkV1> ReadAsync(Guid id, string purpose, CancellationToken token = default) => throw new NotSupportedException();
        public Task<InventoryTransferReceiptWorkCandidatesV1> SearchAsync(string purpose, string? query = null, string? cursor = null, int limit = 25, CancellationToken cancellationToken = default)
        {
            Called = true;
            return Task.FromResult(new InventoryTransferReceiptWorkCandidatesV1(Company, purpose, query, limit,
                Enumerable.Range(0, Count).Select(_ => new InventoryTransferReceiptWorkCandidateV1(
                    new(Guid.NewGuid(), Company, "3", "Accepted", Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), ["SENTETIK-ETIKET"])).ToArray(), false, null));
        }
    }
    private sealed class Display : IOrganizationLocationDisplayReaderV1
    {
        public List<IReadOnlyCollection<Guid>> Batches { get; } = []; public Action? AfterRead { get; init; }
        public Task<IReadOnlyList<OrganizationLocationDisplayV1>> ReadAsync(Guid companyId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
        {
            Assert.Equal(Company, companyId); Assert.InRange(ids.Count, 1, 100); Batches.Add(ids); AfterRead?.Invoke();
            return Task.FromResult<IReadOnlyList<OrganizationLocationDisplayV1>>(ids.Select(id => new OrganizationLocationDisplayV1(id, "SENTETIK", "SENTETIK depo")).ToArray());
        }
    }
    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true; public string UserId => "SENTETIK";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public List<string> Seen { get; } = []; public bool Deny { get; set; }
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken token = default)
        {
            Seen.Add(permission);
            return Task.FromResult(Deny || permission != InventoryPermissionManifestV1.StockTransferOverageApprove || companyId != Company ? PermissionScope.Denied
                : new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }
}

using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class ManufacturingOutputSelectionReaderTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Product = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid Location = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid Site = Guid.Parse("40000000-0000-0000-0000-000000000004");

    [Fact]
    public async Task Maps_only_the_bounded_output_facts_and_fences_manufacturing_authority()
    {
        var products = new Products(new ProductStockCandidatesV1(
            [new ProductStockCandidateV1(Product, "12", "PRD-12", "Galvaniz bant",
                Guid.NewGuid(), "kg", Guid.NewGuid(), "4")], false));
        var locations = new Locations(new OrganizationLocationCandidatesV1(
            [new OrganizationLocationCandidateV1(Location, "HOL-1", "Üretim holü", Site, "OSM", "Osmangazi")],
            true));
        var authority = new Authority();
        var reader = Reader(products, locations, authority);

        var productAnswer = await reader.SearchProductsAsync(
            new ManufacturingOutputProductQueryV1("bant", new DateOnly(2026, 9, 25), 17), default);
        var locationAnswer = await reader.SearchLocationsAsync(
            new ManufacturingOutputLocationQueryV1("hol", 19), default);

        Assert.Equal(new ProductStockCandidateQueryV1("bant", new DateOnly(2026, 9, 25), 17),
            Assert.Single(products.Queries));
        Assert.Equal((Company, new OrganizationLocationCandidateQueryV1("hol", 19)),
            Assert.Single(locations.Queries));
        Assert.Equal(new ManufacturingOutputProductCandidateV1(Product, "12", "PRD-12", "Galvaniz bant", "kg"),
            Assert.Single(productAnswer.Items));
        Assert.Equal(new ManufacturingOutputLocationCandidateV1(
            Location, "HOL-1", "Üretim holü", Site, "OSM", "Osmangazi"),
            Assert.Single(locationAnswer.Items));
        Assert.True(locationAnswer.SearchWindowFull);
        Assert.Equal(4, authority.Calls);
        Assert.All(authority.Permissions, permission =>
            Assert.Equal(ErpPermissions.ManufacturingFactsRecord, permission));
    }

    [Fact]
    public async Task Revoked_or_swapped_scope_after_an_owner_read_releases_no_candidates()
    {
        var user = new User();
        var products = new Products(new ProductStockCandidatesV1([], false)) { DuringRead = user.Swap };
        var reader = Reader(products, new Locations(new OrganizationLocationCandidatesV1([], false)),
            new Authority(), user);

        var error = await Assert.ThrowsAsync<ManufacturingOutputSelectionException>(() =>
            reader.SearchProductsAsync(new ManufacturingOutputProductQueryV1(null, new DateOnly(2026, 9, 25)), default));

        Assert.Equal((ManufacturingOutputSelectionFailure.Denied, ManufacturingOutputSelectionCodesV1.Denied),
            (error.Failure, error.Code));
    }

    [Fact]
    public async Task Duplicate_or_incomplete_owner_answers_fail_closed()
    {
        var duplicate = new ProductStockCandidateV1(Product, "12", "PRD-12", "Galvaniz bant",
            Guid.NewGuid(), "kg", Guid.NewGuid(), "4");
        var products = new Products(new ProductStockCandidatesV1([duplicate, duplicate], false));
        var reader = Reader(products, new Locations(new OrganizationLocationCandidatesV1([], false)), new Authority());

        var error = await Assert.ThrowsAsync<ManufacturingOutputSelectionException>(() =>
            reader.SearchProductsAsync(new ManufacturingOutputProductQueryV1(null, new DateOnly(2026, 9, 25)), default));

        Assert.Equal((ManufacturingOutputSelectionFailure.Unavailable,
                ManufacturingOutputSelectionCodesV1.Unavailable),
            (error.Failure, error.Code));
    }

    private static ManufacturingOutputSelectionReader Reader(
        IProductStockCandidatesV1 products,
        IOrganizationLocationCandidatesV1 locations,
        IErpAuthorityRevalidationService authority,
        ICurrentUser? user = null)
    {
        var company = new CompanyContext();
        company.Set(Company);
        return new ManufacturingOutputSelectionReader(products, locations, authority, company, user ?? new User());
    }

    private sealed class Products(ProductStockCandidatesV1 answer) : IProductStockCandidatesV1
    {
        public List<ProductStockCandidateQueryV1> Queries { get; } = [];
        public Action? DuringRead { get; init; }

        public Task<ProductStockCandidatesV1> SearchAsync(
            ProductStockCandidateQueryV1 query,
            CancellationToken cancellationToken)
        {
            Queries.Add(query);
            DuringRead?.Invoke();
            return Task.FromResult(answer);
        }
    }

    private sealed class Locations(OrganizationLocationCandidatesV1 answer) : IOrganizationLocationCandidatesV1
    {
        public List<(Guid CompanyId, OrganizationLocationCandidateQueryV1 Query)> Queries { get; } = [];

        public Task<OrganizationLocationCandidatesV1> SearchAsync(
            Guid companyId,
            OrganizationLocationCandidateQueryV1 query,
            CancellationToken cancellationToken = default)
        {
            Queries.Add((companyId, query));
            return Task.FromResult(answer);
        }
    }

    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public int Calls { get; private set; }
        public List<string> Permissions { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Permissions.Add(permission);
            return Task.FromResult(permission == ErpPermissions.ManufacturingFactsRecord && companyId == Company
                ? new PermissionScope(true, true, new HashSet<string>(StringComparer.Ordinal),
                    new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
        }
    }

    private sealed class User : ICurrentUser
    {
        private readonly HashSet<Guid> allowed = [Company];

        public string? UserId { get; private set; } = "mfg-recorder";
        public bool IsAuthenticated => true;
        public IReadOnlySet<Guid> AllowedCompanyIds => allowed;
        public bool CanAccessCompany(Guid companyId) => allowed.Contains(companyId);
        public void Swap() => UserId = "other-recorder";
    }
}

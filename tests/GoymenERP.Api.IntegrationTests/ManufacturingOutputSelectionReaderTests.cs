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
        Assert.Equal(new ManufacturingOutputProductCandidateV1(Product, "12", "PRD-12", "Galvaniz bant", "kg",
                ManufacturingOutputPieceCountingV1.Unavailable),
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

    [Fact]
    public async Task Without_the_product_piece_port_every_candidate_is_piece_counting_unavailable()
    {
        var products = new Products(new ProductStockCandidatesV1([Candidate(Product, "PRD-12")], false));
        var reader = Reader(products, new Locations(new OrganizationLocationCandidatesV1([], false)), new Authority());

        var answer = await reader.SearchProductsAsync(
            new ManufacturingOutputProductQueryV1(null, new DateOnly(2026, 9, 25)), default);

        Assert.Equal(ManufacturingOutputPieceCountingV1.Unavailable, Assert.Single(answer.Items).PieceCounting);
    }

    [Fact]
    public async Task Each_candidate_carries_the_piece_counting_the_product_owner_answers_for_the_query_date()
    {
        // AK-7b (URT-D112): the selector reads the Product owner's port per candidate with the declaration's own rule.
        var counted = Product;
        var plain = Guid.Parse("20000000-0000-0000-0000-000000000003");
        var missing = Guid.Parse("20000000-0000-0000-0000-000000000004");
        var broken = Guid.Parse("20000000-0000-0000-0000-000000000005");
        var foreign = Guid.Parse("20000000-0000-0000-0000-000000000006");
        var products = new Products(new ProductStockCandidatesV1(
            [Candidate(counted, "PRD-1"), Candidate(plain, "PRD-2"), Candidate(missing, "PRD-3"), Candidate(broken, "PRD-4"),
                Candidate(foreign, "PRD-5")], false));
        var pieces = new Pieces
        {
            Answers =
            {
                [counted] = () => Found(Company, counted),
                [plain] = () => ProductTheoreticalKilogramsPerUnitAnswerV1.Without(
                    ProductTheoreticalKilogramsPerUnitOutcomeV1.NoSource, ProductTheoreticalKilogramsPerUnitCodesV1.UnitNotOnCard),
                [missing] = () => ProductTheoreticalKilogramsPerUnitAnswerV1.Without(
                    ProductTheoreticalKilogramsPerUnitOutcomeV1.NoSource, ProductTheoreticalKilogramsPerUnitCodesV1.NoSource),
                [broken] = () => throw new InvalidOperationException("SENTETIK owner read failure"),
                // A found figure that names another company's evidence is never trusted.
                [foreign] = () => Found(Guid.Parse("10000000-0000-0000-0000-0000000000ff"), foreign),
            },
        };
        var authority = new Authority();
        var reader = Reader(products, new Locations(new OrganizationLocationCandidatesV1([], false)), authority, pieces: pieces);

        var answer = await reader.SearchProductsAsync(
            new ManufacturingOutputProductQueryV1(null, new DateOnly(2026, 9, 25)), default);

        Assert.Equal(
            [(counted, ManufacturingOutputPieceCountingV1.Counted), (plain, ManufacturingOutputPieceCountingV1.NotCounted),
                (missing, ManufacturingOutputPieceCountingV1.SourceMissing), (broken, ManufacturingOutputPieceCountingV1.Unavailable),
                (foreign, ManufacturingOutputPieceCountingV1.Unavailable)],
            answer.Items.Select(item => (item.ProductId, item.PieceCounting)));
        Assert.All(pieces.Asked, asked => Assert.Equal(
            (GoymenERP.Modules.Settings.Application.Contracts.V1.SettingsUnitIdentitiesV1.Piece, new DateOnly(2026, 9, 25)),
            (asked.UnitId, asked.EffectiveOn)));
        Assert.Equal([counted, plain, missing, broken, foreign], pieces.Asked.Select(asked => asked.ProductId));
        // Before the owner read, after it, after the failed piece read and after the last piece read.
        Assert.Equal(4, authority.Calls);
    }

    [Fact]
    public async Task A_scope_swapped_during_a_piece_read_releases_no_candidates()
    {
        var user = new User();
        var products = new Products(new ProductStockCandidatesV1([Candidate(Product, "PRD-12")], false));
        var pieces = new Pieces { Answers = { [Product] = () => { user.Swap(); return Found(Company, Product); } } };
        var reader = Reader(products, new Locations(new OrganizationLocationCandidatesV1([], false)), new Authority(), user, pieces);

        var error = await Assert.ThrowsAsync<ManufacturingOutputSelectionException>(() =>
            reader.SearchProductsAsync(new ManufacturingOutputProductQueryV1(null, new DateOnly(2026, 9, 25)), default));

        Assert.Equal(ManufacturingOutputSelectionFailure.Denied, error.Failure);
    }

    private static ProductStockCandidateV1 Candidate(Guid productId, string code) =>
        new(productId, "12", code, "SENTETIK " + code, Guid.NewGuid(), "kg", Guid.NewGuid(), "4");

    private static ProductTheoreticalKilogramsPerUnitAnswerV1 Found(Guid company, Guid product) =>
        ProductTheoreticalKilogramsPerUnitAnswerV1.Found(0.4m, new ProductTheoreticalKilogramsPerUnitEvidenceV1(
            company, product, GoymenERP.Modules.Settings.Application.Contracts.V1.SettingsUnitIdentitiesV1.Piece,
            ProductPhysicalUnitKindV1.Piece, "12", "product-unit-conversion.v1", 4, null, null, null, null, null,
            Guid.Parse("a7000000-0000-0000-0000-0000000000a7"), 1, "unit-semantics.v1"));

    private static ManufacturingOutputSelectionReader Reader(
        IProductStockCandidatesV1 products,
        IOrganizationLocationCandidatesV1 locations,
        IErpAuthorityRevalidationService authority,
        ICurrentUser? user = null,
        IProductTheoreticalKilogramsPerUnitReadPortV1? pieces = null)
    {
        var company = new CompanyContext();
        company.Set(Company);
        return new ManufacturingOutputSelectionReader(products, locations, authority, company, user ?? new User(), pieces);
    }

    private sealed class Pieces : IProductTheoreticalKilogramsPerUnitReadPortV1
    {
        public Dictionary<Guid, Func<ProductTheoreticalKilogramsPerUnitAnswerV1>> Answers { get; } = [];
        public List<(Guid ProductId, Guid UnitId, DateOnly EffectiveOn)> Asked { get; } = [];

        public Task<ProductTheoreticalKilogramsPerUnitAnswerV1> ReadAsync(Guid productId, Guid unitId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default)
        {
            Asked.Add((productId, unitId, effectiveOn));
            return Task.FromResult(Answers[productId]());
        }
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

using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class InventoryStockProductFilterHostTests
{
    private static readonly Guid CompanyId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private const string ActorId = "typed-stock-filter-actor";

    [Fact]
    public async Task Exact_version_family_and_inclusive_measure_ranges_return_only_matching_labels()
    {
        var familyId = Guid.NewGuid();
        var firstProduct = Guid.NewGuid();
        var secondProduct = Guid.NewGuid();
        var firstLabel = Guid.NewGuid();
        var secondLabel = Guid.NewGuid();
        var products = new ProductMatcher(query => new([
            query.Selections.Single(selection => selection.ProductId == firstProduct),
        ]));
        var host = new InventoryStockProductFilterHost(products, new Company(CompanyId), new User());

        var matches = await host.MatchAsync(new(
            CompanyId,
            ActorId,
            [new(firstLabel, firstProduct, "4"), new(secondLabel, secondProduct, "9")],
            new(familyId, null, 1m, 2m, 1250m, null, null)));

        Assert.Equal([firstLabel], matches);
        Assert.Single(products.Queries);
    }

    [Fact]
    public async Task Missing_typed_measure_is_not_zero_and_never_matches()
    {
        var familyId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var labelId = Guid.NewGuid();
        var products = new ProductMatcher(_ => new([]));
        var host = new InventoryStockProductFilterHost(products, new Company(CompanyId), new User());

        var matches = await host.MatchAsync(new(
            CompanyId,
            ActorId,
            [new(labelId, productId, "1")],
            new(familyId, 1m, null, null, null, null, null)));

        Assert.Empty(matches);
    }

    [Fact]
    public async Task Multiple_stored_versions_of_one_product_are_matched_in_one_bounded_product_read()
    {
        var familyId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var oldLabel = Guid.NewGuid();
        var currentLabel = Guid.NewGuid();
        var products = new ProductMatcher(query => new([
            query.Selections.Single(selection => selection.ExpectedProductVersion == "2"),
        ]));
        var host = new InventoryStockProductFilterHost(products, new Company(CompanyId), new User());

        var matches = await host.MatchAsync(new(
            CompanyId,
            ActorId,
            [new(oldLabel, productId, "1"), new(currentLabel, productId, "2")],
            new(familyId, 2m, null, null, null, null, null)));

        Assert.Equal([currentLabel], matches);
        Assert.Single(products.Queries);
        Assert.Equal(2, products.Queries[0].Selections.Count);
    }

    [Fact]
    public async Task Company_or_actor_drift_is_code_less_forbidden_before_product_read()
    {
        var products = new ProductMatcher(_ => throw new InvalidOperationException("must not read"));
        var company = new Company(CompanyId);
        var user = new User();
        var host = new InventoryStockProductFilterHost(products, company, user);
        var request = new InventoryStockProductFilterRequestV1(
            CompanyId,
            ActorId,
            [new(Guid.NewGuid(), Guid.NewGuid(), "1")],
            new(Guid.NewGuid(), null, null, null, null, null, null));

        company.CompanyId = Guid.NewGuid();
        var error = await Assert.ThrowsAsync<InventoryStockPositionException>(() => host.MatchAsync(request));

        Assert.Equal((403, InventoryStockPositionCodesV1.Forbidden), (error.StatusCode, error.Code));
        Assert.Empty(products.Queries);
    }

    private sealed class ProductMatcher(
        Func<ProductStockTechnicalMatchQueryV1, ProductStockTechnicalMatchBatchV1> answer)
        : IProductStockTechnicalMatcherV1
    {
        public List<ProductStockTechnicalMatchQueryV1> Queries { get; } = [];

        public Task<ProductStockTechnicalMatchBatchV1> MatchAsync(
            ProductStockTechnicalMatchQueryV1 query,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Task.FromResult(answer(query));
        }
    }

    private sealed class Company(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; set; } = companyId;
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => ActorId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { CompanyId };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}

using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// KLN-D39/D70 (STK-S30): the central adapter from Product's similarity facts to Inventory's similar-product check.
/// It passes only the exact requested pairs, keeps Product's refused authority as 403 and never trusts a torn answer.
/// SENTETIK / NOT_ENV.
/// </summary>
public sealed class InventoryProductSimilarityFactsHostTests
{
    private static readonly Guid CompanyId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private const string ActorId = "typed-similarity-facts-actor";

    [Fact]
    public async Task Facts_are_mapped_one_to_one_for_the_exact_requested_pairs()
    {
        var first = new InventoryProductVersionV1(Guid.NewGuid(), "3");
        var second = new InventoryProductVersionV1(Guid.NewGuid(), "8");
        var products = new ProductFacts(query => new([
            Result(query.Selections[1], identity: false, key: null),
            Result(query.Selections[0], identity: true, key: "shared"),
        ]));
        var host = new InventoryProductSimilarityFactsHost(products, new Company(CompanyId), new User());

        var facts = await host.ReadAsync([first, second]);

        var query = Assert.Single(products.Queries);
        Assert.Equal(new[] { (first.ProductId, "3"), (second.ProductId, "8") },
            query.Selections.Select(selection => (selection.ProductId, selection.ExpectedProductVersion)));
        Assert.Equal(2, facts.Count);
        var mapped = Assert.Single(facts, fact => fact.ProductId == first.ProductId);
        Assert.Equal(("3", "SYN-P", "Sentetik ürün", "shared", 1.25m, 1250m, (decimal?)null),
            (mapped.ProductVersion, mapped.ProductCode, mapped.ProductName, mapped.SimilarityKey,
                mapped.ThicknessMm, mapped.WidthMm, mapped.LengthMm));
        var unknown = Assert.Single(facts, fact => fact.ProductId == second.ProductId);
        Assert.Equal("SYN-COAT-A", mapped.CoatingCode);
        Assert.Equal(((string?)null, (string?)null, (string?)null, (string?)null),
            (unknown.ProductCode, unknown.ProductName, unknown.SimilarityKey, unknown.CoatingCode));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("duplicate")]
    [InlineData("identity")]
    public async Task A_torn_Product_answer_is_503_never_a_partial_fact_list(string defect)
    {
        var requested = new InventoryProductVersionV1(Guid.NewGuid(), "3");
        var other = new InventoryProductVersionV1(Guid.NewGuid(), "4");
        var products = new ProductFacts(query =>
        {
            var own = Result(query.Selections[0], identity: true, key: "k");
            var second = Result(query.Selections[1], identity: true, key: "k");
            return defect switch
            {
                "missing" => new([own]),
                "foreign" => new([own, second with { ProductId = Guid.NewGuid() }]),
                "duplicate" => new([own, own]),
                _ => new([own, second with { Identity = new(Guid.NewGuid(), "4", "SYN-P", "Sentetik ürün") }]),
            };
        });
        var host = new InventoryProductSimilarityFactsHost(products, new Company(CompanyId), new User());

        var error = await Assert.ThrowsAsync<InventoryAllocationException>(() => host.ReadAsync([requested, other]));

        Assert.Equal((503, InventoryAllocationSuggestionCodesV1.ProductFactsUnavailable), (error.Status, error.Code));
    }

    [Fact]
    public async Task Product_refusal_is_403_and_any_other_Product_failure_is_503()
    {
        var pair = new InventoryProductVersionV1(Guid.NewGuid(), "1");
        var refused = new InventoryProductSimilarityFactsHost(
            new ProductFacts(_ => throw new ProductOperationException("product_forbidden", 403)),
            new Company(CompanyId), new User());
        var failed = new InventoryProductSimilarityFactsHost(
            new ProductFacts(_ => throw new ProductOperationException("product_runtime_unconfigured", 503)),
            new Company(CompanyId), new User());

        var forbidden = await Assert.ThrowsAsync<InventoryAllocationException>(() => refused.ReadAsync([pair]));
        var unavailable = await Assert.ThrowsAsync<InventoryAllocationException>(() => failed.ReadAsync([pair]));

        Assert.Equal((403, InventoryAllocationSuggestionCodesV1.ProductFactsForbidden), (forbidden.Status, forbidden.Code));
        Assert.Equal((503, InventoryAllocationSuggestionCodesV1.ProductFactsUnavailable), (unavailable.Status, unavailable.Code));
    }

    [Fact]
    public async Task An_invalid_batch_or_a_drifted_company_is_refused_before_any_Product_read()
    {
        var pair = new InventoryProductVersionV1(Guid.NewGuid(), "1");
        var products = new ProductFacts(_ => throw new InvalidOperationException("must not read"));
        var company = new Company(CompanyId);
        var host = new InventoryProductSimilarityFactsHost(products, company, new User());

        var duplicate = await Assert.ThrowsAsync<InventoryAllocationException>(() => host.ReadAsync([pair, pair]));
        var blank = await Assert.ThrowsAsync<InventoryAllocationException>(() =>
            host.ReadAsync([pair with { ProductVersion = " " }]));
        var empty = await Assert.ThrowsAsync<InventoryAllocationException>(() => host.ReadAsync([]));
        company.CompanyId = Guid.NewGuid();
        var drifted = await Assert.ThrowsAsync<InventoryAllocationException>(() => host.ReadAsync([pair]));

        Assert.All(new[] { duplicate, blank, empty }, error =>
            Assert.Equal((503, InventoryAllocationSuggestionCodesV1.ProductFactsUnavailable), (error.Status, error.Code)));
        Assert.Equal((403, InventoryAllocationSuggestionCodesV1.ProductFactsForbidden), (drifted.Status, drifted.Code));
        Assert.Empty(products.Queries);
    }

    private static ProductStockSimilarityResultV1 Result(
        ProductStockTechnicalMatchSelectionV1 selection,
        bool identity,
        string? key) =>
        new(
            selection.ProductId,
            selection.ExpectedProductVersion,
            identity ? new(selection.ProductId, selection.ExpectedProductVersion, "SYN-P", "Sentetik ürün") : null,
            key,
            key is null ? null : 1.25m,
            key is null ? null : 1250m,
            null,
            key is null ? null : "SYN-COAT-A");

    private sealed class ProductFacts(
        Func<ProductStockSimilarityQueryV1, ProductStockSimilarityBatchV1> answer)
        : IProductStockSimilarityFactsV1
    {
        public List<ProductStockSimilarityQueryV1> Queries { get; } = [];

        public Task<ProductStockSimilarityBatchV1> ReadSimilarityAsync(
            ProductStockSimilarityQueryV1 query,
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

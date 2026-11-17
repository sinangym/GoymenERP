using GoymenERP.Api.PurchasingRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Actual host adapter with explicitly synthetic Product contracts; no SQL/runtime-owner acceptance claim.</summary>
public sealed class PurchaseInvoiceImportProductTypeBridgeTests
{
    [Theory]
    [InlineData(ProductItemTypesV1.Steel, PurchasingProductItemTypeKindV1.Steel)]
    [InlineData(ProductItemTypesV1.Scrap, PurchasingProductItemTypeKindV1.Scrap)]
    [InlineData(ProductItemTypesV1.Service, PurchasingProductItemTypeKindV1.Service)]
    public async Task Import_product_discovery_carries_the_real_owner_item_type_and_its_independent_version(string type, PurchasingProductItemTypeKindV1 kind)
    {
        var inputs = new Inputs { Type = type }; var adapter = inputs.Adapter;
        var value = await adapter.SearchAsync("SYN", new(2026, 11, 17), default);
        var product = Assert.Single(value.Items);
        Assert.Equal((inputs.ProductId, "3", type, "7"), (product.ProductId, product.ProductVersion, product.ItemType, product.ItemTypeVersion));
        var source = await adapter.GetAsync(inputs.ProductId);
        Assert.Equal(PurchasingProductItemTypeOutcomeV1.Found, source.Outcome);
        Assert.Equal((kind, 7L), (source.ItemType!.Value, source.Version!.Value));
        Assert.Equal(inputs.CompanyId, source.CompanyId); Assert.Equal(inputs.ProductId, source.ProductId);
    }

    [Theory]
    [InlineData(ProductItemTypeOutcomeV1.Unavailable)]
    [InlineData(ProductItemTypeOutcomeV1.NotFound)]
    [InlineData(ProductItemTypeOutcomeV1.NotSet)]
    public async Task Missing_owner_type_never_appears_as_a_General_or_empty_successful_product_search(ProductItemTypeOutcomeV1 outcome)
    {
        var inputs = new Inputs { Outcome = outcome };
        var error = await Assert.ThrowsAsync<PurchasingServiceException>(() => inputs.Adapter.SearchAsync(null, new(2026, 11, 17), default));
        Assert.Equal(503, error.Status);
    }

    [Theory]
    [InlineData("General", 7)]
    [InlineData("Steel", 0)]
    public async Task Malformed_owner_type_or_version_fails_closed(string type, long version)
    {
        var inputs = new Inputs { Type = type, TypeVersion = version };
        var answer = await inputs.Adapter.GetAsync(inputs.ProductId);
        Assert.Equal(PurchasingProductItemTypeOutcomeV1.Unavailable, answer.Outcome);
    }

    [Fact]
    public async Task Denied_or_changed_company_never_discloses_an_owner_type()
    {
        var denied = new Inputs { Outcome = ProductItemTypeOutcomeV1.Denied };
        Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() => denied.Adapter.SearchAsync(null, new(2026, 11, 17), default))).Status);
        var moved = new Inputs(); moved.AfterRead = () => moved.CompanyId = Guid.NewGuid();
        var value = await moved.Adapter.GetAsync(moved.ProductId);
        Assert.Equal(PurchasingProductItemTypeOutcomeV1.Denied, value.Outcome); Assert.Null(value.ItemType); Assert.Null(value.Version);
    }

    private sealed class Inputs : ICompanyContext, ICurrentUser, IProductItemTypeReaderV1, IProductPurchaseCandidatesV1
    {
        public Guid CompanyId { get; set; } = Guid.NewGuid(); public Guid ProductId { get; } = Guid.NewGuid();
        public string Type { get; init; } = ProductItemTypesV1.Steel; public long TypeVersion { get; init; } = 7;
        public ProductItemTypeOutcomeV1 Outcome { get; init; } = ProductItemTypeOutcomeV1.Found;
        public Action? AfterRead { get; set; }
        public PurchaseInvoiceImportProductSearch Adapter => new(this, this, this, this);
        public Task<ProductItemTypeReadV1> GetAsync(Guid productId, CancellationToken token = default)
        { AfterRead?.Invoke(); return Task.FromResult(new ProductItemTypeReadV1(Outcome, Type, TypeVersion)); }
        public Task<ProductPurchaseCandidatesV1> SearchAsync(ProductPurchaseCandidateQueryV1 query, CancellationToken token)
            => Task.FromResult(new ProductPurchaseCandidatesV1([new(ProductId, "3", "SYN", "SENTETIK ürün", Guid.NewGuid(), "KGM", Guid.NewGuid(), "2")], false));
        public bool IsResolved => true; public void Set(Guid companyId) => CompanyId = companyId;
        public bool IsAuthenticated => true; public string UserId => "SENTETIK-actor";
        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { CompanyId }; public bool CanAccessCompany(Guid companyId) => companyId == CompanyId;
    }
}

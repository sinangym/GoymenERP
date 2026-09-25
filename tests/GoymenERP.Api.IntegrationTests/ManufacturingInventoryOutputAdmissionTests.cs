using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class ManufacturingInventoryOutputAdmissionTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Product = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid Location = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly DateTimeOffset At = new(2026, 9, 25, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exact_eligible_product_and_active_company_location_are_accepted_even_when_external(bool isExternal)
    {
        var products = new Products(Eligible());
        var locations = new Locations(new OrganizationLocationStatusV1(Location, true, isExternal));
        var admission = new ManufacturingInventoryOutputAdmission(products, locations);

        await admission.ValidateAsync(Company, Command(), CancellationToken.None);

        Assert.Equal(new ProductStockSelectionV1(Product, "12", new DateOnly(2026, 9, 25)),
            Assert.Single(products.Selections));
        Assert.Equal((Company, Location), Assert.Single(locations.Requests));
    }

    [Fact]
    public async Task Counterparty_identity_is_fail_closed_before_owner_reads_until_its_reference_port_exists()
    {
        var products = new Products(Eligible());
        var locations = new Locations(new OrganizationLocationStatusV1(Location, true, false));
        var admission = new ManufacturingInventoryOutputAdmission(products, locations);

        var error = await Assert.ThrowsAsync<ManufacturingInventoryRealizationPortException>(() =>
            admission.ValidateAsync(Company,
                Command(ownerKind: StockPartyKind.Counterparty, ownerId: Guid.NewGuid()), CancellationToken.None));

        Assert.Equal((ManufacturingApiStatus.Unavailable, InventoryManufacturingRealizationCodesV1.Unavailable),
            (error.Status, error.Code));
        Assert.Empty(products.Selections);
        Assert.Empty(locations.Requests);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("custodian")]
    public async Task Company_party_identity_must_be_the_resolved_company(string dimension)
    {
        var products = new Products(Eligible());
        var locations = new Locations(new OrganizationLocationStatusV1(Location, true, false));
        var admission = new ManufacturingInventoryOutputAdmission(products, locations);
        var command = dimension == "owner"
            ? Command(ownerId: Guid.NewGuid())
            : Command(custodianId: Guid.NewGuid());

        var error = await Assert.ThrowsAsync<ManufacturingInventoryRealizationPortException>(() =>
            admission.ValidateAsync(Company, command, CancellationToken.None));

        Assert.Equal((ManufacturingApiStatus.Invalid, InventoryManufacturingRealizationCodesV1.InvalidRequest,
                dimension + "Id"),
            (error.Status, error.Code, error.Field));
        Assert.Empty(products.Selections);
        Assert.Empty(locations.Requests);
    }

    [Fact]
    public async Task Product_owner_refusal_is_an_existing_inventory_product_ineligible_answer()
    {
        var products = new Products(new ProductStockEligibilityV1(null,
            [new ProductFieldError("expectedProductVersion", "version_conflict")]));
        var locations = new Locations(new OrganizationLocationStatusV1(Location, true, false));
        var admission = new ManufacturingInventoryOutputAdmission(products, locations);

        var error = await Assert.ThrowsAsync<ManufacturingInventoryRealizationPortException>(() =>
            admission.ValidateAsync(Company, Command(), CancellationToken.None));

        Assert.Equal((ManufacturingApiStatus.Unprocessable, InventoryOpeningCodesV1.ProductIneligible, "productId"),
            (error.Status, error.Code, error.Field));
        Assert.Empty(locations.Requests);
    }

    [Theory]
    [InlineData(false, InventoryOpeningCodesV1.LocationNotFound)]
    [InlineData(true, InventoryOpeningCodesV1.LocationInactive)]
    public async Task Missing_or_inactive_location_keeps_the_existing_inventory_location_answer(
        bool present,
        string expectedCode)
    {
        var products = new Products(Eligible());
        var locations = new Locations(present
            ? new OrganizationLocationStatusV1(Location, false, false)
            : null);
        var admission = new ManufacturingInventoryOutputAdmission(products, locations);

        var error = await Assert.ThrowsAsync<ManufacturingInventoryRealizationPortException>(() =>
            admission.ValidateAsync(Company, Command(), CancellationToken.None));

        Assert.Equal((ManufacturingApiStatus.Unprocessable, expectedCode, "locationId"),
            (error.Status, error.Code, error.Field));
    }

    [Fact]
    public async Task Product_authority_denial_is_not_relabelled_as_ineligibility()
    {
        var products = new Products(new ProductOperationException("product_access_denied", 403));
        var admission = new ManufacturingInventoryOutputAdmission(products,
            new Locations(new OrganizationLocationStatusV1(Location, true, false)));

        var error = await Assert.ThrowsAsync<ManufacturingInventoryRealizationPortException>(() =>
            admission.ValidateAsync(Company, Command(), CancellationToken.None));

        Assert.Equal((ManufacturingApiStatus.Denied, ManufacturingApiCodes.AuthorityDenied),
            (error.Status, error.Code));
    }

    private static ProductStockEligibilityV1 Eligible() => new(
        new ProductStockSnapshotV1(Product, "12", "P-12", "Ürün", StockUnit(), new DateOnly(2026, 9, 25)), []);

    private static ProductSalesUnitProfileV1 StockUnit() => new(
        Company,
        Guid.Parse("40000000-0000-0000-0000-000000000004"),
        Guid.Parse("50000000-0000-0000-0000-000000000005"),
        "1");

    private static ManufacturingInventoryOutputCommand Command(
        StockPartyKind ownerKind = StockPartyKind.Company,
        Guid? ownerId = null,
        StockPartyKind custodianKind = StockPartyKind.Company,
        Guid? custodianId = null) =>
        new(Guid.NewGuid(), 2, Guid.NewGuid(), Guid.NewGuid(), 8, OutputClass.GoodProduct,
            Product, "12", Guid.NewGuid(), Guid.NewGuid(), "LBL-OUT-1", "H-1", "C-1", ["quality-certificate"],
            ownerKind, ownerId ?? Company, custodianKind, custodianId ?? Company, Location, BlockState.Free,
            Guid.NewGuid(), new Kilograms(10m), At);

    private sealed class Products : IProductStockEligibilityV1
    {
        private readonly ProductStockEligibilityV1? answer;
        private readonly Exception? exception;

        public Products(ProductStockEligibilityV1 answer) => this.answer = answer;

        public Products(Exception exception) => this.exception = exception;

        public List<ProductStockSelectionV1> Selections { get; } = [];

        public Task<ProductStockEligibilityV1> EvaluateAsync(
            ProductStockSelectionV1 selection,
            CancellationToken cancellationToken)
        {
            Selections.Add(selection);
            return exception is null
                ? Task.FromResult(answer!)
                : Task.FromException<ProductStockEligibilityV1>(exception);
        }
    }

    private sealed class Locations(OrganizationLocationStatusV1? answer) : IOrganizationLocationStatusReaderV1
    {
        public List<(Guid CompanyId, Guid LocationId)> Requests { get; } = [];

        public Task<OrganizationLocationStatusV1?> ReadAsync(
            Guid companyId,
            Guid locationId,
            CancellationToken cancellationToken = default)
        {
            Requests.Add((companyId, locationId));
            return Task.FromResult(answer);
        }
    }
}

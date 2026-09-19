using GoymenERP.Api.Endpoints;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// INV-OPEN-1 part 2, central: the two host adapters of the opening command. The location answer is
/// Organization's module read under the Inventory owner's precedence (NotFound, External, Inactive,
/// Active); the product answer is Product's stock eligibility with its caller refusals kept as 401/403 and
/// its field errors relative to the line. Nothing here reaches a database: the owners are doubles that
/// return exactly what the real ports can return.
/// </summary>
public sealed class InventoryOpeningHostPortTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Location = Guid.Parse("4f000000-0000-4000-8000-000000000001");
    private static readonly Guid Product = Guid.Parse("7e000000-0000-4000-8000-000000000001");
    private static readonly DateOnly CountedOn = new(2026, 9, 19);

    [Theory]
    [InlineData(null, null, InventoryLocationStatus.NotFound)]
    [InlineData(true, false, InventoryLocationStatus.Active)]
    [InlineData(false, false, InventoryLocationStatus.Inactive)]
    [InlineData(true, true, InventoryLocationStatus.External)]
    // What the place is comes before its state: an inactive external location is External.
    [InlineData(false, true, InventoryLocationStatus.External)]
    public async Task A_location_answer_follows_the_owner_precedence(bool? isActive, bool? isExternal,
        InventoryLocationStatus expected)
    {
        var reader = new Locations(isActive is null
            ? null
            : new OrganizationLocationStatusV1(Location, isActive.Value, isExternal!.Value));

        Assert.Equal(expected, await new OrganizationInventoryLocationStatusPort(reader).ReadAsync(Company, Location, default));
        Assert.Equal([(Company, Location)], reader.Reads);
    }

    [Fact]
    public async Task An_owner_that_cannot_answer_is_never_read_as_active()
    {
        var port = new OrganizationInventoryLocationStatusPort(new Locations(failure: new OrganizationCompanyContextMismatchException()));

        await Assert.ThrowsAsync<OrganizationCompanyContextMismatchException>(() => port.ReadAsync(Company, Location, default));
    }

    [Fact]
    public async Task An_eligible_product_is_asked_for_the_counted_version_on_the_counted_date()
    {
        var eligibility = new Eligibility(new ProductStockEligibilityV1(
            new ProductStockSnapshotV1(Product, "4", "SYN-P", "SENTETIK",
                new ProductSalesUnitProfileV1(Company, Guid.NewGuid(), Guid.NewGuid(), "1"), CountedOn), []));

        var answer = await new ProductInventoryStockPort(eligibility).EvaluateAsync(Product, "4", CountedOn, default);

        Assert.True(answer.IsEligible);
        Assert.Empty(answer.Issues);
        Assert.Equal([new ProductStockSelectionV1(Product, "4", CountedOn)], eligibility.Selections);
    }

    [Fact]
    public async Task A_refused_product_returns_its_errors_with_only_the_version_field_renamed()
    {
        var eligibility = new Eligibility(new ProductStockEligibilityV1(null,
        [
            new ProductFieldError("productId", "lifecycle_conflict"),
            new ProductFieldError("expectedProductVersion", "version_conflict"),
            new ProductFieldError("references.units.stock", "invalid_reference"),
        ]));

        var answer = await new ProductInventoryStockPort(eligibility).EvaluateAsync(Product, "3", CountedOn, default);

        Assert.False(answer.IsEligible);
        Assert.Equal(
            [
                new InventoryFieldIssueV1("productId", "lifecycle_conflict"),
                new InventoryFieldIssueV1("productVersion", "version_conflict"),
                new InventoryFieldIssueV1("references.units.stock", "invalid_reference"),
            ],
            answer.Issues);
    }

    [Theory]
    [InlineData(401, 401, "unauthenticated")]
    [InlineData(403, 403, "forbidden")]
    public async Task A_refusal_of_the_caller_stays_a_refusal_of_the_caller(int productStatus, int status, string code)
    {
        var port = new ProductInventoryStockPort(new Eligibility(failure: new ProductOperationException("product_access_denied", productStatus)));

        var refused = await Assert.ThrowsAsync<InventoryOpeningServiceException>(
            () => port.EvaluateAsync(Product, "4", CountedOn, default));

        Assert.Equal((status, code), (refused.Status, refused.Code));
    }

    [Fact]
    public async Task Any_other_owner_failure_is_not_an_answer()
    {
        var port = new ProductInventoryStockPort(new Eligibility(failure: new ProductOperationException("product_family_policy_unavailable", 503)));

        var failure = await Assert.ThrowsAsync<ProductOperationException>(() => port.EvaluateAsync(Product, "4", CountedOn, default));
        Assert.Equal(503, failure.StatusCode);
    }

    private sealed class Locations(OrganizationLocationStatusV1? answer = null, Exception? failure = null)
        : IOrganizationLocationStatusReaderV1
    {
        public List<(Guid CompanyId, Guid LocationId)> Reads { get; } = [];

        public Task<OrganizationLocationStatusV1?> ReadAsync(Guid companyId, Guid locationId,
            CancellationToken cancellationToken = default)
        {
            Reads.Add((companyId, locationId));
            return failure is null ? Task.FromResult(answer) : Task.FromException<OrganizationLocationStatusV1?>(failure);
        }
    }

    private sealed class Eligibility(ProductStockEligibilityV1? answer = null, Exception? failure = null)
        : IProductStockEligibilityV1
    {
        public List<ProductStockSelectionV1> Selections { get; } = [];

        public Task<ProductStockEligibilityV1> EvaluateAsync(ProductStockSelectionV1 selection, CancellationToken cancellationToken)
        {
            Selections.Add(selection);
            return failure is null ? Task.FromResult(answer!) : Task.FromException<ProductStockEligibilityV1>(failure);
        }
    }
}

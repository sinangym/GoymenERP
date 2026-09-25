using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using static GoymenERP.Product.UnitTests.ProductSalesEligibilityServiceTests;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductSalesCandidateServiceTests
{
    [Fact]
    public async Task Returns_real_eligibility_identity_and_settings_unit_code_not_a_master_card()
    {
        var world = new World();
        var reader = new Reader();
        var result = await Service(world, reader).SearchAsync(new("  PSQL  ", On), default);
        var item = Assert.Single(result.Items);
        Assert.Equal((ProductId, ProductSalesEligibilityServiceTests.Version, "PSQL-1", "Galvaniz bant"),
            (item.ProductId, item.ProductVersion, item.Code, item.Name));
        var unit = Assert.Single(item.OrderUnits);
        Assert.Equal((SalesUnitId, "synthetic-sales", world.Settings.ProfileIdFor(SalesUnitId), ProductSalesEligibilityServiceTests.Version),
            (unit.UnitId, unit.Code, unit.ProfileId, unit.ProfileVersion));
        Assert.Equal("PSQL", reader.Query!.Q);
        Assert.All(world.Authorization.AskedPermissions, permission =>
            Assert.Equal(ProductPermissionManifestV1.OperationalSelectionRead, permission));
        Assert.False(result.SearchWindowFull);
    }

    [Fact]
    public async Task Denial_precedes_query_validation_and_storage()
    {
        var world = new World();
        world.Authorization.Granted = false;
        var reader = new Reader();
        var error = await Assert.ThrowsAsync<ProductOperationException>(() =>
            Service(world, reader).SearchAsync(new(null, On, 101), default));
        Assert.Equal(403, error.StatusCode);
        Assert.Equal(0, reader.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Invalid_bounds_do_not_read(int limit)
    {
        var reader = new Reader();
        await Assert.ThrowsAsync<ProductValidationException>(() =>
            Service(new World(), reader).SearchAsync(new(null, On, limit), default));
        Assert.Equal(0, reader.Calls);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("actor")]
    [InlineData("access")]
    [InlineData("grant")]
    public async Task Changed_authority_discards_the_entire_window(string change)
    {
        var world = new World();
        var reader = new Reader { During = () =>
        {
            if (change == "grant") world.Authorization.Granted = false;
            else world.Change(change);
        } };
        var error = await Assert.ThrowsAsync<ProductOperationException>(() =>
            Service(world, reader).SearchAsync(new(null, On), default));
        Assert.Equal((403, "product_access_denied"), (error.StatusCode, error.Code));
        Assert.Equal(0, world.Records.Reads);
    }

    [Fact]
    public async Task Storage_failure_after_revocation_releases_only_denial()
    {
        var world = new World();
        var reader = new Reader { During = () => world.Authorization.Granted = false,
            Failure = new InvalidOperationException("private storage detail") };
        var error = await Assert.ThrowsAsync<ProductOperationException>(() =>
            Service(world, reader).SearchAsync(new(null, On), default));
        Assert.Equal(403, error.StatusCode);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(2, true)]
    [InlineData(1, false)]
    public async Task Nonactive_or_nonsellable_records_never_become_candidates(int lifecycle, bool sellable)
    {
        var world = new World { Record = StoredProduct(lifecycle, new(sellable, 1)) };
        var result = await Service(world, new Reader()).SearchAsync(new(null, On), default);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Concurrent_product_version_change_is_omitted_and_window_is_not_an_exhaustive_claim()
    {
        var world = new World { Record = StoredProduct() with { Version = long.Parse(ProductSalesEligibilityServiceTests.Version) + 1 } };
        var result = await Service(world, new Reader { HasMore = true }).SearchAsync(new(null, On), default);
        Assert.Empty(result.Items);
        Assert.True(result.SearchWindowFull);
    }

    [Fact]
    public async Task Unavailable_family_is_not_disguised_as_empty_search()
    {
        var world = new World();
        world.Family.Result = null;
        var error = await Assert.ThrowsAsync<ProductOperationException>(() =>
            Service(world, new Reader()).SearchAsync(new(null, On), default));
        Assert.Equal(503, error.StatusCode);
    }

    [Fact]
    public async Task Unit_label_read_cannot_release_data_after_permission_revocation()
    {
        var world = new World();
        world.Settings.During = () => { if (world.Settings.Calls == 3) world.Authorization.Granted = false; };
        var error = await Assert.ThrowsAsync<ProductOperationException>(() =>
            Service(world, new Reader()).SearchAsync(new(null, On), default));
        Assert.Equal(403, error.StatusCode);
    }

    [Fact]
    public async Task Changed_unit_profile_is_not_joined_to_an_older_eligibility_snapshot()
    {
        var world = new World();
        world.Settings.Shape = (_, profile) => world.Settings.Calls == 3
            ? profile with { Profile = profile.Profile with { Version = 99 } } : profile;
        var result = await Service(world, new Reader()).SearchAsync(new(null, On), default);
        Assert.Empty(result.Items);
    }

    private static ProductSalesCandidateService Service(World world, Reader reader) =>
        new(world.Company, world.User, world.Authorization, reader, world.Service(),
            new ProductUnitProfileAdapter(world.Settings, world.Company));

    private sealed class Reader : IProductSalesCandidateReader
    {
        public int Calls { get; private set; }
        public ProductSalesCandidateQueryV1? Query { get; private set; }
        public Action? During { get; init; }
        public Exception? Failure { get; init; }
        public bool HasMore { get; init; }
        public Task<ProductSalesCandidateWindow> ReadAsync(ProductSalesReadScope scope,
            ProductSalesCandidateQueryV1 query, CancellationToken cancellationToken)
        {
            Calls++;
            Query = query;
            Assert.Equal(CompanyId, scope.CompanyId);
            During?.Invoke();
            return Failure is null ? Task.FromResult(new ProductSalesCandidateWindow([StoredProduct()], HasMore))
                : Task.FromException<ProductSalesCandidateWindow>(Failure);
        }
    }
}

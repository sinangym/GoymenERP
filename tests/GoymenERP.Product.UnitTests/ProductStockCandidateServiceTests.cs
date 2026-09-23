using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using static GoymenERP.Product.UnitTests.ProductSalesEligibilityServiceTests;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductStockCandidateServiceTests
{
    [Fact]
    public async Task Returns_only_the_real_stock_eligibility_identity_and_settings_unit_label()
    {
        var world = new World();
        var reader = new Reader();

        var result = await Service(world, reader).SearchAsync(new("  PSQL  ", On), default);

        var item = Assert.Single(result.Items);
        Assert.Equal((ProductId, ProductSalesEligibilityServiceTests.Version, "PSQL-1", "Galvaniz bant"),
            (item.ProductId, item.ProductVersion, item.Code, item.Name));
        Assert.Equal((StockUnitId, "synthetic-kg", world.Settings.ProfileIdFor(StockUnitId),
                ProductSalesEligibilityServiceTests.Version),
            (item.StockUnitId, item.StockUnitCode, item.ProfileId, item.ProfileVersion));
        Assert.Equal("PSQL", reader.Query!.Q);
        Assert.All(world.Authorization.AskedPermissions, permission =>
            Assert.Equal(ProductPermissionManifestV1.OperationalSelectionRead, permission));
        Assert.False(result.SearchWindowFull);
    }

    [Fact]
    public async Task Stock_candidates_do_not_invent_a_commercial_role_requirement()
    {
        var world = new World { Record = StoredProduct(sellable: new(false, 1)) };

        var result = await Service(world, new Reader()).SearchAsync(new(null, On), default);

        Assert.Single(result.Items);
    }

    [Fact]
    public async Task Denial_precedes_query_validation_and_storage()
    {
        var world = new World();
        world.Authorization.Granted = false;
        var reader = new Reader();

        var error = await Assert.ThrowsAsync<ProductOperationException>(() =>
            Service(world, reader).SearchAsync(new(null, On, 101), default));

        Assert.Equal((403, ProductSalesEligibilityService.AccessDeniedCode), (error.StatusCode, error.Code));
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
        var reader = new Reader
        {
            During = () =>
            {
                if (change == "grant") world.Authorization.Granted = false;
                else world.Change(change);
            },
        };

        var error = await Assert.ThrowsAsync<ProductOperationException>(() =>
            Service(world, reader).SearchAsync(new(null, On), default));

        Assert.Equal((403, ProductSalesEligibilityService.AccessDeniedCode), (error.StatusCode, error.Code));
        Assert.Equal(0, world.Records.Reads);
    }

    [Fact]
    public async Task Storage_failure_after_revocation_releases_only_denial()
    {
        var world = new World();
        var reader = new Reader
        {
            During = () => world.Authorization.Granted = false,
            Failure = new InvalidOperationException("private storage detail"),
        };

        var error = await Assert.ThrowsAsync<ProductOperationException>(() =>
            Service(world, reader).SearchAsync(new(null, On), default));

        Assert.Equal(403, error.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Nonactive_records_never_become_candidates(int lifecycle)
    {
        var world = new World { Record = StoredProduct(lifecycle: lifecycle) };

        var result = await Service(world, new Reader()).SearchAsync(new(null, On), default);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Missing_stock_binding_and_concurrent_version_change_are_omitted()
    {
        var missing = new World { Record = StoredProduct(units: new(Sales: SalesUnitId)) };
        Assert.Empty((await Service(missing, new Reader()).SearchAsync(new(null, On), default)).Items);

        var changed = new World
        {
            Record = StoredProduct() with { Version = long.Parse(ProductSalesEligibilityServiceTests.Version) + 1 },
        };
        var result = await Service(changed, new Reader { HasMore = true }).SearchAsync(new(null, On), default);
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
        world.Settings.During = () =>
        {
            if (world.Settings.Calls == 2) world.Authorization.Granted = false;
        };

        var error = await Assert.ThrowsAsync<ProductOperationException>(() =>
            Service(world, new Reader()).SearchAsync(new(null, On), default));

        Assert.Equal(403, error.StatusCode);
    }

    [Fact]
    public async Task Changed_unit_profile_is_not_joined_to_an_older_eligibility_snapshot()
    {
        var world = new World();
        world.Settings.Shape = (_, profile) => world.Settings.Calls == 2
            ? profile with { Profile = profile.Profile with { Version = 99 } }
            : profile;

        var result = await Service(world, new Reader()).SearchAsync(new(null, On), default);

        Assert.Empty(result.Items);
    }

    private static ProductStockCandidateService Service(World world, Reader reader) =>
        new(
            world.Company,
            world.User,
            world.Authorization,
            reader,
            new ProductStockEligibilityService(
                world.Company,
                world.User,
                world.Authorization,
                world.Records,
                world.Family,
                new ProductUnitProfileAdapter(world.Settings, world.Company)),
            new ProductUnitProfileAdapter(world.Settings, world.Company));

    private sealed class Reader : IProductStockCandidateReader
    {
        public int Calls { get; private set; }
        public ProductStockCandidateQueryV1? Query { get; private set; }
        public Action? During { get; init; }
        public Exception? Failure { get; init; }
        public bool HasMore { get; init; }

        public Task<ProductStockCandidateWindow> ReadAsync(
            ProductStockReadScope scope,
            ProductStockCandidateQueryV1 query,
            CancellationToken cancellationToken)
        {
            Calls++;
            Query = query;
            Assert.Equal(CompanyId, scope.CompanyId);
            During?.Invoke();
            return Failure is null
                ? Task.FromResult(new ProductStockCandidateWindow([StoredProduct()], HasMore))
                : Task.FromException<ProductStockCandidateWindow>(Failure);
        }
    }
}

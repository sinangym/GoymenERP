using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Fixtures;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

// InMemory owner-port tests. These are not SQL/browser acceptance or evidence of host gate/real grants.
public sealed class ProductStockCandidateFixtureStoreTests
{
    [Fact]
    public async Task Active_stock_card_reopens_with_real_catalog_tuple_and_exact_replay()
    {
        await using var provider = Services();
        await Registry(provider);
        await ReleasedCodeGroup(provider);
        ProductStockCandidateFixtureOutput? first = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var scope = provider.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IProductStockCandidateFixtureStore>().EnsureAsync(default);
            first ??= result;
            Assert.Equal(first, result);
        }

        await using var read = provider.CreateAsyncScope();
        var db = read.ServiceProvider.GetRequiredService<ProductDbContext>();
        var record = (await db.Products.AsNoTracking().SingleAsync()).ToRecord();
        var card = new ProductCardView(ProductDraftView.FromProduct(record.RestoreProduct()), record.RestoreReferences());
        Assert.Equal("Active", card.Product.Lifecycle);
        Assert.Equal("2", card.Product.Version);
        Assert.Equal(first!.ProductId, card.Product.Id);
        Assert.Equal(first.ProductVersion, card.Product.Version);
        Assert.Equal(first.Code, card.Product.Code);
        Assert.Equal(first.Name, card.Product.Name);
        Assert.Equal(ApprovedUnitProfileCatalog.GdcKilogramV1.UnitId, first.UnitId);
        Assert.Equal(ApprovedUnitProfileCatalog.GdcKilogramV1.ProfileId, first.ProfileId);
        Assert.Equal("1", first.ProfileVersion);
        Assert.Equal(first.UnitId, card.References.Units!.Stock);
        Assert.Equal(first.UnitId, card.References.Units.Sales);
        Assert.Null(card.References.Units.Purchase);
        Assert.Empty(await new ProductFamilyRequirementPolicyV2(new SqlProductFamilyDefinitionSource(db))
            .ValidateAsync(card, default));
        var groups = await db.CodeGroups.ToListAsync();
        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, group => group.ToContract() == ProductInitialCodeGroupV1.Sac().ToContract());
        Assert.Contains(groups, group => group.Id == ProductStockCandidateFixture.GroupId
            && group.Name == ProductStockCandidateFixture.Name && group.Prefix == ProductStockCandidateFixture.Prefix);
        Assert.Equal(1, (await db.CodeCounters.SingleAsync()).LastSequence);
        var registration = await db.CodeRegistrations.SingleAsync();
        Assert.Equal(first.ProductId, registration.ProductId);
        Assert.Equal(first.Code, registration.Code);
        Assert.True(registration.IsCurrent);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("version")]
    [InlineData("inactive")]
    [InlineData("company")]
    [InlineData("unit")]
    [InlineData("unavailable")]
    public async Task Missing_or_mismatched_settings_never_materializes_a_card(string fault)
    {
        var settings = new Settings { Fault = fault };
        await using var provider = Services(settings);
        await Registry(provider);
        await using var scope = provider.CreateAsyncScope();
        await Assert.ThrowsAnyAsync<Exception>(() =>
            scope.ServiceProvider.GetRequiredService<IProductStockCandidateFixtureStore>().EnsureAsync(default));
        await AssertEmpty(scope.ServiceProvider.GetRequiredService<ProductDbContext>());
    }

    [Fact]
    public async Task Registry_must_be_materialized_in_a_prior_scope()
    {
        await using var provider = Services();
        await using var scope = provider.CreateAsyncScope();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IProductStockCandidateFixtureStore>().EnsureAsync(default));
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        Assert.Empty(await db.Families.ToListAsync());
        await AssertEmpty(db);
    }

    [Fact]
    public async Task Tracked_foreign_changes_are_not_flushed()
    {
        await using var provider = Services();
        await Registry(provider);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        var family = await db.Families.SingleAsync();
        db.Entry(family).Property(row => row.SourceRef).CurrentValue = "FOREIGN-RELEASE";
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IProductStockCandidateFixtureStore>().EnsureAsync(default));
        await using var read = provider.CreateAsyncScope();
        var stored = read.ServiceProvider.GetRequiredService<ProductDbContext>();
        Assert.Equal(ProductTechnicalCardFixture.SourceRef, (await stored.Families.SingleAsync()).SourceRef);
        await AssertEmpty(stored);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Partial_or_foreign_store_is_never_repaired(bool foreign)
    {
        await using var provider = Services();
        await Registry(provider);
        await using (var seed = provider.CreateAsyncScope())
            await seed.ServiceProvider.GetRequiredService<IProductCodeGroupFixtureStore>().EnsureAsync(new(
                foreign ? Guid.NewGuid() : ProductStockCandidateFixture.GroupId,
                ProductStockCandidateFixture.Name, ProductStockCandidateFixture.Prefix), default);
        await using var scope = provider.CreateAsyncScope();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IProductStockCandidateFixtureStore>().EnsureAsync(default));
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        Assert.Single(await db.CodeGroups.ToListAsync());
        Assert.Empty(await db.Products.ToListAsync());
        Assert.Empty(await db.CodeCounters.ToListAsync());
        Assert.Empty(await db.CodeRegistrations.ToListAsync());
    }

    [Fact]
    public async Task Existing_pur01_fixture_is_not_reused_or_overwritten()
    {
        await using var provider = Services();
        await Registry(provider);
        await using (var purchase = provider.CreateAsyncScope())
            await purchase.ServiceProvider.GetRequiredService<IProductPurchaseCandidateFixtureStore>().EnsureAsync(default);
        await using (var stock = provider.CreateAsyncScope())
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                stock.ServiceProvider.GetRequiredService<IProductStockCandidateFixtureStore>().EnsureAsync(default));
        await using var read = provider.CreateAsyncScope();
        var record = (await read.ServiceProvider.GetRequiredService<ProductDbContext>().Products.AsNoTracking().SingleAsync()).ToRecord();
        Assert.Equal(ProductPurchaseCandidateFixture.ProductId, record.Id);
        Assert.Equal(ProductPurchaseCandidateFixture.Name, record.Name);
        var units = Assert.IsType<ProductUnitReferences>(record.RestoreReferences().Units);
        Assert.Equal(units.Stock, units.Purchase);
    }

    [Fact]
    public void Fixture_is_not_registered_implicitly()
    {
        using var provider = new ServiceCollection().AddProductTechnicalCardFixtureStore().BuildServiceProvider();
        Assert.Null(provider.GetService<IProductStockCandidateFixtureStore>());
    }

    private static async Task AssertEmpty(ProductDbContext db)
    {
        Assert.Empty(await db.Products.ToListAsync());
        Assert.Empty(await db.CodeGroups.ToListAsync());
        Assert.Empty(await db.CodeCounters.ToListAsync());
        Assert.Empty(await db.CodeRegistrations.ToListAsync());
    }

    private static async Task Registry(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IProductTechnicalCardFixtureStore>().EnsureAsync(default);
    }

    private static async Task ReleasedCodeGroup(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var released = ProductInitialCodeGroupV1.Sac();
        await scope.ServiceProvider.GetRequiredService<IProductCodeGroupFixtureStore>().EnsureAsync(
            new(released.Id, released.Name, released.Prefix), default);
    }

    private static ServiceProvider Services(Settings? settings = null)
    {
        var database = Guid.NewGuid().ToString();
        return new ServiceCollection()
            .AddSingleton<ICompanyContext, Company>()
            .AddSingleton<ISettingsPrerequisiteProvider>(settings ?? new Settings())
            .AddScoped<ProductUnitProfileAdapter>()
            .AddDbContext<ProductDbContext>(options => options.UseInMemoryDatabase(database))
            .AddProductTechnicalCardFixtureStore().AddProductCodeGroupFixtureStore()
            .AddProductPurchaseCandidateFixtureStore().AddProductStockCandidateFixtureStore().BuildServiceProvider();
    }

    private sealed class Company : ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId { get; private set; } = ApprovedUnitProfileCatalog.GdcCompanyId;
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    private sealed class Settings : ISettingsPrerequisiteProvider
    {
        public string? Fault { get; init; }
        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default)
        {
            var approved = ApprovedUnitProfileCatalog.GdcKilogramV1;
            Assert.Equal(approved.UnitId, unitId);
            Assert.Equal(ProductStockCandidateFixture.EffectiveOn, effectiveOn);
            if (Fault == "unavailable") throw new SettingsPrerequisiteException("settings_unavailable");
            var profile = new UnitProfileV1(approved.CompanyId, approved.UnitId, approved.UnitCode,
                approved.DimensionCode, approved.IsKilogram, new(approved.ProfileId, approved.Version),
                approved.Scale, approved.IsActive, approved.EffectiveFrom, approved.EffectiveThrough);
            return Task.FromResult(Fault switch
            {
                "profile" => profile with { Profile = profile.Profile with { ProfileId = Guid.NewGuid() } },
                "version" => profile with { Profile = profile.Profile with { Version = 2 } },
                "inactive" => profile with { IsActive = false },
                "company" => profile with { CompanyId = Guid.NewGuid() },
                "unit" => profile with { UnitId = Guid.NewGuid() },
                _ => profile,
            });
        }

        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn,
            DateOnly? valueOn, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

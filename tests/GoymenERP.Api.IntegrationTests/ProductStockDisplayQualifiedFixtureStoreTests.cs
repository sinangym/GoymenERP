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

// Owner-port/source behavior only. InMemory is not SQL/browser acceptance or proof of the host gate.
public sealed class ProductStockDisplayQualifiedFixtureStoreTests
{
    [Fact]
    public async Task Fixed_card_is_active_sellable_and_uses_only_the_released_display_sources()
    {
        var stored = ProductStockDisplayQualifiedFixture.Record();
        var card = new ProductCardView(
            ProductDraftView.FromProduct(stored.RestoreProduct()),
            stored.RestoreReferences());
        var definition = ProductInitialTechnicalDefinitionV1.Definition();
        var family = definition.Families.Single(row =>
            row.Id == ProductInitialTechnicalDefinitionV1.HotRolledSlitCoilFamilyId);
        var template = Assert.IsType<ProductTemplateV2>(
            ProductFamilyDefinitionRulesV2.TemplateOf(definition, family.Id));

        Assert.Equal(("Active", "2", true),
            (card.Product.Lifecycle, card.Product.Version, card.Product.Roles.Sellable.Value));
        Assert.Equal((ProductInitialTechnicalDefinitionV1.HotRolledSlitCoilFamilyId,
                ProductInitialTechnicalDefinitionV1.TemplateId, "1"),
            (card.Product.FamilyId, card.References.TemplateId, card.References.TemplateVersion));
        Assert.Equal(ApprovedUnitProfileCatalog.GdcKilogramV1.UnitId, card.References.Units!.Stock);
        Assert.Equal(ApprovedUnitProfileCatalog.GdcKilogramV1.UnitId, card.References.Units.Sales);
        Assert.Equal(1.000m, card.References.Attributes!["thickness-mm"].GetDecimal());
        Assert.Equal(1000.000m, card.References.Attributes["width-mm"].GetDecimal());
        Assert.Equal("S235JR", card.References.Attributes["steel-grade"].GetString());
        Assert.Equal("uncoated", card.References.Attributes["coating"].GetString());
        Assert.Equal(ProductInitialTechnicalDefinitionV1.SourceRef, family.SourceRef);
        Assert.Equal(ProductInitialTechnicalDefinitionV1.SourceRef, template.SourceRef);
        Assert.False(ProductManufacturingTechnicalSnapshotService.IsSynthetic(family.SourceRef));
        Assert.False(ProductManufacturingTechnicalSnapshotService.IsSynthetic(template.SourceRef));
        Assert.Empty(ProductManufacturingTechnicalSnapshotService.ValidateTechnicalReferences(
            card.References, definition, template));
        Assert.False(ProductManufacturingTechnicalSnapshotService.UsesSyntheticDictionaryValue(
            card.References, definition, template));
        Assert.Empty(await new ProductFamilyRequirementPolicyV2(new Definition(definition))
            .ValidateAsync(card, default));
    }

    [Fact]
    public async Task Missing_released_registry_fails_closed_without_materializing_a_product()
    {
        await using var provider = Services();
        await using var scope = provider.CreateAsyncScope();
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider
            .GetRequiredService<IProductStockDisplayQualifiedFixtureStore>()
            .EnsureAsync(default));
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        Assert.Empty(await db.Families.ToListAsync());
        Assert.Empty(await db.Products.ToListAsync());
        Assert.Empty(await db.CodeGroups.ToListAsync());
        Assert.Empty(await db.CodeRegistrations.ToListAsync());
        Assert.Empty(await db.CodeCounters.ToListAsync());
    }

    [Fact]
    public async Task Synthetic_only_registry_is_not_promoted_to_a_display_qualified_card()
    {
        await using var provider = Services();
        await using (var registry = provider.CreateAsyncScope())
        {
            await registry.ServiceProvider.GetRequiredService<IProductTechnicalCardFixtureStore>()
                .EnsureAsync(default);
        }

        await using var scope = provider.CreateAsyncScope();
        var before = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        var registryBefore = await new SqlProductFamilyDefinitionSource(before).GetAsync(default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider
            .GetRequiredService<IProductStockDisplayQualifiedFixtureStore>()
            .EnsureAsync(default));
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        var registryAfter = await new SqlProductFamilyDefinitionSource(db).GetAsync(default);
        Assert.Equal(registryBefore.Families, registryAfter.Families);
        Assert.Equal(registryBefore.Templates, registryAfter.Templates);
        Assert.Equal(registryBefore.Entries, registryAfter.Entries);
        Assert.Single(await db.Families.ToListAsync());
        Assert.Empty(await db.Products.ToListAsync());
        Assert.Empty(await db.CodeGroups.ToListAsync());
        Assert.Empty(await db.CodeRegistrations.ToListAsync());
        Assert.Empty(await db.CodeCounters.ToListAsync());
    }

    [Fact]
    public async Task Pending_foreign_change_is_refused_without_flush()
    {
        await using var provider = Services();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        db.Products.Add(ProductRow.FromRecord(ProductStockDisplayQualifiedFixture.Record()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider
            .GetRequiredService<IProductStockDisplayQualifiedFixtureStore>()
            .EnsureAsync(default));

        await using var read = provider.CreateAsyncScope();
        Assert.Empty(await read.ServiceProvider.GetRequiredService<ProductDbContext>().Products.ToListAsync());
    }

    [Fact]
    public void Fixture_is_registered_only_when_explicitly_requested()
    {
        using var normal = new ServiceCollection().AddProductTechnicalCardFixtureStore().BuildServiceProvider();
        Assert.Null(normal.GetService<IProductStockDisplayQualifiedFixtureStore>());

        using var fixture = Services();
        Assert.NotNull(fixture.GetService<IProductStockDisplayQualifiedFixtureStore>());
    }

    private static ServiceProvider Services()
    {
        var database = Guid.NewGuid().ToString();
        return new ServiceCollection()
            .AddSingleton<ICompanyContext, Company>()
            .AddSingleton<ISettingsPrerequisiteProvider, Settings>()
            .AddScoped<ProductUnitProfileAdapter>()
            .AddDbContext<ProductDbContext>(options => options.UseInMemoryDatabase(database))
            .AddProductTechnicalCardFixtureStore()
            .AddProductStockDisplayQualifiedFixtureStore()
            .BuildServiceProvider();
    }

    private sealed class Definition(ProductFamilyDefinitionV2 value) : IProductFamilyDefinitionSourceV2
    {
        public Task<ProductFamilyDefinitionV2> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(value);
    }

    private sealed class Company : ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId { get; private set; } = ApprovedUnitProfileCatalog.GdcCompanyId;
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    private sealed class Settings : ISettingsPrerequisiteProvider
    {
        public Task<UnitProfileV1> GetUnitProfileAsync(
            Guid unitId,
            DateOnly effectiveOn,
            CancellationToken cancellationToken = default)
        {
            var approved = ApprovedUnitProfileCatalog.GdcKilogramV1;
            return Task.FromResult(new UnitProfileV1(
                approved.CompanyId,
                approved.UnitId,
                approved.UnitCode,
                approved.DimensionCode,
                approved.IsKilogram,
                new(approved.ProfileId, approved.Version),
                approved.Scale,
                approved.IsActive,
                approved.EffectiveFrom,
                approved.EffectiveThrough));
        }

        public Task<MoneyProfileV1> GetMoneyProfileAsync(
            Guid currencyId,
            string operation,
            DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(
            string operation,
            DateOnly occurredOn,
            DateOnly? valueOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

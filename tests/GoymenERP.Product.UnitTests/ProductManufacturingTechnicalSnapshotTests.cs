using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using static GoymenERP.Product.UnitTests.ProductSalesEligibilityServiceTests;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductManufacturingTechnicalSnapshotTests
{
    private const string StoredVersion = ProductSalesEligibilityServiceTests.Version;
    private static readonly Guid FamilyId = Guid.Parse("c6100000-0000-4000-8000-000000000201");
    private static readonly Guid TemplateId = Guid.Parse("c6200000-0000-4000-8000-000000000201");
    private static readonly ProductManufacturingTechnicalSnapshotSelectionV1 Selection = new(ProductId, StoredVersion, On);

    [Fact]
    public async Task Accepted_real_revisions_and_kg_profile_produce_the_exact_technical_snapshot()
    {
        var world = World();

        var result = await Service(world).ReadAsync(Selection, default);

        Assert.Empty(result.Errors);
        var snapshot = Assert.IsType<ProductManufacturingTechnicalSnapshotV1>(result.Snapshot);
        Assert.Equal((ProductId, StoredVersion, 1.25m, 1250m, On),
            (snapshot.ProductId, snapshot.ProductVersion, snapshot.ThicknessMm, snapshot.WidthMm, snapshot.EffectiveOn));
        Assert.Equal((FamilyId, "gdc-coil", "#66/5739827887"),
            (snapshot.Family.FamilyId, snapshot.Family.FamilyCode, snapshot.Family.SourceRevision));
        Assert.Equal((TemplateId, "7", "gdc-coil-v7", "#66/5739827887"),
            (snapshot.Template.TemplateId, snapshot.Template.TemplateVersion,
                snapshot.Template.TemplateCode, snapshot.Template.SourceRevision));
        Assert.Equal((CompanyId, StockUnitId, "synthetic-kg", world.Settings.ProfileIdFor(StockUnitId), StoredVersion),
            (snapshot.UnitKg.CompanyId, snapshot.UnitKg.UnitId, snapshot.UnitKg.UnitCode,
                snapshot.UnitKg.ProfileId, snapshot.UnitKg.ProfileVersion));
        Assert.Equal(4, world.Authorization.Calls);
        Assert.All(world.Authorization.AskedPermissions,
            permission => Assert.Equal(ProductPermissionManifestV1.OperationalSelectionRead, permission));
    }

    [Theory]
    [InlineData("missing", ProductFamilyPolicyCodesV1.Required)]
    [InlineData("candidate", ProductFamilyPolicyCodesV1.ValueCandidate)]
    public async Task Missing_or_candidate_family_data_never_produces_a_snapshot(string fault, string code)
    {
        var world = World(fault == "missing" ? TechnicalRecord(includeThickness: false) : TechnicalRecord());
        var source = new DefinitionSource(Definition(candidateQuality: fault == "candidate"));

        var result = await Service(world, source).ReadAsync(Selection, default);

        Assert.Null(result.Snapshot);
        Assert.Contains(result.Errors, error => error.Code == code);
        Assert.Equal(0, world.Settings.Calls);
    }

    [Fact]
    public async Task Synthetic_browser_definition_is_fail_closed_even_when_structurally_valid()
    {
        var world = World();
        var error = await Assert.ThrowsAsync<ProductOperationException>(
            () => Service(world, new(Definition(sourceRevision: "SYNTHETIC-browser-suite-u02")))
                .ReadAsync(Selection, default));

        Assert.Equal((503, ProductManufacturingTechnicalSnapshotService.UnavailableCode),
            (error.StatusCode, error.Code));
        Assert.Equal(0, world.Settings.Calls);
    }

    [Fact]
    public async Task Synthetic_dictionary_provenance_is_fail_closed_even_under_real_family_revisions()
    {
        var world = World();
        var error = await Assert.ThrowsAsync<ProductOperationException>(
            () => Service(world, new(Definition(entrySourceRevision: "SYNTHETIC-browser-suite-u02")))
                .ReadAsync(Selection, default));

        Assert.Equal((503, ProductManufacturingTechnicalSnapshotService.UnavailableCode),
            (error.StatusCode, error.Code));
        Assert.Equal(0, world.Settings.Calls);
    }

    [Theory]
    [InlineData("stale", "expectedProductVersion", ProductApiV1.VersionConflict)]
    [InlineData("inactive", "productId", ProductApiV1.LifecycleConflict)]
    public async Task Stale_version_and_non_active_lifecycle_stop_before_definition_read(
        string fault, string field, string code)
    {
        var source = new DefinitionSource(Definition());
        var world = World(fault == "inactive" ? TechnicalRecord() with { Lifecycle = 2 } : TechnicalRecord());
        var selection = fault == "stale" ? Selection with { ExpectedProductVersion = "1" } : Selection;

        var result = await Service(world, source).ReadAsync(selection, default);

        Assert.Null(result.Snapshot);
        Assert.Contains(result.Errors, error => (error.Field, error.Code) == (field, code));
        Assert.Equal(0, source.Reads);
    }

    [Fact]
    public async Task An_inactive_or_non_kilogram_Settings_profile_refuses_unitKg()
    {
        var world = World();
        world.Settings.Shape = (_, profile) => profile with { IsActive = false, IsKilogram = false };

        var result = await Service(world).ReadAsync(Selection, default);

        Assert.Null(result.Snapshot);
        Assert.Equal([new ProductFieldError("references.units.stock", "invalid_reference")], result.Errors);
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("company")]
    [InlineData("actor")]
    public async Task A_scope_change_during_definition_read_hides_the_result(string fault)
    {
        var world = World();
        var source = new DefinitionSource(Definition())
        {
            During = () =>
            {
                if (fault == "permission") world.Authorization.Granted = false;
                else world.Change(fault);
            },
        };

        var error = await Assert.ThrowsAsync<ProductOperationException>(
            () => Service(world, source).ReadAsync(Selection, default));

        Assert.Equal((403, ProductSalesEligibilityService.AccessDeniedCode), (error.StatusCode, error.Code));
        Assert.Equal(0, world.Settings.Calls);
    }

    [Fact]
    public async Task Disabled_runtime_resolves_only_the_fail_closed_port_and_never_the_concrete_service()
    {
        var services = new ServiceCollection();
        services.AddProductManufacturingTechnicalSnapshot(_ => false);
        await using var provider = services.BuildServiceProvider();

        var port = provider.GetRequiredService<IProductManufacturingTechnicalSnapshotV1>();
        var error = await Assert.ThrowsAsync<ProductOperationException>(() => port.ReadAsync(Selection, default));

        Assert.Equal((503, "product_runtime_unconfigured"), (error.StatusCode, error.Code));
        Assert.Null(provider.GetService<ProductManufacturingTechnicalSnapshotService>());
        Assert.Single(provider.GetServices<IProductManufacturingTechnicalSnapshotV1>());
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Missing_owner_prerequisite_resolves_the_fail_closed_port(
        bool settingsRegistered, bool definitionRegistered)
    {
        var services = new ServiceCollection();
        if (settingsRegistered)
        {
            var world = World();
            services.AddSingleton<ISettingsPrerequisiteProvider>(world.Settings);
        }
        if (definitionRegistered)
            services.AddSingleton<IProductFamilyDefinitionSourceV2>(new DefinitionSource(Definition()));
        services.AddProductManufacturingTechnicalSnapshot(_ => true);
        await using var provider = services.BuildServiceProvider();

        var port = provider.GetRequiredService<IProductManufacturingTechnicalSnapshotV1>();
        var error = await Assert.ThrowsAsync<ProductOperationException>(() => port.ReadAsync(Selection, default));

        Assert.Equal((503, "product_runtime_unconfigured"), (error.StatusCode, error.Code));
        Assert.Null(provider.GetService<ProductManufacturingTechnicalSnapshotService>());
    }

    [Fact]
    public void Enabled_runtime_resolves_only_the_gated_interface_to_the_real_service()
    {
        var world = World();
        var services = new ServiceCollection();
        services.AddSingleton<ICompanyContext>(world.Company);
        services.AddSingleton<ICurrentUser>(world.User);
        services.AddSingleton<IErpAuthorityRevalidationService>(world.Authorization);
        services.AddSingleton<ISettingsPrerequisiteProvider>(world.Settings);
        services.AddSingleton<IProductFamilyDefinitionSourceV2>(new DefinitionSource(Definition()));
        services.AddSingleton<IProductManufacturingTechnicalRecordReader>(new TechnicalRecords(world.Records));
        services.AddScoped<ProductUnitProfileAdapter>();
        services.AddProductManufacturingTechnicalSnapshot(_ => true);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.IsType<ProductManufacturingTechnicalSnapshotService>(
            scope.ServiceProvider.GetRequiredService<IProductManufacturingTechnicalSnapshotV1>());
        Assert.Null(scope.ServiceProvider.GetService<ProductManufacturingTechnicalSnapshotService>());
        Assert.Single(scope.ServiceProvider.GetServices<IProductManufacturingTechnicalSnapshotV1>());
    }

    private static ProductSalesEligibilityServiceTests.World World(ProductStoredRecord? record = null) =>
        new() { Record = record ?? TechnicalRecord() };

    private static ProductManufacturingTechnicalSnapshotService Service(
        ProductSalesEligibilityServiceTests.World world,
        DefinitionSource? source = null) =>
        new(world.Company, world.User, world.Authorization, new TechnicalRecords(world.Records),
            source ?? new(Definition()), new(world.Settings, world.Company));

    private static ProductStoredRecord TechnicalRecord(bool includeThickness = true)
    {
        var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["widthMm"] = JsonSerializer.SerializeToElement(1250m),
            ["quality"] = JsonSerializer.SerializeToElement("DX51D"),
            ["coating"] = JsonSerializer.SerializeToElement("none"),
        };
        if (includeThickness) attributes["thicknessMm"] = JsonSerializer.SerializeToElement(1.25m);
        var references = new ProductReferences(TemplateId, "7", attributes, new(Stock: StockUnitId));
        return StoredProduct(units: references.Units) with
        {
            FamilyId = FamilyId,
            ReferencesJson = JsonSerializer.Serialize(references, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        };
    }

    private static ProductFamilyDefinitionV2 Definition(bool candidateQuality = false,
        string sourceRevision = "#66/5739827887", string? entrySourceRevision = null) => new(
        [new(FamilyId, "gdc-coil", "GDC Coil", "coil", "rolling", "band",
            ProductFamilyRuleSetsV2.GdcBandCoil, sourceRevision)],
        [new(TemplateId, "7", "gdc-coil-v7", "GDC Coil v7", new HashSet<Guid> { FamilyId },
            [new("thicknessMm", ProductTemplateFieldKindV2.MeasureMillimetre,
                    ProductTemplateFieldRolesV2.Thickness, true, null, false, null, null, null),
                new("widthMm", ProductTemplateFieldKindV2.MeasureMillimetre,
                    ProductTemplateFieldRolesV2.Width, true, null, false, null, null, null),
                new("quality", ProductTemplateFieldKindV2.DictionaryReference,
                    ProductTemplateFieldRolesV2.Quality, true, "qualities", false, null, null, null),
                new("coating", ProductTemplateFieldKindV2.DictionaryReference,
                    ProductTemplateFieldRolesV2.Coating, true, "coatings", false, null, null, null)],
            sourceRevision)],
        [new("qualities", "DX51D", "DX51D", candidateQuality
                ? ProductDefinitionEntryStatusV2.Candidate : ProductDefinitionEntryStatusV2.Approved,
                entrySourceRevision ?? sourceRevision, null, null, false),
            new("qualities", "DX52D", "DX52D", ProductDefinitionEntryStatusV2.Approved,
                entrySourceRevision ?? sourceRevision, null, null, false),
            new("coatings", "none", "Uncoated", ProductDefinitionEntryStatusV2.Approved,
                entrySourceRevision ?? sourceRevision, null, null, true)]);

    private sealed class DefinitionSource(ProductFamilyDefinitionV2 definition) : IProductFamilyDefinitionSourceV2
    {
        public int Reads { get; private set; }
        public Action? During { get; init; }

        public Task<ProductFamilyDefinitionV2> GetAsync(CancellationToken cancellationToken)
        {
            Reads++;
            During?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(definition);
        }
    }

    private sealed class TechnicalRecords(ProductSalesEligibilityServiceTests.Records inner)
        : IProductManufacturingTechnicalRecordReader
    {
        public Task<ProductStoredRecord?> ReadForManufacturingTechnicalSnapshotAsync(
            ProductManufacturingTechnicalReadScope scope, Guid productId, CancellationToken cancellationToken) =>
            inner.ReadForSalesSelectionAsync(new(scope.CompanyId, scope.ActorId), productId, cancellationToken);
    }
}

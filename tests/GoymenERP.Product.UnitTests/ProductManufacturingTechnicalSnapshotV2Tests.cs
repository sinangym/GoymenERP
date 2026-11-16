using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using static GoymenERP.Product.UnitTests.ProductSalesEligibilityServiceTests;

namespace GoymenERP.Product.UnitTests;

/// <summary>
/// B-09: the V2 technical snapshot composes the unchanged V1 port and the "Ebat/kalınlık" read port. These facts pin the
/// recipe–product binding, the explicit dimension source, the kg/m source rank (DEC-PRD-009) and the fail-closed channels.
/// </summary>
public sealed class ProductManufacturingTechnicalSnapshotV2Tests
{
    private const string StoredVersion = ProductSalesEligibilityServiceTests.Version;
    private static readonly Guid FamilyId = Guid.Parse("c6100000-0000-4000-8000-000000000301");
    private static readonly Guid TemplateId = Guid.Parse("c6200000-0000-4000-8000-000000000301");
    private static readonly Guid ProfileId = Guid.Parse("c6300000-0000-4000-8000-000000000301");
    private static readonly ProductManufacturingTechnicalSnapshotSelectionV2 Selection = new(ProductId, StoredVersion, On);

    [Fact]
    public async Task Profile_row_publishes_the_V1_snapshot_the_binding_and_the_product_master_kg_per_metre()
    {
        var world = new World { Row = ProfileRow(version: 3) };

        var result = await world.Service().ReadAsync(Selection, default);

        Assert.Empty(result.Errors);
        var snapshot = Assert.IsType<ProductManufacturingTechnicalSnapshotV2>(result.Snapshot);
        Assert.Same(world.Technical.Answer.Snapshot, snapshot.Technical);
        Assert.Equal(new ProductManufacturingRecipeBindingV2(ProductId, StoredVersion, TemplateId, "7", "3"),
            snapshot.RecipeBinding);
        Assert.Equal(new ProductManufacturingDimensionV2(ProductManufacturingDimensionSourceV2.Stored, "3",
                ProductDimensionSectionKindsV1.Profile, null, 40m, null, 20m, null, 2m),
            snapshot.Dimension);
        Assert.Equal(new ProductManufacturingUnitWeightV2(ProductManufacturingUnitWeightSourceV2.ProductMaster, 1.75m),
            snapshot.UnitWeight);
        Assert.Equal(new[] { (ProductId, StoredVersion, On) }, world.Technical.Requests);
        Assert.Equal(new[] { ProductId }, world.Dimensions.Requests);
        Assert.Equal(3, world.Authorization.Calls);
        Assert.All(world.Authorization.AskedPermissions,
            permission => Assert.Equal(ProductPermissionManifestV1.OperationalSelectionRead, permission));
        Assert.All(world.Authorization.AskedFor, company => Assert.Equal(CompanyId, company));
    }

    [Fact]
    public async Task Sheet_row_keeps_its_exact_measures_and_publishes_no_unit_weight()
    {
        var world = new World { Row = SheetRow(version: 1) };

        var result = await world.Service().ReadAsync(Selection, default);

        var snapshot = Assert.IsType<ProductManufacturingTechnicalSnapshotV2>(result.Snapshot);
        Assert.Equal(new ProductManufacturingDimensionV2(ProductManufacturingDimensionSourceV2.Stored, "1",
                ProductDimensionSectionKindsV1.Sheet, 1.25m, 1250m, 2500.125m, null, null, null),
            snapshot.Dimension);
        Assert.Equal(new ProductManufacturingUnitWeightV2(ProductManufacturingUnitWeightSourceV2.None, null),
            snapshot.UnitWeight);
        Assert.Equal("1", snapshot.RecipeBinding.DimensionVersion);
    }

    [Fact]
    public async Task Round_tube_without_table_weight_publishes_no_unit_weight_and_never_an_estimate()
    {
        var world = new World { Row = Found(ProductDimensionSectionKindsV1.RoundTube, outerDiameter: 33.7m, wall: 2m) };

        var result = await world.Service().ReadAsync(Selection, default);

        var snapshot = Assert.IsType<ProductManufacturingTechnicalSnapshotV2>(result.Snapshot);
        Assert.Equal(ProductManufacturingUnitWeightSourceV2.None, snapshot.UnitWeight.Source);
        Assert.Null(snapshot.UnitWeight.KgPerMetre);
        Assert.Equal((33.7m, 2m), (snapshot.Dimension.OuterDiameterMm!.Value, snapshot.Dimension.WallThicknessMm!.Value));
    }

    [Fact]
    public async Task Structural_and_painted_rows_publish_their_KLN_D87_fields_exactly_and_no_estimated_unit_weight()
    {
        var structural = new World
        {
            Row = Found(ProductDimensionSectionKindsV1.StructuralProfile, length: 12000m) with
            {
                ProfileSeries = ProductProfileSeriesV1.Ipe, ProfileSize = "200",
            },
        };
        var structuralSnapshot = Assert.IsType<ProductManufacturingTechnicalSnapshotV2>(
            (await structural.Service().ReadAsync(Selection, default)).Snapshot);
        Assert.Equal(new ProductManufacturingDimensionV2(ProductManufacturingDimensionSourceV2.Stored, "1",
                ProductDimensionSectionKindsV1.StructuralProfile, null, null, 12000m, null, null, null, "IPE", "200"),
            structuralSnapshot.Dimension);
        Assert.Equal(new ProductManufacturingUnitWeightV2(ProductManufacturingUnitWeightSourceV2.None, null),
            structuralSnapshot.UnitWeight);

        var painted = new World
        {
            Row = Found(ProductDimensionSectionKindsV1.PaintedErwTube, outerDiameter: 48.3m, wall: 2.6m) with
            {
                SurfaceFinish = "RAL 7016",
            },
        };
        var paintedSnapshot = Assert.IsType<ProductManufacturingTechnicalSnapshotV2>(
            (await painted.Service().ReadAsync(Selection, default)).Snapshot);
        Assert.Equal(("RAL 7016", (string?)null, (string?)null, (decimal?)null), (paintedSnapshot.Dimension.SurfaceFinish,
            paintedSnapshot.Dimension.ProfileSeries, paintedSnapshot.Dimension.ProfileSize, paintedSnapshot.Dimension.MeshSpacingMm));

        var mesh = new World
        {
            Row = Found(ProductDimensionSectionKindsV1.WeldedMesh, width: 2150m, length: 5000m, outerDiameter: 5m) with
            {
                MeshSpacingMm = 150m,
            },
        };
        var meshSnapshot = Assert.IsType<ProductManufacturingTechnicalSnapshotV2>(
            (await mesh.Service().ReadAsync(Selection, default)).Snapshot);
        Assert.Equal(150m, meshSnapshot.Dimension.MeshSpacingMm);
        Assert.Null(meshSnapshot.UnitWeight.KgPerMetre);
    }

    [Theory]
    [InlineData("structural-without-size")]
    [InlineData("painted-without-surface")]
    [InlineData("tube-with-series")]
    [InlineData("mesh-wire-too-thick")]
    public async Task Stored_KLN_D87_row_that_breaks_its_rule_is_an_integrity_fault(string fault)
    {
        var row = fault switch
        {
            "structural-without-size" => Found(ProductDimensionSectionKindsV1.StructuralProfile) with { ProfileSeries = "IPE" },
            "painted-without-surface" => Found(ProductDimensionSectionKindsV1.PaintedErwProfile, width: 40m, height: 20m, wall: 2m),
            "tube-with-series" => Found(ProductDimensionSectionKindsV1.ErwTube, outerDiameter: 48.3m, wall: 2.6m) with
            {
                ProfileSeries = "IPE", ProfileSize = "200",
            },
            _ => Found(ProductDimensionSectionKindsV1.WeldedMesh, width: 2150m, length: 5000m, outerDiameter: 150m) with
            {
                MeshSpacingMm = 150m,
            },
        };
        var world = new World { Row = row };

        await Assert.ThrowsAsync<InvalidOperationException>(() => world.Service().ReadAsync(Selection, default));
    }

    [Fact]
    public async Task Product_without_a_row_is_no_source_with_version_zero_and_never_a_zero_measure()
    {
        var world = new World { Row = ProductDimensionReadV1.Without(ProductDimensionOutcomeV1.NoSource) };

        var result = await world.Service().ReadAsync(Selection with { ExpectedDimensionVersion = "0" }, default);

        var snapshot = Assert.IsType<ProductManufacturingTechnicalSnapshotV2>(result.Snapshot);
        Assert.Equal(new ProductManufacturingDimensionV2(ProductManufacturingDimensionSourceV2.NoSource,
                ProductManufacturingTechnicalSnapshotServiceV2.NoDimensionVersion, null, null, null, null, null, null, null),
            snapshot.Dimension);
        Assert.Equal(new ProductManufacturingUnitWeightV2(ProductManufacturingUnitWeightSourceV2.None, null),
            snapshot.UnitWeight);
        Assert.Equal("0", snapshot.RecipeBinding.DimensionVersion);
    }

    [Fact]
    public async Task Matching_expected_dimension_version_answers_the_snapshot()
    {
        var world = new World { Row = ProfileRow(version: 12) };

        var result = await world.Service().ReadAsync(Selection with { ExpectedDimensionVersion = "12" }, default);

        Assert.Empty(result.Errors);
        Assert.Equal("12", Assert.IsType<ProductManufacturingTechnicalSnapshotV2>(result.Snapshot).RecipeBinding.DimensionVersion);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("0")]
    public async Task Different_current_dimension_version_is_a_version_conflict_without_a_snapshot(string expected)
    {
        var world = new World { Row = ProfileRow(version: 3) };

        var result = await world.Service().ReadAsync(Selection with { ExpectedDimensionVersion = expected }, default);

        Assert.Null(result.Snapshot);
        Assert.Equal(new[] { new ProductFieldError(ProductManufacturingTechnicalSnapshotCodesV2.ExpectedDimensionVersionField,
            ProductApiV1.VersionConflict) }, result.Errors);
    }

    [Fact]
    public async Task Bound_row_that_was_removed_is_a_version_conflict()
    {
        var world = new World { Row = ProductDimensionReadV1.Without(ProductDimensionOutcomeV1.NoSource) };

        var result = await world.Service().ReadAsync(Selection with { ExpectedDimensionVersion = "4" }, default);

        Assert.Null(result.Snapshot);
        Assert.Equal(new[] { new ProductFieldError(ProductManufacturingTechnicalSnapshotCodesV2.ExpectedDimensionVersionField,
            ProductApiV1.VersionConflict) }, result.Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("01")]
    [InlineData("-1")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("+1")]
    [InlineData("abc")]
    [InlineData("1.0")]
    [InlineData("99999999999999999999")]
    public async Task Expected_dimension_version_that_is_not_a_version_is_refused_without_reading_the_row(string expected)
    {
        var world = new World { Row = ProfileRow(version: 1) };

        var result = await world.Service().ReadAsync(Selection with { ExpectedDimensionVersion = expected }, default);

        Assert.Null(result.Snapshot);
        Assert.Equal(new[] { new ProductFieldError(ProductManufacturingTechnicalSnapshotCodesV2.ExpectedDimensionVersionField,
            ProductManufacturingTechnicalSnapshotCodesV2.InvalidVersion) }, result.Errors);
        Assert.Empty(world.Dimensions.Requests);
    }

    [Fact]
    public async Task V1_field_errors_pass_through_with_the_binding_error_and_the_row_is_not_read()
    {
        var world = new World { Row = ProfileRow(version: 1) };
        var v1Error = new ProductFieldError("expectedProductVersion", ProductApiV1.VersionConflict);
        world.Technical.Answer = new(null, [v1Error]);

        var result = await world.Service().ReadAsync(Selection with { ExpectedDimensionVersion = "x" }, default);

        Assert.Null(result.Snapshot);
        Assert.Equal(new[] { v1Error, new ProductFieldError(ProductManufacturingTechnicalSnapshotCodesV2.ExpectedDimensionVersionField,
            ProductManufacturingTechnicalSnapshotCodesV2.InvalidVersion) }, result.Errors);
        Assert.Empty(world.Dimensions.Requests);
        Assert.Equal(2, world.Authorization.Calls);
    }

    [Fact]
    public async Task V1_answer_without_snapshot_or_error_is_an_integrity_fault()
    {
        var world = new World { Row = ProfileRow(version: 1) };
        world.Technical.Answer = new(null, []);

        await Assert.ThrowsAsync<InvalidOperationException>(() => world.Service().ReadAsync(Selection, default));
        Assert.Empty(world.Dimensions.Requests);
    }

    [Fact]
    public async Task V1_failure_propagates_after_the_scope_is_measured_again()
    {
        var world = new World { Row = ProfileRow(version: 1) };
        world.Technical.Failure = new ProductOperationException(ProductManufacturingTechnicalSnapshotService.UnavailableCode, 503);

        var error = await Assert.ThrowsAsync<ProductOperationException>(() => world.Service().ReadAsync(Selection, default));

        Assert.Equal((503, ProductManufacturingTechnicalSnapshotService.UnavailableCode), (error.StatusCode, error.Code));
        Assert.Equal(2, world.Authorization.Calls);
        Assert.Empty(world.Dimensions.Requests);
    }

    [Fact]
    public async Task Denied_dimension_read_is_the_access_denied_failure()
    {
        var world = new World { Row = ProductDimensionReadV1.Without(ProductDimensionOutcomeV1.Denied) };

        var error = await Assert.ThrowsAsync<ProductOperationException>(() => world.Service().ReadAsync(Selection, default));

        Assert.Equal((403, ProductSalesEligibilityService.AccessDeniedCode), (error.StatusCode, error.Code));
    }

    [Fact]
    public async Task Unavailable_dimension_read_is_the_dimension_provider_503()
    {
        var world = new World { Row = ProductDimensionReadV1.Without(ProductDimensionOutcomeV1.Unavailable) };

        var error = await Assert.ThrowsAsync<ProductOperationException>(() => world.Service().ReadAsync(Selection, default));

        Assert.Equal((503, ProductDimensionProblemCodesV1.ProviderUnavailable), (error.StatusCode, error.Code));
    }

    [Fact]
    public async Task Card_the_dimension_store_no_longer_sees_is_an_invalid_reference()
    {
        var world = new World { Row = ProductDimensionReadV1.Without(ProductDimensionOutcomeV1.NotFound) };

        var result = await world.Service().ReadAsync(Selection, default);

        Assert.Null(result.Snapshot);
        Assert.Equal(new[] { new ProductFieldError("productId", "invalid_reference") }, result.Errors);
    }

    [Theory]
    [InlineData("profile-without-kg")]
    [InlineData("sheet-with-kg")]
    [InlineData("missing-version")]
    [InlineData("zero-version")]
    [InlineData("unknown-kind")]
    [InlineData("unknown-outcome")]
    public async Task Stored_row_that_breaks_its_section_kind_rule_is_an_integrity_fault(string fault)
    {
        var row = fault switch
        {
            "profile-without-kg" => ProfileRow(version: 1) with { KgPerMetre = null },
            "sheet-with-kg" => SheetRow(version: 1) with { KgPerMetre = 1m },
            "missing-version" => ProfileRow(version: 1) with { Version = null },
            "zero-version" => ProfileRow(version: 1) with { Version = 0 },
            "unknown-kind" => ProfileRow(version: 1) with { SectionKind = "Angle" },
            _ => ProfileRow(version: 1) with { Outcome = (ProductDimensionOutcomeV1)99 },
        };
        var world = new World { Row = row };

        await Assert.ThrowsAsync<InvalidOperationException>(() => world.Service().ReadAsync(Selection, default));
    }

    [Fact]
    public async Task Missing_permission_refuses_before_any_read()
    {
        var world = new World { Row = ProfileRow(version: 1) };
        world.Authorization.Granted = false;

        var error = await Assert.ThrowsAsync<ProductOperationException>(() => world.Service().ReadAsync(Selection, default));

        Assert.Equal((403, ProductSalesEligibilityService.AccessDeniedCode), (error.StatusCode, error.Code));
        Assert.Empty(world.Technical.Requests);
        Assert.Empty(world.Dimensions.Requests);
    }

    [Fact]
    public async Task Unresolved_company_refuses_before_any_read_or_permission_question()
    {
        var world = new World { Row = ProfileRow(version: 1) };
        world.Company.IsResolved = false;

        var error = await Assert.ThrowsAsync<ProductOperationException>(() => world.Service().ReadAsync(Selection, default));

        Assert.Equal((403, ProductSalesEligibilityService.AccessDeniedCode), (error.StatusCode, error.Code));
        Assert.Equal(0, world.Authorization.Calls);
        Assert.Empty(world.Technical.Requests);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("actor")]
    [InlineData("permission")]
    public async Task Scope_change_during_the_dimension_read_hides_the_answer(string change)
    {
        var world = new World { Row = ProfileRow(version: 1) };
        var otherCompany = Guid.Parse("10000000-0000-0000-0000-000000000002");
        world.Dimensions.During = () =>
        {
            switch (change)
            {
                case "company":
                    world.User.Allowed.Add(otherCompany);
                    world.Company.Set(otherCompany);
                    break;
                case "actor":
                    world.User.UserId = "another-actor";
                    break;
                default:
                    world.Authorization.Granted = false;
                    break;
            }
        };

        var error = await Assert.ThrowsAsync<ProductOperationException>(() => world.Service().ReadAsync(Selection, default));

        Assert.Equal((403, ProductSalesEligibilityService.AccessDeniedCode), (error.StatusCode, error.Code));
    }

    [Fact]
    public async Task Scope_change_during_the_V1_read_hides_the_answer_and_skips_the_row()
    {
        var world = new World { Row = ProfileRow(version: 1) };
        world.Technical.During = () => world.User.UserId = "another-actor";

        var error = await Assert.ThrowsAsync<ProductOperationException>(() => world.Service().ReadAsync(Selection, default));

        Assert.Equal((403, ProductSalesEligibilityService.AccessDeniedCode), (error.StatusCode, error.Code));
        Assert.Empty(world.Dimensions.Requests);
    }

    [Fact]
    public async Task Cancelled_read_stops_before_any_question()
    {
        var world = new World { Row = ProfileRow(version: 1) };
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => world.Service().ReadAsync(Selection, cancellation.Token));
        Assert.Equal(0, world.Authorization.Calls);
    }

    [Fact]
    public async Task Profile_whose_table_holds_both_directions_with_different_kg_per_metre_is_a_table_conflict_without_a_snapshot()
    {
        // URN-D19: the read port names the conflict; V2 answers the explicit field error and never one of the two values.
        var world = new World
        {
            Row = Found(ProductDimensionSectionKindsV1.Profile, width: 40m, height: 60m, wall: 3m, version: 2) with
            {
                KgPerMetreSourceLabel = "SENTETIK KATALOG",
                KgPerMetreTableVersion = 4,
                KgPerMetreConflict = true,
            },
        };

        var result = await world.Service().ReadAsync(Selection, default);

        Assert.Null(result.Snapshot);
        Assert.Equal(new ProductFieldError(ProductManufacturingTechnicalSnapshotCodesV2.UnitWeightField,
            ProductManufacturingTechnicalSnapshotCodesV2.UnitWeightTableConflict), Assert.Single(result.Errors));
    }

    [Fact]
    public void Conflicting_lookup_that_still_carries_a_kg_per_metre_is_an_integrity_fault()
    {
        var row = ProfileRow(version: 1) with { KgPerMetreConflict = true };

        Assert.Throws<InvalidOperationException>(() => ProductManufacturingTechnicalSnapshotServiceV2.Interpret(row));
    }

    [Fact]
    public void Verified_kg_per_metre_of_the_fason_check_is_only_the_company_table_value()
    {
        // URN-D20: the table value (answered as ProductMaster) is the single verified kg/m; anything else fails closed.
        Assert.True(ProductVerifiedUnitWeightV2.TryRequire(
            new ProductManufacturingUnitWeightV2(ProductManufacturingUnitWeightSourceV2.ProductMaster, 1.75m),
            out var kgPerMetre, out var accepted));
        Assert.Equal((1.75m, (string?)null), (kgPerMetre, accepted));

        foreach (var unitWeight in new[]
                 {
                     new ProductManufacturingUnitWeightV2(ProductManufacturingUnitWeightSourceV2.None, null),
                     new ProductManufacturingUnitWeightV2(ProductManufacturingUnitWeightSourceV2.ManualVerified, 1.75m),
                     new ProductManufacturingUnitWeightV2(ProductManufacturingUnitWeightSourceV2.GeometricEstimate, 1.75m),
                     new ProductManufacturingUnitWeightV2(ProductManufacturingUnitWeightSourceV2.ProductMaster, null),
                     new ProductManufacturingUnitWeightV2(ProductManufacturingUnitWeightSourceV2.ProductMaster, 0m),
                 })
        {
            Assert.False(ProductVerifiedUnitWeightV2.TryRequire(unitWeight, out var none, out var refusal));
            Assert.Equal((0m, ProductManufacturingTechnicalSnapshotCodesV2.VerifiedUnitWeightMissing), (none, refusal));
        }
    }

    [Fact]
    public void Unit_weight_priority_is_manual_verified_then_product_master_then_geometric_estimate()
    {
        Assert.Equal(new[]
        {
            ProductManufacturingUnitWeightSourceV2.ManualVerified,
            ProductManufacturingUnitWeightSourceV2.ProductMaster,
            ProductManufacturingUnitWeightSourceV2.GeometricEstimate,
        }, ProductManufacturingUnitWeightPriorityV2.Order);
        Assert.DoesNotContain(ProductManufacturingUnitWeightSourceV2.None, ProductManufacturingUnitWeightPriorityV2.Order);
    }

    [Theory]
    [InlineData(ProductManufacturingUnitWeightSourceV2.ManualVerified, ProductManufacturingUnitWeightSourceV2.ProductMaster, true)]
    [InlineData(ProductManufacturingUnitWeightSourceV2.ProductMaster, ProductManufacturingUnitWeightSourceV2.GeometricEstimate, true)]
    [InlineData(ProductManufacturingUnitWeightSourceV2.ManualVerified, ProductManufacturingUnitWeightSourceV2.GeometricEstimate, true)]
    [InlineData(ProductManufacturingUnitWeightSourceV2.GeometricEstimate, ProductManufacturingUnitWeightSourceV2.ProductMaster, false)]
    [InlineData(ProductManufacturingUnitWeightSourceV2.ProductMaster, ProductManufacturingUnitWeightSourceV2.ManualVerified, false)]
    [InlineData(ProductManufacturingUnitWeightSourceV2.ProductMaster, ProductManufacturingUnitWeightSourceV2.ProductMaster, false)]
    [InlineData(ProductManufacturingUnitWeightSourceV2.GeometricEstimate, ProductManufacturingUnitWeightSourceV2.None, true)]
    [InlineData(ProductManufacturingUnitWeightSourceV2.None, ProductManufacturingUnitWeightSourceV2.GeometricEstimate, false)]
    [InlineData(ProductManufacturingUnitWeightSourceV2.None, ProductManufacturingUnitWeightSourceV2.None, false)]
    public void Unit_weight_rank_follows_the_locked_priority_and_none_never_outranks(
        ProductManufacturingUnitWeightSourceV2 candidate, ProductManufacturingUnitWeightSourceV2 current, bool outranks)
    {
        Assert.Equal(outranks, ProductManufacturingUnitWeightPriorityV2.Outranks(candidate, current));
    }

    [Fact]
    public async Task Disabled_runtime_resolves_the_fail_closed_V2_port()
    {
        var services = new ServiceCollection();
        services.AddProductManufacturingTechnicalSnapshot(_ => false);
        await using var provider = services.BuildServiceProvider();

        var port = provider.GetRequiredService<IProductManufacturingTechnicalSnapshotV2>();
        var error = await Assert.ThrowsAsync<ProductOperationException>(() => port.ReadAsync(Selection, default));

        Assert.Equal((503, "product_runtime_unconfigured"), (error.StatusCode, error.Code));
        Assert.Null(provider.GetService<ProductManufacturingTechnicalSnapshotServiceV2>());
        Assert.Single(provider.GetServices<IProductManufacturingTechnicalSnapshotV2>());
    }

    [Fact]
    public async Task Enabled_runtime_without_the_dimension_read_port_resolves_the_fail_closed_V2_port()
    {
        var world = new World();
        var services = Prerequisites(world);
        services.AddProductManufacturingTechnicalSnapshot(_ => true);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var port = scope.ServiceProvider.GetRequiredService<IProductManufacturingTechnicalSnapshotV2>();
        var error = await Assert.ThrowsAsync<ProductOperationException>(() => port.ReadAsync(Selection, default));

        Assert.Equal((503, "product_runtime_unconfigured"), (error.StatusCode, error.Code));
        Assert.IsType<ProductManufacturingTechnicalSnapshotService>(
            scope.ServiceProvider.GetRequiredService<IProductManufacturingTechnicalSnapshotV1>());
    }

    [Fact]
    public void Enabled_runtime_resolves_V2_beside_the_unchanged_V1_port()
    {
        var world = new World();
        var services = Prerequisites(world);
        services.AddSingleton<IProductDimensionReaderV1>(world.Dimensions);
        services.AddProductManufacturingTechnicalSnapshot(_ => true);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.IsType<ProductManufacturingTechnicalSnapshotServiceV2>(
            scope.ServiceProvider.GetRequiredService<IProductManufacturingTechnicalSnapshotV2>());
        Assert.IsType<ProductManufacturingTechnicalSnapshotService>(
            scope.ServiceProvider.GetRequiredService<IProductManufacturingTechnicalSnapshotV1>());
        Assert.Null(scope.ServiceProvider.GetService<ProductManufacturingTechnicalSnapshotServiceV2>());
        Assert.Single(scope.ServiceProvider.GetServices<IProductManufacturingTechnicalSnapshotV2>());
    }

    private static ServiceCollection Prerequisites(World world)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICompanyContext>(world.Company);
        services.AddSingleton<ICurrentUser>(world.User);
        services.AddSingleton<IErpAuthorityRevalidationService>(world.Authorization);
        services.AddSingleton<ISettingsPrerequisiteProvider>(new Settings(world.Company));
        services.AddSingleton<IProductFamilyDefinitionSourceV2>(new UnavailableProductFamilyDefinitionSourceV2());
        services.AddSingleton<IProductManufacturingTechnicalRecordReader>(new NoTechnicalRecords());
        services.AddScoped<ProductUnitProfileAdapter>();
        return services;
    }

    private static ProductDimensionReadV1 ProfileRow(long version) =>
        Found(ProductDimensionSectionKindsV1.Profile, width: 40m, height: 20m, wall: 2m, kgPerMetre: 1.75m, version: version);

    private static ProductDimensionReadV1 SheetRow(long version) =>
        Found(ProductDimensionSectionKindsV1.Sheet, thickness: 1.25m, width: 1250m, length: 2500.125m, version: version);

    private static ProductDimensionReadV1 Found(string sectionKind, decimal? thickness = null, decimal? width = null,
        decimal? length = null, decimal? height = null, decimal? outerDiameter = null, decimal? wall = null,
        decimal? kgPerMetre = null, long version = 1) =>
        new(ProductDimensionOutcomeV1.Found, sectionKind, thickness, width, length, height, outerDiameter, wall, kgPerMetre,
            version);

    private static ProductManufacturingTechnicalSnapshotV1 TechnicalSnapshot() => new(
        ProductId,
        StoredVersion,
        new ProductManufacturingFamilyRevisionV1(FamilyId, "synthetic-profile", "synthetic-revision"),
        new ProductManufacturingTemplateRevisionV1(TemplateId, "7", "synthetic-profile-v7", "synthetic-revision"),
        2m,
        40m,
        new ProductManufacturingKilogramUnitV1(CompanyId, StockUnitId, "synthetic-kg", ProfileId, StoredVersion),
        On);

    private sealed class World
    {
        public Context Company { get; } = new();
        public User User { get; } = new();
        public Authorization Authorization { get; } = new();
        public TechnicalPort Technical { get; } = new();
        public DimensionReader Dimensions { get; } = new();

        public ProductDimensionReadV1 Row
        {
            get => Dimensions.Answer;
            init => Dimensions.Answer = value;
        }

        public ProductManufacturingTechnicalSnapshotServiceV2 Service() =>
            new(Company, User, Authorization, Technical, Dimensions);
    }

    private sealed class TechnicalPort : IProductManufacturingTechnicalSnapshotV1
    {
        public ProductManufacturingTechnicalSnapshotResultV1 Answer { get; set; } = new(TechnicalSnapshot(), []);
        public Exception? Failure { get; set; }
        public Action? During { get; set; }
        public List<(Guid, string, DateOnly)> Requests { get; } = [];

        public Task<ProductManufacturingTechnicalSnapshotResultV1> ReadAsync(
            ProductManufacturingTechnicalSnapshotSelectionV1 selection, CancellationToken cancellationToken)
        {
            Requests.Add((selection.ProductId, selection.ExpectedProductVersion, selection.EffectiveOn));
            During?.Invoke();
            return Failure is not null ? Task.FromException<ProductManufacturingTechnicalSnapshotResultV1>(Failure)
                : Task.FromResult(Answer);
        }
    }

    private sealed class DimensionReader : IProductDimensionReaderV1
    {
        public ProductDimensionReadV1 Answer { get; set; } =
            ProductDimensionReadV1.Without(ProductDimensionOutcomeV1.NoSource);
        public Action? During { get; set; }
        public List<Guid> Requests { get; } = [];

        public Task<ProductDimensionReadV1> GetAsync(Guid productId, CancellationToken cancellationToken = default)
        {
            Requests.Add(productId);
            During?.Invoke();
            return Task.FromResult(Answer);
        }
    }

    private sealed class NoTechnicalRecords : IProductManufacturingTechnicalRecordReader
    {
        public Task<ProductStoredRecord?> ReadForManufacturingTechnicalSnapshotAsync(
            ProductManufacturingTechnicalReadScope scope, Guid productId, CancellationToken cancellationToken) =>
            Task.FromResult<ProductStoredRecord?>(null);
    }
}

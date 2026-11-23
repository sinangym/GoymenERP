using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.UnitConversion;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using static GoymenERP.Product.UnitTests.ProductSalesEligibilityServiceTests;

namespace GoymenERP.Product.UnitTests;

/// <summary>
/// AK-1 (SAT-D437, KLN-D119): the Product owner's theoretical kg/unit read over the real B-07 source composition and
/// synthetic owner replies. No SQL or production acceptance is claimed.
/// </summary>
public sealed partial class ProductPurchaseUnitConversionSourceTests
{
    [Fact]
    public async Task Theoretical_kg_per_piece_of_a_sheet_is_the_shared_calculator_figure_with_its_complete_source_evidence()
    {
        var world = new World();

        var answer = await Port(world).ReadAsync(ProductId, SalesUnitId, On);

        // 2 mm × 1000 mm × 2000 mm × 7.85 g/cm³ = 31.4 kg (ProductUnitConversionCalculatorV1.SheetKgPerPiece).
        Assert.Equal((ProductTheoreticalKilogramsPerUnitOutcomeV1.Found, 31.4m, (string?)null),
            (answer.Outcome, answer.KilogramsPerUnit, answer.Code));
        var evidence = Assert.IsType<ProductTheoreticalKilogramsPerUnitEvidenceV1>(answer.Evidence);
        Assert.Equal((CompanyId, ProductId, SalesUnitId, ProductPhysicalUnitKindV1.Piece),
            (evidence.CompanyId, evidence.ProductId, evidence.UnitId, evidence.UnitKind));
        Assert.Equal((ProductSalesEligibilityServiceTests.Version, ProductUnitConversionCalculatorV1.FormulaVersion, (long?)9L),
            (evidence.ProductVersion, evidence.FormulaVersion, evidence.DimensionVersion));
        Assert.Equal(((Guid?)world.Policy.Value.Profile.ProfileId, (long?)17L, (decimal?)7.85m),
            (evidence.PolicyId, evidence.PolicyVersion, evidence.DensityGramsPerCubicCentimetre));
        Assert.Equal((world.Profiles.ProfileIdFor(SalesUnitId), 9007199254740993L, "synthetic-v1"),
            (evidence.UnitProfileId, evidence.UnitProfileVersion, evidence.UnitSemanticsVersion));
        Assert.Equal(1, answer.ContractVersion);
    }

    [Fact]
    public async Task Fixed_mass_units_answer_their_semantic_scale_without_a_formula()
    {
        var world = new World();

        var kilogram = await Port(world).ReadAsync(ProductId, StockUnitId, On);
        var tonne = await Port(world).ReadAsync(ProductId, Tonne, On);

        Assert.Equal((ProductTheoreticalKilogramsPerUnitOutcomeV1.Found, 1m, ProductPhysicalUnitKindV1.Kilogram),
            (kilogram.Outcome, kilogram.KilogramsPerUnit, kilogram.Evidence!.UnitKind));
        Assert.Equal((ProductTheoreticalKilogramsPerUnitOutcomeV1.Found, 1000m, ProductPhysicalUnitKindV1.Tonne),
            (tonne.Outcome, tonne.KilogramsPerUnit, tonne.Evidence!.UnitKind));
    }

    [Fact]
    public async Task A_unit_that_is_not_on_the_card_is_no_source_and_never_computed_from_its_kind()
    {
        var answer = await Port(new World()).ReadAsync(ProductId, Guid.Parse("4b0e3c4f-7d21-4c3a-9a55-3c7d0a0e9b11"), On);

        AssertWithout(answer, ProductTheoreticalKilogramsPerUnitOutcomeV1.NoSource,
            ProductTheoreticalKilogramsPerUnitCodesV1.UnitNotOnCard);
    }

    [Theory]
    [InlineData("no-dimensions")]
    [InlineData("no-policy")]
    [InlineData("inactive-profile")]
    [InlineData("missing-profile")]
    [InlineData("unsupported-section")]
    public async Task A_card_without_a_kilogram_source_for_the_unit_is_no_source_fail_closed(string gap)
    {
        var world = new World();
        switch (gap)
        {
            case "no-dimensions":
                world.Dimension.Value = ProductDimensionReadV1.Without(ProductDimensionOutcomeV1.NoSource);
                break;
            case "no-policy":
                world.Policy.Failure = new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.UnitConversionPolicyNotFound);
                break;
            case "inactive-profile":
                world.Profiles.Shape = (unit, profile) => unit == SalesUnitId ? profile with { IsActive = false } : profile;
                break;
            case "missing-profile":
                world.Profiles.Failure = new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.UnitProfileNotFound);
                world.Profiles.DuringUnit = SalesUnitId;
                break;
            default:
                world.Dimension.Value = new(ProductDimensionOutcomeV1.Found, "EqualAngle", 2m, 40m, 6000m, 20m, null, 2m, null, 9);
                break;
        }

        var answer = await Port(world).ReadAsync(ProductId, SalesUnitId, On);

        AssertWithout(answer, ProductTheoreticalKilogramsPerUnitOutcomeV1.NoSource, ProductTheoreticalKilogramsPerUnitCodesV1.NoSource);
    }

    [Fact]
    public async Task A_weight_table_conflict_is_refused_and_never_resolved_by_picking_a_row()
    {
        var world = new World();
        world.Dimension.Value = world.Dimension.Value with { SectionKind = ProductDimensionSectionKindsV1.RoundTube,
            KgPerMetre = 2m, KgPerMetreTableVersion = 11, KgPerMetreSourceLabel = "SENTETIK", KgPerMetreConflict = true };

        var answer = await Port(world).ReadAsync(ProductId, SalesUnitId, On);

        AssertWithout(answer, ProductTheoreticalKilogramsPerUnitOutcomeV1.Refused,
            ProductTheoreticalKilogramsPerUnitCodesV1.WeightConflict);
    }

    [Fact]
    public async Task SAT_D477_a_two_direction_table_conflict_stays_refused_and_names_the_entered_direction_figure_as_selected()
    {
        var world = new World();
        // Card 40 × 60 × 2, 6 m; the table holds 40×60 = 3.5 kg/m and 60×40 = 3.6 kg/m (URN-D19 conflict).
        world.Dimension.Value = new(ProductDimensionOutcomeV1.Found, ProductDimensionSectionKindsV1.Profile, null, 40m, 6000m, 60m, null,
            2m, null, 9, "SENTETIK-tablo", 11, KgPerMetreConflict: true, ConflictEnteredKgPerMetre: 3.5m, ConflictReversedKgPerMetre: 3.6m);

        var answer = await Port(world).ReadAsync(ProductId, SalesUnitId, On);

        // The outcome other consumers read is unchanged: a refusal without a figure.
        AssertWithout(answer, ProductTheoreticalKilogramsPerUnitOutcomeV1.Refused, ProductTheoreticalKilogramsPerUnitCodesV1.WeightConflict);
        var conflict = Assert.IsType<ProductTheoreticalKilogramsPerUnitTableConflictV1>(answer.TableConflict);
        Assert.Equal((21m, 21.6m, 3.5m, 3.6m, "weight-table:entered-orientation"),
            (conflict.SelectedKilogramsPerUnit, conflict.OtherKilogramsPerUnit, conflict.SelectedKgPerMetre, conflict.OtherKgPerMetre,
                conflict.Selection));
        Assert.Equal((CompanyId, ProductId, SalesUnitId, ProductPhysicalUnitKindV1.Piece, (long?)11L, "SENTETIK-tablo"),
            (conflict.Evidence.CompanyId, conflict.Evidence.ProductId, conflict.Evidence.UnitId, conflict.Evidence.UnitKind,
                conflict.Evidence.WeightTableVersion, conflict.Evidence.WeightTableSource));
    }

    [Fact]
    public async Task SAT_D477_a_conflict_without_both_directions_stays_a_plain_refusal()
    {
        var world = new World();
        world.Dimension.Value = new(ProductDimensionOutcomeV1.Found, ProductDimensionSectionKindsV1.Profile, null, 40m, 6000m, 60m, null,
            2m, null, 9, "SENTETIK-tablo", 11, KgPerMetreConflict: true, ConflictEnteredKgPerMetre: 3.5m);

        var answer = await Port(world).ReadAsync(ProductId, SalesUnitId, On);

        AssertWithout(answer, ProductTheoreticalKilogramsPerUnitOutcomeV1.Refused, ProductTheoreticalKilogramsPerUnitCodesV1.WeightConflict);
        Assert.Null(answer.TableConflict);
    }

    [Fact]
    public async Task Owner_not_found_denied_and_unavailable_answers_pass_through_without_a_figure()
    {
        var notFound = new World();
        notFound.Record.Value = null!;
        var denied = new World();
        denied.User.Allowed.Clear();
        var unavailable = new World();
        unavailable.Semantics.Shape = value => value with { Status = SettingsPhysicalUnitStatusV1.Unavailable };

        AssertWithout(await Port(notFound).ReadAsync(ProductId, SalesUnitId, On), ProductTheoreticalKilogramsPerUnitOutcomeV1.NotFound, null);
        AssertWithout(await Port(denied).ReadAsync(ProductId, SalesUnitId, On), ProductTheoreticalKilogramsPerUnitOutcomeV1.Denied, null);
        AssertWithout(await Port(unavailable).ReadAsync(ProductId, SalesUnitId, On),
            ProductTheoreticalKilogramsPerUnitOutcomeV1.Unavailable, ProductTheoreticalKilogramsPerUnitCodesV1.Unavailable);
    }

    [Fact]
    public async Task An_unresolved_company_is_denied_before_the_owner_source_is_read()
    {
        var world = new World();
        world.Company.IsResolved = false;

        AssertWithout(await Port(world).ReadAsync(ProductId, SalesUnitId, On), ProductTheoreticalKilogramsPerUnitOutcomeV1.Denied, null);
        Assert.Equal(0, world.Record.Calls);
    }

    [Fact]
    public async Task An_owner_answer_for_another_product_or_company_is_unavailable_not_handed_over()
    {
        var world = new World();
        var real = await world.Service().ReadAsync(ProductId, On);
        var foreignProduct = new FixedSource(real with { Source = real.Source! with { ProductId = Guid.NewGuid() } });
        var foreignCompany = new FixedSource(real with { Source = real.Source! with { CompanyId = Guid.NewGuid() } });
        var duplicateUnit = new FixedSource(real with
        {
            Source = real.Source! with { Units = [.. real.Source.Units, real.Source.Units.Single(unit => unit.UnitId == SalesUnitId)] },
        });

        foreach (var source in new[] { foreignProduct, foreignCompany, duplicateUnit })
        {
            AssertWithout(await new ProductTheoreticalKilogramsPerUnitReadPort(source, world.Company).ReadAsync(ProductId, SalesUnitId, On),
                ProductTheoreticalKilogramsPerUnitOutcomeV1.Unavailable, ProductTheoreticalKilogramsPerUnitCodesV1.Unavailable);
        }
    }

    [Fact]
    public async Task An_empty_product_or_unit_is_a_caller_fault_before_any_read()
    {
        var world = new World();

        await Assert.ThrowsAsync<ArgumentException>(() => Port(world).ReadAsync(Guid.Empty, SalesUnitId, On));
        await Assert.ThrowsAsync<ArgumentException>(() => Port(world).ReadAsync(ProductId, Guid.Empty, On));
        Assert.Equal(0, world.Record.Calls);
    }

    [Fact]
    public async Task The_registration_serves_the_port_and_a_disabled_runtime_answers_unavailable()
    {
        var services = new ServiceCollection().AddProductPurchaseUnitConversion(_ => false);
        services.AddScoped<GoymenERP.BuildingBlocks.Multitenancy.ICompanyContext>(_ => new Context());
        var descriptor = Assert.Single(services, item => item.ServiceType == typeof(IProductTheoreticalKilogramsPerUnitReadPortV1));
        Assert.Equal((ServiceLifetime.Scoped, typeof(ProductTheoreticalKilogramsPerUnitReadPort)),
            (descriptor.Lifetime, descriptor.ImplementationType));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var answer = await scope.ServiceProvider.GetRequiredService<IProductTheoreticalKilogramsPerUnitReadPortV1>()
            .ReadAsync(ProductId, SalesUnitId, On);

        AssertWithout(answer, ProductTheoreticalKilogramsPerUnitOutcomeV1.Unavailable, ProductTheoreticalKilogramsPerUnitCodesV1.Unavailable);
    }

    [Fact]
    public void The_answer_keeps_its_invariants()
    {
        var evidence = new ProductTheoreticalKilogramsPerUnitEvidenceV1(CompanyId, ProductId, SalesUnitId,
            ProductPhysicalUnitKindV1.Piece, "1", ProductUnitConversionCalculatorV1.FormulaVersion, 1, null, null,
            Guid.Parse("8d4f7a51-62a9-4f1e-8f0a-3c55a4f0b2d1"), 1, 7.85m, Guid.Parse("2a0d9c7e-31c4-4f6b-9e1e-6b2f9d4c8a71"), 1, "v1");

        Assert.Throws<ArgumentOutOfRangeException>(() => ProductTheoreticalKilogramsPerUnitAnswerV1.Found(0m, evidence));
        Assert.Throws<ArgumentException>(() => ProductTheoreticalKilogramsPerUnitAnswerV1.Found(1m, evidence with { UnitProfileVersion = 0 }));
        Assert.Throws<ArgumentException>(() => ProductTheoreticalKilogramsPerUnitAnswerV1.Found(1m, evidence with { PolicyVersion = null }));
        Assert.Throws<ArgumentException>(() => ProductTheoreticalKilogramsPerUnitAnswerV1.Found(1m,
            evidence with { PolicyId = null, PolicyVersion = null }));
        Assert.Throws<ArgumentException>(() => ProductTheoreticalKilogramsPerUnitAnswerV1.Found(1m, evidence with { UnitSemanticsVersion = " " }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProductTheoreticalKilogramsPerUnitAnswerV1.Without(ProductTheoreticalKilogramsPerUnitOutcomeV1.Found));
        Assert.Throws<ArgumentException>(() =>
            ProductTheoreticalKilogramsPerUnitAnswerV1.Without(ProductTheoreticalKilogramsPerUnitOutcomeV1.NoSource));
        Assert.Throws<ArgumentException>(() =>
            ProductTheoreticalKilogramsPerUnitAnswerV1.Without(ProductTheoreticalKilogramsPerUnitOutcomeV1.Denied, "code"));
        Assert.Equal(1m, ProductTheoreticalKilogramsPerUnitAnswerV1.Found(1m, evidence).KilogramsPerUnit);
    }

    private static ProductTheoreticalKilogramsPerUnitReadPort Port(World world) => new(world.Service(), world.Company);

    private static void AssertWithout(ProductTheoreticalKilogramsPerUnitAnswerV1 answer,
        ProductTheoreticalKilogramsPerUnitOutcomeV1 outcome, string? code)
    {
        Assert.Equal((outcome, (decimal?)null, code), (answer.Outcome, answer.KilogramsPerUnit, answer.Code));
        Assert.Null(answer.Evidence);
    }

    private sealed class FixedSource(ProductPurchaseUnitConversionReadV1 read) : IProductPurchaseUnitConversionV1
    {
        public Task<ProductPurchaseUnitConversionReadV1> ReadAsync(Guid productId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => Task.FromResult(read);

        public Task<ProductPurchaseUnitConversionResultV1> ConvertAsync(ProductPurchaseUnitConversionRequestV1 request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ProductPurchaseWeighbridgeResultV1> CheckWeighbridgeAsync(ProductPurchaseWeighbridgeRequestV1 request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

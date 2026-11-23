using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.UnitConversion;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using static GoymenERP.Product.UnitTests.ProductSalesEligibilityServiceTests;

namespace GoymenERP.Product.UnitTests;

/// <summary>B-07 source composition over synthetic owner replies; no SQL or production acceptance is claimed.</summary>
public sealed partial class ProductPurchaseUnitConversionSourceTests
{
    private static readonly Guid Tonne = Guid.Parse("6765373c-58a2-437e-8352-50e8af3487b5");
    private static readonly Guid Metre = Guid.Parse("ced78a0f-ea9f-48d5-9e85-a5ddc2aa27e5");

    [Fact]
    public async Task Contract_V1_keeps_source_versions_default_price_and_explicit_decimal_remainder()
    {
        var answer = await new World().Service().ConvertAsync(new(ProductId, 100m, StockUnitId, SalesUnitId, On));
        var json = JsonSerializer.Serialize(answer, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("source").GetProperty("contractVersion").GetInt32());
        Assert.Equal(5.8m, document.RootElement.GetProperty("remainderKg").GetDecimal());
        Assert.Equal(ProductSalesEligibilityServiceTests.Version, document.RootElement.GetProperty("source").GetProperty("productVersion").GetString());
        Assert.Equal(SalesUnitId, document.RootElement.GetProperty("source").GetProperty("defaultPriceUnitId").GetGuid());
    }

    [Fact]
    public async Task Sheet_source_uses_real_card_references_density_and_exact_versioned_policy()
    {
        var world = new World();
        var answer = await world.Service().ConvertAsync(new(ProductId, 3m, SalesUnitId, null, On));
        Assert.Equal(ProductPurchaseUnitConversionOutcomeV1.Available, answer.Outcome);
        Assert.Equal(94.2m, answer.Quantity);
        var source = Assert.IsType<ProductPurchaseUnitConversionSourceV1>(answer.Source);
        Assert.Equal((CompanyId, ProductId, ProductSalesEligibilityServiceTests.Version, SalesUnitId, UnitConversionClassV1.FormulaBound),
            (source.CompanyId, source.ProductId, source.ProductVersion, source.DefaultPriceUnitId, source.ConversionClass));
        Assert.Equal(31.4m, source.Units.Single(unit => unit.UnitId == SalesUnitId).KgPerUnit);
        Assert.Equal((world.Policy.Value.Profile.ProfileId, 17L, 9L), (source.PolicyId, source.PolicyVersion, source.DimensionVersion));
        Assert.Equal(ProductUnitConversionCalculatorV1.FormulaVersion, source.FormulaVersion);
        Assert.All(world.Profiles.Requests, request => Assert.Equal(On, request.Item2));
    }

    [Fact]
    public async Task Kg_to_piece_uses_configured_whole_rounding_and_publishes_the_unallocated_remainder()
    {
        var world = new World();
        var result = await world.Service().ConvertAsync(new(ProductId, 100m, StockUnitId, SalesUnitId, On));
        Assert.Equal((3m, 100m, 5.8m), (result.Quantity, result.InputKg, result.RemainderKg));
        world.Policy.Value = world.Policy.Value with { WholeUnitRounding = MidpointRounding.ToPositiveInfinity };
        var roundedUp = await world.Service().ConvertAsync(new(ProductId, 100m, StockUnitId, SalesUnitId, On));
        Assert.Equal((4m, -25.6m), (roundedUp.Quantity, roundedUp.RemainderKg));
    }

    [Fact]
    public async Task Coefficient_is_rounded_once_and_the_shown_coefficient_is_used_by_the_inverse()
    {
        var world = new World();
        world.Dimension.Value = Sheet(1m, 1000m, 1000m);
        world.Policy.Value = world.Policy.Value with { DensityGramsPerCubicCentimetre = 0.3335m };
        var result = await world.Service().ConvertAsync(new(ProductId, 1.002m, StockUnitId, SalesUnitId, On));
        Assert.Equal(0.334m, result.Source!.Units.Single(unit => unit.UnitId == SalesUnitId).KgPerUnit);
        Assert.Equal((3m, 0m), (result.Quantity, result.RemainderKg));
    }

    [Fact]
    public async Task Company_table_profile_uses_rounded_kg_per_metre_then_separate_piece_coefficient()
    {
        var world = new World();
        world.Dimension.Value = new(ProductDimensionOutcomeV1.Found, ProductDimensionSectionKindsV1.Profile,
            null, 40m, 6000m, 20m, null, 2m, 1.2345m, 9, "SENTETIK-table", 11);
        var result = await world.Service().ConvertAsync(new(ProductId, 1m, SalesUnitId, null, On));
        Assert.Equal(7.410m, result.Quantity);
        Assert.Equal((11L, "SENTETIK-table"), (result.Source!.WeightTableVersion, result.Source.WeightTableSource));
    }

    [Fact]
    public async Task Coil_converts_metres_with_the_shared_coefficient_and_never_coating_or_trim_guesses()
    {
        var world = new World();
        world.Dimension.Value = new(ProductDimensionOutcomeV1.Found, ProductDimensionSectionKindsV1.Coil,
            2m, 1000m, null, null, null, null, null, 9);
        Assert.Equal(47.1m, (await world.Service().ConvertAsync(new(ProductId, 3m, Metre, null, On))).Quantity);
    }

    [Theory]
    [InlineData("RoundTube")]
    [InlineData("Profile")]
    [InlineData("StructuralProfile")]
    [InlineData("EqualAngle")]
    [InlineData("ChequeredSheet")]
    public async Task A_missing_table_or_unsupported_section_never_uses_a_geometry_estimate(string section)
    {
        var world = new World();
        world.Dimension.Value = new(ProductDimensionOutcomeV1.Found, section, 2m, 40m, 6000m, 20m, 33.7m, 2m, null, 9);
        Assert.Equal(ProductPurchaseUnitConversionOutcomeV1.NoSource,
            (await world.Service().ConvertAsync(new(ProductId, 1m, SalesUnitId, null, On))).Outcome);
    }

    [Fact]
    public async Task Table_conflict_is_an_explicit_source_reason_and_never_a_formula_fallback()
    {
        var world = new World();
        world.Dimension.Value = world.Dimension.Value with { SectionKind = ProductDimensionSectionKindsV1.RoundTube,
            KgPerMetre = 2m, KgPerMetreTableVersion = 11, KgPerMetreSourceLabel = "SENTETIK", KgPerMetreConflict = true };
        var result = await world.Service().ConvertAsync(new(ProductId, 1m, SalesUnitId, null, On));
        Assert.Equal(ProductPurchaseUnitConversionOutcomeV1.NoSource, result.Outcome);
        Assert.Equal("product_profile_weight_conflict", result.Source!.Reason);
    }

    [Fact]
    public async Task Fixed_mass_units_are_typed_owner_scales_and_need_no_dimension_formula_or_density_default()
    {
        var world = new World();
        world.Dimension.Value = ProductDimensionReadV1.Without(ProductDimensionOutcomeV1.NoSource);
        world.Policy.Failure = new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.UnitConversionPolicyNotFound);
        var result = await world.Service().ConvertAsync(new(ProductId, 2.5m, Tonne, StockUnitId, On));
        Assert.Equal((ProductPurchaseUnitConversionOutcomeV1.Available, 2500m), (result.Outcome, result.Quantity));
        Assert.Equal(UnitConversionClassV1.None, result.Source!.ConversionClass);
    }

    [Fact]
    public async Task Missing_policy_or_unit_profile_is_no_source_and_never_an_automatic_profile_activation()
    {
        var world = new World();
        world.Policy.Failure = new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.UnitConversionPolicyNotFound);
        Assert.Equal(ProductPurchaseUnitConversionOutcomeV1.NoSource,
            (await world.Service().ConvertAsync(new(ProductId, 1m, SalesUnitId, null, On))).Outcome);
        world.Policy.Failure = null;
        world.Profiles.Shape = (unit, profile) => unit == SalesUnitId ? profile with { IsActive = false } : profile;
        Assert.Equal(ProductPurchaseUnitConversionOutcomeV1.NoSource,
            (await world.Service().ConvertAsync(new(ProductId, 1m, SalesUnitId, null, On))).Outcome);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("foreign")]
    [InlineData("wrong-scale")]
    public async Task Malformed_or_unavailable_typed_semantics_are_technical_and_not_permanent_no_source(string fault)
    {
        var world = new World();
        world.Semantics.Shape = value => fault switch
        {
            "foreign" => value with { UnitId = Guid.NewGuid() },
            "wrong-scale" => value with { Kind = SettingsPhysicalUnitKindV1.Tonne, KgPerUnit = 1m },
            _ => value with { Status = SettingsPhysicalUnitStatusV1.Unavailable },
        };
        Assert.Equal(ProductPurchaseUnitConversionOutcomeV1.Unavailable, (await world.Service().ReadAsync(ProductId, On)).Outcome);
    }

    [Fact]
    public async Task Unknown_identity_is_not_classified_from_its_code_or_dimension_name()
    {
        var world = new World();
        world.Semantics.Shape = value => value.UnitId == SalesUnitId
            ? new(SettingsPhysicalUnitStatusV1.NotFound, value.UnitId, null, null, null) : value;
        var answer = await world.Service().ConvertAsync(new(ProductId, 1m, SalesUnitId, null, On));
        Assert.Equal(ProductPurchaseUnitConversionOutcomeV1.NoSource, answer.Outcome);
        Assert.Null(answer.Source!.Units.Single(unit => unit.UnitId == SalesUnitId).Kind);
    }

    [Theory]
    [InlineData("31.714", true)]
    [InlineData("31.086", true)]
    [InlineData("31.71401", false)]
    public async Task Weighbridge_uses_theoretical_base_absolute_difference_and_inclusive_equal_boundary(string weighed, bool accepted)
    {
        var result = await new World().Service().CheckWeighbridgeAsync(new(ProductId, 1m, SalesUnitId,
            decimal.Parse(weighed, CultureInfo.InvariantCulture), On));
        Assert.Equal((ProductPurchaseUnitConversionOutcomeV1.Available, accepted, 31.4m),
            (result.Outcome, result.WithinTolerance, result.TheoreticalKg));
    }

    [Fact]
    public async Task Weighbridge_boundary_is_the_recorded_policy_and_no_density_or_tolerance_is_seeded()
    {
        var world = new World();
        world.Policy.Value = world.Policy.Value with { ToleranceBoundary = SettingsWeighbridgeToleranceBoundaryV1.Exclusive };
        Assert.False((await world.Service().CheckWeighbridgeAsync(new(ProductId, 1m, SalesUnitId, 31.714m, On))).WithinTolerance);
    }

    [Fact]
    public async Task Missing_price_reference_stays_null_and_does_not_fall_back_to_stock_or_sales()
    {
        var world = new World();
        world.Record.Value = StoredProduct(units: new(Stock: StockUnitId, Sales: SalesUnitId));
        Assert.Null((await world.Service().ReadAsync(ProductId, On)).Source!.DefaultPriceUnitId);
    }

    [Theory]
    [InlineData("record")]
    [InlineData("dimensions")]
    [InlineData("semantics")]
    [InlineData("profiles")]
    [InlineData("policy")]
    public async Task Actor_change_during_every_awaited_source_hides_its_answer_or_failure(string source)
    {
        var world = new World();
        Action change = () => world.User.UserId = "another-test-actor";
        switch (source)
        {
            case "record": world.Record.During = change; break;
            case "dimensions": world.Dimension.During = change; break;
            case "semantics": world.Semantics.During = change; break;
            case "profiles": world.Profiles.During = change; break;
            default: world.Policy.During = change; break;
        }
        Assert.Equal(ProductPurchaseUnitConversionOutcomeV1.Denied, (await world.Service().ReadAsync(ProductId, On)).Outcome);
    }

    [Fact]
    public async Task Missing_membership_denies_before_storage_and_cancellation_propagates()
    {
        var world = new World();
        world.User.Allowed.Clear();
        Assert.Equal(ProductPurchaseUnitConversionOutcomeV1.Denied, (await world.Service().ReadAsync(ProductId, On)).Outcome);
        Assert.Equal(0, world.Record.Calls);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => world.Service().ReadAsync(ProductId, On, cancellation.Token));
    }

    [Fact]
    public async Task Card_or_dimension_change_during_composition_is_unavailable_instead_of_mixed_provenance()
    {
        var world = new World();
        world.Record.During = () => { if (world.Record.Calls == 2) world.Record.Value = world.Record.Value with { Version = 9 }; };
        Assert.Equal(ProductPurchaseUnitConversionOutcomeV1.Unavailable, (await world.Service().ReadAsync(ProductId, On)).Outcome);
        var other = new World();
        other.Dimension.During = () => { if (other.Dimension.Calls == 2) other.Dimension.Value = other.Dimension.Value with { Version = 10 }; };
        Assert.Equal(ProductPurchaseUnitConversionOutcomeV1.Unavailable, (await other.Service().ReadAsync(ProductId, On)).Outcome);
    }

    [Fact]
    public async Task Disabled_runtime_has_a_real_unavailable_contract_and_no_successful_placeholder()
    {
        var services = new ServiceCollection().AddProductPurchaseUnitConversion(_ => false);
        using var provider = services.BuildServiceProvider();
        var source = provider.GetRequiredService<IProductPurchaseUnitConversionV1>();
        Assert.Equal(ProductPurchaseUnitConversionOutcomeV1.Unavailable, (await source.ReadAsync(ProductId, On)).Outcome);
    }

    private static ProductDimensionReadV1 Sheet(decimal thickness = 2m, decimal width = 1000m, decimal length = 2000m) =>
        new(ProductDimensionOutcomeV1.Found, ProductDimensionSectionKindsV1.Sheet, thickness, width, length, null, null, null, null, 9);

    private sealed class World
    {
        public Context Company { get; } = new();
        public User User { get; } = new();
        public Records Record { get; } = new();
        public Dimension Dimension { get; } = new();
        public Semantics Semantics { get; } = new();
        public Settings Profiles { get; }
        public Policy Policy { get; } = new();
        public World() => Profiles = new(Company);
        public ProductPurchaseUnitConversionService Service() => new(Company, User, Record, Dimension, Semantics,
            new(Profiles, Company), Policy);
    }
    private sealed class Records : IProductPurchaseRecordReader
    {
        public ProductStoredRecord Value { get; set; } = StoredProduct(units: new(StockUnitId, Tonne, SalesUnitId, SalesUnitId, Metre));
        public Action? During { get; set; }
        public int Calls { get; private set; }
        public Task<ProductStoredRecord?> ReadAsync(ProductPurchaseReadScope scope, Guid productId, CancellationToken cancellationToken)
        { Calls++; During?.Invoke(); return Task.FromResult<ProductStoredRecord?>(Value); }
    }
    private sealed class Dimension : IProductDimensionReaderV1
    {
        public ProductDimensionReadV1 Value { get; set; } = Sheet();
        public Action? During { get; set; }
        public int Calls { get; private set; }
        public Task<ProductDimensionReadV1> GetAsync(Guid productId, CancellationToken cancellationToken = default)
        { Calls++; During?.Invoke(); return Task.FromResult(Value); }
    }
    private sealed class Semantics : ISettingsPhysicalUnitSemanticsV1
    {
        public Func<SettingsPhysicalUnitSemanticsV1, SettingsPhysicalUnitSemanticsV1>? Shape { get; set; }
        public Action? During { get; set; }
        public Task<SettingsPhysicalUnitSemanticsV1> ReadAsync(Guid unitId, CancellationToken cancellationToken = default)
        {
            During?.Invoke();
            var kind = unitId == StockUnitId ? SettingsPhysicalUnitKindV1.Kilogram : unitId == Tonne ? SettingsPhysicalUnitKindV1.Tonne
                : unitId == Metre ? SettingsPhysicalUnitKindV1.Metre : SettingsPhysicalUnitKindV1.Piece;
            var value = new SettingsPhysicalUnitSemanticsV1(SettingsPhysicalUnitStatusV1.Found, unitId, kind,
                kind == SettingsPhysicalUnitKindV1.Kilogram ? 1m : kind == SettingsPhysicalUnitKindV1.Tonne ? 1000m : null, "synthetic-v1");
            return Task.FromResult(Shape?.Invoke(value) ?? value);
        }
    }
    private sealed class Policy : ISettingsUnitConversionPolicyProviderV1
    {
        public SettingsUnitConversionPolicyV1 Value { get; set; } = new(CompanyId, new(Guid.NewGuid(), 17), 7.85m,
            3, MidpointRounding.AwayFromZero, 2, MidpointRounding.AwayFromZero, MidpointRounding.ToNegativeInfinity,
            1m, SettingsWeighbridgeToleranceBoundaryV1.Inclusive, SettingsWeighbridgeToleranceBaseV1.TheoreticalKg, On, null);
        public Exception? Failure { get; set; }
        public Action? During { get; set; }
        public Task<SettingsUnitConversionPolicyV1> GetUnitConversionPolicyAsync(DateOnly effectiveOn, CancellationToken cancellationToken = default)
        { During?.Invoke(); return Failure is null ? Task.FromResult(Value) : Task.FromException<SettingsUnitConversionPolicyV1>(Failure); }
    }
}

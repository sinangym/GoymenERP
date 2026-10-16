using GoymenERP.Modules.Manufacturing.Domain.Engines;
using GoymenERP.Modules.Manufacturing.Domain.Engines.TubeProfile;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests.Engines.TubeProfile;

public sealed class TubeProfilePlanCalculatorV2Tests
{
    private static readonly Guid Product = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid PackageSource = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid SpeedSource = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid UnitKgSource = Guid.Parse("40000000-0000-0000-0000-000000000001");

    [Fact]
    public void Gdc_tub_2_complete_golden_is_exact_and_unrounded()
    {
        var calculation = TubeProfilePlanCalculatorV2.Calculate(Spec(
            targetPieces: 1000,
            package: new TubeProfilePackageStandardSnapshotV1(PackageSource, "package-v3", 50),
            speed: new TubeProfileLineSpeedSnapshotV1(SpeedSource, "speed-v7", 30m),
            unitKg: new TubeProfileVerifiedUnitKgSnapshotV1(UnitKgSource, "unit-kg-v11", 12.345m)));

        Assert.Equal(TubeProfilePlanCalculatorV2.FormulaVersion, calculation.Quantity.Value!.FormulaVersion);
        Assert.Equal((1000, 6000m),
            (calculation.Quantity.Value.TargetPieces, calculation.Quantity.Value.TotalMetres));
        Assert.Equal((20, 0, 20),
            (calculation.Package.Value!.FullPackages,
                calculation.Package.Value.RemainderPieces,
                calculation.Package.Value.SuggestedPackageCount));
        Assert.Equal(200m, calculation.Duration.Value!.RunMinutes);
        Assert.Equal(12_345m, calculation.TheoreticalMass.Value!.TheoreticalKg);
        Assert.All([calculation.Quantity.Outcome, calculation.Package.Outcome, calculation.Duration.Outcome,
            calculation.TheoreticalMass.Outcome], outcome => Assert.Equal(EngineOutcome.Computed, outcome));
    }

    [Fact]
    public void Partial_final_group_is_visible_and_never_changes_the_target()
    {
        var calculation = TubeProfilePlanCalculatorV2.Calculate(Spec(
            targetPieces: 975,
            package: new TubeProfilePackageStandardSnapshotV1(PackageSource, "package-v3", 50)));

        Assert.Equal((975, 5850m),
            (calculation.Quantity.Value!.TargetPieces, calculation.Quantity.Value.TotalMetres));
        Assert.Equal((19, 25, 20),
            (calculation.Package.Value!.FullPackages,
                calculation.Package.Value.RemainderPieces,
                calculation.Package.Value.SuggestedPackageCount));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void Three_optional_sources_are_independent_and_missing_affects_only_its_own_answer(
        bool hasPackage, bool hasSpeed, bool hasUnitKg)
    {
        var calculation = TubeProfilePlanCalculatorV2.Calculate(Spec(
            targetPieces: 1000,
            package: hasPackage
                ? new TubeProfilePackageStandardSnapshotV1(PackageSource, "package-v3", 50)
                : null,
            speed: hasSpeed
                ? new TubeProfileLineSpeedSnapshotV1(SpeedSource, "speed-v7", 30m)
                : null,
            unitKg: hasUnitKg
                ? new TubeProfileVerifiedUnitKgSnapshotV1(UnitKgSource, "unit-kg-v11", 12.345m)
                : null));

        Assert.Equal(EngineOutcome.Computed, calculation.Quantity.Outcome);
        Assert.Equal((1000, 6000m),
            (calculation.Quantity.Value!.TargetPieces, calculation.Quantity.Value.TotalMetres));
        AssertOwnOutcome(calculation.Package, hasPackage, TubeProfilePlanCalculatorV2.PackageStandardParameter);
        AssertOwnOutcome(calculation.Duration, hasSpeed, TubeProfilePlanCalculatorV2.LineSpeedParameter);
        AssertOwnOutcome(calculation.TheoreticalMass, hasUnitKg,
            TubeProfilePlanCalculatorV2.VerifiedUnitKgParameter);
    }

    [Fact]
    public void Verified_unit_kg_precision_is_owned_by_the_snapshot_and_is_not_rounded_here()
    {
        var calculation = TubeProfilePlanCalculatorV2.Calculate(Spec(
            targetPieces: 1000,
            unitKg: new TubeProfileVerifiedUnitKgSnapshotV1(UnitKgSource, "unit-kg-v11", 12.345678m)));

        Assert.Equal(12_345.678m, calculation.TheoreticalMass.Value!.TheoreticalKg);
    }

    [Fact]
    public void Representable_maximum_total_metres_does_not_overflow_before_scaling()
    {
        var calculation = TubeProfilePlanCalculatorV2.Calculate(Spec(
            targetPieces: 1000,
            targetLengthMm: decimal.MaxValue));

        Assert.Equal(EngineOutcome.Computed, calculation.Quantity.Outcome);
        Assert.Equal(decimal.MaxValue, calculation.Quantity.Value!.TotalMetres);
    }

    [Fact]
    public void Unrepresentable_total_metres_refuses_only_quantity_and_a_representable_duration_still_computes()
    {
        var calculation = TubeProfilePlanCalculatorV2.Calculate(Spec(
            targetPieces: 1001,
            targetLengthMm: decimal.MaxValue,
            package: new TubeProfilePackageStandardSnapshotV1(PackageSource, "package-v3", 50),
            speed: new TubeProfileLineSpeedSnapshotV1(SpeedSource, "speed-v7", 1001m),
            unitKg: new TubeProfileVerifiedUnitKgSnapshotV1(UnitKgSource, "unit-kg-v11", 1m)));

        AssertRefused(calculation.Quantity, TubeProfilePlanCalculatorV2.TotalMetresResult);
        Assert.Equal((20, 1, 21),
            (calculation.Package.Value!.FullPackages,
                calculation.Package.Value.RemainderPieces,
                calculation.Package.Value.SuggestedPackageCount));
        Assert.Equal(decimal.MaxValue / 1000m, calculation.Duration.Value!.RunMinutes);
        Assert.Equal(1001m, calculation.TheoreticalMass.Value!.TheoreticalKg);
    }

    [Fact]
    public void Representable_duration_uses_full_cross_factor_cancellation_independent_of_quantity()
    {
        const decimal length = decimal.MaxValue - 3m;
        var calculation = TubeProfilePlanCalculatorV2.Calculate(Spec(
            targetPieces: 7,
            targetLengthMm: length,
            package: new TubeProfilePackageStandardSnapshotV1(PackageSource, "package-v3", 2),
            speed: new TubeProfileLineSpeedSnapshotV1(SpeedSource, "speed-v7", 21m),
            unitKg: new TubeProfileVerifiedUnitKgSnapshotV1(UnitKgSource, "unit-kg-v11", 1m)));

        AssertRefused(calculation.Quantity, TubeProfilePlanCalculatorV2.TotalMetresResult);
        Assert.Equal((3, 1, 4),
            (calculation.Package.Value!.FullPackages,
                calculation.Package.Value.RemainderPieces,
                calculation.Package.Value.SuggestedPackageCount));
        Assert.Equal(26409387504754779197847983.444m, calculation.Duration.Value!.RunMinutes);
        Assert.Equal(7m, calculation.TheoreticalMass.Value!.TheoreticalKg);
    }

    [Fact]
    public void Non_terminating_duration_refuses_without_selecting_a_rounding_policy()
    {
        var calculation = TubeProfilePlanCalculatorV2.Calculate(Spec(
            targetPieces: 1,
            targetLengthMm: 1000m,
            package: new TubeProfilePackageStandardSnapshotV1(PackageSource, "package-v3", 1),
            speed: new TubeProfileLineSpeedSnapshotV1(SpeedSource, "speed-v7", 3m),
            unitKg: new TubeProfileVerifiedUnitKgSnapshotV1(UnitKgSource, "unit-kg-v11", 1m)));

        Assert.Equal(1m, calculation.Quantity.Value!.TotalMetres);
        Assert.Equal(1, calculation.Package.Value!.SuggestedPackageCount);
        AssertRefused(calculation.Duration, TubeProfilePlanCalculatorV2.RunMinutesResult);
        Assert.Equal(1m, calculation.TheoreticalMass.Value!.TheoreticalKg);
    }

    [Fact]
    public void Unrepresentable_duration_refuses_only_duration()
    {
        var calculation = TubeProfilePlanCalculatorV2.Calculate(Spec(
            targetPieces: 1000,
            targetLengthMm: decimal.MaxValue,
            package: new TubeProfilePackageStandardSnapshotV1(PackageSource, "package-v3", 50),
            speed: new TubeProfileLineSpeedSnapshotV1(SpeedSource, "speed-v7", 0.1m),
            unitKg: new TubeProfileVerifiedUnitKgSnapshotV1(UnitKgSource, "unit-kg-v11", 1m)));

        Assert.Equal(decimal.MaxValue, calculation.Quantity.Value!.TotalMetres);
        Assert.Equal(20, calculation.Package.Value!.SuggestedPackageCount);
        AssertRefused(calculation.Duration, TubeProfilePlanCalculatorV2.RunMinutesResult);
        Assert.Equal(1000m, calculation.TheoreticalMass.Value!.TheoreticalKg);
    }

    [Fact]
    public void Unrepresentable_theoretical_mass_refuses_only_mass()
    {
        var calculation = TubeProfilePlanCalculatorV2.Calculate(Spec(
            targetPieces: 2,
            targetLengthMm: 1000m,
            package: new TubeProfilePackageStandardSnapshotV1(PackageSource, "package-v3", 1),
            speed: new TubeProfileLineSpeedSnapshotV1(SpeedSource, "speed-v7", 1m),
            unitKg: new TubeProfileVerifiedUnitKgSnapshotV1(UnitKgSource, "unit-kg-v11", decimal.MaxValue)));

        Assert.Equal(2m, calculation.Quantity.Value!.TotalMetres);
        Assert.Equal(2, calculation.Package.Value!.SuggestedPackageCount);
        Assert.Equal(2m, calculation.Duration.Value!.RunMinutes);
        AssertRefused(calculation.TheoreticalMass, TubeProfilePlanCalculatorV2.TheoreticalKgResult);
    }

    [Fact]
    public void Duration_below_decimal_range_refuses_instead_of_becoming_zero()
    {
        var calculation = TubeProfilePlanCalculatorV2.Calculate(Spec(
            targetPieces: 1,
            targetLengthMm: 0.001m,
            package: new TubeProfilePackageStandardSnapshotV1(PackageSource, "package-v3", 1),
            speed: new TubeProfileLineSpeedSnapshotV1(SpeedSource, "speed-v7", decimal.MaxValue),
            unitKg: new TubeProfileVerifiedUnitKgSnapshotV1(UnitKgSource, "unit-kg-v11", 1m)));

        Assert.Equal(0.000001m, calculation.Quantity.Value!.TotalMetres);
        Assert.Equal(1, calculation.Package.Value!.SuggestedPackageCount);
        AssertRefused(calculation.Duration, TubeProfilePlanCalculatorV2.RunMinutesResult);
        Assert.Equal(1m, calculation.TheoreticalMass.Value!.TheoreticalKg);
    }

    [Fact]
    public void Exact_quantity_that_would_require_silent_scale_rounding_is_refused()
    {
        const decimal length = 79228162514264337593543950.335m;
        var calculation = TubeProfilePlanCalculatorV2.Calculate(Spec(
            targetPieces: 3,
            targetLengthMm: length,
            package: new TubeProfilePackageStandardSnapshotV1(PackageSource, "package-v3", 2),
            speed: new TubeProfileLineSpeedSnapshotV1(SpeedSource, "speed-v7", 3m),
            unitKg: new TubeProfileVerifiedUnitKgSnapshotV1(UnitKgSource, "unit-kg-v11", 1m)));

        AssertRefused(calculation.Quantity, TubeProfilePlanCalculatorV2.TotalMetresResult);
        Assert.Equal((1, 1, 2),
            (calculation.Package.Value!.FullPackages,
                calculation.Package.Value.RemainderPieces,
                calculation.Package.Value.SuggestedPackageCount));
        Assert.Equal(length / 1000m, calculation.Duration.Value!.RunMinutes);
        Assert.Equal(3m, calculation.TheoreticalMass.Value!.TheoreticalKg);
    }

    [Fact]
    public void Exact_mass_that_would_require_silent_scale_rounding_is_refused()
    {
        const decimal unitKg = 7.9228162514264337593543950335m;
        var calculation = TubeProfilePlanCalculatorV2.Calculate(Spec(
            targetPieces: 3,
            targetLengthMm: 1000m,
            package: new TubeProfilePackageStandardSnapshotV1(PackageSource, "package-v3", 1),
            speed: new TubeProfileLineSpeedSnapshotV1(SpeedSource, "speed-v7", 1m),
            unitKg: new TubeProfileVerifiedUnitKgSnapshotV1(UnitKgSource, "unit-kg-v11", unitKg)));

        Assert.Equal(3m, calculation.Quantity.Value!.TotalMetres);
        Assert.Equal(3, calculation.Package.Value!.SuggestedPackageCount);
        Assert.Equal(3m, calculation.Duration.Value!.RunMinutes);
        AssertRefused(calculation.TheoreticalMass, TubeProfilePlanCalculatorV2.TheoreticalKgResult);
    }

    [Fact]
    public void Null_spec_fails_closed()
    {
        Assert.Throws<ArgumentNullException>(() => TubeProfilePlanCalculatorV2.Calculate(null!));
    }

    private static TubeProfilePlanSpecV1 Spec(
        int targetPieces,
        decimal targetLengthMm = 6000m,
        TubeProfilePackageStandardSnapshotV1? package = null,
        TubeProfileLineSpeedSnapshotV1? speed = null,
        TubeProfileVerifiedUnitKgSnapshotV1? unitKg = null) =>
        new(Product, "product-v5", targetLengthMm, targetPieces, package, speed, unitKg);

    private static void AssertOwnOutcome<T>(EngineResult<T> result, bool hasSource, string parameter)
        where T : class
    {
        if (hasSource)
        {
            Assert.Equal(EngineOutcome.Computed, result.Outcome);
            Assert.NotNull(result.Value);
            Assert.Empty(result.MissingParameters);
            return;
        }

        Assert.Equal(EngineOutcome.Unavailable, result.Outcome);
        Assert.Null(result.Value);
        Assert.Equal(parameter, Assert.Single(result.MissingParameters));
    }

    private static void AssertRefused<T>(EngineResult<T> result, string affectedResult)
        where T : class
    {
        Assert.Equal(EngineOutcome.Refused, result.Outcome);
        Assert.Null(result.Value);
        Assert.Empty(result.MissingParameters);
        Assert.Equal(TubeProfilePlanCalculatorV2.ArithmeticNotRepresentableCode, result.RefusalCode);
        Assert.Equal(affectedResult, result.RefusedParameter);
    }
}

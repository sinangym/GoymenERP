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
    public void Null_spec_fails_closed()
    {
        Assert.Throws<ArgumentNullException>(() => TubeProfilePlanCalculatorV2.Calculate(null!));
    }

    private static TubeProfilePlanSpecV1 Spec(
        int targetPieces,
        TubeProfilePackageStandardSnapshotV1? package = null,
        TubeProfileLineSpeedSnapshotV1? speed = null,
        TubeProfileVerifiedUnitKgSnapshotV1? unitKg = null) =>
        new(Product, "product-v5", 6000m, targetPieces, package, speed, unitKg);

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
}

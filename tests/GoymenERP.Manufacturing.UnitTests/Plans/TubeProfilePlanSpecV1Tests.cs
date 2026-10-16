using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests.Plans;

public sealed class TubeProfilePlanSpecV1Tests
{
    private static readonly Guid Product = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid PackageSource = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid SpeedSource = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid UnitKgSource = Guid.Parse("40000000-0000-0000-0000-000000000001");

    [Fact]
    public void Required_target_and_three_independent_source_snapshots_are_kept_exactly()
    {
        var package = new TubeProfilePackageStandardSnapshotV1(PackageSource, "package-v3", 50);
        var speed = new TubeProfileLineSpeedSnapshotV1(SpeedSource, "speed-v7", 30m);
        var unitKg = new TubeProfileVerifiedUnitKgSnapshotV1(UnitKgSource, "unit-kg-v11", 12.345678m);

        var spec = new TubeProfilePlanSpecV1(Product, "product-v5", 6000.000m, 1000,
            package, speed, unitKg);

        Assert.Equal((Product, "product-v5", 6000.000m, 1000),
            (spec.OutputProductId, spec.ExpectedProductVersion, spec.TargetLengthMm, spec.TargetPieceCount));
        Assert.Same(package, spec.PackageStandardSnapshot);
        Assert.Same(speed, spec.LineSpeedSnapshot);
        Assert.Same(unitKg, spec.VerifiedUnitKgSnapshot);
        Assert.Equal(12.345678m, spec.VerifiedUnitKgSnapshot!.UnitKg);
    }

    [Fact]
    public void Optional_sources_can_be_absent_without_hiding_the_required_target()
    {
        var spec = new TubeProfilePlanSpecV1(Product, "product-v5", 6000m, 975);

        Assert.Equal((975, 6000m), (spec.TargetPieceCount, spec.TargetLengthMm));
        Assert.Null(spec.PackageStandardSnapshot);
        Assert.Null(spec.LineSpeedSnapshot);
        Assert.Null(spec.VerifiedUnitKgSnapshot);
    }

    [Fact]
    public void Empty_product_identity_or_version_fails_closed()
    {
        Assert.Throws<ArgumentException>(() => new TubeProfilePlanSpecV1(Guid.Empty, "product-v5", 6000m, 1000));
        Assert.Throws<ArgumentException>(() => new TubeProfilePlanSpecV1(Product, "", 6000m, 1000));
        Assert.Throws<ArgumentException>(() => new TubeProfilePlanSpecV1(Product, "   ", 6000m, 1000));
    }

    [Fact]
    public void Target_length_must_be_positive_and_at_most_three_decimal_places()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TubeProfilePlanSpecV1(Product, "product-v5", 0m, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TubeProfilePlanSpecV1(Product, "product-v5", -1m, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TubeProfilePlanSpecV1(Product, "product-v5", 6000.0001m, 1000));

        Assert.Equal(6000.000m,
            new TubeProfilePlanSpecV1(Product, "product-v5", 6000.000m, 1000).TargetLengthMm);
    }

    [Fact]
    public void Target_piece_count_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TubeProfilePlanSpecV1(Product, "product-v5", 6000m, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TubeProfilePlanSpecV1(Product, "product-v5", 6000m, -1));
    }

    [Fact]
    public void Package_snapshot_requires_source_version_and_positive_pieces_per_package()
    {
        Assert.Throws<ArgumentException>(() =>
            new TubeProfilePackageStandardSnapshotV1(Guid.Empty, "package-v3", 50));
        Assert.Throws<ArgumentException>(() =>
            new TubeProfilePackageStandardSnapshotV1(PackageSource, " ", 50));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TubeProfilePackageStandardSnapshotV1(PackageSource, "package-v3", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TubeProfilePackageStandardSnapshotV1(PackageSource, "package-v3", -1));
    }

    [Fact]
    public void Speed_snapshot_requires_source_version_and_a_positive_speed()
    {
        Assert.Throws<ArgumentException>(() =>
            new TubeProfileLineSpeedSnapshotV1(Guid.Empty, "speed-v7", 30m));
        Assert.Throws<ArgumentException>(() =>
            new TubeProfileLineSpeedSnapshotV1(SpeedSource, "", 30m));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TubeProfileLineSpeedSnapshotV1(SpeedSource, "speed-v7", 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TubeProfileLineSpeedSnapshotV1(SpeedSource, "speed-v7", -1m));
    }

    [Fact]
    public void Unit_kg_snapshot_requires_source_version_and_a_positive_value_without_choosing_precision()
    {
        Assert.Throws<ArgumentException>(() =>
            new TubeProfileVerifiedUnitKgSnapshotV1(Guid.Empty, "unit-kg-v11", 12.345m));
        Assert.Throws<ArgumentException>(() =>
            new TubeProfileVerifiedUnitKgSnapshotV1(UnitKgSource, "   ", 12.345m));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TubeProfileVerifiedUnitKgSnapshotV1(UnitKgSource, "unit-kg-v11", 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TubeProfileVerifiedUnitKgSnapshotV1(UnitKgSource, "unit-kg-v11", -1m));

        Assert.Equal(12.345678m,
            new TubeProfileVerifiedUnitKgSnapshotV1(UnitKgSource, "unit-kg-v11", 12.345678m).UnitKg);
    }
}

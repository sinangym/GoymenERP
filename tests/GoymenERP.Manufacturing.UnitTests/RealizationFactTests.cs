using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// #280 review P2-1 (SİPARİŞ 5728721339 = FİNANS 5728731264): realization inputs are facts with
/// their own id and time (plan v2 R6, §7.3), not anonymous totals. A fact applied twice is
/// refused and changes nothing; an older WIP reading never replaces a newer one. Also the
/// guards SİPARİŞ P3-3 listed as untested.
/// </summary>
public sealed class RealizationFactTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkCenter = Guid.Parse("20000000-0000-0000-0000-00000000c001");
    private static readonly Guid MasterRoll = Guid.Parse("30000000-0000-0000-0000-00000000a001");
    private static readonly Guid Planner = Guid.Parse("40000000-0000-0000-0000-00000000b001");
    private static readonly DateTimeOffset Morning = new(2026, 9, 18, 8, 0, 0, TimeSpan.FromHours(3));

    private static Kilograms Kg(decimal value) => new(value);

    private static ProductionOrder Order()
    {
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(MasterRoll, Kg(10_000m))]);
        plan.Approve(Planner, DateTimeOffset.UnixEpoch);
        return ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
    }

    [Fact]
    public void The_same_issue_slip_applied_twice_is_refused_and_the_mass_is_not_doubled()
    {
        var order = Order();
        var slip = Guid.NewGuid();
        order.IssueInput(slip, MasterRoll, Kg(10_000m), Morning);

        var error = Assert.Throws<ManufacturingRuleException>(
            () => order.IssueInput(slip, MasterRoll, Kg(10_000m), Morning.AddMinutes(1)));

        Assert.Equal(ManufacturingRuleCodes.InputAlreadyIssued, error.Code);
        Assert.Equal(10_000m, order.BalanceOf(MasterRoll).Issued.Value);
        Assert.Single(order.Issues);
    }

    [Fact]
    public void The_same_return_applied_twice_is_refused()
    {
        var order = Order();
        order.Issue(MasterRoll, Kg(10_000m));
        var returnId = Guid.NewGuid();
        order.ReturnUnusedInput(returnId, MasterRoll, Kg(2_000m), Morning);

        var error = Assert.Throws<ManufacturingRuleException>(
            () => order.ReturnUnusedInput(returnId, MasterRoll, Kg(2_000m), Morning.AddMinutes(1)));

        Assert.Equal(ManufacturingRuleCodes.ReturnAlreadyRecorded, error.Code);
        Assert.Equal(2_000m, order.BalanceOf(MasterRoll).Returned.Value);
    }

    [Fact]
    public void Each_input_fact_keeps_its_id_roll_mass_and_utc_time()
    {
        var order = Order();
        var slip = Guid.NewGuid();
        var returnId = Guid.NewGuid();

        order.IssueInput(slip, MasterRoll, Kg(6_000m), Morning);
        order.Issue(MasterRoll, Kg(4_000m));
        order.ReturnUnusedInput(returnId, MasterRoll, Kg(500m), Morning.AddHours(2));

        var first = order.Issues[0];
        Assert.Equal((slip, MasterRoll, 6_000m), (first.IssueId, first.ParentRollId, first.Mass.Value));
        Assert.Equal(TimeSpan.Zero, first.IssuedAtUtc.Offset);
        Assert.Equal(Morning.UtcDateTime, first.IssuedAtUtc.UtcDateTime);
        Assert.Equal(2, order.Issues.Count);
        Assert.Equal(returnId, Assert.Single(order.Returns).ReturnId);
        // The totals are the facts' sum.
        Assert.Equal(9_500m, order.BalanceOf(MasterRoll).NetIssued.Value);
    }

    [Fact]
    public void The_latest_wip_reading_counts_and_an_older_one_never_replaces_it()
    {
        var order = Order();
        order.Issue(MasterRoll, Kg(10_000m));
        order.RecordWorkInProgress(Guid.NewGuid(), MasterRoll, Kg(7_000m), Morning);
        order.RecordWorkInProgress(Guid.NewGuid(), MasterRoll, Kg(4_000m), Morning.AddHours(1));

        var stale = Assert.Throws<ManufacturingRuleException>(
            () => order.RecordWorkInProgress(Guid.NewGuid(), MasterRoll, Kg(9_000m), Morning.AddMinutes(30)));
        Assert.Equal(ManufacturingRuleCodes.WipMeasurementNotLatest, stale.Code);

        var tie = Assert.Throws<ManufacturingRuleException>(
            () => order.RecordWorkInProgress(Guid.NewGuid(), MasterRoll, Kg(9_000m), Morning.AddHours(1)));
        Assert.Equal(ManufacturingRuleCodes.WipMeasurementNotLatest, tie.Code);

        Assert.Equal(4_000m, order.BalanceOf(MasterRoll).WorkInProgress.Value);
        // Both accepted readings stay as history.
        Assert.Equal(2, order.WipMeasurements.Count);
    }

    [Fact]
    public void The_same_wip_reading_applied_twice_is_refused()
    {
        var order = Order();
        order.Issue(MasterRoll, Kg(10_000m));
        var measurementId = Guid.NewGuid();
        order.RecordWorkInProgress(measurementId, MasterRoll, Kg(7_000m), Morning);

        var error = Assert.Throws<ManufacturingRuleException>(
            () => order.RecordWorkInProgress(measurementId, MasterRoll, Kg(6_000m), Morning.AddHours(1)));

        Assert.Equal(ManufacturingRuleCodes.WipAlreadyMeasured, error.Code);
        Assert.Equal(7_000m, order.BalanceOf(MasterRoll).WorkInProgress.Value);
    }

    [Fact]
    public void Facts_need_their_own_identity()
    {
        var order = Order();
        Assert.Throws<ArgumentException>(() => order.IssueInput(Guid.Empty, MasterRoll, Kg(1m), Morning));
        order.Issue(MasterRoll, Kg(10_000m));
        Assert.Throws<ArgumentException>(() => order.ReturnUnusedInput(Guid.Empty, MasterRoll, Kg(1m), Morning));
        Assert.Throws<ArgumentException>(() => order.RecordWorkInProgress(Guid.Empty, MasterRoll, Kg(1m), Morning));
        Assert.Throws<ArgumentException>(
            () => order.DeclareOutput(Guid.Empty, MasterRoll, OutputClass.GoodProduct, Kg(1m), Morning));
    }

    // SİPARİŞ P3-3: guards that had no test.

    [Fact]
    public void An_issue_of_zero_mass_is_refused()
    {
        var order = Order();
        Assert.Throws<ArgumentException>(() => order.Issue(MasterRoll, Kilograms.Zero));
        Assert.Empty(order.Issues);
    }

    [Fact]
    public void An_undefined_output_class_is_refused()
    {
        var order = Order();
        order.Issue(MasterRoll, Kg(10_000m));
        Assert.Throws<ArgumentOutOfRangeException>(() => order.Declare(MasterRoll, (OutputClass)99, Kg(1m)));
        Assert.Empty(order.Outputs);
    }

    [Fact]
    public void A_target_needs_a_defined_basis_and_a_positive_mass()
    {
        Assert.Throws<ArgumentException>(() => new ProductionTarget(TargetBasis.InputMass, Kilograms.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProductionTarget((TargetBasis)99, Kg(1m)));
    }

    [Fact]
    public void A_planned_input_needs_a_positive_mass_and_a_roll()
    {
        Assert.Throws<ArgumentException>(() => new PlannedInput(MasterRoll, Kilograms.Zero));
        Assert.Throws<ArgumentException>(() => new PlannedInput(Guid.Empty, Kg(1m)));
    }

    [Fact]
    public void Work_in_progress_needs_input_issued_from_that_roll()
    {
        var order = Order();
        var error = Assert.Throws<ManufacturingRuleException>(() => order.MeasureWip(MasterRoll, Kg(1m)));
        Assert.Equal(ManufacturingRuleCodes.RollNotIssued, error.Code);
        Assert.Empty(order.WipMeasurements);
    }
}

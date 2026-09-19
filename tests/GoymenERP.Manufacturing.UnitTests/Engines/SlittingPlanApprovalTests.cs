using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Engines;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests.Engines;

/// <summary>
/// MFG-6 slice 2: a plan's slitting spec and the fail-closed approval gate. Every outside value here is a
/// SYNTHETIC test input (the master §28.4 golden coil, density 7850 kg/m³ as its examples imply); none is a
/// production parameter. The planned mass is the engine's parent mass (the kg that passes the line).
/// </summary>
public sealed class SlittingPlanApprovalTests
{
    private static readonly Guid Company = Guid.Parse("6f000000-0000-4000-8000-00000000000a");
    private static readonly Guid WorkCenter = Guid.Parse("6f100000-0000-4000-8000-000000000001");
    private static readonly Guid Roll = Guid.Parse("6f200000-0000-4000-8000-000000000001");
    private static readonly Guid Approver = Guid.Parse("6f300000-0000-4000-8000-000000000001");
    private static readonly DateTimeOffset At = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    private static readonly SlitLine[] GoldenLines = [new(150m, 4), new(240m, 3)];

    [Fact]
    public void A_spec_needs_a_line_and_takes_nominal_millimetres_and_a_positive_target()
    {
        Assert.Equal(ManufacturingRuleCodes.SlittingSpecWithoutLines,
            Assert.Throws<ManufacturingRuleException>(() => new SlittingSpec([])).Code);
        Assert.Equal(ManufacturingRuleCodes.MillimetrePrecisionExceeded,
            Assert.Throws<ManufacturingRuleException>(() => new SlitLine(150.0005m, 4)).Code);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlitLine(0m, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlitLine(150m, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlittingSpec(GoldenLines, Kilograms.Zero));

        // Trailing zeros are not digits (Ö2): 150.5000 mm is 150.5 mm.
        Assert.Equal(150.5m, new SlitLine(150.5000m, 1).BandWidthMm);
        Assert.Equal(new SlittingSpec(GoldenLines), new SlittingSpec([new SlitLine(150m, 4), new SlitLine(240m, 3)]));
    }

    [Fact]
    public void A_plan_without_a_spec_is_approved_as_before()
    {
        var plan = Plan(spec: null);

        plan.Approve(1, Approver, At);

        Assert.Equal((PlanStatus.Approved, 2L), (plan.Status, plan.Version));
    }

    [Fact]
    public void Nothing_known_refuses_the_approval_and_names_every_missing_input_nothing_changes()
    {
        var plan = Plan(new SlittingSpec(GoldenLines));

        var refusal = Assert.Throws<ManufacturingRuleException>(() => plan.Approve(1, Approver, At));

        Assert.Equal(ManufacturingRuleCodes.PlanParametersUnavailable, refusal.Code);
        Assert.Equal(
            ["densityKgPerCubicMetre", "labelState", "lineSpeedMetresPerMinute", "netUsableWidthMm", "parentWidthMm", "thicknessMm"],
            refusal.Parameters);
        Assert.Equal((PlanStatus.Draft, 1L, (Guid?)null), (plan.Status, plan.Version, plan.ApprovedByUserId));
    }

    [Fact]
    public void With_a_winding_target_the_diameter_tolerance_and_core_are_named_too_never_placeholders()
    {
        var plan = Plan(new SlittingSpec(GoldenLines, new Kilograms(2000m)));

        var refusal = Assert.Throws<ManufacturingRuleException>(
            () => plan.Approve(1, Approver, At, SlittingPlanParameters.Unknown));

        Assert.Equal(ManufacturingRuleCodes.PlanParametersUnavailable, refusal.Code);
        // The fit's computed inputs (walking / required metres, the outer diameter) are not outside
        // parameters: the engines above already named what they lack.
        Assert.Equal(
            ["densityKgPerCubicMetre", "innerDiameterMm", "labelState", "lineSpeedMetresPerMinute", "maximumOuterDiameterMm",
                "netUsableWidthMm", "parentWidthMm", "thicknessMm", "toleranceMetres"],
            refusal.Parameters);
    }

    [Fact]
    public void Every_input_known_approves_and_a_single_gap_still_refuses_naming_only_it()
    {
        var spec = new SlittingSpec(GoldenLines, new Kilograms(2000m));

        var complete = Plan(spec);
        complete.Approve(1, Approver, At, Known());
        Assert.Equal((PlanStatus.Approved, 2L, (Guid?)Approver), (complete.Status, complete.Version, complete.ApprovedByUserId));

        var noSpeed = Plan(spec);
        var refusal = Assert.Throws<ManufacturingRuleException>(
            () => noSpeed.Approve(1, Approver, At, Known() with { LineSpeedMetresPerMinute = null }));
        Assert.Equal((ManufacturingRuleCodes.PlanParametersUnavailable, "lineSpeedMetresPerMinute"),
            (refusal.Code, Assert.Single(refusal.Parameters)));

        // A roll the parameters do not mention is unknown as a whole.
        var otherRoll = Plan(spec);
        var unknownRoll = Assert.Throws<ManufacturingRuleException>(() => otherRoll.Approve(1, Approver, At,
            Known() with { Rolls = new Dictionary<Guid, SlittingRollParameters> { [Guid.NewGuid()] = KnownRoll() } }));
        Assert.Equal(["densityKgPerCubicMetre", "labelState", "netUsableWidthMm", "parentWidthMm", "thicknessMm"],
            unknownRoll.Parameters);
    }

    [Fact]
    public void A_wrong_value_is_refused_before_missing_ones_are_named()
    {
        // The slit lines use more width than the coil has (master §28.4 invariant), while the density is missing too.
        var tooWide = Plan(new SlittingSpec([new SlitLine(150m, 4), new SlitLine(240m, 4)]));
        var refusal = Assert.Throws<ManufacturingRuleException>(() => tooWide.Approve(1, Approver, At,
            Known() with { Rolls = Rolls(KnownRoll() with { DensityKgPerCubicMetre = null }) }));
        Assert.Equal((ManufacturingRuleCodes.PlanEngineRefused, EngineCodes.UsedWidthExceedsNetWidth, "lines"),
            (refusal.Code, refusal.Parameters[0], refusal.Parameters[1]));

        var notUsable = Plan(new SlittingSpec(GoldenLines));
        var label = Assert.Throws<ManufacturingRuleException>(() => notUsable.Approve(1, Approver, At,
            Known() with { Rolls = Rolls(KnownRoll() with { LabelUsable = false }) }));
        Assert.Equal((ManufacturingRuleCodes.PlanLabelNotUsable, SlittingApprovalGate.LabelStateParameter),
            (label.Code, Assert.Single(label.Parameters)));
    }

    [Theory]
    [InlineData(1500, 2000, "diameterFits")] // the 150 mm band winds to 1555.85… mm > 1500 mm
    [InlineData(5000, 3000, "metresSufficient")] // 3000 kg of the 150 mm band needs 2038.2 m > 1953.3 m walking
    public void A_winding_fit_that_computes_and_fails_is_refused_never_a_pass(int maximumOuterDiameterMm, int targetKg, string check)
    {
        var plan = Plan(new SlittingSpec(GoldenLines, new Kilograms(targetKg)));

        var refusal = Assert.Throws<ManufacturingRuleException>(() => plan.Approve(1, Approver, At,
            Known() with { MaximumOuterDiameterMm = maximumOuterDiameterMm }));

        Assert.Equal((ManufacturingRuleCodes.PlanWindingDoesNotFit, check), (refusal.Code, Assert.Single(refusal.Parameters)));
        Assert.Equal(PlanStatus.Draft, plan.Status);
    }

    [Fact]
    public void The_version_fence_and_the_draft_rule_come_before_the_gate()
    {
        var plan = Plan(new SlittingSpec(GoldenLines));
        Assert.Throws<ManufacturingVersionConflictException>(() => plan.Approve(7, Approver, At));
    }

    [Fact]
    public void A_stored_approval_is_restored_without_re_running_the_gate()
    {
        // A stored approval stands under the inputs it was approved with; restoring it needs no parameters.
        var restored = ProductionPlan.Restore(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(Roll, new Kilograms(28750m))], target: null, Approver, At, version: 2,
            spec: new SlittingSpec(GoldenLines));

        Assert.Equal((PlanStatus.Approved, 2L), (restored.Status, restored.Version));
        Assert.Equal(new SlittingSpec(GoldenLines), restored.Spec);
    }

    private static ProductionPlan Plan(SlittingSpec? spec) => new(Guid.NewGuid(), Company, WorkCenter,
        ManufacturingProcess.Slitting, [new PlannedInput(Roll, new Kilograms(28750m))], target: null, spec);

    private static SlittingRollParameters KnownRoll() => new(
        LabelUsable: true, NetUsableWidthMm: 1500m, ParentWidthMm: 1500m, ThicknessMm: 1.25m, DensityKgPerCubicMetre: 7850m);

    private static Dictionary<Guid, SlittingRollParameters> Rolls(SlittingRollParameters roll) => new() { [Roll] = roll };

    private static SlittingPlanParameters Known() => new(Rolls(KnownRoll()), LineSpeedMetresPerMinute: 120m,
        InnerDiameterMm: 508m, MaximumOuterDiameterMm: 1600m, ToleranceMetres: 0m);
}

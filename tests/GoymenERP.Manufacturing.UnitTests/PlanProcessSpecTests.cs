using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Engines;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// Üretim U2-b1 (API-12; URT-D25, URT-D26, URT-D27): the Tube / Profile, Paint and Cold Rolling values a plan enters
/// itself — validated per process, fed to the engines (the plan's value wins over the profile; an absent one stays named
/// missing), part of a Draft revision, captured at approval with source <c>Plan</c> and checked again on restore.
/// </summary>
public sealed class PlanProcessSpecTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkCenter = Guid.Parse("20000000-0000-0000-0000-00000000f001");
    private static readonly Guid Roll = Guid.Parse("30000000-0000-0000-0000-00000000f001");
    private static readonly Guid Approver = Guid.Parse("40000000-0000-0000-0000-00000000f002");
    private static readonly Guid Profile = Guid.Parse("50000000-0000-0000-0000-00000000f001");
    private static readonly DateOnly Today = new(2026, 11, 2);
    private static readonly DateTimeOffset At = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    private static PlanTechnicalParameterSnapshotV1 Coil => new(
        Guid.NewGuid(), "1", Guid.NewGuid(), "SAC", "r1", Guid.NewGuid(), "1", "T", "r1", Company, Guid.NewGuid(), "kg",
        Guid.NewGuid(), "1", Today, 2.000m, 1_000.000m);

    private static PlannedInput Input(decimal kg = 7_850m) => new(Roll, new Kilograms(kg), 1, Coil, null);

    private static PlanParameterSnapshotV1 Snapshot(ManufacturingProcess process,
        params (string Key, string Unit, decimal Value)[] values) =>
        new(Profile, "0", PlanParameterSnapshotV1.CatalogSource, process, null, new DateOnly(2026, 11, 1), null, Today,
            values.Select(value => new PlanParameterValueV1(value.Key, value.Unit, value.Value, null)));

    private static ProductionPlan Draft(ManufacturingProcess process, PlanProcessSpec? spec) =>
        new(Guid.NewGuid(), Company, WorkCenter, process, [Input()], processSpec: spec);

    private static PlanProcessSpec Tube(int pieces = 5) =>
        new(packageCount: 2, piecesPerPackage: pieces, pieceLengthMm: 6_000m, unitKg: 12.5m);

    private static PlanProcessSpec Paint() =>
        new(packageCount: 2, piecesPerPackage: 5, pieceLengthMm: 6_000m, perimeterMm: 100m,
            paintDescription: "  Polyester toz / Akzo  ", paintColorCode: "RAL 9016");

    [Fact]
    public void Each_value_is_positive_within_its_scale_and_a_spec_carries_at_least_one()
    {
        Assert.ThrowsAny<ArgumentException>(() => new PlanProcessSpec(packageCount: 0));
        Assert.ThrowsAny<ArgumentException>(() => new PlanProcessSpec(piecesPerPackage: -1));
        Assert.ThrowsAny<ArgumentException>(() => new PlanProcessSpec(pieceLengthMm: 0m));
        Assert.ThrowsAny<ArgumentException>(() => new PlanProcessSpec(pieceLengthMm: 1.0001m));
        Assert.ThrowsAny<ArgumentException>(() => new PlanProcessSpec(unitKg: 1.00001m));
        Assert.ThrowsAny<ArgumentException>(() => new PlanProcessSpec(paintDescription: "   "));
        Assert.ThrowsAny<ArgumentException>(() => new PlanProcessSpec(paintDescription: new string('x', 129)));
        Assert.ThrowsAny<ArgumentException>(() => new PlanProcessSpec(paintColorCode: new string('x', 33)));
        Assert.ThrowsAny<ArgumentException>(() => new PlanProcessSpec());

        Assert.Equal(1.2345m, new PlanProcessSpec(unitKg: 1.2345m).UnitKg);
        Assert.Equal("Polyester toz / Akzo", Paint().PaintDescription);
    }

    [Fact]
    public void A_spec_belongs_only_to_the_process_whose_values_it_carries()
    {
        Assert.Equal(Tube(), Draft(ManufacturingProcess.TubeProfile, Tube()).ProcessSpec);
        Assert.NotNull(Draft(ManufacturingProcess.Paint, Paint()).ProcessSpec);
        Assert.NotNull(Draft(ManufacturingProcess.ColdRolling, new PlanProcessSpec(targetThicknessMm: 1.5m)).ProcessSpec);

        // Another process's value, any spec on Slitting / Cut to Length, and a Paint spec without its paint are refused.
        Assert.Throws<ArgumentException>(() => Draft(ManufacturingProcess.TubeProfile, Paint()));
        Assert.Throws<ArgumentException>(() => Draft(ManufacturingProcess.Paint, Tube()));
        Assert.Throws<ArgumentException>(() => Draft(ManufacturingProcess.ColdRolling, Tube()));
        Assert.Throws<ArgumentException>(() => Draft(ManufacturingProcess.TubeProfile,
            new PlanProcessSpec(targetThicknessMm: 1.5m)));
        Assert.Throws<ArgumentException>(() => Draft(ManufacturingProcess.Slitting, new PlanProcessSpec(packageCount: 1)));
        Assert.Throws<ArgumentException>(() => Draft(ManufacturingProcess.CutToLength, new PlanProcessSpec(packageCount: 1)));
        Assert.Throws<ArgumentException>(() => Draft(ManufacturingProcess.Paint, new PlanProcessSpec(packageCount: 1)));
    }

    [Fact]
    public void The_engines_read_the_plan_values_and_still_name_what_is_absent()
    {
        // Tube / Profile: 2 × 5 pieces × 6 m = 60 m at the profile's 12 m/min = 5 minutes; 10 pieces × 12.5 kg = 125 kg.
        var tube = Draft(ManufacturingProcess.TubeProfile, Tube());
        var tubeCalculation = ProductionPlanCalculatorV1.Calculate(tube, PlanCalculationParameters.FromCapturedSnapshots(
            tube.Inputs, Snapshot(ManufacturingProcess.TubeProfile, ("lineSpeedMetresPerMinute", "m/min", 12m))));
        var run = tubeCalculation.PlanAnswers.Single(answer => answer.Formula == "TUB-001…003");
        Assert.Equal(EngineOutcome.Computed, run.Outcome);
        Assert.Equal(5m, run.Values.Single(value => value.Name == "runMinutes").Value);
        var mass = tubeCalculation.PlanAnswers.Single(answer => answer.Formula == "TUB-004");
        Assert.Equal(125m, mass.Values.Single(value => value.Name == "totalKg").Value);

        // Paint: the piece length is the engine's profile length. 10 pieces × 6 m over 3 profiles at 20 m/min = 1 minute;
        // 10 pieces × 0.1 m × 6 m = 6 m².
        var paint = Draft(ManufacturingProcess.Paint, Paint());
        var paintCalculation = ProductionPlanCalculatorV1.Calculate(paint, PlanCalculationParameters.FromCapturedSnapshots(
            paint.Inputs, Snapshot(ManufacturingProcess.Paint, ("simultaneousProfiles", "count", 3m),
                ("lineSpeedMetresPerMinute", "m/min", 20m))));
        var paintRun = paintCalculation.PlanAnswers.Single(answer => answer.Formula == "PNT-001…004");
        Assert.Equal(1m, paintRun.Values.Single(value => value.Name == "minutes").Value);
        var area = paintCalculation.PlanAnswers.Single(answer => answer.Formula == "PNT-005");
        Assert.Equal(EngineOutcome.Computed, area.Outcome);
        Assert.Equal(6m, area.Values.Single(value => value.Name == "areaSquareMetres").Value);
        Assert.DoesNotContain("profileLengthMm", paintCalculation.MissingParameters);
        // Without the profile the run (and so the area) names the line values the plan does not carry.
        Assert.Contains("lineSpeedMetresPerMinute", ProductionPlanCalculatorV1.Calculate(paint).MissingParameters);

        // Cold Rolling: the target thickness is known; nothing defaults what the plan and the profile do not carry.
        var cold = Draft(ManufacturingProcess.ColdRolling, new PlanProcessSpec(targetThicknessMm: 1.5m));
        var coldCalculation = ProductionPlanCalculatorV1.Calculate(cold);
        Assert.DoesNotContain("targetThicknessMm", coldCalculation.MissingParameters);
        Assert.Contains("targetThicknessMm",
            ProductionPlanCalculatorV1.Calculate(Draft(ManufacturingProcess.ColdRolling, null)).MissingParameters);
    }

    [Fact]
    public void The_spec_is_part_of_a_revision()
    {
        var tube = Draft(ManufacturingProcess.TubeProfile, Tube());

        tube.Revise(1, WorkCenter, ManufacturingProcess.TubeProfile, [Input()], null, processSpec: Tube(pieces: 6));

        Assert.Equal(6, tube.ProcessSpec!.PiecesPerPackage);
        Assert.Equal(ManufacturingRuleCodes.PlanRevisionNoChange, Assert.Throws<ManufacturingRuleException>(
            () => tube.Revise(2, WorkCenter, ManufacturingProcess.TubeProfile, [Input()], null,
                processSpec: Tube(pieces: 6))).Code);
        // Removing the spec is a change too; a value of another process is refused.
        tube.Revise(2, WorkCenter, ManufacturingProcess.TubeProfile, [Input()], null);
        Assert.Null(tube.ProcessSpec);
        Assert.Throws<ArgumentException>(() => tube.Revise(3, WorkCenter, ManufacturingProcess.TubeProfile, [Input()],
            null, processSpec: Paint()));
    }

    [Fact]
    public void Approval_captures_each_plan_value_with_source_plan_keeping_the_profiles_unit_and_restore_checks_it()
    {
        var plan = Draft(ManufacturingProcess.Paint, Paint());
        var profile = Snapshot(ManufacturingProcess.Paint, ("filmMicrons", "µm", 80m), ("profileLengthMm", "mm", 5_000m),
            ("perimeterMm", "millimetre", 90m));

        plan.Approve(1, Approver, At, parameters: null, profile);

        var values = plan.ParameterSnapshot!.Values.ToDictionary(value => value.Key);
        Assert.Null(values["filmMicrons"].Source);
        Assert.Equal(new PlanParameterValueV1("profileLengthMm", "mm", 6_000m, null, PlanParameterValueV1.PlanSource),
            values["profileLengthMm"]);
        // The replaced value keeps the profile's unit for that key; an added one takes the plan's.
        Assert.Equal("millimetre", values["perimeterMm"].Unit);
        Assert.Equal(100m, values["perimeterMm"].Number);
        Assert.Equal(new PlanParameterValueV1("packageCount", "count", 2m, null, PlanParameterValueV1.PlanSource),
            values["packageCount"]);
        Assert.False(values.ContainsKey("pieceLengthMm"));

        var restored = ProductionPlan.Restore(plan.Id, Company, WorkCenter, ManufacturingProcess.Paint, plan.Inputs,
            null, Approver, At, 2, parameterSnapshot: plan.ParameterSnapshot, processSpec: Paint());
        Assert.Equal(plan.ParameterSnapshot, restored.ParameterSnapshot);
        Assert.Equal(ManufacturingRuleCodes.StoredRecordInvalid, Assert.Throws<ManufacturingRuleException>(
            () => ProductionPlan.Restore(plan.Id, Company, WorkCenter, ManufacturingProcess.Paint, plan.Inputs, null,
                Approver, At, 2, parameterSnapshot: plan.ParameterSnapshot,
                processSpec: new PlanProcessSpec(packageCount: 3, piecesPerPackage: 5, pieceLengthMm: 6_000m,
                    perimeterMm: 100m, paintDescription: "Polyester toz / Akzo", paintColorCode: "RAL 9016"))).Code);
        Assert.Equal(ManufacturingRuleCodes.StoredRecordInvalid, Assert.Throws<ManufacturingRuleException>(
            () => ProductionPlan.Restore(plan.Id, Company, WorkCenter, ManufacturingProcess.Paint, plan.Inputs, null,
                Approver, At, 2, parameterSnapshot: plan.ParameterSnapshot)).Code);
    }
}

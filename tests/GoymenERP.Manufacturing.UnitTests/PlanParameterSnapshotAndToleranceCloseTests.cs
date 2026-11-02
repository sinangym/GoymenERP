using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Engines;
using GoymenERP.Modules.Manufacturing.Domain.Engines.Balance;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// Üretim U1-b (URT-D5, D6, D7, D9, D23; DEC-PRDCT-008): the Settings parameter profile feeds the one plan calculator by the
/// engines' own names, is captured at approval and never refreshed; the close applies BOTH the fire maximum and the ± mass
/// balance tolerance of the supplied snapshot (catalog version 0 or a published version), with the unexplained difference
/// kept as its own category.
/// </summary>
public sealed class PlanParameterSnapshotAndToleranceCloseTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkCenter = Guid.Parse("20000000-0000-0000-0000-00000000d001");
    private static readonly Guid OtherWorkCenter = Guid.Parse("20000000-0000-0000-0000-00000000d002");
    private static readonly Guid Roll = Guid.Parse("30000000-0000-0000-0000-00000000d001");
    private static readonly Guid Planner = Guid.Parse("40000000-0000-0000-0000-00000000d001");
    private static readonly Guid Closer = Guid.Parse("40000000-0000-0000-0000-00000000d002");
    private static readonly Guid Profile = Guid.Parse("50000000-0000-0000-0000-00000000d001");
    private static readonly DateOnly Today = new(2026, 11, 2);
    private static readonly DateTimeOffset CloseAt = new(2026, 11, 2, 15, 0, 0, TimeSpan.Zero);

    private static Kilograms Kg(decimal value) => new(value);

    private static PlanParameterSnapshotV1 Snapshot(ManufacturingProcess process, string version = "0",
        string source = PlanParameterSnapshotV1.CatalogSource, Guid? workCenter = null,
        params (string Key, string Unit, decimal Value)[] values) =>
        new(Profile, version, source, process, workCenter, new DateOnly(2026, 11, 1), null, Today,
            values.Select(value => new PlanParameterValueV1(value.Key, value.Unit, value.Value, null)));

    private static PlanTechnicalParameterSnapshotV1 Technical(decimal thicknessMm, decimal widthMm) => new(
        Guid.NewGuid(), "1", Guid.NewGuid(), "SAC", "r1", Guid.NewGuid(), "1", "T", "r1", Company, Guid.NewGuid(), "kg",
        Guid.NewGuid(), "1", Today, thicknessMm, widthMm);

    private static PlannedInput Input(decimal kg, PlanTechnicalParameterSnapshotV1? technical = null) =>
        new(Roll, Kg(kg), 1, technical, null);

    private static PlanFormulaAnswer Answer(PlanCalculation calculation, string formula) =>
        calculation.PlanAnswers.Concat(calculation.Rolls.SelectMany(roll => roll.Answers))
            .Single(answer => answer.Formula == formula);

    [Fact]
    public void Paint_profile_values_feed_the_painting_engines_by_their_own_names()
    {
        var profile = Snapshot(ManufacturingProcess.Paint, values:
        [
            ("lineSpeedMetresPerMinute", "m/min", 20m), ("filmMicrons", "um", 80m), ("solidsRatio", "ratio", 0.55m),
            ("scrapFactor", "factor", 1.10m), ("simultaneousProfiles", "count", 3m),
        ]);
        var inputs = new[] { Input(1_000m) };

        var without = ProductionPlanCalculatorV1.Calculate(ManufacturingProcess.Paint, inputs, null, null,
            PlanCalculationParameters.FromCapturedSnapshots(inputs));
        var with = ProductionPlanCalculatorV1.Calculate(ManufacturingProcess.Paint, inputs, null, null,
            PlanCalculationParameters.FromCapturedSnapshots(inputs, profile));

        Assert.Contains("lineSpeedMetresPerMinute", without.MissingParameters);
        Assert.Contains("filmMicrons", without.MissingParameters);
        // The profile names nothing it supplies; the plan's own run inputs stay named (never defaulted).
        Assert.DoesNotContain("lineSpeedMetresPerMinute", with.MissingParameters);
        Assert.DoesNotContain("simultaneousProfiles", with.MissingParameters);
        Assert.DoesNotContain("filmMicrons", with.MissingParameters);
        Assert.DoesNotContain("solidsRatio", with.MissingParameters);
        Assert.DoesNotContain("scrapFactor", with.MissingParameters);
        Assert.Contains("packageCount", with.MissingParameters);
        Assert.Contains("perimeterMm", with.MissingParameters);
    }

    [Fact]
    public void Density_from_the_profile_and_dimensions_from_Product_compute_cut_to_length_and_cold_rolling()
    {
        var technical = Technical(1.000m, 1_000.000m);
        var inputs = new[] { Input(7_850m, technical) };
        var cut = ProductionPlanCalculatorV1.Calculate(ManufacturingProcess.CutToLength, inputs, null, null,
            new CutToLengthSpec([new CutToLengthLine(Guid.NewGuid(), 1, 1_000m, 10)]),
            PlanCalculationParameters.FromCapturedSnapshots(inputs, Snapshot(ManufacturingProcess.CutToLength,
                values: [("densityKgPerCubicMetre", "kg/m3", 7_850m), ("kerfMm", "mm", 0m)])));
        var cutAnswer = Answer(cut, "CUT-001…006");
        Assert.Equal(EngineOutcome.Computed, cutAnswer.Outcome);
        Assert.Equal(1_000m, cutAnswer.Values.Single(value => value.Name == "inputMetres").Value);

        var rolling = ProductionPlanCalculatorV1.Calculate(ManufacturingProcess.ColdRolling, inputs, null, null,
            PlanCalculationParameters.FromCapturedSnapshots(inputs, Snapshot(ManufacturingProcess.ColdRolling,
                values: [("densityKgPerCubicMetre", "kg/m3", 7_850m)])));
        // Density and Product's dimensions are known; the plan's own target thickness and scrap stay named.
        Assert.Equal(["scrapKg", "targetThicknessMm"],
            Answer(rolling, "CR-001…005").MissingParameters.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Slitting_gate_passes_when_the_captured_values_supply_every_engine_input()
    {
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
            [Input(10_000m, Technical(1.000m, 1_250.000m))], null, new SlittingSpec([new SlitLine(250m, 4)]));
        var profile = Snapshot(ManufacturingProcess.Slitting, "3", PlanParameterSnapshotV1.SettingsSource, WorkCenter,
            ("densityKgPerCubicMetre", "kg/m3", 7_850m), ("lineSpeedMetresPerMinute", "m/min", 60m));

        var captured = SlittingPlanParameters.FromCaptured(plan.Inputs, profile, new Dictionary<Guid, bool> { [Roll] = true });
        var missing = Assert.Throws<ManufacturingRuleException>(() => SlittingApprovalGate.Require(plan, captured));
        // Only the net usable width (entered on the plan, not built yet) is missing; nothing else is defaulted.
        Assert.Equal(ManufacturingRuleCodes.PlanParametersUnavailable, missing.Code);
        Assert.Equal(["netUsableWidthMm"], missing.Parameters);

        var withWidth = captured with
        {
            Rolls = captured.Rolls!.ToDictionary(pair => pair.Key,
                pair => pair.Value with { NetUsableWidthMm = 1_230m }),
        };
        SlittingApprovalGate.Require(plan, withWidth);
        plan.Approve(plan.Version, Planner, CloseAt, withWidth, profile);
        Assert.Equal((PlanStatus.Approved, profile), (plan.Status, plan.ParameterSnapshot));
    }

    [Fact]
    public void A_captured_snapshot_is_never_refreshed_by_a_newer_profile()
    {
        var inputs = new[] { Input(1_000m) };
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Paint, inputs);
        var captured = Snapshot(ManufacturingProcess.Paint, values: [("filmMicrons", "um", 80m)]);
        plan.Approve(plan.Version, Planner, CloseAt, null, captured);
        var newer = Snapshot(ManufacturingProcess.Paint, "1", PlanParameterSnapshotV1.SettingsSource,
            values: [("filmMicrons", "um", 120m)]);

        var restored = ProductionPlan.Restore(plan.Id, Company, WorkCenter, ManufacturingProcess.Paint, inputs, null,
            Planner, CloseAt, plan.Version, parameterSnapshot: captured);

        Assert.Equal(80m, restored.ParameterSnapshot!.Numbers()["filmMicrons"]);
        Assert.NotEqual(newer, restored.ParameterSnapshot);
        // A snapshot belongs only to an approved plan of the same process.
        Assert.Equal(ManufacturingRuleCodes.StoredRecordInvalid, Assert.Throws<ManufacturingRuleException>(() =>
            ProductionPlan.Restore(plan.Id, Company, WorkCenter, ManufacturingProcess.Paint, inputs, null, null, null, 1,
                parameterSnapshot: captured)).Code);
        Assert.Throws<ArgumentException>(() => new ProductionPlan(Guid.NewGuid(), Company, WorkCenter,
                ManufacturingProcess.Slitting, inputs)
            .Approve(1, Planner, CloseAt, null, captured));
    }

    [Theory]
    [InlineData("Catalog", "1")]
    [InlineData("Settings", "0")]
    [InlineData("Other", "1")]
    [InlineData("Settings", "1a")]
    public void A_snapshot_refuses_a_source_and_version_that_do_not_belong_together(string source, string version) =>
        Assert.Throws<ArgumentException>(() => Snapshot(ManufacturingProcess.Paint, version, source,
            values: [("filmMicrons", "um", 80m)]));

    private static ClosureTolerancePolicySnapshotV1 Catalog(decimal fire = 5m, decimal? massBalance = 2m,
        Guid? workCenter = null) =>
        new(Company, Profile, 0, ManufacturingProcess.Slitting, fire, true, new DateOnly(2026, 11, 1), null, Today,
            massBalance, ClosureTolerancePolicySnapshotV1.CatalogSource, workCenter);

    private static ClosureTolerancePolicySnapshotV1 Published(long version, decimal fire, decimal? massBalance,
        Guid? workCenter = null) =>
        new(Company, Guid.NewGuid(), version, ManufacturingProcess.Slitting, fire, true, new DateOnly(2026, 11, 2), null,
            Today, massBalance, ClosureTolerancePolicySnapshotV1.SettingsSource, workCenter);

    private static ProductionOrder Order()
    {
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(Roll, Kg(10_000m))]);
        plan.Approve(plan.Version, Planner, DateTimeOffset.UnixEpoch);
        return ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
    }

    /// <summary>1000 kg issued, 30 kg scrap (3 % fire), the rest good except <paramref name="unexplainedKg"/>.</summary>
    private static ProductionOrder Realized(decimal unexplainedKg, decimal scrapKg = 30m)
    {
        var order = Order();
        order.Issue(Roll, Kg(1_000m));
        order.Declare(Roll, OutputClass.Scrap, Kg(scrapKg));
        order.Declare(Roll, OutputClass.GoodProduct, Kg(1_000m - scrapKg - unexplainedKg));
        return order;
    }

    [Theory]
    [InlineData(0, ClosureDispositionV1.Balanced)]
    [InlineData(20, ClosureDispositionV1.Balanced)] // + 2 % exactly: inside (inclusive bound)
    [InlineData(20.001, ClosureDispositionV1.ApprovalRequired)]
    public void Catalog_version_zero_accepts_a_difference_within_two_percent_and_routes_the_rest_to_approval(
        double unexplained, ClosureDispositionV1 expected)
    {
        var order = Realized((decimal)unexplained);
        var assessment = order.AssessClosure(Catalog());

        Assert.Equal(expected, assessment.Disposition);
        var roll = Assert.Single(assessment.Rolls);
        // The unexplained difference stays its own category: never folded into fire.
        Assert.Equal((decimal)unexplained, roll.DifferenceKg);
        Assert.Equal(3m, roll.FirePercent);
        if (expected == ClosureDispositionV1.ApprovalRequired)
        {
            Assert.Equal(ClosureAssessmentCodesV1.MassDifferenceOutsideTolerance, roll.Code);
        }
    }

    [Fact]
    public void A_negative_difference_is_measured_against_the_same_plus_minus_tolerance()
    {
        var order = Order();
        order.Issue(Roll, Kg(1_000m));
        order.Declare(Roll, OutputClass.Scrap, Kg(10m));
        order.Declare(Roll, OutputClass.GoodProduct, Kg(1_005m));

        var within = order.AssessClosure(Catalog());
        Assert.Equal((ClosureDispositionV1.Balanced, -15m, -1.5m),
            (within.Disposition, within.Rolls[0].DifferenceKg, within.Rolls[0].DifferencePercent));
        var tight = order.AssessClosure(Catalog(massBalance: 1m));
        Assert.Equal(ClosureDispositionV1.ApprovalRequired, tight.Disposition);
    }

    [Fact]
    public void Fire_above_the_maximum_needs_approval_even_inside_the_mass_tolerance()
    {
        var order = Realized(10m, scrapKg: 60m);
        var assessment = order.AssessClosure(Catalog());

        Assert.Equal(ClosureDispositionV1.ApprovalRequired, assessment.Disposition);
        Assert.Equal((6m, ClosureAssessmentCodesV1.FireAboveMaximum),
            (assessment.Rolls[0].FirePercent, assessment.Rolls[0].Code));
        Assert.Equal(ClosureDispositionV1.Balanced, order.AssessClosure(Catalog(fire: 6m)).Disposition);
    }

    [Fact]
    public void A_published_line_version_decides_with_its_own_values_and_is_stored_on_close()
    {
        var order = Realized(15m);
        var published = Published(2, 5m, 1m, WorkCenter);

        Assert.Equal(ClosureDispositionV1.ApprovalRequired, order.AssessClosure(published).Disposition);
        Assert.Equal(ClosureDispositionV1.Balanced, order.AssessClosure(Catalog()).Disposition);
        Assert.Equal((ClosureDispositionV1.Unavailable, ClosureAssessmentCodesV1.PolicyScopeMismatch, "workCenterId"),
            (order.AssessClosure(Published(2, 5m, 2m, OtherWorkCenter)) is var other
                ? (other.Disposition, other.Code, other.RefusedParameter)
                : default));

        var approval = ClosureExceptionApprovalV1.Create(Company, order.Id, Closer, "Teyitli hat sapması", order.Version,
            published.PolicyId, published.PolicyVersion, CloseAt);
        order.CloseWithPolicy(order.Version, Closer, CloseAt, published, approval);
        Assert.Equal((OrderStatus.Closed, published, approval), (order.Status, order.ClosurePolicy, order.ClosureExceptionApproval));
    }

    [Fact]
    public void Catalog_close_within_tolerance_needs_no_approval_and_accepts_version_zero_evidence()
    {
        var order = Realized(5m);
        var catalog = Catalog();
        order.CloseWithPolicy(order.Version, Closer, CloseAt, catalog, null);
        Assert.Equal(OrderStatus.Closed, order.Status);

        var outside = Realized(50m);
        var approval = ClosureExceptionApprovalV1.Create(Company, outside.Id, Closer, "Teyitli", outside.Version,
            catalog.PolicyId, 0, CloseAt);
        Assert.Equal(ClosureAssessmentCodesV1.ApprovalRequired, Assert.Throws<ManufacturingRuleException>(
            () => outside.CloseWithPolicy(outside.Version, Closer, CloseAt, catalog, null)).Code);
        outside.CloseWithPolicy(outside.Version, Closer, CloseAt, catalog, approval);
        Assert.Equal(OrderStatus.Closed, outside.Status);
    }

    [Fact]
    public void An_unknown_mass_tolerance_keeps_the_earlier_fail_closed_answers()
    {
        Assert.Equal(ClosureDispositionV1.FireDeclarationRequired,
            Realized(5m).AssessClosure(Published(1, 5m, null)).Disposition);
    }

    [Theory]
    [InlineData("Catalog", 1L, "policyVersion")]
    [InlineData("Settings", 0L, "policyVersion")]
    [InlineData("Elsewhere", 1L, "source")]
    public void Source_and_version_must_belong_together(string source, long version, string parameter)
    {
        var policy = Catalog() with { Source = source, PolicyVersion = version };
        var assessment = Realized(0m).AssessClosure(policy);
        Assert.Equal((ClosureDispositionV1.Refused, ClosureAssessmentCodesV1.PolicyInvalid, parameter),
            (assessment.Disposition, assessment.Code, assessment.RefusedParameter));
    }

    [Fact]
    public void The_difference_percent_is_one_signed_division_over_the_actual_input()
    {
        var result = MassBalanceEngineV1.DifferencePercent(new MassDifferencePercentInputV1(-20m, 1_000m));
        Assert.Equal(-2m, result.Value!.DifferencePercent);
        Assert.Equal(["actualInputKg"],
            MassBalanceEngineV1.DifferencePercent(new MassDifferencePercentInputV1(1m, null)).MissingParameters);
    }
}

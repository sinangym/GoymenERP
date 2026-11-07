using GoymenERP.Modules.Manufacturing.Domain.Engines;
using GoymenERP.Modules.Manufacturing.Domain.Engines.Winding;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests.Engines;

public sealed class WindingSectionPlannerV1Tests
{
    [Fact]
    public void Plans_golden_40_tonnes_by_the_smallest_selected_limit_and_keeps_the_remainder()
    {
        var result = WindingSectionPlannerV1.Plan(new WindingSectionPlanInputV1(
            TotalPlannedKg: 40000m,
            TargetKg: 15000m,
            DiameterCapacityKg: 18421.550123m,
            MachineLimitKg: 16000m,
            HandlingLimitKg: 14000m));

        Assert.Equal(EngineOutcome.Computed, result.Outcome);
        Assert.Equal(WindingSectionPlannerV1.FormulaVersion, result.Value!.FormulaVersion);
        Assert.Equal(14000m, result.Value.EffectiveSectionLimitKg);
        Assert.Equal([14000m, 14000m, 12000m], result.Value.Sections.Select(section => section.PlannedKg));
        Assert.Equal([0, 1, 2], result.Value.Sections.Select(section => section.SourceOrdinal));
        Assert.Equal([false, false, true], result.Value.Sections.Select(section => section.IsPositiveRemainder));
        Assert.Equal(result.Value.TotalPlannedKg, result.Value.Sections.Sum(section => section.PlannedKg));
    }

    [Fact]
    public void Uses_diameter_capacity_when_it_is_the_smallest_limit_without_rounding_it()
    {
        var result = WindingSectionPlannerV1.Plan(new WindingSectionPlanInputV1(
            10m, 8m, 3.3333333333333333333333333333m, 9m, 7m));

        Assert.Equal(EngineOutcome.Computed, result.Outcome);
        Assert.Equal(3.3333333333333333333333333333m, result.Value!.EffectiveSectionLimitKg);
        Assert.Equal(4, result.Value.Sections.Count);
        Assert.Equal(0.0000000000000000000000000001m, result.Value.Sections[3].PlannedKg);
        Assert.True(result.Value.Sections[3].IsPositiveRemainder);
        Assert.Equal(10m, result.Value.Sections.Sum(section => section.PlannedKg));
    }

    [Theory]
    [InlineData(12, 20, 30, 40, 12)]
    [InlineData(30, 20, 12, 40, 12)]
    public void Selects_target_and_machine_limits_when_each_is_the_smallest(
        int target, int diameter, int machine, int handling, int expected)
    {
        var result = WindingSectionPlannerV1.Plan(new WindingSectionPlanInputV1(
            25m, target, diameter, machine, handling));

        Assert.Equal(EngineOutcome.Computed, result.Outcome);
        Assert.Equal(expected, result.Value!.EffectiveSectionLimitKg);
        Assert.Equal([expected, expected, 1m], result.Value.Sections.Select(section => section.PlannedKg));
        Assert.Equal(25m, result.Value.Sections.Sum(section => section.PlannedKg));
    }

    [Fact]
    public void Exact_division_does_not_create_a_zero_remainder_section()
    {
        var result = WindingSectionPlannerV1.Plan(new WindingSectionPlanInputV1(42m, 14m, 20m, 16m, 14m));

        Assert.Equal(EngineOutcome.Computed, result.Outcome);
        Assert.Equal([14m, 14m, 14m], result.Value!.Sections.Select(section => section.PlannedKg));
        Assert.All(result.Value.Sections, section => Assert.False(section.IsPositiveRemainder));
    }

    [Theory]
    [InlineData("totalPlannedKg")]
    [InlineData("targetKg")]
    [InlineData("diameterCapacityKg")]
    [InlineData("machineLimitKg")]
    [InlineData("handlingLimitKg")]
    public void A_missing_selected_value_is_unavailable_not_zero_or_unlimited(string missingParameter)
    {
        decimal? total = 40m;
        decimal? target = 15m;
        decimal? diameter = 18m;
        decimal? machine = 16m;
        decimal? handling = 14m;
        switch (missingParameter)
        {
            case "totalPlannedKg": total = null; break;
            case "targetKg": target = null; break;
            case "diameterCapacityKg": diameter = null; break;
            case "machineLimitKg": machine = null; break;
            case "handlingLimitKg": handling = null; break;
        }

        var result = WindingSectionPlannerV1.Plan(
            new WindingSectionPlanInputV1(total, target, diameter, machine, handling));

        Assert.Equal(EngineOutcome.Unavailable, result.Outcome);
        Assert.Equal([missingParameter], result.MissingParameters);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData("totalPlannedKg", 0)]
    [InlineData("targetKg", -1)]
    [InlineData("diameterCapacityKg", 0)]
    [InlineData("machineLimitKg", -1)]
    [InlineData("handlingLimitKg", 0)]
    public void A_zero_or_negative_value_is_refused(string parameter, int supplied)
    {
        decimal? total = 40m;
        decimal? target = 15m;
        decimal? diameter = 18m;
        decimal? machine = 16m;
        decimal? handling = 14m;
        switch (parameter)
        {
            case "totalPlannedKg": total = supplied; break;
            case "targetKg": target = supplied; break;
            case "diameterCapacityKg": diameter = supplied; break;
            case "machineLimitKg": machine = supplied; break;
            case "handlingLimitKg": handling = supplied; break;
        }

        var result = WindingSectionPlannerV1.Plan(
            new WindingSectionPlanInputV1(total, target, diameter, machine, handling));

        Assert.Equal(EngineOutcome.Refused, result.Outcome);
        Assert.Equal(EngineCodes.ValueNotPositive, result.RefusalCode);
        Assert.Equal(parameter, result.RefusedParameter);
    }

    [Theory]
    [InlineData("totalPlannedKg")]
    [InlineData("targetKg")]
    [InlineData("machineLimitKg")]
    [InlineData("handlingLimitKg")]
    public void A_nominal_kg_with_more_than_three_decimals_is_refused_without_rounding(string parameter)
    {
        decimal? total = 40m;
        decimal? target = 15m;
        decimal? diameter = 18.123456m;
        decimal? machine = 16m;
        decimal? handling = 14m;
        switch (parameter)
        {
            case "totalPlannedKg": total = 40.0001m; break;
            case "targetKg": target = 15.0001m; break;
            case "machineLimitKg": machine = 16.0001m; break;
            case "handlingLimitKg": handling = 14.0001m; break;
        }

        var result = WindingSectionPlannerV1.Plan(
            new WindingSectionPlanInputV1(total, target, diameter, machine, handling));

        Assert.Equal(EngineOutcome.Refused, result.Outcome);
        Assert.Equal(EngineCodes.PrecisionExceeded, result.RefusalCode);
        Assert.Equal(parameter, result.RefusedParameter);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Refuses_a_result_that_cannot_be_addressed_without_an_arbitrary_coil_cap()
    {
        var result = WindingSectionPlannerV1.Plan(new WindingSectionPlanInputV1(
            decimal.MaxValue, 1m, 0.0000000000000000000000000001m, 1m, 1m));

        Assert.Equal(EngineOutcome.Refused, result.Outcome);
        Assert.Equal(WindingSectionPlannerV1.SectionCountOverflowCode, result.RefusalCode);
        Assert.Equal("totalPlannedKg", result.RefusedParameter);
    }

    [Fact]
    public void Stored_spec_preserves_source_order_and_plan_identity_without_creating_inventory_labels()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var spec = new WindingSectionPlanSpecV1(
        [
            new WindingSectionPlanLineV1(firstId, 14000m),
            new WindingSectionPlanLineV1(secondId, 12000m),
        ]);

        Assert.Equal([firstId, secondId], spec.Sections.Select(section => section.PlanWindingSectionId));
        Assert.Equal([14000m, 12000m], spec.Sections.Select(section => section.PlannedKg));
        Assert.DoesNotContain(spec.Sections[0].GetType().GetProperties(), property =>
            property.Name.Contains("Label", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Repeating_the_same_input_and_caller_ids_preserves_the_same_values_order_and_identity()
    {
        var input = new WindingSectionPlanInputV1(40000m, 15000m, 18421.550123m, 16000m, 14000m);
        var firstPlan = WindingSectionPlannerV1.Plan(input).Value!;
        var secondPlan = WindingSectionPlannerV1.Plan(input).Value!;
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        var firstSpec = new WindingSectionPlanSpecV1(firstPlan.Sections.Select((section, index) =>
            new WindingSectionPlanLineV1(ids[index], section.PlannedKg)));
        var secondSpec = new WindingSectionPlanSpecV1(secondPlan.Sections.Select((section, index) =>
            new WindingSectionPlanLineV1(ids[index], section.PlannedKg)));

        Assert.Equal(firstPlan.EffectiveSectionLimitKg, secondPlan.EffectiveSectionLimitKg);
        Assert.Equal(firstPlan.Sections, secondPlan.Sections);
        Assert.Equal(firstSpec.Sections, secondSpec.Sections);
    }

    [Fact]
    public void Stored_spec_rejects_empty_or_duplicate_plan_section_identity()
    {
        var duplicate = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => new WindingSectionPlanLineV1(Guid.Empty, 1m));
        Assert.Throws<ArgumentException>(() => new WindingSectionPlanSpecV1([]));
        Assert.Throws<ArgumentException>(() => new WindingSectionPlanSpecV1(
        [
            new WindingSectionPlanLineV1(duplicate, 1m),
            new WindingSectionPlanLineV1(duplicate, 2m),
        ]));
    }

    [Fact]
    public void Existing_winding_v1_remains_the_source_of_ideal_geometry()
    {
        var target = WindingEngineV1.Target(new WindingTargetInputV1(7850m, 3m, 1500m, 15000m, 508m));

        Assert.Equal(EngineOutcome.Computed, target.Outcome);
        Assert.Equal("GDC-WND/1 (master v3 §28.5)", target.Value!.FormulaVersion);
        Assert.Equal(35.325m, target.Value.KgPerMetre);
    }
}

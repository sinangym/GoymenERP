using GoymenERP.Modules.Manufacturing.Domain.Engines;
using GoymenERP.Modules.Manufacturing.Domain.Engines.Slitting;
using GoymenERP.Modules.Manufacturing.Domain.Engines.Winding;

namespace GoymenERP.Manufacturing.UnitTests.Engines;

/// <summary>
/// The master v3 golden examples (§28.4 Dilme, §28.5 Bobin Sarım), each asserted twice:
/// <list type="bullet">
/// <item>against the master's printed value, to the precision the master prints it (three decimals, so
/// within 0.0005). This is not a rounding policy: the engine rounds nothing, the master prints three
/// decimals;</item>
/// <item>against an independent 40-digit reference (Python <c>decimal</c>, prec 40), within 1e-18, so a
/// formula change the master's three decimals could hide still fails.</item>
/// </list>
/// The density 7850 kg/m³ is the TEST INPUT the master's examples imply (MAT-001), not a production
/// parameter: the engine has no built-in density (OPEN-007).
/// </summary>
public sealed class SlittingWindingGoldenTests
{
    private const decimal MasterPrecision = 0.0005m;
    private const decimal ReferencePrecision = 0.000000000000000001m;
    private const decimal ExampleDensity = 7850m;

    [Fact]
    public void Slitting_master_golden_28_750_kg_on_1500_mm_as_150x4_and_240x3()
    {
        var result = SlittingEngineV1.Plan(new SlittingPlanInputV1(28750m, 1500m,
            [new SlitLineInputV1(150m, 4), new SlitLineInputV1(240m, 3)]));

        Assert.Equal(EngineOutcome.Computed, result.Outcome);
        var plan = result.Value!;
        Assert.Equal(SlittingEngineV1.FormulaVersion, plan.FormulaVersion);
        Assert.Equal([600m, 720m], plan.Lines.Select(line => line.UsedWidthMm)); // SLT-002
        Assert.Equal(1320m, plan.TotalUsedWidthMm); // SLT-004
        Assert.Equal(180m, plan.RemainingWidthMm); // SLT-005
        Near(25300m, plan.PlanKg, MasterPrecision); // SLT-006
        Near(3450m, plan.RemainingKg, MasterPrecision); // SLT-007
        Near(19.166666666666666666666666667m, plan.KgPerMillimetreWidth, ReferencePrecision); // SLT-001
        Near(25300m, plan.PlanKg, ReferencePrecision);
        Near(3450m, plan.RemainingKg, ReferencePrecision);
    }

    [Fact]
    public void Line_time_master_golden_13_400_kg_1_25x1240_at_120_m_per_minute()
    {
        var result = SlittingEngineV1.LineTime(new LineTimeInputV1(13400m, 1.25m, 1240m, ExampleDensity, 120m));

        Assert.Equal(EngineOutcome.Computed, result.Outcome);
        var time = result.Value!;
        Assert.Equal(12.1675m, time.KgPerMetre);
        Near(1101.294m, time.WalkingMetres, MasterPrecision);
        Near(9.177m, time.Minutes, MasterPrecision);
        Near(1101.294431888226833778508321m, time.WalkingMetres, ReferencePrecision);
        Near(9.177453599068556948154236011m, time.Minutes, ReferencePrecision);
    }

    [Fact]
    public void Winding_master_golden_3x1500_to_15_000_kg_on_a_508_mm_core()
    {
        var result = WindingEngineV1.Target(new WindingTargetInputV1(ExampleDensity, 3m, 1500m, 15000m, 508m));

        Assert.Equal(EngineOutcome.Computed, result.Outcome);
        var winding = result.Value!;
        Assert.Equal(WindingEngineV1.FormulaVersion, winding.FormulaVersion);
        Assert.Equal(35.325m, winding.KgPerMetre); // WND-001
        Near(424.628m, winding.RequiredMetres, MasterPrecision); // WND-002
        Near(1371.140m, winding.OuterDiameterMm, MasterPrecision); // WND-003
        Near(431.570m, winding.WindingHeightMm, MasterPrecision); // WND-004
        Near(424.628450106157112526539278m, winding.RequiredMetres, ReferencePrecision);
        Near(1371.140110814457822243353916m, winding.OuterDiameterMm, ReferencePrecision);
        Near(431.570055407228911121676958m, winding.WindingHeightMm, ReferencePrecision);
    }

    private static void Near(decimal expected, decimal actual, decimal precision) =>
        Assert.True(Math.Abs(expected - actual) < precision, $"expected {expected} ± {precision}, got {actual}");
}

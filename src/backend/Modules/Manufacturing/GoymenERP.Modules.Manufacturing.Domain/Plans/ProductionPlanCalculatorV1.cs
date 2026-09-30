using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Engines;
using GoymenERP.Modules.Manufacturing.Domain.Engines.ColdRolling;
using GoymenERP.Modules.Manufacturing.Domain.Engines.CutToLength;
using GoymenERP.Modules.Manufacturing.Domain.Engines.Painting;
using GoymenERP.Modules.Manufacturing.Domain.Engines.Slitting;
using GoymenERP.Modules.Manufacturing.Domain.Engines.TubeProfile;

namespace GoymenERP.Modules.Manufacturing.Domain.Plans;

/// <summary>
/// What is known from OUTSIDE the plan for its calculation, by the engines' own parameter names (for example
/// <c>densityKgPerCubicMetre</c>, <c>thicknessMm</c>, <c>lineSpeedMetresPerMinute</c>). A roll's own value wins over the
/// plan-wide one. A name that is absent is UNKNOWN — never 0 and never a default: the engine then answers
/// <see cref="EngineOutcome.Unavailable"/> and names it (OPEN-007: density, speed, kerf, tolerance and the other values
/// are not decided; DEC-SET-001: their source is the Settings / Policy Registry, which is not built yet).
/// </summary>
public sealed record PlanCalculationParameters(
    IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, decimal>>? Rolls = null,
    IReadOnlyDictionary<string, decimal>? Plan = null)
{
    /// <summary>Nothing is known: the state until the Product, Inventory and registry sources are wired.</summary>
    public static PlanCalculationParameters Unknown { get; } = new();

    internal decimal? Value(Guid? roll, string name)
    {
        if (roll is { } id && Rolls is { } rolls && rolls.TryGetValue(id, out var own) && own.TryGetValue(name, out var value))
        {
            return value;
        }

        return Plan is { } plan && plan.TryGetValue(name, out var shared) ? shared : null;
    }

    /// <summary>A count parameter must be a whole number that fits; anything else is the caller's error, never rounded.</summary>
    internal int? Count(Guid? roll, string name)
    {
        if (Value(roll, name) is not { } value)
        {
            return null;
        }

        if (decimal.Truncate(value) != value || value is < int.MinValue or > int.MaxValue)
        {
            throw new ArgumentException($"The count parameter '{name}' must be a whole number.", nameof(name));
        }

        return (int)value;
    }
}

/// <summary>One named, unrounded value a formula computed.</summary>
public sealed record PlanCalculatedValue(string Name, decimal Value);

/// <summary>
/// One engine call's answer inside the plan calculation, exactly as the engine gave it: <c>Computed</c> with its named
/// values, <c>Unavailable</c> naming every missing input, or <c>Refused</c> with its code and parameter.
/// </summary>
public sealed record PlanFormulaAnswer(
    string Formula,
    string FormulaVersion,
    EngineOutcome Outcome,
    IReadOnlyList<PlanCalculatedValue> Values,
    IReadOnlyList<string> MissingParameters,
    string? RefusalCode,
    string? RefusedParameter);

/// <summary>The formulas that run on one planned parent roll, with the roll's planned mass as their input mass.</summary>
public sealed record PlanRollCalculation(Guid ParentRollId, Kilograms PlannedMass, IReadOnlyList<PlanFormulaAnswer> Answers);

/// <summary>
/// The plan's declared target against the plan. Only in the basis the plan declared (as the order's remaining target):
/// an <see cref="TargetBasis.InputMass"/> target is compared with the planned input mass (target − planned, signed:
/// a negative value means more is planned than the target, which is reported, not corrected). A
/// <see cref="TargetBasis.GoodOutputMass"/> target needs a theoretical good-output mass, which no formula version
/// defines for a plan yet, so it is <see cref="EngineOutcome.Unavailable"/> and says so — never estimated.
/// </summary>
public sealed record PlanTargetCalculation(
    TargetBasis Basis,
    Kilograms Target,
    EngineOutcome Outcome,
    decimal? TargetMinusPlannedInputKg,
    IReadOnlyList<string> MissingParameters);

/// <summary>The whole theoretical calculation of one plan content (see <see cref="ProductionPlanCalculatorV1"/>).</summary>
public sealed record PlanCalculation(
    string CalculatorVersion,
    ManufacturingProcess Process,
    int PlannedRollCount,
    Kilograms PlannedInputMass,
    PlanTargetCalculation? Target,
    IReadOnlyList<PlanFormulaAnswer> PlanAnswers,
    IReadOnlyList<PlanRollCalculation> Rolls,
    IReadOnlyList<string> MissingParameters,
    bool HasRefusal);

/// <summary>
/// AT-061 / DEC-PRDCT-005: the ONE deterministic calculation of a production plan's content, used by the live preview
/// and by anything that asks what a plan computes (DEC-PRDCT-004, AGENTS: formula / UI / report use the same backend
/// calculator). It composes the versioned process engines (master v3 §28.3–§28.8) — it adds no formula, no parameter
/// value, no rounding and no tolerance of its own:
/// <list type="bullet">
/// <item><b>Plan totals:</b> the planned roll count and the exact sum of the planned masses (Ö2 kilograms add exactly).</item>
/// <item><b>Per roll</b> (the roll's planned mass is the engine's input mass): Slitting SLT-001…007 (with the plan's slit
/// lines when it carries a spec; otherwise the lines are named missing) and SLT-010; Cold Rolling CR-001…005; Cut to
/// Length CUT-001…006 (the plan carries no cut lines, so they are named missing).</item>
/// <item><b>Per plan</b> (no roll mass enters these formulas): Tube / Profile TUB-001…003 and TUB-004; Painting
/// PNT-001…004, PNT-005 and PNT-006.</item>
/// <item><b>Outside values</b> come only from <see cref="PlanCalculationParameters"/>; an unknown one is named, never
/// defaulted. A later call that needs an earlier call's result (TUB-004's pieces, PNT-005's pieces, PNT-006's area)
/// receives it only when that call computed; otherwise it names that input too.</item>
/// </list>
/// The answer is a function of (process, inputs, target, spec, parameters) only: the same content always answers the
/// same, so the preview of a content and the calculation of the plan saved from it are identical.
/// </summary>
public static class ProductionPlanCalculatorV1
{
    /// <summary>The composition's version. A changed composition is a new version, never an edit (DEC-PRDCT-004).</summary>
    public const string Version = "GDC-PLAN-CALC/1";

    public static PlanCalculation Calculate(ProductionPlan plan, PlanCalculationParameters? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Calculate(plan.Process, plan.Inputs, plan.Target, plan.Spec, parameters);
    }

    public static PlanCalculation Calculate(ManufacturingProcess process, IReadOnlyList<PlannedInput> inputs,
        ProductionTarget? target, SlittingSpec? spec, PlanCalculationParameters? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (!Enum.IsDefined(process))
        {
            throw new ArgumentOutOfRangeException(nameof(process), process, "Tanımsız üretim süreci.");
        }

        parameters ??= PlanCalculationParameters.Unknown;
        var total = inputs.Aggregate(Kilograms.Zero, (sum, input) => sum + input.PlannedMass);
        var rolls = inputs
            .Select(input => new PlanRollCalculation(input.ParentRollId, input.PlannedMass,
                RollAnswers(process, input, spec, parameters)))
            .ToArray();
        var planAnswers = PlanAnswers(process, parameters);
        var targetCalculation = target is null ? null : TargetOf(target, total);

        var answers = planAnswers.Concat(rolls.SelectMany(roll => roll.Answers)).ToArray();
        var missing = new SortedSet<string>(answers.SelectMany(answer => answer.MissingParameters), StringComparer.Ordinal);
        if (targetCalculation is not null)
        {
            missing.UnionWith(targetCalculation.MissingParameters);
        }

        return new PlanCalculation(Version, process, inputs.Count, total, targetCalculation, planAnswers, rolls,
            [.. missing], answers.Any(answer => answer.Outcome == EngineOutcome.Refused));
    }

    /// <summary>The name a GoodOutputMass target needs and no plan formula supplies yet.</summary>
    public const string TheoreticalGoodOutputParameter = "theoreticalGoodOutputKg";

    private static PlanTargetCalculation TargetOf(ProductionTarget target, Kilograms plannedInput) =>
        target.Basis == TargetBasis.InputMass
            ? new PlanTargetCalculation(target.Basis, target.Mass, EngineOutcome.Computed,
                target.Mass.Value - plannedInput.Value, [])
            : new PlanTargetCalculation(target.Basis, target.Mass, EngineOutcome.Unavailable, null,
                [TheoreticalGoodOutputParameter]);

    private static PlanFormulaAnswer[] RollAnswers(ManufacturingProcess process, PlannedInput input, SlittingSpec? spec,
        PlanCalculationParameters parameters)
    {
        var roll = input.ParentRollId;
        var kg = input.PlannedMass.Value;
        decimal? P(string name) => parameters.Value(roll, name);

        switch (process)
        {
            case ManufacturingProcess.Slitting:
            {
                IReadOnlyList<SlitLineInputV1> lines = spec is null
                    ? [new SlitLineInputV1(null, null)]
                    : [.. spec.Lines.Select(line => new SlitLineInputV1(line.BandWidthMm, line.Count))];
                return
                [
                    Answer("SLT-001…007", SlittingEngineV1.FormulaVersion,
                        SlittingEngineV1.Plan(new SlittingPlanInputV1(kg, P("netUsableWidthMm"), lines)),
                        result =>
                        [
                            new("kgPerMillimetreWidth", result.KgPerMillimetreWidth),
                            new("totalUsedWidthMm", result.TotalUsedWidthMm),
                            new("remainingWidthMm", result.RemainingWidthMm),
                            new("planKg", result.PlanKg),
                            new("remainingKg", result.RemainingKg),
                        ]),
                    Answer("SLT-010", SlittingEngineV1.FormulaVersion,
                        SlittingEngineV1.LineTime(new LineTimeInputV1(kg, P("thicknessMm"), P("parentWidthMm"),
                            P("densityKgPerCubicMetre"), P("lineSpeedMetresPerMinute"))),
                        result =>
                        [
                            new("kgPerMetre", result.KgPerMetre),
                            new("walkingMetres", result.WalkingMetres),
                            new("minutes", result.Minutes),
                        ]),
                ];
            }

            case ManufacturingProcess.ColdRolling:
                return
                [
                    Answer("CR-001…005", ColdRollingEngineV1.FormulaVersion,
                        ColdRollingEngineV1.Roll(new ColdRollingInputV1(kg, P("widthMm"), P("inputThicknessMm"),
                            P("targetThicknessMm"), P("scrapKg"), P("densityKgPerCubicMetre"))),
                        result =>
                        [
                            new("inputMetres", result.InputMetres),
                            new("outputKg", result.OutputKg),
                            new("elongationRatio", result.ElongationRatio),
                            new("outputMetres", result.OutputMetres),
                            new("reductionPercent", result.ReductionPercent),
                        ]),
                ];

            case ManufacturingProcess.CutToLength:
                // The plan carries no cut lines (§28.3's target lengths and counts): the engine names them missing.
                return
                [
                    Answer("CUT-001…006", CutToLengthEngineV1.FormulaVersion,
                        CutToLengthEngineV1.Plan(new CutPlanInputV1(kg, P("thicknessMm"), P("widthMm"),
                            P("densityKgPerCubicMetre"), [new CutLineInputV1(null, null)])),
                        result =>
                        [
                            new("inputMetres", result.InputMetres),
                            new("planKg", result.PlanKg),
                            new("planMetres", result.PlanMetres),
                            new("remainingKg", result.RemainingKg),
                            new("remainingMetres", result.RemainingMetres),
                        ]),
                ];

            default:
                return [];
        }
    }

    private static PlanFormulaAnswer[] PlanAnswers(ManufacturingProcess process, PlanCalculationParameters parameters)
    {
        decimal? P(string name) => parameters.Value(null, name);
        int? C(string name) => parameters.Count(null, name);

        switch (process)
        {
            case ManufacturingProcess.TubeProfile:
            {
                var run = TubeProfileEngineV1.Run(new TubeRunInputV1(C("packageCount"), C("piecesPerPackage"),
                    P("pieceLengthMm"), P("lineSpeedMetresPerMinute")));
                return
                [
                    Answer("TUB-001…003", TubeProfileEngineV1.FormulaVersion, run,
                        result =>
                        [
                            new("totalPieces", result.TotalPieces),
                            new("totalMetres", result.TotalMetres),
                            new("runMinutes", result.RunMinutes),
                        ]),
                    Answer("TUB-004", TubeProfileEngineV1.FormulaVersion,
                        TubeProfileEngineV1.Mass(new TubeMassInputV1(run.Value?.TotalPieces, P("unitKg"))),
                        result => [new("totalKg", result.TotalKg)]),
                ];
            }

            case ManufacturingProcess.Paint:
            {
                var run = PaintingEngineV1.Run(new PaintRunInputV1(C("packageCount"), C("piecesPerPackage"),
                    P("profileLengthMm"), C("simultaneousProfiles"), P("lineSpeedMetresPerMinute")));
                var area = PaintingEngineV1.Area(new PaintAreaInputV1(P("perimeterMm"), P("profileLengthMm"),
                    run.Value?.TotalPieces));
                return
                [
                    Answer("PNT-001…004", PaintingEngineV1.FormulaVersion, run,
                        result =>
                        [
                            new("totalPieces", result.TotalPieces),
                            new("profileMetres", result.ProfileMetres),
                            new("linePassMetres", result.LinePassMetres),
                            new("minutes", result.Minutes),
                        ]),
                    Answer("PNT-005", PaintingEngineV1.FormulaVersion, area,
                        result => [new("areaSquareMetres", result.AreaSquareMetres)]),
                    Answer("PNT-006", PaintingEngineV1.FormulaVersion,
                        PaintingEngineV1.Paint(new PaintLitresInputV1(area.Value?.AreaSquareMetres, P("filmMicrons"),
                            P("solidsRatio"), P("scrapFactor"))),
                        result => [new("theoreticalLitres", result.TheoreticalLitres)]),
                ];
            }

            default:
                return [];
        }
    }

    private static PlanFormulaAnswer Answer<T>(string formula, string formulaVersion, EngineResult<T> result,
        Func<T, PlanCalculatedValue[]> values)
        where T : class =>
        new(formula, formulaVersion, result.Outcome,
            result.Value is { } computed ? values(computed) : [],
            [.. result.MissingParameters],
            result.RefusalCode,
            result.RefusedParameter);
}

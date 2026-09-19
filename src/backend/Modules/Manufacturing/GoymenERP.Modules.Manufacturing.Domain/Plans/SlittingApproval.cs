using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Engines;
using GoymenERP.Modules.Manufacturing.Domain.Engines.Slitting;
using GoymenERP.Modules.Manufacturing.Domain.Engines.Winding;

namespace GoymenERP.Modules.Manufacturing.Domain.Plans;

/// <summary>
/// What is known of one planned parent roll from OUTSIDE the plan, at approval. Every value is null until
/// its owner supplies it; null is "unknown", never 0 and never a default.
/// <list type="bullet">
/// <item><see cref="LabelUsable"/>: Inventory's module read of the label (not yet published).</item>
/// <item><see cref="ThicknessMm"/>, <see cref="ParentWidthMm"/>: Product's role read of the card (not yet
/// published; data waits on Q6).</item>
/// <item><see cref="NetUsableWidthMm"/>, <see cref="DensityKgPerCubicMetre"/>: the Settings / Policy
/// Registry (DEC-SET-001, OPEN-007; not yet built).</item>
/// </list>
/// </summary>
public sealed record SlittingRollParameters(
    bool? LabelUsable = null,
    decimal? NetUsableWidthMm = null,
    decimal? ParentWidthMm = null,
    decimal? ThicknessMm = null,
    decimal? DensityKgPerCubicMetre = null);

/// <summary>
/// The approval's outside inputs: one <see cref="SlittingRollParameters"/> per planned roll, and the
/// registry's line values (OPEN-007, OPEN-004/005). A roll with no entry, or a value left null, is unknown.
/// </summary>
public sealed record SlittingPlanParameters(
    IReadOnlyDictionary<Guid, SlittingRollParameters>? Rolls = null,
    decimal? LineSpeedMetresPerMinute = null,
    decimal? InnerDiameterMm = null,
    decimal? MaximumOuterDiameterMm = null,
    decimal? ToleranceMetres = null)
{
    /// <summary>Nothing is known: the state until the Inventory, Product and registry sources exist.</summary>
    public static SlittingPlanParameters Unknown { get; } = new();
}

/// <summary>
/// The MFG-6 approval gate: a plan that carries a <see cref="SlittingSpec"/> is approved only when every
/// engine it claims computes (master v3 §28.4 / §28.5, the MFG-6 engines, version 1). Nothing is approved
/// with gaps, and nothing is defaulted.
/// <list type="number">
/// <item>Per roll: the label must be known usable; SLT-001…007 (the plan) and SLT-010 (the line time) must
/// compute with the roll's planned mass as the parent mass (the kg that passes the line).</item>
/// <item>With a winding target, per roll and per distinct band width: WND-001…004 and the WND-008/009 fit
/// (the parent's walking metres against the band's required metres, the outer diameter against the line's
/// maximum). A fit that computes and fails is refused (<see cref="ManufacturingRuleCodes.PlanWindingDoesNotFit"/>).</item>
/// </list>
/// A refusal (a supplied value is wrong) wins over missing values. Missing values are answered together,
/// as the sorted, distinct parameter names — not per roll, so the list is stable.
/// </summary>
public static class SlittingApprovalGate
{
    public const string LabelStateParameter = "labelState";

    /// <summary>Throws <see cref="ManufacturingRuleException"/> unless every engine input of the plan's spec is known and valid.</summary>
    public static void Require(ProductionPlan plan, SlittingPlanParameters? parameters)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Spec is not { } spec)
        {
            return;
        }

        parameters ??= SlittingPlanParameters.Unknown;
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        (string Code, string Parameter)? refused = null;

        void Take<T>(EngineResult<T> result)
            where T : class
        {
            if (result.Outcome == EngineOutcome.Refused)
            {
                refused ??= (result.RefusalCode!, result.RefusedParameter!);
            }
            else if (result.Outcome == EngineOutcome.Unavailable)
            {
                missing.UnionWith(result.MissingParameters);
            }
        }

        var lines = spec.Lines.Select(line => new SlitLineInputV1(line.BandWidthMm, line.Count)).ToArray();
        foreach (var input in plan.Inputs)
        {
            var roll = parameters.Rolls is { } rolls && rolls.TryGetValue(input.ParentRollId, out var known)
                ? known
                : new SlittingRollParameters();
            if (roll.LabelUsable is null)
            {
                missing.Add(LabelStateParameter);
            }
            else if (roll.LabelUsable == false)
            {
                refused ??= (ManufacturingRuleCodes.PlanLabelNotUsable, LabelStateParameter);
            }

            var parentKg = input.PlannedMass.Value;
            Take(SlittingEngineV1.Plan(new SlittingPlanInputV1(parentKg, roll.NetUsableWidthMm, lines)));
            var lineTime = SlittingEngineV1.LineTime(new LineTimeInputV1(parentKg, roll.ThicknessMm, roll.ParentWidthMm,
                roll.DensityKgPerCubicMetre, parameters.LineSpeedMetresPerMinute));
            Take(lineTime);

            if (spec.WindingTargetKg is not { } target)
            {
                continue;
            }

            foreach (var bandWidth in spec.Lines.Select(line => line.BandWidthMm).Distinct())
            {
                var winding = WindingEngineV1.Target(new WindingTargetInputV1(roll.DensityKgPerCubicMetre, roll.ThicknessMm,
                    bandWidth, target.Value, parameters.InnerDiameterMm));
                Take(winding);
                // WND-008/009 take the walking metres, required metres and outer diameter the engines above
                // computed. When those could not be computed they are null here; the fit then names them, and
                // those names are dropped below, because the engine above already named the real missing input.
                var fit = WindingEngineV1.Fit(new WindingFitInputV1(
                    lineTime.Value?.WalkingMetres,
                    parameters.ToleranceMetres,
                    winding.Value?.RequiredMetres,
                    winding.Value?.OuterDiameterMm,
                    parameters.MaximumOuterDiameterMm));
                Take(fit);
                // A computed fit that fails is a refusal, never a pass (fail-closed).
                if (fit.Value is { MetresSufficient: false })
                {
                    refused ??= (ManufacturingRuleCodes.PlanWindingDoesNotFit, "metresSufficient");
                }
                else if (fit.Value is { DiameterFits: false })
                {
                    refused ??= (ManufacturingRuleCodes.PlanWindingDoesNotFit, "diameterFits");
                }
            }
        }

        if (refused is { } refusal)
        {
            // The plan's own refusals carry their code and the named check; an engine's refusal is wrapped,
            // carrying the engine's code and the parameter it named.
            var ownRefusal = refusal.Code is ManufacturingRuleCodes.PlanLabelNotUsable or ManufacturingRuleCodes.PlanWindingDoesNotFit;
            throw new ManufacturingRuleException(
                ownRefusal ? refusal.Code : ManufacturingRuleCodes.PlanEngineRefused,
                "Planın hesabı girdilerinden birini reddetti; plan onaylanmadı.",
                ownRefusal ? new[] { refusal.Parameter } : new[] { refusal.Code, refusal.Parameter });
        }

        // The fit's placeholders stand for engine results that are already named missing; drop their names.
        missing.ExceptWith(FitPlaceholders);
        if (missing.Count > 0)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.PlanParametersUnavailable,
                "Planın hesabı için gereken değerler eksik; plan onaylanmadı.", [.. missing]);
        }
    }

    /// <summary>The fit's inputs that are other engines' results, not outside parameters.</summary>
    private static readonly string[] FitPlaceholders = ["walkingMetres", "requiredMetres", "outerDiameterMm"];
}

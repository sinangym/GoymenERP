using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Modules.Manufacturing.Application;

// AT-061 / AT-062 (DEC-PRDCT-005, locked): the button-less backend preview of a plan content. The screen sends what the
// user has typed; the server answers with the ONE plan calculator (ProductionPlanCalculatorV1) — the same function that
// answers for a saved plan of the same content — and writes nothing. No formula, parameter value or tolerance lives in
// the screen.

/// <summary>
/// The plan content to preview: the plan save's grammar without the plan id (nothing is created) — the work center,
/// the planned inputs with the Inventory label version the screen read, and the optional target.
/// </summary>
public sealed record PlanPreviewRequestV1(
    Guid WorkCenterId, IReadOnlyList<PlannedInputRequestV1>? Inputs, TargetRequestV1? Target,
    SlittingSpecRequestV1? SlittingSpec = null);

/// <summary>One named, unrounded value as lossless invariant text.</summary>
public sealed record PlanCalculatedValueViewV1(string Name, string Value);

/// <summary>
/// One engine call as the engine answered it: <c>Computed</c> with its named values, <c>Unavailable</c> naming every
/// missing input (a parameter with no decided value or source — OPEN-007 — is never defaulted), or <c>Refused</c> with
/// its code and parameter. <see cref="Formula"/> is the master v3 §28 id range; <see cref="FormulaVersion"/> the engine's.
/// </summary>
public sealed record PlanFormulaAnswerViewV1(
    string Formula, string FormulaVersion, string Outcome, IReadOnlyList<PlanCalculatedValueViewV1> Values,
    IReadOnlyList<string> MissingParameters, string? RefusalCode, string? RefusedParameter);

/// <summary>The formulas run on one planned parent roll, its planned mass (as the plan save stores it) their input mass.</summary>
public sealed record PlanRollPreviewViewV1(Guid ParentRollId, string PlannedKg, IReadOnlyList<PlanFormulaAnswerViewV1> Answers);

/// <summary>
/// The declared target against the plan, only in its own basis: <c>InputMass</c> → <c>Computed</c> with
/// <see cref="TargetMinusPlannedInputKg"/> (signed; negative = more planned than the target, reported, not corrected);
/// <c>GoodOutputMass</c> → <c>Unavailable</c>, naming <c>theoreticalGoodOutputKg</c>.
/// </summary>
public sealed record PlanTargetPreviewViewV1(
    string Basis, string Kg, string Outcome, string? TargetMinusPlannedInputKg, IReadOnlyList<string> MissingParameters);

/// <summary>
/// The plan content's theoretical calculation (<c>GDC-PLAN-CALC/1</c>): the process the work center runs, the planned
/// roll count and input mass, the target comparison, the plan-level and per-roll engine answers, and every missing
/// parameter (sorted, distinct). <see cref="HasRefusal"/> is true when an engine refused a supplied value. A preview is
/// not a record: nothing is stored, audited or reserved.
/// </summary>
public sealed record PlanPreviewViewV1(
    string CalculatorVersion, Guid WorkCenterId, string Process, int PlannedRollCount, string PlannedInputKg,
    PlanTargetPreviewViewV1? Target, IReadOnlyList<PlanFormulaAnswerViewV1> Answers,
    IReadOnlyList<PlanRollPreviewViewV1> Rolls, IReadOnlyList<string> MissingParameters, bool HasRefusal);

public sealed partial class ManufacturingCommandService
{
    /// <summary>
    /// <c>POST /plans/preview</c>: the plan save's own path up to — and never including — the write. In the save's
    /// order: the request grammar; the fresh authority check of the save's grant (company-wide); Organization's process of
    /// the work center; Settings' kilogram profile for every mass and Inventory's exact label version for every roll;
    /// the fresh fence again; the plan's own domain rules (at least one roll, no roll twice). Any refusal is the save's
    /// own answer for the same content (status, code, field), so the screen can say why and on which field.
    /// Then the ONE plan calculator on the would-be plan with the outside parameters that are known today: none
    /// (<see cref="PlanCalculationParameters.Unknown"/> — the registry, Product and Inventory sources are not wired), so
    /// every engine that needs one answers <c>Unavailable</c> and names it. No command record, no audit, no store write.
    /// </summary>
    public async Task<ManufacturingApiResult<PlanPreviewViewV1>> PreviewPlanAsync(PlanPreviewRequestV1? request,
        string permission, CancellationToken cancellationToken)
    {
        if (!HasContext() || ActorGuid() is not { } actorId)
        {
            return Denied<PlanPreviewViewV1>();
        }

        var companyId = companyContext.CompanyId;

        if (request is null || request.WorkCenterId == Guid.Empty || request.Inputs is null || request.Inputs.Count == 0
            || request.Inputs.Any(input => input is null || input.ParentRollId == Guid.Empty))
        {
            return Invalid<PlanPreviewViewV1>("plan");
        }

        var content = ParsePlanContent(request.Inputs, request.Target, request.SlittingSpec);
        if (content.InvalidField is { } invalidField)
        {
            return Invalid<PlanPreviewViewV1>(invalidField);
        }

        // The save's grant, measured fresh before any owner is asked (#276).
        if (await FenceCapturedAsync<PlanPreviewViewV1>(permission, companyId, actorId, cancellationToken) is { } denied)
        {
            return denied;
        }

        var process = await ResolveProcessAsync(
            request.WorkCenterId, permission, companyId, actorId, cancellationToken);
        if (process.Refusal is not null)
        {
            return Refusal<PlanPreviewViewV1>(process.Refusal);
        }

        if (content.SlittingSpec is not null && process.Value != ManufacturingProcess.Slitting)
        {
            return Invalid<PlanPreviewViewV1>("slittingSpec");
        }

        var verified = await VerifyPlanContentAsync(
            content.Inputs!, content.TargetKg, permission, companyId, actorId, cancellationToken);
        if (verified.Refusal is not null)
        {
            return Refusal<PlanPreviewViewV1>(verified.Refusal);
        }

        var slittingSpec = await VerifySlittingSpecAsync(content.SlittingSpec, cancellationToken);
        if (slittingSpec.Refusal is not null)
        {
            return Refusal<PlanPreviewViewV1>(slittingSpec.Refusal);
        }

        if (await FenceCapturedAsync<PlanPreviewViewV1>(permission, companyId, actorId, cancellationToken) is { } fenced)
        {
            return fenced;
        }

        ProductionPlan plan;
        try
        {
            // The would-be plan, built by the same constructor as the save, so its domain rules answer the same. Its
            // id is transient and never stored; the calculation does not read it.
            plan = new ProductionPlan(Guid.NewGuid(), companyId, request.WorkCenterId, process.Value,
                PlannedInputs(verified.Inputs), Target(request.Target, verified.TargetMass),
                BuildSlittingSpec(content.SlittingSpec, slittingSpec.WindingTarget));
        }
        catch (ManufacturingRuleException exception)
        {
            return new ManufacturingApiResult<PlanPreviewViewV1>(ManufacturingApiStatus.Unprocessable, null, false,
                exception.Code);
        }
        catch (ArgumentException)
        {
            // The transient domain constructor is an implementation detail. Keep the public preview field stable
            // instead of leaking whichever parameter name a constructor happens to use.
            return Invalid<PlanPreviewViewV1>("plan");
        }

        return new ManufacturingApiResult<PlanPreviewViewV1>(ManufacturingApiStatus.Ok,
            PlanPreviewView(plan.WorkCenterId, ProductionPlanCalculatorV1.Calculate(plan,
                PlanCalculationParameters.FromCapturedSnapshots(plan.Inputs))),
            false, null);
    }

    /// <summary>
    /// The one text form of a plan calculation. The preview answers with it, and a stored plan's calculation is the
    /// same view of <see cref="ProductionPlanCalculatorV1.Calculate(ProductionPlan, PlanCalculationParameters?)"/>.
    /// </summary>
    public static PlanPreviewViewV1 PlanPreviewView(Guid workCenterId, PlanCalculation calculation) => new(
        calculation.CalculatorVersion, workCenterId, calculation.Process.ToString(), calculation.PlannedRollCount,
        Text(calculation.PlannedInputMass.Value),
        calculation.Target is { } target
            ? new PlanTargetPreviewViewV1(target.Basis.ToString(), Text(target.Target.Value), target.Outcome.ToString(),
                target.TargetMinusPlannedInputKg is { } gap ? Text(gap) : null, [.. target.MissingParameters])
            : null,
        [.. calculation.PlanAnswers.Select(FormulaAnswerView)],
        [.. calculation.Rolls.Select(roll => new PlanRollPreviewViewV1(roll.ParentRollId, Text(roll.PlannedMass.Value),
            [.. roll.Answers.Select(FormulaAnswerView)]))],
        [.. calculation.MissingParameters],
        calculation.HasRefusal);

    private static PlanFormulaAnswerViewV1 FormulaAnswerView(PlanFormulaAnswer answer) => new(
        answer.Formula, answer.FormulaVersion, answer.Outcome.ToString(),
        [.. answer.Values.Select(value => new PlanCalculatedValueViewV1(value.Name, Text(value.Value)))],
        [.. answer.MissingParameters], answer.RefusalCode, answer.RefusedParameter);
}

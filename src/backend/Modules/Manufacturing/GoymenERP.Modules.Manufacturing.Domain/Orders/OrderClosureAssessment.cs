using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Engines;
using GoymenERP.Modules.Manufacturing.Domain.Engines.Balance;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Modules.Manufacturing.Domain.Orders;

/// <summary>
/// The immutable identity, version and value of the per-process closure tolerance supplied by its owner.
/// Manufacturing never supplies a default percentage. The runtime adapter that reads Settings and persists this
/// snapshot is deliberately outside this domain-only slice.
/// </summary>
public sealed record ClosureTolerancePolicySnapshotV1(
    Guid CompanyId,
    Guid PolicyId,
    long PolicyVersion,
    ManufacturingProcess Process,
    decimal MaximumFirePercent);

/// <summary>
/// The exact BAL-004 numerator and denominator supplied for one roll. The calculator does not decide which mass is
/// fire or whether raw material means issued or net-issued mass; those meanings belong to the future owner contract.
/// </summary>
public sealed record RollScrapPercentInputV1(Guid ParentRollId, ScrapPercentInputV1 Input);

public enum ClosureDispositionV1
{
    Balanced = 1,
    FireDeclarationRequired = 2,
    ApprovalRequired = 3,
    ReviewRequired = 4,
    Unavailable = 5,
    Refused = 6,
}

public static class ClosureAssessmentCodesV1
{
    public const string PolicyInvalid = "manufacturing_closure_policy_invalid";
    public const string PolicyScopeMismatch = "manufacturing_closure_policy_scope_mismatch";
    public const string RollInputInvalid = "manufacturing_closure_roll_input_invalid";
    public const string FireMassMismatch = "manufacturing_closure_fire_mass_mismatch";
    public const string NegativeDifferenceReview = "manufacturing_closure_negative_difference_review";
}

/// <summary>One roll's read-only closure decision. No result here records a fact, approval or close.</summary>
public sealed record RollClosureAssessmentV1(
    Guid ParentRollId,
    ClosureDispositionV1 Disposition,
    decimal DifferenceKg,
    decimal? FirePercent,
    string? Code,
    string? RefusedParameter,
    IReadOnlyList<string> MissingParameters);

/// <summary>
/// The read-only assessment at one policy snapshot. <see cref="ClosureDispositionV1.FireDeclarationRequired"/> says
/// an explicit fire fact is still required; it is never an automatic output or permission to close.
/// <see cref="ClosureDispositionV1.ApprovalRequired"/> likewise says only that the supplied percentage is outside the
/// supplied policy. It is not an approval and names no approver or permission.
/// </summary>
public sealed record OrderClosureAssessmentV1(
    ClosureDispositionV1 Disposition,
    ClosureTolerancePolicySnapshotV1 Policy,
    IReadOnlyList<RollClosureAssessmentV1> Rolls,
    string? Code,
    string? RefusedParameter,
    IReadOnlyList<string> MissingParameters);

/// <summary>
/// OPEN-005 / Ö7 preparation: evaluates the existing BAL-003 differences roll by roll against an explicitly supplied
/// BAL-004 calculation and per-process policy snapshot. It does not choose BAL-004 inputs, generate a fact, write off a
/// difference, approve an exception or mutate the order.
/// </summary>
public static class OrderClosureAssessmentCalculatorV1
{
    public static OrderClosureAssessmentV1 Assess(
        ProductionOrder order,
        ClosureTolerancePolicySnapshotV1 policy,
        IReadOnlyList<RollScrapPercentInputV1> scrapPercentInputs)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(scrapPercentInputs);
        var inputs = scrapPercentInputs.ToArray();

        if (policy.CompanyId == Guid.Empty)
        {
            return Whole(ClosureDispositionV1.Refused, policy, ClosureAssessmentCodesV1.PolicyInvalid, "companyId");
        }

        if (policy.PolicyId == Guid.Empty)
        {
            return Whole(ClosureDispositionV1.Refused, policy, ClosureAssessmentCodesV1.PolicyInvalid, "policyId");
        }

        if (policy.PolicyVersion <= 0)
        {
            return Whole(ClosureDispositionV1.Refused, policy, ClosureAssessmentCodesV1.PolicyInvalid, "policyVersion");
        }

        if (!Enum.IsDefined(policy.Process))
        {
            return Whole(ClosureDispositionV1.Refused, policy, ClosureAssessmentCodesV1.PolicyInvalid, "process");
        }

        if (policy.MaximumFirePercent < 0m)
        {
            return Whole(ClosureDispositionV1.Refused, policy, ClosureAssessmentCodesV1.PolicyInvalid,
                "maximumFirePercent");
        }

        if (order.CompanyId != policy.CompanyId || order.Process != policy.Process)
        {
            return Whole(ClosureDispositionV1.Unavailable, policy, ClosureAssessmentCodesV1.PolicyScopeMismatch,
                order.CompanyId != policy.CompanyId ? "companyId" : "process");
        }

        if (order.Status != OrderStatus.Open)
        {
            return Whole(ClosureDispositionV1.Refused, policy, ManufacturingRuleCodes.OrderClosed, "order");
        }

        if (inputs.Any(input => input is null || input.ParentRollId == Guid.Empty || input.Input is null))
        {
            return Whole(ClosureDispositionV1.Refused, policy, ClosureAssessmentCodesV1.RollInputInvalid,
                "scrapPercentInputs");
        }

        var plannedRolls = order.PlannedInputs.Select(input => input.ParentRollId).ToHashSet();
        if (inputs.Any(input => !plannedRolls.Contains(input.ParentRollId))
            || inputs.GroupBy(input => input.ParentRollId).Any(group => group.Count() != 1))
        {
            return Whole(ClosureDispositionV1.Refused, policy, ClosureAssessmentCodesV1.RollInputInvalid,
                "parentRollId");
        }

        var supplied = inputs.ToDictionary(input => input.ParentRollId);
        var rolls = order.PlannedInputs
            .Select(input => AssessRoll(order.BalanceOf(input.ParentRollId), supplied.GetValueOrDefault(input.ParentRollId),
                policy.MaximumFirePercent))
            .ToArray();

        var disposition = rolls.Select(roll => roll.Disposition).OrderByDescending(Priority).First();
        var firstProblem = rolls.FirstOrDefault(roll => roll.Disposition == disposition
            && (roll.Code is not null || roll.MissingParameters.Count > 0));
        return new OrderClosureAssessmentV1(disposition, policy, rolls, firstProblem?.Code,
            firstProblem?.RefusedParameter, firstProblem?.MissingParameters ?? []);
    }

    private static RollClosureAssessmentV1 AssessRoll(
        MassBalance balance,
        RollScrapPercentInputV1? supplied,
        decimal maximumFirePercent)
    {
        var difference = balance.Unexplained;
        if (difference == 0m)
        {
            return Roll(balance.ParentRollId, ClosureDispositionV1.Balanced, difference);
        }

        if (difference < 0m)
        {
            return Roll(balance.ParentRollId, ClosureDispositionV1.ReviewRequired, difference,
                code: ClosureAssessmentCodesV1.NegativeDifferenceReview);
        }

        if (supplied is null)
        {
            return Roll(balance.ParentRollId, ClosureDispositionV1.Unavailable, difference,
                missing: ["fireKg", "rawMaterialKg"]);
        }

        var calculated = MassBalanceEngineV1.ScrapPercent(supplied.Input);
        if (calculated.Outcome == EngineOutcome.Unavailable)
        {
            return Roll(balance.ParentRollId, ClosureDispositionV1.Unavailable, difference,
                missing: calculated.MissingParameters);
        }

        if (calculated.Outcome == EngineOutcome.Refused)
        {
            return Roll(balance.ParentRollId, ClosureDispositionV1.Refused, difference,
                code: calculated.RefusalCode, refusedParameter: calculated.RefusedParameter);
        }

        // The caller must explicitly classify THIS positive BAL-003 gap as the BAL-004 fire numerator. A different
        // numerator may be valid in another policy, but it cannot silently close this difference.
        if (supplied.Input.FireKg != difference)
        {
            return Roll(balance.ParentRollId, ClosureDispositionV1.Refused, difference,
                firePercent: calculated.Value!.FirePercent, code: ClosureAssessmentCodesV1.FireMassMismatch,
                refusedParameter: "fireKg");
        }

        var firePercent = calculated.Value!.FirePercent;
        return Roll(balance.ParentRollId,
            firePercent <= maximumFirePercent
                ? ClosureDispositionV1.FireDeclarationRequired
                : ClosureDispositionV1.ApprovalRequired,
            difference, firePercent);
    }

    private static int Priority(ClosureDispositionV1 disposition) => disposition switch
    {
        ClosureDispositionV1.Refused => 6,
        ClosureDispositionV1.Unavailable => 5,
        ClosureDispositionV1.ReviewRequired => 4,
        ClosureDispositionV1.ApprovalRequired => 3,
        ClosureDispositionV1.FireDeclarationRequired => 2,
        ClosureDispositionV1.Balanced => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(disposition), disposition, null),
    };

    private static RollClosureAssessmentV1 Roll(
        Guid parentRollId,
        ClosureDispositionV1 disposition,
        decimal difference,
        decimal? firePercent = null,
        string? code = null,
        string? refusedParameter = null,
        IReadOnlyList<string>? missing = null) =>
        new(parentRollId, disposition, difference, firePercent, code, refusedParameter, missing ?? []);

    private static OrderClosureAssessmentV1 Whole(
        ClosureDispositionV1 disposition,
        ClosureTolerancePolicySnapshotV1 policy,
        string code,
        string refusedParameter) =>
        new(disposition, policy, [], code, refusedParameter, []);
}

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
    decimal MaximumFirePercent,
    bool IsActive,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    DateOnly SelectedForDate);

/// <summary>
/// The immutable business evidence for one out-of-tolerance close approval. It carries no authority by itself: the
/// application must freshly prove the dedicated approval permission before it may create and persist this value.
/// The order and policy versions bind the decision to the exact assessment the approver saw.
/// </summary>
public sealed record ClosureExceptionApprovalV1
{
    public const int ReasonMaximumLength = 256;

    private ClosureExceptionApprovalV1(Guid companyId, Guid orderId, Guid approvedByUserId, string reason, long orderVersion,
        Guid policyId, long policyVersion, DateTimeOffset approvedAtUtc)
    {
        CompanyId = companyId;
        OrderId = orderId;
        ApprovedByUserId = approvedByUserId;
        Reason = reason;
        OrderVersion = orderVersion;
        PolicyId = policyId;
        PolicyVersion = policyVersion;
        ApprovedAtUtc = approvedAtUtc;
    }

    public Guid CompanyId { get; }
    public Guid OrderId { get; }
    public Guid ApprovedByUserId { get; }
    public string Reason { get; }
    public long OrderVersion { get; }
    public Guid PolicyId { get; }
    public long PolicyVersion { get; }
    public DateTimeOffset ApprovedAtUtc { get; }

    public static ClosureExceptionApprovalV1 Create(Guid companyId, Guid orderId, Guid approvedByUserId, string reason,
        long orderVersion,
        Guid policyId, long policyVersion, DateTimeOffset approvedAtUtc)
    {
        var normalized = reason?.Trim();
        if (companyId == Guid.Empty || orderId == Guid.Empty || approvedByUserId == Guid.Empty
            || string.IsNullOrEmpty(normalized)
            || normalized.Length > ReasonMaximumLength || normalized.Any(char.IsControl)
            || orderVersion <= 0 || policyId == Guid.Empty || policyVersion <= 0 || approvedAtUtc == default)
            throw new ArgumentException("Geçerli onaylayan, gerekçe, emir sürümü, politika sürümü ve zaman zorunludur.");

        return new(companyId, orderId, approvedByUserId, normalized, orderVersion, policyId, policyVersion,
            approvedAtUtc.ToUniversalTime());
    }

    public bool Matches(Guid companyId, Guid orderId, Guid actorId, long orderVersion,
        ClosureTolerancePolicySnapshotV1 policy) =>
        companyId != Guid.Empty && companyId == CompanyId && orderId != Guid.Empty && orderId == OrderId
        && actorId != Guid.Empty && actorId == ApprovedByUserId && orderVersion == OrderVersion
        && policy is not null && policy.PolicyId == PolicyId && policy.PolicyVersion == PolicyVersion;
}

/// <summary>
/// The exact BAL-004 numerator and denominator supplied for one roll. The calculator verifies them against the owned
/// facts fixed by #66/5917438293: fire is declared scrap plus declared non-stock process loss; raw material is issued
/// minus returned. Callers cannot reclassify second quality or an unexplained difference through this input.
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
    public const string PolicyNotEffective = "manufacturing_closure_policy_not_effective";
    public const string PolicyScopeMismatch = "manufacturing_closure_policy_scope_mismatch";
    public const string FireDeclarationRequired = "manufacturing_closure_fire_declaration_required";
    public const string ApprovalRequired = "manufacturing_closure_approval_required";
    public const string ApprovalInvalid = "manufacturing_closure_approval_invalid";
    public const string ApprovalUnexpected = "manufacturing_closure_approval_unexpected";
    public const string RollInputInvalid = "manufacturing_closure_roll_input_invalid";
    public const string FireMassMismatch = "manufacturing_closure_fire_mass_mismatch";
    public const string NetIssuedMassMismatch = "manufacturing_closure_net_issued_mass_mismatch";
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
/// OPEN-005 / Ö7 preparation: evaluates the existing BAL-003 facts roll by roll against an explicitly supplied
/// BAL-004 calculation and per-process policy snapshot. It verifies the calculation against the order's owned facts;
/// it does not generate a fact, write off a difference, approve an exception or mutate the order.
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

        if (policy.EffectiveThrough is DateOnly through && through < policy.EffectiveFrom)
        {
            return Whole(ClosureDispositionV1.Refused, policy, ClosureAssessmentCodesV1.PolicyInvalid,
                "effectiveThrough");
        }

        if (!policy.IsActive || policy.SelectedForDate < policy.EffectiveFrom
            || policy.EffectiveThrough is DateOnly effectiveThrough && policy.SelectedForDate > effectiveThrough)
        {
            return Whole(ClosureDispositionV1.Unavailable, policy, ClosureAssessmentCodesV1.PolicyNotEffective,
                "selectedForDate");
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
        if (difference < 0m)
        {
            return Roll(balance.ParentRollId, ClosureDispositionV1.ReviewRequired, difference,
                code: ClosureAssessmentCodesV1.NegativeDifferenceReview);
        }

        // #66/5917438293 (3A): fire is exactly the declared scrap plus the declared process loss that was recovered
        // as neither scrap nor stock. Unexplained is intentionally not one of those declarations and can never be
        // promoted to fire here; SecondQuality and the other output classes never enter this numerator either.
        if (difference > 0m)
        {
            return Roll(balance.ParentRollId, ClosureDispositionV1.FireDeclarationRequired, difference,
                code: ClosureAssessmentCodesV1.FireDeclarationRequired,
                missing: ["scrapOrProcessLossDeclaration"]);
        }

        var fireKg = balance.OutputOf(OutputClass.Scrap).Value
            + balance.OutputOf(OutputClass.ProcessLoss).Value;
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

        // The caller supplies the exact BAL-004 inputs derived from the owned records. Neither a second-quality
        // output nor either declared fire component may be added again by the caller.
        if (supplied.Input.FireKg != fireKg)
        {
            return Roll(balance.ParentRollId, ClosureDispositionV1.Refused, difference,
                firePercent: calculated.Value!.FirePercent, code: ClosureAssessmentCodesV1.FireMassMismatch,
                refusedParameter: "fireKg");
        }

        // #66/5917438293 (4A): the denominator is exactly issued minus returned for this roll. A positive but
        // different caller value is just as unsafe as a missing/zero/negative value because it changes the ratio.
        if (supplied.Input.RawMaterialKg != balance.NetIssued.Value)
        {
            return Roll(balance.ParentRollId, ClosureDispositionV1.Refused, difference,
                firePercent: calculated.Value!.FirePercent, code: ClosureAssessmentCodesV1.NetIssuedMassMismatch,
                refusedParameter: "rawMaterialKg");
        }

        var firePercent = calculated.Value!.FirePercent;
        return Roll(balance.ParentRollId,
            firePercent > maximumFirePercent
                ? ClosureDispositionV1.ApprovalRequired
                : ClosureDispositionV1.Balanced,
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

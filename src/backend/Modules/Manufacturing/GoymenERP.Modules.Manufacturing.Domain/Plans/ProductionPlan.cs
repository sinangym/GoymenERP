using GoymenERP.Modules.Manufacturing.Domain.Common;

namespace GoymenERP.Modules.Manufacturing.Domain.Plans;

public enum PlanStatus
{
    Draft = 1,
    Approved = 2,
}

/// <summary>
/// What is intended to be produced, from which parent rolls, on which work center (plan v2
/// §7.3). A plan is not an order and not a realization: approving it records a decision and
/// nothing else — it issues no input, moves no stock and consumes nothing.
/// </summary>
public sealed class ProductionPlan
{
    private readonly List<PlannedInput> _inputs;

    public ProductionPlan(
        Guid id,
        Guid companyId,
        Guid workCenterId,
        ManufacturingProcess process,
        IEnumerable<PlannedInput> inputs,
        ProductionTarget? target = null,
        SlittingSpec? spec = null)
    {
        Id = RequireId(id, nameof(id));
        CompanyId = RequireId(companyId, nameof(companyId));
        WorkCenterId = RequireId(workCenterId, nameof(workCenterId));
        if (!Enum.IsDefined(process))
        {
            throw new ArgumentOutOfRangeException(nameof(process), process, "Tanımsız üretim süreci.");
        }

        Process = process;
        ArgumentNullException.ThrowIfNull(inputs);
        _inputs = inputs.ToList();
        if (_inputs.Any(input => input is null))
        {
            throw new ArgumentException("Planlanan girdi boş olamaz.", nameof(inputs));
        }

        if (_inputs.Count == 0)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.PlanWithoutInputs, "Plan en az bir ana rulo içermelidir.");
        }

        if (_inputs.Select(input => input.ParentRollId).Distinct().Count() != _inputs.Count)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.DuplicateRollInPlan, "Aynı ana rulo planda iki kez yer alamaz.");
        }

        Target = target;
        Spec = spec;
        Status = PlanStatus.Draft;
    }

    public Guid Id { get; }

    public Guid CompanyId { get; }

    public Guid WorkCenterId { get; }

    public ManufacturingProcess Process { get; }

    /// <summary>The plan's declared target, or null when the plan declares none.</summary>
    public ProductionTarget? Target { get; }

    /// <summary>
    /// MFG-6: what the plan cuts, or null for a plan that claims no engine computation (the MFG-5 plan:
    /// inputs and target only). Part of the plan's content: set at creation, never changed. A plan that
    /// carries one is approved only through <see cref="SlittingApprovalGate"/>.
    /// </summary>
    public SlittingSpec? Spec { get; }

    public PlanStatus Status { get; private set; }

    /// <summary>Optimistic concurrency, as on the production order (MFG-3): Approve names the version it read.</summary>
    public long Version { get; private set; } = 1;

    public Guid? ApprovedByUserId { get; private set; }

    public DateTimeOffset? ApprovedAtUtc { get; private set; }

    public IReadOnlyList<PlannedInput> Inputs => _inputs.AsReadOnly();

    /// <summary>
    /// Rebuilds a stored plan (MFG-5). Every rule of a new plan holds again, and the approval is
    /// replayed (version fence and draft rule), so a stored plan that breaks a rule is refused, not
    /// repaired. The stored version must be the one the plan's own changes add up to. The MFG-6
    /// approval gate is NOT re-run: a stored approval stands under the inputs it was approved with.
    /// </summary>
    public static ProductionPlan Restore(
        Guid id,
        Guid companyId,
        Guid workCenterId,
        ManufacturingProcess process,
        IEnumerable<PlannedInput> inputs,
        ProductionTarget? target,
        Guid? approvedByUserId,
        DateTimeOffset? approvedAtUtc,
        long version,
        SlittingSpec? spec = null)
    {
        var plan = new ProductionPlan(id, companyId, workCenterId, process, inputs, target, spec);
        if (approvedByUserId is { } approvedBy && approvedAtUtc is { } approvedAt)
        {
            plan.ApplyApproval(plan.Version, approvedBy, approvedAt);
        }
        else if (approvedByUserId is not null || approvedAtUtc is not null)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.StoredRecordInvalid, "Kayıtlı onay izi onaylayanı ve anı birlikte taşımalı.");
        }

        if (plan.Version != version)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.StoredRecordInvalid, "Kayıtlı plan sürümü planın kendi değişiklikleriyle uyuşmuyor.");
        }

        return plan;
    }

    /// <summary>Approves with nothing known from outside: a plan with a slitting spec is then refused, naming every missing input.</summary>
    public void Approve(long expectedVersion, Guid approvedByUserId, DateTimeOffset approvedAtUtc) =>
        Approve(expectedVersion, approvedByUserId, approvedAtUtc, parameters: null);

    /// <summary>
    /// The version fence first, then "only a draft is approved", then — for a plan with a slitting spec —
    /// the MFG-6 approval gate (<see cref="SlittingApprovalGate"/>): nothing is approved with gaps.
    /// </summary>
    public void Approve(long expectedVersion, Guid approvedByUserId, DateTimeOffset approvedAtUtc,
        SlittingPlanParameters? parameters)
    {
        RequireApprovable(expectedVersion);
        SlittingApprovalGate.Require(this, parameters);
        ApplyApproval(expectedVersion, approvedByUserId, approvedAtUtc);
    }

    private void ApplyApproval(long expectedVersion, Guid approvedByUserId, DateTimeOffset approvedAtUtc)
    {
        RequireApprovable(expectedVersion);
        ApprovedByUserId = RequireId(approvedByUserId, nameof(approvedByUserId));
        ApprovedAtUtc = approvedAtUtc.ToUniversalTime();
        Status = PlanStatus.Approved;
        Version++;
    }

    private void RequireApprovable(long expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ManufacturingVersionConflictException(expectedVersion, Version);
        }

        if (Status != PlanStatus.Draft)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.PlanNotDraft, "Yalnız taslak plan onaylanabilir.");
        }
    }

    private static Guid RequireId(Guid value, string parameterName) =>
        value == Guid.Empty ? throw new ArgumentException("Kimlik boş olamaz.", parameterName) : value;
}

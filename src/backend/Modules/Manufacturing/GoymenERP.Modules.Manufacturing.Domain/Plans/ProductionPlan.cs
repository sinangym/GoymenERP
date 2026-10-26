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
        SlittingSpec? spec = null,
        CutToLengthSpec? cutToLengthSpec = null)
    {
        Id = RequireId(id, nameof(id));
        CompanyId = RequireId(companyId, nameof(companyId));
        WorkCenterId = RequireId(workCenterId, nameof(workCenterId));
        if (!Enum.IsDefined(process))
        {
            throw new ArgumentOutOfRangeException(nameof(process), process, "Tanımsız üretim süreci.");
        }

        if (process != ManufacturingProcess.Slitting && spec is not null)
        {
            throw new ArgumentException("Dilme planı ayrıntıları yalnız Dilme/Sarım sürecinde kullanılabilir.", nameof(spec));
        }

        if (process != ManufacturingProcess.CutToLength && cutToLengthSpec is not null)
        {
            throw new ArgumentException("Boy kesme ayrıntıları yalnız Boy Kesme sürecinde kullanılabilir.", nameof(cutToLengthSpec));
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

        RequireConsistentCommercialContext(_inputs, nameof(inputs));

        Target = target;
        Spec = spec;
        CutToLengthSpec = cutToLengthSpec;
        Status = PlanStatus.Draft;
    }

    public Guid Id { get; }

    public Guid CompanyId { get; }

    /// <summary>
    /// The work center the plan is for. A Draft revision (<see cref="Revise"/>) may move it to another work center
    /// of the SAME process; the process itself never changes.
    /// </summary>
    public Guid WorkCenterId { get; private set; }

    public ManufacturingProcess Process { get; }

    /// <summary>The plan's declared target, or null when the plan declares none.</summary>
    public ProductionTarget? Target { get; private set; }

    /// <summary>
    /// MFG-6: what the plan cuts, or null for a plan that claims no engine computation (the MFG-5 plan:
    /// inputs and target only). Part of the plan's content: set at creation, never changed. A plan that
    /// carries one is approved only through <see cref="SlittingApprovalGate"/>.
    /// </summary>
    public SlittingSpec? Spec { get; }

    /// <summary>Ordered coil-to-sheet cut lines; null on legacy plans and every other process.</summary>
    public CutToLengthSpec? CutToLengthSpec { get; private set; }

    public PlanStatus Status { get; private set; }

    /// <summary>Optimistic concurrency, as on the production order (MFG-3): Approve names the version it read.</summary>
    public long Version { get; private set; } = 1;

    /// <summary>
    /// MFG-PLAN-EDIT: how many Draft revisions the plan carries. Revision 0 is the plan as created; each revision
    /// replaces the work center, the planned inputs and the target as a whole and advances the version by one.
    /// </summary>
    public int RevisionCount { get; private set; }

    public Guid? ApprovedByUserId { get; private set; }

    public DateTimeOffset? ApprovedAtUtc { get; private set; }

    public IReadOnlyList<PlannedInput> Inputs => _inputs.AsReadOnly();

    /// <summary>
    /// Rebuilds a stored plan (MFG-5). Every rule of a new plan holds again, and the approval is
    /// replayed (version fence and draft rule), so a stored plan that breaks a rule is refused, not
    /// repaired. The stored version must be the one the plan's own changes add up to. The MFG-6
    /// approval gate is NOT re-run: a stored approval stands under the inputs it was approved with.
    /// MFG-PLAN-EDIT: the stored Draft revisions are replayed, in their order, through <see cref="Revise"/> BEFORE the
    /// approval (a revision exists only on a Draft); the version then counts one step per revision.
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
        SlittingSpec? spec = null,
        IEnumerable<PlanRevisionContent>? revisions = null,
        CutToLengthSpec? cutToLengthSpec = null)
    {
        if (process != ManufacturingProcess.Slitting && spec is not null)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.StoredRecordInvalid,
                "Kayıtlı dilme planı ayrıntıları Dilme/Sarım dışındaki bir sürece bağlı olamaz.");
        }


        if (process != ManufacturingProcess.CutToLength && cutToLengthSpec is not null)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.StoredRecordInvalid,
                "Kayıtlı boy kesme ayrıntıları Boy Kesme dışındaki bir sürece bağlı olamaz.");
        }

        var plan = new ProductionPlan(id, companyId, workCenterId, process, inputs, target, spec, cutToLengthSpec);
        foreach (var revision in revisions ?? [])
        {
            ArgumentNullException.ThrowIfNull(revision);
            try
            {
                plan.Revise(plan.Version, revision.WorkCenterId, process, revision.Inputs, revision.Target,
                    revision.CutToLengthSpec);
            }
            catch (ManufacturingRuleException exception)
            {
                // A stored revision the rules would refuse is refused, not repaired.
                throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                    $"Kayıtlı plan revizyonu planın kurallarını karşılamıyor ({exception.Code}).");
            }
        }

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

    /// <summary>
    /// MFG-PLAN-EDIT: the edit of a plan that has no downstream effect yet. Only a Draft is revised: an approved plan
    /// has recorded a decision and may have opened orders (one approved plan may open more than one order,
    /// #66 5728934213), and an order carries its own snapshot from which inputs are issued — so an approved plan is
    /// never edited here; a post-approval revision with snapshot and difference (PR-09) is a separate, undecided flow.
    /// A revision replaces the work center, the planned inputs and the target AS A WHOLE and advances the version by
    /// one. In order, and nothing is applied on a refusal:
    /// <list type="number">
    /// <item>the version fence (<see cref="ManufacturingVersionConflictException"/>);</item>
    /// <item>only a Draft (<see cref="ManufacturingRuleCodes.PlanNotDraft"/>);</item>
    /// <item>a plan with a slitting spec is not revised (<see cref="ManufacturingRuleCodes.PlanSlittingSpecFixed"/>):
    /// how its spec balances against the parent coil is open (AT-044, #66 NEEDS-BUSINESS 5741547443);</item>
    /// <item>the process is fixed (<see cref="ManufacturingRuleCodes.PlanProcessFixed"/>): the new work center must run
    /// the plan's own process — no plan changes its process while process chains are open;</item>
    /// <item>the plan's own input rules (at least one roll, no roll twice);</item>
    /// <item>a revision that changes nothing is refused (<see cref="ManufacturingRuleCodes.PlanRevisionNoChange"/>),
    /// so the history holds only real changes.</item>
    /// </list>
    /// </summary>
    public void Revise(long expectedVersion, Guid workCenterId, ManufacturingProcess workCenterProcess,
        IEnumerable<PlannedInput> inputs, ProductionTarget? target, CutToLengthSpec? cutToLengthSpec = null)
    {
        RequireRevisable(expectedVersion);
        var newWorkCenter = RequireId(workCenterId, nameof(workCenterId));
        if (!Enum.IsDefined(workCenterProcess))
        {
            throw new ArgumentOutOfRangeException(nameof(workCenterProcess), workCenterProcess, "Tanımsız üretim süreci.");
        }

        if (workCenterProcess != Process)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.PlanProcessFixed,
                "Planın süreci değişmez; yeni iş merkezi planın kendi sürecini yürütmelidir.");
        }


        if (Process != ManufacturingProcess.CutToLength && cutToLengthSpec is not null)
        {
            throw new ArgumentException("Boy kesme ayrıntıları yalnız Boy Kesme sürecinde kullanılabilir.", nameof(cutToLengthSpec));
        }

        ArgumentNullException.ThrowIfNull(inputs);
        var newInputs = inputs.ToList();
        if (newInputs.Any(input => input is null))
        {
            throw new ArgumentException("Planlanan girdi boş olamaz.", nameof(inputs));
        }

        if (newInputs.Count == 0)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.PlanWithoutInputs, "Plan en az bir ana rulo içermelidir.");
        }

        if (newInputs.Select(input => input.ParentRollId).Distinct().Count() != newInputs.Count)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.DuplicateRollInPlan, "Aynı ana rulo planda iki kez yer alamaz.");
        }

        RequireConsistentCommercialContext(newInputs, nameof(inputs));

        if (newWorkCenter == WorkCenterId && Equals(target, Target) && newInputs.SequenceEqual(_inputs)
            && CutSpecsEqual(cutToLengthSpec, CutToLengthSpec))
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.PlanRevisionNoChange,
                "Düzenleme planda hiçbir şeyi değiştirmiyor.");
        }

        WorkCenterId = newWorkCenter;
        Target = target;
        CutToLengthSpec = cutToLengthSpec;
        _inputs.Clear();
        _inputs.AddRange(newInputs);
        RevisionCount++;
        Version++;
    }

    /// <summary>
    /// The revision's preconditions on the plan as it stands — the version fence, only a Draft, no slitting spec —
    /// without a new content: a caller may ask before it reads anything else, and <see cref="Revise"/> asks again.
    /// </summary>
    public void RequireRevisable(long expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ManufacturingVersionConflictException(expectedVersion, Version);
        }

        if (Status != PlanStatus.Draft)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.PlanNotDraft,
                "Yalnız taslak plan düzenlenebilir; onaylı plan bir karar kaydetmiştir ve emir açmış olabilir.");
        }

        if (Spec is not null)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.PlanSlittingSpecFixed,
                "Dilme ayrıntısı taşıyan plan düzenlenmez; ana ruloyla dengesi iş kararı bekliyor (AT-044).");
        }
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

    private static bool CutSpecsEqual(CutToLengthSpec? left, CutToLengthSpec? right) =>
        ReferenceEquals(left, right)
        || left is not null && right is not null && left.Lines.SequenceEqual(right.Lines);

    private static void RequireConsistentCommercialContext(
        IReadOnlyList<PlannedInput> inputs,
        string parameterName)
    {
        // Null is legacy/unknown provenance, never an inference that the material belongs to the
        // company. Physical owner admission remains an application-layer responsibility. When
        // commercial provenance is present, every such input in one plan names one exact context.
        var expected = inputs
            .Select(input => input.CommercialContext)
            .FirstOrDefault(context => context is not null);
        if (expected is null)
        {
            return;
        }

        if (inputs.Any(input => input.CommercialContext is { } context && context != expected))
        {
            throw new ArgumentException(
                "Bir plandaki müşteri malı girdiler aynı taraf, hesap ve sürüm bağlamını taşımalıdır.",
                parameterName);
        }
    }
}

/// <summary>
/// MFG-PLAN-EDIT: one stored Draft revision's content — the work center, the planned inputs and the target that
/// replaced the previous ones as a whole. <see cref="ProductionPlan.Restore"/> replays them in their order.
/// </summary>
public sealed record PlanRevisionContent(Guid WorkCenterId, IReadOnlyList<PlannedInput> Inputs, ProductionTarget? Target,
    CutToLengthSpec? CutToLengthSpec = null);

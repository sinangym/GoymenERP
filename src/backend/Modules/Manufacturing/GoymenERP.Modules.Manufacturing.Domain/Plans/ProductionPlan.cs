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
        ProductionTarget? target = null)
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
        if (_inputs.Count == 0)
        {
            throw new ArgumentException("Plan en az bir ana rulo içermelidir.", nameof(inputs));
        }

        if (_inputs.Select(input => input.ParentRollId).Distinct().Count() != _inputs.Count)
        {
            throw new ArgumentException("Aynı ana rulo planda iki kez yer alamaz.", nameof(inputs));
        }

        Target = target;
        Status = PlanStatus.Draft;
    }

    public Guid Id { get; }

    public Guid CompanyId { get; }

    public Guid WorkCenterId { get; }

    public ManufacturingProcess Process { get; }

    /// <summary>The plan's declared target, or null when the plan declares none.</summary>
    public ProductionTarget? Target { get; }

    public PlanStatus Status { get; private set; }

    public Guid? ApprovedByUserId { get; private set; }

    public DateTimeOffset? ApprovedAtUtc { get; private set; }

    public IReadOnlyList<PlannedInput> Inputs => _inputs;

    public void Approve(Guid approvedByUserId, DateTimeOffset approvedAtUtc)
    {
        if (Status != PlanStatus.Draft)
        {
            throw new ManufacturingRuleException("plan_not_draft", "Yalnız taslak plan onaylanabilir.");
        }

        ApprovedByUserId = RequireId(approvedByUserId, nameof(approvedByUserId));
        ApprovedAtUtc = approvedAtUtc;
        Status = PlanStatus.Approved;
    }

    private static Guid RequireId(Guid value, string parameterName) =>
        value == Guid.Empty ? throw new ArgumentException("Kimlik boş olamaz.", parameterName) : value;
}

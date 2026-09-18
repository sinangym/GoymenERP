using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Modules.Manufacturing.Domain.Orders;

/// <summary>
/// The executable instruction created from an APPROVED plan (plan v2 §7.3: recipe, plan, order
/// and realization are separate). The order keeps its own snapshot of the plan's inputs, so a
/// later change to a plan or a master does not rewrite it.
/// <para>
/// Realization is recorded here as facts, in any number of partial steps: input issued from a
/// parent roll, unused input returned, outputs declared against their parent roll, and the mass
/// still in process. The stock movements those facts cause belong to Inventory and are not
/// performed by this module.
/// </para>
/// </summary>
public sealed class ProductionOrder
{
    private readonly List<PlannedInput> _plannedInputs;
    private readonly Dictionary<Guid, decimal> _issued = [];
    private readonly Dictionary<Guid, decimal> _returned = [];
    private readonly Dictionary<Guid, decimal> _workInProgress = [];
    private readonly List<OutputDeclaration> _outputs = [];

    private ProductionOrder(Guid id, ProductionPlan plan)
    {
        Id = id == Guid.Empty ? throw new ArgumentException("Kimlik boş olamaz.", nameof(id)) : id;
        PlanId = plan.Id;
        CompanyId = plan.CompanyId;
        WorkCenterId = plan.WorkCenterId;
        Process = plan.Process;
        _plannedInputs = plan.Inputs.ToList();
    }

    public Guid Id { get; }

    public Guid PlanId { get; }

    public Guid CompanyId { get; }

    public Guid WorkCenterId { get; }

    public ManufacturingProcess Process { get; }

    public IReadOnlyList<PlannedInput> PlannedInputs => _plannedInputs;

    public IReadOnlyList<OutputDeclaration> Outputs => _outputs;

    public static ProductionOrder FromApprovedPlan(Guid id, ProductionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Status != PlanStatus.Approved)
        {
            throw new ManufacturingRuleException(
                "plan_not_approved",
                "Üretim emri yalnız onaylı plandan oluşturulabilir.");
        }

        return new ProductionOrder(id, plan);
    }

    /// <summary>Input actually taken from a planned parent roll into production.</summary>
    public void IssueInput(Guid parentRollId, Kilograms mass)
    {
        RequirePlannedRoll(parentRollId);
        RequirePositive(mass, nameof(mass));
        _issued[parentRollId] = IssuedOf(parentRollId) + mass.Value;
    }

    /// <summary>Unused input sent back; it can never exceed what is still issued.</summary>
    public void ReturnUnusedInput(Guid parentRollId, Kilograms mass)
    {
        RequireIssuedRoll(parentRollId);
        RequirePositive(mass, nameof(mass));
        if (ReturnedOf(parentRollId) + mass.Value > IssuedOf(parentRollId))
        {
            throw new ManufacturingRuleException(
                "return_exceeds_issued",
                "İade edilen kütle çıkış yapılan kütleyi aşamaz.");
        }

        _returned[parentRollId] = ReturnedOf(parentRollId) + mass.Value;
    }

    /// <summary>
    /// One output of the run, bound to the parent roll it was cut from. Cost is not calculated
    /// in this phase (plan v2 R6), so every declaration carries <see cref="CostResult.NotCalculated"/>.
    /// </summary>
    public OutputDeclaration DeclareOutput(Guid outputId, Guid parentRollId, OutputClass outputClass, Kilograms mass)
    {
        if (outputId == Guid.Empty)
        {
            throw new ArgumentException("Çıktı kimliği boş olamaz.", nameof(outputId));
        }

        if (!Enum.IsDefined(outputClass))
        {
            throw new ArgumentOutOfRangeException(nameof(outputClass), outputClass, "Tanımsız çıktı sınıfı.");
        }

        RequireIssuedRoll(parentRollId);
        RequirePositive(mass, nameof(mass));
        if (_outputs.Any(output => output.OutputId == outputId))
        {
            throw new ManufacturingRuleException("output_already_declared", "Bu çıktı zaten beyan edildi.");
        }

        var declaration = new OutputDeclaration(outputId, parentRollId, outputClass, mass, CostResult.NotCalculated);
        _outputs.Add(declaration);
        return declaration;
    }

    /// <summary>The mass of this parent roll that is still in process, as measured now.</summary>
    public void RecordWorkInProgress(Guid parentRollId, Kilograms mass)
    {
        RequireIssuedRoll(parentRollId);
        _workInProgress[parentRollId] = mass.Value;
    }

    public MassBalance BalanceOf(Guid parentRollId)
    {
        RequirePlannedRoll(parentRollId);
        var byClass = _outputs
            .Where(output => output.ParentRollId == parentRollId)
            .GroupBy(output => output.Class)
            .ToDictionary(
                group => group.Key,
                group => group.Aggregate(Kilograms.Zero, (sum, output) => sum + output.Mass));

        return new MassBalance(
            parentRollId,
            new Kilograms(IssuedOf(parentRollId)),
            new Kilograms(ReturnedOf(parentRollId)),
            byClass,
            new Kilograms(_workInProgress.GetValueOrDefault(parentRollId)));
    }

    private decimal IssuedOf(Guid parentRollId) => _issued.GetValueOrDefault(parentRollId);

    private decimal ReturnedOf(Guid parentRollId) => _returned.GetValueOrDefault(parentRollId);

    private void RequirePlannedRoll(Guid parentRollId)
    {
        if (_plannedInputs.All(input => input.ParentRollId != parentRollId))
        {
            throw new ManufacturingRuleException(
                "roll_not_in_order",
                "Ana rulo bu emrin planlanan girdileri arasında değil.");
        }
    }

    private void RequireIssuedRoll(Guid parentRollId)
    {
        RequirePlannedRoll(parentRollId);
        if (IssuedOf(parentRollId) == 0m)
        {
            throw new ManufacturingRuleException(
                "roll_not_issued",
                "Bu ana rulodan henüz üretime girdi çıkışı yapılmadı.");
        }
    }

    private static void RequirePositive(Kilograms mass, string parameterName)
    {
        if (mass.Value == 0m)
        {
            throw new ArgumentException("Kütle sıfırdan büyük olmalıdır.", parameterName);
        }
    }
}

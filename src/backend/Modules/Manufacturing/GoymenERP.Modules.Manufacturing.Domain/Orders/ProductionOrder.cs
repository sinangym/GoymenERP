using System.Diagnostics;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Modules.Manufacturing.Domain.Orders;

/// <summary>
/// The executable instruction created from an APPROVED plan (plan v2 §7.3: recipe, plan, order
/// and realization are separate). The order keeps its own snapshot of the plan's inputs, so a
/// later change to a plan or a master does not rewrite it.
/// <para>
/// Realization is recorded as facts, each with its own id and time (R6), in any number of
/// partial steps: input issued from a parent roll, unused input returned, outputs declared
/// against their parent roll, and measurements of the mass still in process. Every total is
/// derived from those facts. The stock movements they cause belong to Inventory and are not
/// performed by this module.
/// </para>
/// </summary>
public sealed class ProductionOrder
{
    private readonly List<PlannedInput> _plannedInputs;
    private readonly List<InputIssue> _issues = [];
    private readonly List<InputReturn> _returns = [];
    private readonly List<WipMeasurement> _wipMeasurements = [];
    private readonly List<OutputDeclaration> _outputs = [];

    private ProductionOrder(Guid id, ProductionPlan plan)
    {
        RequireId(id, nameof(id));
        Id = id;
        PlanId = plan.Id;
        CompanyId = plan.CompanyId;
        WorkCenterId = plan.WorkCenterId;
        Process = plan.Process;
        Target = plan.Target;
        _plannedInputs = plan.Inputs.ToList();
        Status = OrderStatus.Open;
    }

    public Guid Id { get; }

    public Guid PlanId { get; }

    public Guid CompanyId { get; }

    public Guid WorkCenterId { get; }

    public ManufacturingProcess Process { get; }

    /// <summary>The plan's declared target as it stood when the order was created.</summary>
    public ProductionTarget? Target { get; }

    public OrderStatus Status { get; private set; }

    public Guid? ClosedByUserId { get; private set; }

    public DateTimeOffset? ClosedAtUtc { get; private set; }

    // Read-only views (ÜRÜN #280 P3-1): a cast cannot reach the lists behind them, so the
    // duplicate, roll and closed-order rules below cannot be bypassed.
    public IReadOnlyList<PlannedInput> PlannedInputs => _plannedInputs.AsReadOnly();

    public IReadOnlyList<InputIssue> Issues => _issues.AsReadOnly();

    public IReadOnlyList<InputReturn> Returns => _returns.AsReadOnly();

    public IReadOnlyList<WipMeasurement> WipMeasurements => _wipMeasurements.AsReadOnly();

    public IReadOnlyList<OutputDeclaration> Outputs => _outputs.AsReadOnly();

    public static ProductionOrder FromApprovedPlan(Guid id, ProductionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Status != PlanStatus.Approved)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.PlanNotApproved,
                "Üretim emri yalnız onaylı plandan oluşturulabilir.");
        }

        return new ProductionOrder(id, plan);
    }

    /// <summary>Input actually taken from a planned parent roll into production.</summary>
    public InputIssue IssueInput(Guid issueId, Guid parentRollId, Kilograms mass, DateTimeOffset issuedAt)
    {
        RequireOpen();
        RequireId(issueId, nameof(issueId));
        RequirePlannedRoll(parentRollId);
        RequirePositive(mass, nameof(mass));
        if (_issues.Any(issue => issue.IssueId == issueId))
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.InputAlreadyIssued, "Bu girdi çıkışı zaten kaydedildi.");
        }

        var issue = new InputIssue(issueId, parentRollId, mass, issuedAt.ToUniversalTime());
        _issues.Add(issue);
        return issue;
    }

    /// <summary>Unused input sent back; it can never exceed what is still issued.</summary>
    public InputReturn ReturnUnusedInput(Guid returnId, Guid parentRollId, Kilograms mass, DateTimeOffset returnedAt)
    {
        RequireOpen();
        RequireId(returnId, nameof(returnId));
        RequireIssuedRoll(parentRollId);
        RequirePositive(mass, nameof(mass));
        if (_returns.Any(entry => entry.ReturnId == returnId))
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.ReturnAlreadyRecorded, "Bu iade zaten kaydedildi.");
        }

        if (ReturnedOf(parentRollId) + mass.Value > IssuedOf(parentRollId))
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.ReturnExceedsIssued,
                "İade edilen kütle çıkış yapılan kütleyi aşamaz.");
        }

        var entry = new InputReturn(returnId, parentRollId, mass, returnedAt.ToUniversalTime());
        _returns.Add(entry);
        return entry;
    }

    /// <summary>
    /// One output of the run, bound to the parent roll it was cut from. Cost is not calculated
    /// in this phase (plan v2 R6), so every declaration carries <see cref="CostResult.NotCalculated"/>.
    /// </summary>
    public OutputDeclaration DeclareOutput(
        Guid outputId, Guid parentRollId, OutputClass outputClass, Kilograms mass, DateTimeOffset declaredAt)
    {
        RequireOpen();
        RequireId(outputId, nameof(outputId));
        if (!Enum.IsDefined(outputClass))
        {
            throw new ArgumentOutOfRangeException(nameof(outputClass), outputClass, "Tanımsız çıktı sınıfı.");
        }

        RequireIssuedRoll(parentRollId);
        RequirePositive(mass, nameof(mass));
        if (_outputs.Any(output => output.OutputId == outputId))
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.OutputAlreadyDeclared, "Bu çıktı zaten beyan edildi.");
        }

        var declaration = new OutputDeclaration(
            outputId, parentRollId, outputClass, mass, declaredAt.ToUniversalTime(), CostResult.NotCalculated);
        _outputs.Add(declaration);
        return declaration;
    }

    /// <summary>
    /// A measurement of this parent roll's mass still in process. The latest measurement is the
    /// roll's work in progress, so a measurement that is not strictly later than the latest one
    /// already recorded is refused: an old reading can neither overwrite a newer one nor tie
    /// with it.
    /// </summary>
    public WipMeasurement RecordWorkInProgress(Guid measurementId, Guid parentRollId, Kilograms mass, DateTimeOffset measuredAt)
    {
        RequireOpen();
        RequireId(measurementId, nameof(measurementId));
        RequireIssuedRoll(parentRollId);
        if (_wipMeasurements.Any(entry => entry.MeasurementId == measurementId))
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.WipAlreadyMeasured, "Bu WIP ölçümü zaten kaydedildi.");
        }

        var at = measuredAt.ToUniversalTime();
        var latest = LatestWipOf(parentRollId);
        if (latest is not null && at <= latest.MeasuredAtUtc)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.WipMeasurementNotLatest,
                "WIP ölçümü bu rulonun son ölçümünden sonra olmalıdır.");
        }

        var measurement = new WipMeasurement(measurementId, parentRollId, mass, at);
        _wipMeasurements.Add(measurement);
        return measurement;
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
            byClass.AsReadOnly(),
            LatestWipOf(parentRollId)?.Mass ?? Kilograms.Zero);
    }

    /// <summary>
    /// Plan v2 §7.3 and Ö7: with no approved tolerance profile, every non-zero unexplained
    /// difference is an exception. An order with one does not close; the difference stays on the
    /// order under its own name and is never written off as loss. The Ö7 exception path (reason +
    /// single approval) needs a named approver and is a recorded dependency, not built here.
    /// </summary>
    public void Close(Guid closedByUserId, DateTimeOffset closedAt)
    {
        RequireOpen();
        RequireId(closedByUserId, nameof(closedByUserId));

        var balances = _plannedInputs.Select(input => BalanceOf(input.ParentRollId)).ToList();

        // TEMPORARY — awaiting Business confirmation ("Üretim emri, işlemde kütle (WIP) varken
        // kapanabilir mi?"). Mass still in process would be left in an ownerless position, so the
        // order stays open until that mass is declared as output or returned. Recording the WIP
        // as zero without doing either does not help: the same mass then shows up as an
        // unexplained difference, which also keeps the order open.
        var inProcess = balances.Where(balance => balance.WorkInProgress.Value != 0m).ToList();
        if (inProcess.Count > 0)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.WipOpen,
                "İşlemde kütle (WIP) varken emir kapatılamaz: "
                + string.Join(", ", inProcess.Select(balance => $"{balance.ParentRollId} ({balance.WorkInProgress})")));
        }

        var unaccounted = balances.Where(balance => !balance.IsFullyAccounted).ToList();
        if (unaccounted.Count > 0)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.UnexplainedDifference,
                "Açıklanamayan kütle farkı olan emir kapatılamaz: "
                + string.Join(", ", unaccounted.Select(balance => $"{balance.ParentRollId} ({balance.Unexplained} kg)")));
        }

        ClosedByUserId = closedByUserId;
        ClosedAtUtc = closedAt.ToUniversalTime();
        Status = OrderStatus.Closed;
    }

    /// <summary>
    /// The open part of the plan's declared target: parent-roll mass issued net of returns for
    /// <see cref="TargetBasis.InputMass"/>, good-product output for
    /// <see cref="TargetBasis.GoodOutputMass"/>. Without a declared target the answer is
    /// <see cref="RemainingTarget.Unavailable"/>.
    /// </summary>
    public RemainingTarget RemainingTargetOf()
    {
        if (Target is null)
        {
            return RemainingTarget.Unavailable;
        }

        var achieved = Target.Basis switch
        {
            TargetBasis.InputMass => _plannedInputs.Aggregate(
                Kilograms.Zero, (sum, input) => sum + BalanceOf(input.ParentRollId).NetIssued),
            TargetBasis.GoodOutputMass => _outputs
                .Where(output => output.Class == OutputClass.GoodProduct)
                .Aggregate(Kilograms.Zero, (sum, output) => sum + output.Mass),
            // ProductionTarget refuses any other basis at construction.
            _ => throw new UnreachableException(),
        };
        return RemainingTarget.Of(Target, achieved);
    }

    private decimal IssuedOf(Guid parentRollId) =>
        _issues.Where(issue => issue.ParentRollId == parentRollId).Sum(issue => issue.Mass.Value);

    private decimal ReturnedOf(Guid parentRollId) =>
        _returns.Where(entry => entry.ParentRollId == parentRollId).Sum(entry => entry.Mass.Value);

    private WipMeasurement? LatestWipOf(Guid parentRollId) =>
        _wipMeasurements
            .Where(entry => entry.ParentRollId == parentRollId)
            .MaxBy(entry => entry.MeasuredAtUtc);

    private void RequireOpen()
    {
        if (Status != OrderStatus.Open)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.OrderClosed, "Kapanmış emre yeni gerçekleşme yazılamaz.");
        }
    }

    private void RequirePlannedRoll(Guid parentRollId)
    {
        if (_plannedInputs.All(input => input.ParentRollId != parentRollId))
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.RollNotInOrder,
                "Ana rulo bu emrin planlanan girdileri arasında değil.");
        }
    }

    private void RequireIssuedRoll(Guid parentRollId)
    {
        RequirePlannedRoll(parentRollId);
        if (IssuedOf(parentRollId) == 0m)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.RollNotIssued,
                "Bu ana rulodan henüz üretime girdi çıkışı yapılmadı.");
        }
    }

    private static void RequireId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Kimlik boş olamaz.", parameterName);
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

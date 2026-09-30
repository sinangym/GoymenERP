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

    /// <summary>
    /// Optimistic concurrency (MFG-3, SİPARİŞ #292 P3-a): every mutation names the version it
    /// read; a stale one is refused whole with <see cref="ManufacturingVersionConflictException"/>
    /// before any rule runs, and every recorded change advances it by one. A refused rule leaves
    /// it unchanged.
    /// </summary>
    public long Version { get; private set; } = 1;

    public Guid? ClosedByUserId { get; private set; }

    public DateTimeOffset? ClosedAtUtc { get; private set; }

    /// <summary>The exact Settings profile selected for a policy-backed close; null for an open or legacy order.</summary>
    public ClosureTolerancePolicySnapshotV1? ClosurePolicy { get; private set; }

    /// <summary>The dedicated exception approval used by an out-of-tolerance close, if one was required.</summary>
    public ClosureExceptionApprovalV1? ClosureExceptionApproval { get; private set; }

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

    /// <summary>
    /// Rebuilds a stored order from its snapshot and its stored facts (MFG-5). The facts are
    /// replayed through the same methods that recorded them — issues first, then returns, outputs
    /// and WIP readings, each in time order — so every rule (fact identity, parent roll, return
    /// bound, latest WIP reading) holds again, and a stored close is replayed through
    /// <see cref="Close"/>, so a closed order must still reconcile roll by roll. A stored record
    /// that breaks a rule is refused, not repaired; the stored version must be exactly the one its
    /// facts add up to.
    /// </summary>
    public static ProductionOrder Restore(
        Guid id,
        Guid planId,
        Guid companyId,
        Guid workCenterId,
        ManufacturingProcess process,
        ProductionTarget? target,
        IEnumerable<PlannedInput> plannedInputs,
        IEnumerable<InputIssue> issues,
        IEnumerable<InputReturn> returns,
        IEnumerable<OutputDeclaration> outputs,
        IEnumerable<WipMeasurement> wipMeasurements,
        Guid? closedByUserId,
        DateTimeOffset? closedAtUtc,
        long version,
        ClosureTolerancePolicySnapshotV1? closurePolicy = null,
        ClosureExceptionApprovalV1? closureExceptionApproval = null)
    {
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(returns);
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentNullException.ThrowIfNull(wipMeasurements);

        // The plan's own rules decide the snapshot: at least one input, each parent roll once.
        var snapshot = new ProductionPlan(planId, companyId, workCenterId, process, plannedInputs, target);
        var order = new ProductionOrder(id, snapshot);

        foreach (var issue in issues.OrderBy(fact => fact.IssuedAtUtc))
        {
            order.IssueInput(order.Version, issue.IssueId, issue.ParentRollId, issue.Mass, issue.IssuedAtUtc);
        }

        foreach (var entry in returns.OrderBy(fact => fact.ReturnedAtUtc))
        {
            order.ReturnUnusedInput(order.Version, entry.ReturnId, entry.ParentRollId, entry.Mass, entry.ReturnedAtUtc);
        }

        foreach (var output in outputs.OrderBy(fact => fact.DeclaredAtUtc))
        {
            if (output.Cost != CostResult.NotCalculated)
            {
                throw new ManufacturingRuleException(
                    ManufacturingRuleCodes.StoredRecordInvalid, "Kayıtlı çıktının maliyet durumu bu dilimde üretilemez.");
            }

            order.DeclareOutput(order.Version, output.OutputId, output.ParentRollId, output.Class, output.Mass,
                output.DeclaredAtUtc);
        }

        foreach (var measurement in wipMeasurements.OrderBy(fact => fact.MeasuredAtUtc))
        {
            order.RecordWorkInProgress(order.Version, measurement.MeasurementId, measurement.ParentRollId,
                measurement.Mass, measurement.MeasuredAtUtc);
        }

        if (closedByUserId is { } closedBy && closedAtUtc is { } closedAt)
        {
            if (closurePolicy is null)
            {
                if (closureExceptionApproval is not null)
                {
                    throw new ManufacturingRuleException(
                        ManufacturingRuleCodes.StoredRecordInvalid,
                        "Kayıtlı istisna onayı tolerans profili olmadan taşınamaz.");
                }

                // Historical rows closed before the policy-backed runtime remain readable. New writes use
                // CloseWithPolicy and always persist the exact Settings snapshot.
                order.Close(order.Version, closedBy, closedAt);
            }
            else
            {
                order.CloseWithPolicy(order.Version, closedBy, closedAt, closurePolicy, closureExceptionApproval);
            }
        }
        else if (closedByUserId is not null || closedAtUtc is not null)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.StoredRecordInvalid, "Kayıtlı kapanış izi kapatanı ve anı birlikte taşımalı.");
        }
        else if (closurePolicy is not null || closureExceptionApproval is not null)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.StoredRecordInvalid, "Açık emir kapanış profili veya onayı taşıyamaz.");
        }

        if (order.Version != version)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.StoredRecordInvalid, "Kayıtlı emir sürümü olgularının toplamıyla uyuşmuyor.");
        }

        return order;
    }

    /// <summary>Input actually taken from a planned parent roll into production.</summary>
    public InputIssue IssueInput(long expectedVersion, Guid issueId, Guid parentRollId, Kilograms mass, DateTimeOffset issuedAt)
    {
        RequireVersion(expectedVersion);
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
        Version++;
        return issue;
    }

    /// <summary>Unused input sent back; it can never exceed what is still issued.</summary>
    public InputReturn ReturnUnusedInput(long expectedVersion, Guid returnId, Guid parentRollId, Kilograms mass, DateTimeOffset returnedAt)
    {
        RequireVersion(expectedVersion);
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
        Version++;
        return entry;
    }

    /// <summary>
    /// One output of the run, bound to the parent roll it was cut from. Cost is not calculated
    /// in this phase (plan v2 R6), so every declaration carries <see cref="CostResult.NotCalculated"/>.
    /// </summary>
    public OutputDeclaration DeclareOutput(
        long expectedVersion, Guid outputId, Guid parentRollId, OutputClass outputClass, Kilograms mass, DateTimeOffset declaredAt)
    {
        RequireVersion(expectedVersion);
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
        Version++;
        return declaration;
    }

    /// <summary>
    /// A measurement of this parent roll's mass still in process. The latest measurement is the
    /// roll's work in progress, so a measurement that is not strictly later than the latest one
    /// already recorded is refused: an old reading can neither overwrite a newer one nor tie
    /// with it.
    /// </summary>
    public WipMeasurement RecordWorkInProgress(long expectedVersion, Guid measurementId, Guid parentRollId, Kilograms mass, DateTimeOffset measuredAt)
    {
        RequireVersion(expectedVersion);
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
        Version++;
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
    /// Read-only OPEN-005 / Ö7 preparation. The supplied snapshot and BAL-004 inputs come from their future owners;
    /// this method chooses no percentage, numerator, denominator, approval or output fact and changes no order state.
    /// </summary>
    public OrderClosureAssessmentV1 AssessClosure(
        ClosureTolerancePolicySnapshotV1 policy,
        IReadOnlyList<RollScrapPercentInputV1> scrapPercentInputs) =>
        OrderClosureAssessmentCalculatorV1.Assess(this, policy, scrapPercentInputs);

    /// <summary>
    /// The runtime assessment. Both BAL-004 operands are derived from this order's immutable facts: declared Scrap
    /// plus ProcessLoss over issued minus returned. A caller cannot substitute a class or denominator.
    /// </summary>
    public OrderClosureAssessmentV1 AssessClosure(ClosureTolerancePolicySnapshotV1 policy) =>
        AssessClosure(policy, _plannedInputs.Select(input =>
        {
            var balance = BalanceOf(input.ParentRollId);
            return new RollScrapPercentInputV1(input.ParentRollId, new(
                balance.OutputOf(OutputClass.Scrap).Value + balance.OutputOf(OutputClass.ProcessLoss).Value,
                balance.NetIssued.Value));
        }).ToArray());

    /// <summary>
    /// The policy-backed close used by the runtime. It persists the exact Settings snapshot and accepts an
    /// out-of-tolerance result only with immutable evidence created after the application freshly proved the
    /// dedicated approval permission. The evidence is checked again here against this company, order, actor,
    /// pre-close order version and policy version; it never supplies authority by itself.
    /// </summary>
    public void CloseWithPolicy(long expectedVersion, Guid closedByUserId, DateTimeOffset closedAt,
        ClosureTolerancePolicySnapshotV1 policy, ClosureExceptionApprovalV1? exceptionApproval)
    {
        RequireClosable(expectedVersion);
        RequireId(closedByUserId, nameof(closedByUserId));
        ArgumentNullException.ThrowIfNull(policy);

        var closeAtUtc = closedAt.ToUniversalTime();
        if (policy.SelectedForDate != DateOnly.FromDateTime(closeAtUtc.UtcDateTime))
        {
            throw new ManufacturingRuleException(
                ClosureAssessmentCodesV1.PolicyScopeMismatch,
                "Tolerans profili kapanışın UTC iş tarihi için seçilmiş olmalıdır.");
        }

        if (exceptionApproval is not null && exceptionApproval.ApprovedAtUtc > closeAtUtc)
        {
            throw new ManufacturingRuleException(
                ClosureAssessmentCodesV1.ApprovalInvalid,
                "Kapanış istisna onayı kapanış anından sonra olamaz.");
        }

        var assessment = AssessClosure(policy);
        switch (assessment.Disposition)
        {
            case ClosureDispositionV1.Balanced when exceptionApproval is not null:
                throw new ManufacturingRuleException(
                    ClosureAssessmentCodesV1.ApprovalUnexpected,
                    "Tolerans içindeki kapanış istisna onayı taşıyamaz.");
            case ClosureDispositionV1.Balanced:
                break;
            case ClosureDispositionV1.ApprovalRequired when exceptionApproval is null:
                throw new ManufacturingRuleException(
                    ClosureAssessmentCodesV1.ApprovalRequired,
                    "Tolerans dışındaki kapanış için ayrı yetkiyle gerekçeli onay zorunludur.");
            case ClosureDispositionV1.ApprovalRequired when exceptionApproval is not null && !exceptionApproval.Matches(
                CompanyId, Id, closedByUserId, Version, policy):
                throw new ManufacturingRuleException(
                    ClosureAssessmentCodesV1.ApprovalInvalid,
                    "Kapanış istisna onayı bu şirket, emir, sürüm, aktör veya politika sürümüne ait değil.");
            case ClosureDispositionV1.ApprovalRequired:
                break;
            default:
                throw new ManufacturingRuleException(
                    assessment.Code ?? ClosureAssessmentCodesV1.PolicyInvalid,
                    "Emir kapanış değerlendirmesi kapanışa izin vermedi.");
        }

        ClosurePolicy = policy;
        ClosureExceptionApproval = exceptionApproval;
        CompleteClose(closedByUserId, closeAtUtc);
    }

    /// <summary>
    /// Closes the order when EVERY parent roll reconciles on its own (Business #66 5728619644 on
    /// DEC-PRDCT-006/007 and Master v3 §28.9): issued − returned = good + second quality + usable
    /// remnant + scrap + process loss + remaining WIP + unexplained, per roll; rolls never offset
    /// each other.
    /// <list type="bullet">
    /// <item>Plan v2 §7.3 and Ö7: with no approved tolerance, a non-zero unexplained difference on
    /// any roll refuses the close; the difference stays on the order under its own name and is
    /// never written off as loss. The Ö7 exception path (reason + single approval) needs a named
    /// approver and is a recorded dependency, not built here.</item>
    /// <item>Remaining WIP does NOT block the close. A WIP reading is explicit, never negative
    /// (<see cref="Kilograms"/>) and bound to an issued parent roll of this order
    /// (<see cref="RecordWorkInProgress"/>). After the close it stays on the order as its own
    /// position — never zeroed, scrapped or turned into good output — and no new realization can
    /// be written (<see cref="ManufacturingRuleCodes.OrderClosed"/>). WIP is not an automatic loss
    /// or output that could hide a difference: writing it down without declaring or returning the
    /// mass makes the difference reappear and refuses the close.</item>
    /// </list>
    /// </summary>
    public void Close(long expectedVersion, Guid closedByUserId, DateTimeOffset closedAt)
    {
        RequireClosable(expectedVersion);
        RequireId(closedByUserId, nameof(closedByUserId));

        var balances = _plannedInputs.Select(input => BalanceOf(input.ParentRollId)).ToList();

        // Each roll on its own: a surplus on one roll never covers a gap on another.
        var unaccounted = balances.Where(balance => !balance.IsFullyAccounted).ToList();
        if (unaccounted.Count > 0)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.UnexplainedDifference,
                "Açıklanamayan kütle farkı olan emir kapatılamaz: "
                + string.Join(", ", unaccounted.Select(balance => $"{balance.ParentRollId} ({balance.Unexplained} kg)")));
        }

        CompleteClose(closedByUserId, closedAt);
    }

    /// <summary>Proves the stored order/version can enter a close decision without mutating it.</summary>
    public void RequireClosable(long expectedVersion)
    {
        RequireVersion(expectedVersion);
        RequireOpen();
    }

    private void CompleteClose(Guid closedByUserId, DateTimeOffset closedAt)
    {
        ClosedByUserId = closedByUserId;
        ClosedAtUtc = closedAt.ToUniversalTime();
        Status = OrderStatus.Closed;
        Version++;
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

    private void RequireVersion(long expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ManufacturingVersionConflictException(expectedVersion, Version);
        }
    }

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

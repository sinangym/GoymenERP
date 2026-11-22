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
public sealed partial class ProductionOrder
{
    private readonly List<PlannedInput> _plannedInputs;
    private readonly List<InputIssue> _issues = [];
    private readonly List<InputReturn> _returns = [];
    private readonly List<WipMeasurement> _wipMeasurements = [];
    private readonly List<OutputDeclaration> _outputs = [];
    private readonly List<OutputReversal> _outputReversals = [];
    private readonly List<PlanOutputLineReference> _plannedOutputLines;

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
        _plannedOutputLines = PlanOutputLineReference.LinesOf(plan.Spec, plan.CutToLengthSpec).ToList();
        Status = OrderStatus.Open;
        // URT-D66 (3): the order opens with the plan's termin, copied once; from then on each keeps its own.
        DueDate = plan.RequestedDueDate;
        PlanDueDate = plan.RequestedDueDate;
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

    /// <summary>F-009 D5 (URT-D33): who cancelled the order; null unless <see cref="OrderStatus.Cancelled"/>.</summary>
    public Guid? CancelledByUserId { get; private set; }

    /// <summary>F-009 D5 (URT-D33): when the order was cancelled (UTC); null unless cancelled.</summary>
    public DateTimeOffset? CancelledAtUtc { get; private set; }

    /// <summary>F-009 D5 (URT-D33): the trimmed reason given for the cancel; null unless cancelled.</summary>
    public string? CancellationReason { get; private set; }

    /// <summary>F-009 D5: the longest cancellation reason (the plan decision reason bound).</summary>
    public const int CancellationReasonMaximumLength = ProductionPlan.DecisionReasonMaximumLength;

    /// <summary>The dedicated exception approval used by an out-of-tolerance close, if one was required.</summary>
    public ClosureExceptionApprovalV1? ClosureExceptionApproval { get; private set; }

    /// <summary>
    /// URT-D66 (3): the order's termin, a calendar day. Copied from the plan's termin when the order opens and changed on
    /// the order alone afterwards (the plan's termin never moves with it). Null only for an order opened before the copy
    /// existed. Not a fact of the order: its <see cref="Version"/> does not move; <see cref="DueDateRevision"/> does.
    /// </summary>
    public DateOnly? DueDate { get; private set; }

    /// <summary>URT-D66 (3): how many times the order's termin was changed on the order (0 = as copied from the plan).</summary>
    public long DueDateRevision { get; private set; }

    /// <summary>
    /// URT-D66 (4): the plan's termin as read with the order — the reference for <see cref="DueDateDiffersFromPlan"/>. Never
    /// written by the order.
    /// </summary>
    public DateOnly? PlanDueDate { get; private set; }

    /// <summary>URT-D66 (4): the screen's "emir termini plandan farklı" flag — the order has a termin and it is not the plan's.</summary>
    public bool DueDateDiffersFromPlan => DueDate is { } own && own != PlanDueDate;

    // Read-only views (ÜRÜN #280 P3-1): a cast cannot reach the lists behind them, so the
    // duplicate, roll and closed-order rules below cannot be bypassed.
    public IReadOnlyList<PlannedInput> PlannedInputs => _plannedInputs.AsReadOnly();

    public IReadOnlyList<InputIssue> Issues => _issues.AsReadOnly();

    public IReadOnlyList<InputReturn> Returns => _returns.AsReadOnly();

    public IReadOnlyList<WipMeasurement> WipMeasurements => _wipMeasurements.AsReadOnly();

    public IReadOnlyList<OutputDeclaration> Outputs => _outputs.AsReadOnly();

    /// <summary>F-009 D2: the reversals of output declarations, each linked to the declaration it takes mass back from.</summary>
    public IReadOnlyList<OutputReversal> OutputReversals => _outputReversals.AsReadOnly();

    /// <summary>
    /// F-009 D2 (URT-D30): the plan's output lines as they stood when the order was created — slit lines by position,
    /// cut lines by id. Empty for a plan without output lines; its outputs bind to their class line.
    /// </summary>
    public IReadOnlyList<PlanOutputLineReference> PlannedOutputLines => _plannedOutputLines.AsReadOnly();

    /// <summary>
    /// Opens the order of an approved plan. Üretim U2-a (URT-D1, "tek plan = tek emir"): a plan opens exactly one order —
    /// a plan that already opened one is refused (<see cref="ManufacturingRuleCodes.PlanOrderExists"/>), and the plan
    /// records the order it opened. A cancelled or Draft plan is not approved (<see cref="ManufacturingRuleCodes.PlanNotApproved"/>).
    /// </summary>
    /// <remarks>
    /// URT-D70: this overload checks no calendar; it is the domain's shorthand for a plan whose termin was already checked.
    /// Opening an order for real goes through <see cref="FromApprovedPlan(Guid, ProductionPlan, DateOnly)"/>.
    /// </remarks>
    public static ProductionOrder FromApprovedPlan(Guid id, ProductionPlan plan) => Open(id, plan, businessToday: null);

    /// <summary>
    /// URT-D70: opens the order of an approved plan as <see cref="FromApprovedPlan(Guid, ProductionPlan)"/> does, and
    /// refuses a plan whose termin is before <paramref name="businessToday"/> (the server's Europe/Istanbul business day)
    /// with <see cref="ManufacturingRuleCodes.DueDateInPast"/>. The user moves the plan's termin forward and opens again.
    /// The termin rule runs after the status and "one order per plan" rules, so each keeps its answer.
    /// </summary>
    public static ProductionOrder FromApprovedPlan(Guid id, ProductionPlan plan, DateOnly businessToday) =>
        Open(id, plan, businessToday);

    private static ProductionOrder Open(Guid id, ProductionPlan plan, DateOnly? businessToday)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Status != PlanStatus.Approved)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.PlanNotApproved,
                "Üretim emri yalnız onaylı plandan oluşturulabilir.");
        }

        if (plan.OrderId is not null)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.PlanOrderExists,
                "Onaylı plan emrini zaten açtı; bir plandan tek emir açılır.");
        }

        // F-009 D5 (URT-D35): a plan released by a cancelled order is re-approved before it opens a new order.
        plan.RequireNoPendingReapproval();

        if (businessToday is { } today)
        {
            plan.RequireDueDateNotPast(today);
        }

        var order = new ProductionOrder(id, plan);
        plan.RecordOrder(order.Id);
        return order;
    }

    /// <summary>
    /// Rebuilds a stored order from its snapshot and its stored facts (MFG-5). The facts are
    /// replayed through the same methods that recorded them — issues first, then returns, outputs
    /// and WIP readings, each in time order — so every rule (fact identity, parent roll, return
    /// bound, latest WIP reading) holds again, and a stored close is replayed through
    /// <see cref="Close"/> (no stored policy: a closed order must still reconcile roll by roll) or
    /// through <see cref="CloseWithPolicy"/> under the rule the close was written with: URT-D42 (3)
    /// — "Kural yeni kapanışlara uygulanır" — so an order closed before URT-D41 with output above
    /// input on a roll stays readable as it is; it is listed by the read-only report, never reopened
    /// or changed. A stored record that breaks a rule is refused, not repaired; the stored version
    /// must be exactly the one its facts add up to.
    /// <para>
    /// F-009 D2: <paramref name="plannedOutputLines"/> is the plan's output line snapshot (none for a plan without
    /// output lines); a stored declaration's line is checked against it again. Output reversals are replayed after the
    /// outputs (each names a declaration) and before the WIP readings, in time order, through
    /// <see cref="ReverseOutput"/>; a stored close is replayed after them, so the close is evaluated on net outputs.
    /// </para>
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
        ClosureExceptionApprovalV1? closureExceptionApproval = null,
        IEnumerable<PlanOutputLineReference>? plannedOutputLines = null,
        IEnumerable<OutputReversal>? outputReversals = null,
        DateOnly? dueDate = null,
        long dueDateRevision = 0,
        DateOnly? planDueDate = null,
        Guid? cancelledByUserId = null,
        DateTimeOffset? cancelledAtUtc = null,
        string? cancellationReason = null,
        IEnumerable<ProductionOrderReopeningV1>? reopenings = null)
    {
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(returns);
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentNullException.ThrowIfNull(wipMeasurements);

        // The plan's own rules decide the snapshot: at least one input, each parent roll once.
        var snapshot = new ProductionPlan(planId, companyId, workCenterId, process, plannedInputs, target);
        var order = new ProductionOrder(id, snapshot);
        if (plannedOutputLines is not null)
        {
            var lines = plannedOutputLines.ToList();
            if (lines.Any(line => line is null || line.Kind == PlanOutputLineKind.ClassLine)
                || lines.Distinct().Count() != lines.Count)
            {
                throw new ManufacturingRuleException(
                    ManufacturingRuleCodes.StoredRecordInvalid,
                    "Kayıtlı plan çıktı satırları her satırı bir kez ve yalnız plan satırı olarak taşımalı.");
            }

            order._plannedOutputLines.AddRange(lines);
        }

        foreach (var issue in issues.OrderBy(fact => fact.IssuedAtUtc))
        {
            order.IssueInput(order.Version, issue.IssueId, issue.ParentRollId, issue.Mass, issue.IssuedAtUtc);
        }

        foreach (var entry in returns.OrderBy(fact => fact.ReturnedAtUtc))
        {
            // F-009 D4: a stored return keeps the bound it was recorded under (one stored before D4 was bounded only by
            // the issued kg), so replay checks the issued bound and not the unconsumed one.
            order.ReturnUnusedInput(order.Version, entry.ReturnId, entry.ParentRollId, entry.Mass, entry.ReturnedAtUtc,
                replayingStoredReturn: true);
        }

        foreach (var output in outputs.OrderBy(fact => fact.DeclaredAtUtc))
        {
            if (output.Cost != CostResult.NotCalculated)
            {
                throw new ManufacturingRuleException(
                    ManufacturingRuleCodes.StoredRecordInvalid, "Kayıtlı çıktının maliyet durumu bu dilimde üretilemez.");
            }

            order.DeclareOutput(order.Version, output.OutputId, output.ParentRollId, output.Class, output.Mass,
                output.DeclaredAtUtc, output.OutputLine, output.Pieces);
        }

        foreach (var reversal in (outputReversals ?? Enumerable.Empty<OutputReversal>()).OrderBy(fact => fact.ReversedAtUtc))
        {
            order.ReverseOutput(order.Version, reversal.ReversalId, reversal.OutputId, reversal.Mass, reversal.Reason,
                reversal.ReversedAtUtc, reversal.Pieces);
        }

        foreach (var measurement in wipMeasurements.OrderBy(fact => fact.MeasuredAtUtc))
        {
            order.RecordWorkInProgress(order.Version, measurement.MeasurementId, measurement.ParentRollId,
                measurement.Mass, measurement.MeasuredAtUtc);
        }

        // URT-D32/D56: historical close decisions were made on their own fact generation. Never re-evaluate them
        // against the corrected current facts; validate their immutable transition chain and retain their headers.
        order.RestoreReopenings(reopenings ?? []);
        var hasCancel = cancelledByUserId is not null || cancelledAtUtc is not null || cancellationReason is not null;
        if (hasCancel)
        {
            // F-009 D5: a stored cancel carries all three fields, and excludes a close.
            if (cancelledByUserId is not { } cancelledBy || cancelledAtUtc is not { } cancelledAt
                || cancellationReason is null || closedByUserId is not null || closedAtUtc is not null
                || closurePolicy is not null || closureExceptionApproval is not null)
            {
                throw new ManufacturingRuleException(
                    ManufacturingRuleCodes.StoredRecordInvalid,
                    "Kayıtlı iptal izi iptal edeni, anı ve gerekçeyi birlikte taşımalı ve kapanışla birlikte bulunamaz.");
            }

            order.Cancel(order.Version, cancelledBy, cancelledAt, cancellationReason);
        }
        else if (closedByUserId is { } closedBy && closedAtUtc is { } closedAt)
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
                order.CloseWithPolicy(order.Version, closedBy, closedAt, closurePolicy, closureExceptionApproval,
                    replayingStoredClose: true);
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

        // URT-D66 (3): a termin changed on the order always has a day; a revision is never negative.
        if (dueDateRevision < 0 || (dueDateRevision > 0 && dueDate is null))
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.StoredRecordInvalid, "Kayıtlı emir termini revizyonuyla uyuşmuyor.");
        }

        order.DueDate = dueDate;
        order.DueDateRevision = dueDateRevision;
        order.PlanDueDate = planDueDate;
        return order;
    }

    /// <summary>
    /// URT-D66 (3): changes the order's termin on the order alone — the plan's termin does not move (URT-D66 (4)). The
    /// order's <see cref="Version"/> does not move either (a termin is no fact); its termin revision advances by one. A
    /// termin is changed, never removed. In order, and nothing is applied on a refusal: the termin revision the caller read
    /// (<see cref="ManufacturingRuleCodes.OrderDueDateChanged"/>); a closed order
    /// (<see cref="ManufacturingRuleCodes.OrderDueDateOrderClosed"/>); no change
    /// (<see cref="ManufacturingRuleCodes.OrderDueDateNoChange"/>); a day earlier than <paramref name="businessToday"/>, the
    /// company's Europe/Istanbul business day from the server's clock (<see cref="ManufacturingRuleCodes.DueDateInPast"/>).
    /// </summary>
    public void ChangeDueDate(long expectedDueDateRevision, DateOnly dueDate, DateOnly businessToday)
    {
        if (expectedDueDateRevision != DueDateRevision)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.OrderDueDateChanged,
                "Emrin termini siz okuduktan sonra değişti; emri yeniden okuyun.");
        }

        if (Status != OrderStatus.Open)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.OrderDueDateOrderClosed,
                "Emir kapanmış; termini artık değişmez.");
        }

        if (dueDate == DueDate)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.OrderDueDateNoChange,
                "İstek emrin termininde hiçbir şeyi değiştirmiyor.");
        }

        if (dueDate < businessToday)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.DueDateInPast,
                "Termin şirketin bugünkü iş gününden önce olamaz.");
        }

        DueDate = dueDate;
        DueDateRevision++;
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

    /// <summary>
    /// Unused input sent back; it can never exceed what is still issued
    /// (<see cref="ManufacturingRuleCodes.ReturnExceedsIssued"/>).
    /// <para>
    /// F-009 D4 (analysis §2, §4.4): nor can it exceed the input still unconsumed — issued minus returned minus the net
    /// output (declarations net of their reversals) minus the latest WIP reading, i.e. the BAL-003 unexplained kg of the
    /// order's "output above input" scope: the parent roll for a roll-based process, the order total for a process
    /// without a main roll input (<see cref="MeasuresOutputAboveInputPerRoll"/>, URT-D49). A larger return is refused
    /// with <see cref="ManufacturingRuleCodes.ReturnExceedsUnconsumed"/>, so the input of a roll whose outputs still
    /// stand cannot be sent back: its outputs are reversed (<see cref="ReverseOutput"/>) or its WIP re-measured first.
    /// </para>
    /// </summary>
    public InputReturn ReturnUnusedInput(long expectedVersion, Guid returnId, Guid parentRollId, Kilograms mass, DateTimeOffset returnedAt) =>
        ReturnUnusedInput(expectedVersion, returnId, parentRollId, mass, returnedAt, replayingStoredReturn: false);

    private InputReturn ReturnUnusedInput(long expectedVersion, Guid returnId, Guid parentRollId, Kilograms mass,
        DateTimeOffset returnedAt, bool replayingStoredReturn)
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

        if (!replayingStoredReturn && mass.Value > UnconsumedInputOf(parentRollId))
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.ReturnExceedsUnconsumed,
                "İade, net çıktı ve yarı mamul düşüldükten sonra kalan girdi kütlesini aşamaz; "
                + "önce çıktı düzeltilmeli veya yarı mamul yeniden ölçülmeli.");
        }

        var entry = new InputReturn(returnId, parentRollId, mass, returnedAt.ToUniversalTime());
        _returns.Add(entry);
        Version++;
        return entry;
    }

    /// <summary>
    /// One output of the run, bound to the parent roll it was cut from. Cost is not calculated
    /// in this phase (plan v2 R6), so every declaration carries <see cref="CostResult.NotCalculated"/>.
    /// <para>
    /// F-009 D2 (URT-D30, ASSUMPTION-URT-F009-1): <paramref name="outputLine"/> binds the declaration to a plan output
    /// line — a good product to one of <see cref="PlannedOutputLines"/> (or to its class line when the plan has none),
    /// every other class to its class line; anything else is refused with
    /// <see cref="ManufacturingRuleCodes.OutputLineNotInPlan"/>. A declaration without a line ("satırsız") is still
    /// accepted as before D2; it counts in every balance but cannot be corrected (<see cref="ReverseOutput"/>).
    /// </para>
    /// <para>
    /// AK-7 (URT-D112): <paramref name="pieces"/> is the piece count (with the captured theoretical kg per piece) of an
    /// output whose product is counted in pieces; whether the product is counted is the product owner's answer, read by
    /// the command before this method. A process loss is never counted
    /// (<see cref="ManufacturingRuleCodes.OutputPiecesNotApplicable"/>).
    /// </para>
    /// </summary>
    public OutputDeclaration DeclareOutput(
        long expectedVersion, Guid outputId, Guid parentRollId, OutputClass outputClass, Kilograms mass, DateTimeOffset declaredAt,
        PlanOutputLineReference? outputLine = null, OutputPieces? pieces = null)
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

        if (outputLine is not null)
        {
            RequireOutputLineFits(outputClass, outputLine);
        }

        if (pieces is not null && outputClass == OutputClass.ProcessLoss)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.OutputPiecesNotApplicable, "Proses kaybı adetle beyan edilmez.");
        }

        var declaration = new OutputDeclaration(
            outputId, parentRollId, outputClass, mass, declaredAt.ToUniversalTime(), CostResult.NotCalculated, outputLine,
            pieces);
        _outputs.Add(declaration);
        Version++;
        return declaration;
    }

    /// <summary>
    /// F-009 D2 (URT-D28, analysis §4.3): takes <paramref name="mass"/> back from one output declaration as a new fact
    /// linked to it (R6 — the declaration itself never changes). Refused when the order is closed
    /// (<see cref="ManufacturingRuleCodes.OrderClosed"/>, analysis §4.6), when the output was never declared here
    /// (<see cref="ManufacturingRuleCodes.OutputNotDeclared"/>), when the declaration has no plan output line
    /// (<see cref="ManufacturingRuleCodes.OutputLineMissing"/>, fail-closed, URT-D30), for a reversal id already recorded
    /// (<see cref="ManufacturingRuleCodes.OutputReversalAlreadyRecorded"/>), and when the reversals of the declaration
    /// would add up to more than its mass (<see cref="ManufacturingRuleCodes.OutputReversalExceedsDeclared"/>). Every
    /// output class is reversed the same way. The bound on mass already used downstream (URT-D31) and the matching
    /// stock movement belong to the correction command and to Inventory, not to this method.
    /// <para>
    /// AK-7 (URT-D112): a correction of a counted declaration names the pieces it takes back (zero allowed:
    /// <see cref="ManufacturingRuleCodes.OutputPiecesRequired"/> when missing); one of an uncounted declaration names none
    /// (<see cref="ManufacturingRuleCodes.OutputPiecesNotApplicable"/>). The pieces taken back never exceed the declared
    /// count (<see cref="ManufacturingRuleCodes.OutputPieceReversalExceedsDeclared"/>), and the net kg and net pieces reach
    /// zero together (<see cref="ManufacturingRuleCodes.OutputPieceReversalInconsistent"/>).
    /// </para>
    /// </summary>
    public OutputReversal ReverseOutput(
        long expectedVersion, Guid reversalId, Guid outputId, Kilograms mass, string reason, DateTimeOffset reversedAt,
        PieceCount? pieces = null)
    {
        RequireVersion(expectedVersion);
        if (Status != OrderStatus.Open)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.OrderClosed, "Kapanmış emirde düzeltme yapılamaz.");
        }

        RequireId(reversalId, nameof(reversalId));
        RequireId(outputId, nameof(outputId));
        RequirePositive(mass, nameof(mass));
        var text = RequireReversalReason(reason);
        var declaration = _outputs.SingleOrDefault(output => output.OutputId == outputId)
            ?? throw new ManufacturingRuleException(
                ManufacturingRuleCodes.OutputNotDeclared, "Düzeltilen çıktı bu emirde beyan edilmedi.");
        if (declaration.OutputLine is null)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.OutputLineMissing,
                "Plan çıktı satırına bağlı olmayan eski beyan düzeltilemez.");
        }

        if (_outputReversals.Any(entry => entry.ReversalId == reversalId))
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.OutputReversalAlreadyRecorded, "Bu çıktı düzeltmesi zaten kaydedildi.");
        }

        if (ReversedOf(outputId) + mass.Value > declaration.Mass.Value)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.OutputReversalExceedsDeclared,
                "Bir beyanın düzeltmeleri toplamı beyan edilen kütleyi aşamaz.");
        }

        RequirePieceReversalFits(declaration, mass, pieces);
        var reversal = new OutputReversal(reversalId, outputId, mass, text, reversedAt.ToUniversalTime(), pieces);
        _outputReversals.Add(reversal);
        Version++;
        return reversal;
    }

    /// <summary>F-009 D2: the declaration's mass minus its reversals (never below zero).</summary>
    public Kilograms NetMassOf(Guid outputId)
    {
        var declaration = _outputs.SingleOrDefault(output => output.OutputId == outputId)
            ?? throw new ManufacturingRuleException(
                ManufacturingRuleCodes.OutputNotDeclared, "Çıktı bu emirde beyan edilmedi.");
        return NetMassOf(declaration);
    }

    /// <summary>
    /// F-009 D2 (URT-D30): the net output of one plan output line of one class — the sum of the net masses of the
    /// declarations bound to it. A declaration without a line counts towards no line.
    /// </summary>
    public Kilograms NetOutputOfLine(OutputClass outputClass, PlanOutputLineReference outputLine)
    {
        ArgumentNullException.ThrowIfNull(outputLine);
        return _outputs
            .Where(output => output.Class == outputClass && output.OutputLine == outputLine)
            .Aggregate(Kilograms.Zero, (sum, output) => sum + NetMassOf(output));
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
        // F-009 D2 (analysis §4.6): the balance counts each declaration net of its reversals.
        var byClass = _outputs
            .Where(output => output.ParentRollId == parentRollId)
            .GroupBy(output => output.Class)
            .ToDictionary(
                group => group.Key,
                group => group.Aggregate(Kilograms.Zero, (sum, output) => sum + NetMassOf(output)));

        return new MassBalance(
            parentRollId,
            new Kilograms(IssuedOf(parentRollId)),
            new Kilograms(ReturnedOf(parentRollId)),
            byClass.AsReadOnly(),
            LatestWipOf(parentRollId)?.Mass ?? Kilograms.Zero);
    }

    /// <summary>
    /// URT-D49: true when this order's process consumes a main parent roll, so "output above input" is measured roll by
    /// roll (URT-D42 (1)); false for a process without one (Paint), whose order total is compared instead
    /// (<see cref="TotalBalance"/>).
    /// </summary>
    public bool MeasuresOutputAboveInputPerRoll => ManufacturingProcessMainInputV1.HasMainRollInput(Process);

    /// <summary>URT-D49 (1): every planned input's <see cref="BalanceOf"/> added into one BAL-003 account.</summary>
    public OrderMassTotalV1 TotalBalance() =>
        OrderMassTotalV1.Of(_plannedInputs.Select(input => BalanceOf(input.ParentRollId)).ToArray());

    /// <summary>
    /// The close's "output above input" rule on this order's facts (URT-D41 (1), URT-D42 (1), URT-D49): on any planned
    /// parent roll for a roll-based process, on the order total for a process without a main roll input. The close
    /// (<see cref="OrderClosureAssessmentCalculatorV1"/>) and the read-only report of earlier closes read it the same way.
    /// </summary>
    public bool HasOutputAboveInput() =>
        MeasuresOutputAboveInputPerRoll
            ? _plannedInputs.Any(input => BalanceOf(input.ParentRollId).OutputExceedsInput)
            : TotalBalance().OutputExceedsInput;

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
        AssessClosure(policy, DerivedScrapPercentInputs());

    private RollScrapPercentInputV1[] DerivedScrapPercentInputs() =>
        _plannedInputs.Select(input =>
        {
            var balance = BalanceOf(input.ParentRollId);
            return new RollScrapPercentInputV1(input.ParentRollId, new(
                balance.OutputOf(OutputClass.Scrap).Value + balance.OutputOf(OutputClass.ProcessLoss).Value,
                balance.NetIssued.Value));
        }).ToArray();

    /// <summary>
    /// The policy-backed close used by the runtime. It persists the exact Settings snapshot and accepts an
    /// out-of-tolerance shortfall only with immutable evidence created after the application freshly proved the
    /// dedicated approval permission. The evidence is checked again here against this company, order, actor,
    /// pre-close order version and policy version; it never supplies authority by itself. Output above input on a
    /// planned parent roll — on the order total for a process without a main roll input (URT-D49) — is refused
    /// whatever the tolerance or approval
    /// (<see cref="ClosureAssessmentCodesV1.OutputExceedsInput"/>, URT-D41 (1)-(2), URT-D42 (1)-(2)).
    /// </summary>
    public void CloseWithPolicy(long expectedVersion, Guid closedByUserId, DateTimeOffset closedAt,
        ClosureTolerancePolicySnapshotV1 policy, ClosureExceptionApprovalV1? exceptionApproval) =>
        CloseWithPolicy(expectedVersion, closedByUserId, closedAt, policy, exceptionApproval, replayingStoredClose: false);

    private void CloseWithPolicy(long expectedVersion, Guid closedByUserId, DateTimeOffset closedAt,
        ClosureTolerancePolicySnapshotV1 policy, ClosureExceptionApprovalV1? exceptionApproval, bool replayingStoredClose)
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

        var assessment = OrderClosureAssessmentCalculatorV1.Assess(this, policy, DerivedScrapPercentInputs(),
            replayingStoredClose);

        // URT-D68 with URT-D42 (3): a paint close is measured on the order total from now on. A paint close stored under
        // the earlier per-input rule (an approval the total no longer needs, or a per-input decision the total reads
        // otherwise) stays readable: when the total does not accept the stored decision, the replay reads it under the
        // per-input rule it was written with. A new close never takes this path.
        if (replayingStoredClose && !MeasuresOutputAboveInputPerRoll
            && !AcceptsStoredDecision(assessment, exceptionApproval, closedByUserId, policy))
        {
            assessment = OrderClosureAssessmentCalculatorV1.Assess(this, policy, DerivedScrapPercentInputs(),
                replayingStoredClose, PaintClosureScope.PerInput);
        }

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
    /// The decision the close switch below accepts: a balanced close without an approval, or an out-of-tolerance close
    /// with the approval that matches this company, order, actor, version and policy.
    /// </summary>
    private bool AcceptsStoredDecision(OrderClosureAssessmentV1 assessment, ClosureExceptionApprovalV1? exceptionApproval,
        Guid closedByUserId, ClosureTolerancePolicySnapshotV1 policy) =>
        assessment.Disposition switch
        {
            ClosureDispositionV1.Balanced => exceptionApproval is null,
            ClosureDispositionV1.ApprovalRequired => exceptionApproval is not null
                && exceptionApproval.Matches(CompanyId, Id, closedByUserId, Version, policy),
            _ => false,
        };

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

    /// <summary>
    /// F-009 D5 (URT-D33/D34): cancels an open order that has no net effect left. Every output must be fully reversed
    /// (net 0 kg — the reversal already checked the downstream lock), every planned roll must be returned in full
    /// (issued − returned = 0) and its latest WIP reading, if any, must be 0 kg; otherwise
    /// <see cref="ManufacturingRuleCodes.OrderCancelHasNetEffect"/>. A cancel is a terminal state: the order takes no
    /// new fact. The plan's release for re-approval is recorded by
    /// <see cref="ProductionPlan.ReleaseCancelledOrder(Guid)"/> in the same unit of work.
    /// </summary>
    public void Cancel(long expectedVersion, Guid cancelledByUserId, DateTimeOffset cancelledAt, string? reason)
    {
        RequireVersion(expectedVersion);
        RequireOpen();
        RequireId(cancelledByUserId, nameof(cancelledByUserId));
        var text = RequireCancellationReason(reason);

        var outputsLeft = _outputs.Any(output => NetMassOf(output).Value != 0m);
        var rollsLeft = _plannedInputs.Any(input =>
            IssuedOf(input.ParentRollId) - ReturnedOf(input.ParentRollId) != 0m
            || (LatestWipOf(input.ParentRollId)?.Mass.Value ?? 0m) != 0m);
        if (outputsLeft || rollsLeft)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.OrderCancelHasNetEffect,
                "Net etkisi kalan emir iptal edilemez: çıktılar düzeltmeyle sıfırlanmalı, girdiler iade edilmeli ve WIP sıfır olmalı.");
        }

        CancelledByUserId = cancelledByUserId;
        CancelledAtUtc = cancelledAt.ToUniversalTime();
        CancellationReason = text;
        Status = OrderStatus.Cancelled;
        Version++;
    }

    private static string RequireCancellationReason(string? reason)
    {
        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > CancellationReasonMaximumLength || text.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"İptal gerekçesi 1–{CancellationReasonMaximumLength} karakter olmalı ve kontrol karakteri taşımamalıdır.",
                nameof(reason));
        }

        return text;
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
    /// <see cref="TargetBasis.GoodOutputMass"/> (net of output reversals, F-009 D2). Without a declared target the answer is
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
                .Aggregate(Kilograms.Zero, (sum, output) => sum + NetMassOf(output)),
            // ProductionTarget refuses any other basis at construction.
            _ => throw new UnreachableException(),
        };
        return RemainingTarget.Of(Target, achieved);
    }

    private decimal IssuedOf(Guid parentRollId) =>
        _issues.Where(issue => issue.ParentRollId == parentRollId).Sum(issue => issue.Mass.Value);

    private decimal ReturnedOf(Guid parentRollId) =>
        _returns.Where(entry => entry.ParentRollId == parentRollId).Sum(entry => entry.Mass.Value);

    /// <summary>
    /// F-009 D4: the input kg a return may still take back — the BAL-003 unexplained kg of the parent roll, or of the
    /// order total for a process without a main roll input (URT-D49). Below zero when more is accounted than issued.
    /// </summary>
    private decimal UnconsumedInputOf(Guid parentRollId) =>
        MeasuresOutputAboveInputPerRoll ? BalanceOf(parentRollId).Unexplained : TotalBalance().Unexplained;

    private decimal ReversedOf(Guid outputId) =>
        _outputReversals.Where(entry => entry.OutputId == outputId).Sum(entry => entry.Mass.Value);

    private Kilograms NetMassOf(OutputDeclaration declaration) =>
        new(declaration.Mass.Value - ReversedOf(declaration.OutputId));

    private int ReversedPiecesOf(Guid outputId) =>
        _outputReversals.Where(entry => entry.OutputId == outputId).Sum(entry => entry.Pieces?.Value ?? 0);

    /// <summary>
    /// AK-7 (URT-D112): the declaration's piece count minus the pieces its corrections took back; null when the
    /// declaration is not counted in pieces.
    /// </summary>
    public PieceCount? NetPiecesOf(Guid outputId)
    {
        var declaration = _outputs.SingleOrDefault(output => output.OutputId == outputId)
            ?? throw new ManufacturingRuleException(
                ManufacturingRuleCodes.OutputNotDeclared, "Çıktı bu emirde beyan edilmedi.");
        return declaration.Pieces is { } pieces
            ? new PieceCount(pieces.Count.Value - ReversedPiecesOf(outputId))
            : null;
    }

    private void RequirePieceReversalFits(OutputDeclaration declaration, Kilograms mass, PieceCount? pieces)
    {
        if (declaration.Pieces is not { } declared)
        {
            if (pieces is not null)
            {
                throw new ManufacturingRuleException(
                    ManufacturingRuleCodes.OutputPiecesNotApplicable, "Adetsiz beyanın düzeltmesi adet taşımaz.");
            }

            return;
        }

        if (pieces is not { } taken)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.OutputPiecesRequired, "Adetli beyanın düzeltmesi geri alınan adedi belirtmelidir.");
        }

        var netPiecesAfter = declared.Count.Value - ReversedPiecesOf(declaration.OutputId) - taken.Value;
        if (netPiecesAfter < 0)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.OutputPieceReversalExceedsDeclared,
                "Bir beyanın düzeltmelerinde geri alınan adet beyan edilen adedi aşamaz.");
        }

        var netMassAfter = declaration.Mass.Value - ReversedOf(declaration.OutputId) - mass.Value;
        if ((netMassAfter == 0m) != (netPiecesAfter == 0))
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.OutputPieceReversalInconsistent,
                "Düzeltme sonrası net kg ve net adet birlikte sıfıra inmelidir.");
        }
    }

    private void RequireOutputLineFits(OutputClass outputClass, PlanOutputLineReference outputLine)
    {
        var fits = outputLine.Kind == PlanOutputLineKind.ClassLine
            ? outputClass != OutputClass.GoodProduct || _plannedOutputLines.Count == 0
            : outputClass == OutputClass.GoodProduct && _plannedOutputLines.Contains(outputLine);
        if (!fits)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.OutputLineNotInPlan,
                "Çıktı satırı bu emrin plan çıktı satırlarından biri değil veya çıktı sınıfına uymuyor.");
        }
    }

    private static string RequireReversalReason(string? reason)
    {
        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > OutputReversal.ReasonMaximumLength || text.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"Düzeltme gerekçesi 1–{OutputReversal.ReasonMaximumLength} karakter olmalı ve kontrol karakteri taşımamalıdır.",
                nameof(reason));
        }

        return text;
    }

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
        if (Status == OrderStatus.Cancelled)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.OrderCancelled, "İptal edilmiş emre yeni gerçekleşme yazılamaz.");
        }

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

using GoymenERP.Modules.Manufacturing.Domain.Common;

namespace GoymenERP.Modules.Manufacturing.Domain.Plans;

public enum PlanStatus
{
    Draft = 1,
    Approved = 2,

    /// <summary>Üretim U2-a: cancelled with a reason; kept, never deleted, and takes no further decision.</summary>
    Cancelled = 3,
}

/// <summary>
/// Üretim U2-a (URT-D2, API-10): one recorded rejection of a Draft plan by its approver — the reason, who rejected it,
/// when, and the plan version it produced. The plan stays a Draft; the rejection is kept in its history.
/// </summary>
public sealed record PlanRejection(int Number, string Reason, Guid RejectedByUserId, DateTimeOffset RejectedAtUtc,
    long PlanVersion);

/// <summary>Üretim U2-a (API-11): the cancellation of a plan — the reason, who cancelled it and when.</summary>
public sealed record PlanCancellation(string Reason, Guid CancelledByUserId, DateTimeOffset CancelledAtUtc);

/// <summary>
/// What is intended to be produced, from which parent rolls, on which work center (plan v2
/// §7.3). A plan is not an order and not a realization: approving it records a decision and
/// nothing else — it issues no input, moves no stock and consumes nothing.
/// </summary>
public sealed class ProductionPlan
{
    /// <summary>The longest rejection or cancellation reason (trimmed) a plan keeps, as a revision reason.</summary>
    public const int DecisionReasonMaximumLength = 256;

    /// <summary>The engines' line-speed parameter name (= the Settings key); a plan-entered value wins (URT-D9).</summary>
    public const string LineSpeedParameter = "lineSpeedMetresPerMinute";

    /// <summary>The unit of the plan-entered line speed (the Settings unit of the same key).</summary>
    public const string LineSpeedUnit = "m/min";

    /// <summary>A plan-entered line speed carries at most the Settings value scale (decimal(18,6)).</summary>
    public const int LineSpeedScale = 6;

    private readonly List<PlannedInput> _inputs;
    private readonly List<PlanRejection> _rejections = [];

    public ProductionPlan(
        Guid id,
        Guid companyId,
        Guid workCenterId,
        ManufacturingProcess process,
        IEnumerable<PlannedInput> inputs,
        ProductionTarget? target = null,
        SlittingSpec? spec = null,
        CutToLengthSpec? cutToLengthSpec = null,
        decimal? lineSpeedMetresPerMinute = null,
        PlanProcessSpec? processSpec = null)
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
        RequireLineSpeed(process, lineSpeedMetresPerMinute, nameof(lineSpeedMetresPerMinute));
        processSpec?.RequireAllowedFor(process, nameof(processSpec));
        RequireNetWidthWithinParents(spec, _inputs);

        Target = target;
        Spec = spec;
        CutToLengthSpec = cutToLengthSpec;
        LineSpeedMetresPerMinute = lineSpeedMetresPerMinute;
        ProcessSpec = processSpec;
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

    /// <summary>
    /// Üretim U2-a (URT-D9 "dilme ve boru hat hızları planda girilir"): the line speed entered on a Slitting or
    /// Tube / Profile plan, or null. It wins over the parameter profile's value for this plan and is captured at approval
    /// with source <c>Plan</c>. Part of the plan's content: a Draft revision replaces it with the rest.
    /// </summary>
    public decimal? LineSpeedMetresPerMinute { get; private set; }

    /// <summary>
    /// Üretim U2-b1 (API-12): the Tube / Profile, Paint or Cold Rolling values entered on the plan, or null. Part of the
    /// plan's content: a Draft revision replaces it with the rest; its numbers are captured at approval with source
    /// <c>Plan</c>.
    /// </summary>
    public PlanProcessSpec? ProcessSpec { get; private set; }

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

    /// <summary>
    /// Üretim U1-b (DEC-PRDCT-008): the Settings parameter profile captured at approval, or null — a draft, a plan approved
    /// before U1-b, or an approval for which Settings had no profile of the process. Never refreshed after approval.
    /// </summary>
    public PlanParameterSnapshotV1? ParameterSnapshot { get; private set; }

    public IReadOnlyList<PlannedInput> Inputs => _inputs.AsReadOnly();

    /// <summary>Üretim U2-a: the recorded rejections, oldest first. A rejected plan stays a Draft.</summary>
    public IReadOnlyList<PlanRejection> Rejections => _rejections.AsReadOnly();

    /// <summary>Üretim U2-a: the cancellation, or null while the plan is not cancelled.</summary>
    public PlanCancellation? Cancellation { get; private set; }

    /// <summary>
    /// Üretim U2-a (URT-D1): the one order this approved plan opened, or null. Not a change of the plan (its version does
    /// not move): the order is its own record; the store supplies it when the plan is rebuilt.
    /// </summary>
    public Guid? OrderId { get; private set; }

    /// <summary>
    /// Üretim U2-b / API-16 (SAT-D219): the approved sales order line this plan supplies, or null. Not part of the plan's
    /// content and not a change of the plan (its version does not move): the link has its own revision
    /// (<see cref="SalesOrderLinkRevision"/>), which every link or unlink advances.
    /// </summary>
    public PlanSalesOrderLink? SalesOrderLink { get; private set; }

    /// <summary>Üretim U2-b / API-16: how many times the sales order link was set or removed (0 = never linked).</summary>
    public long SalesOrderLinkRevision { get; private set; }

    /// <summary>The longest preparer identity the store keeps (the actor id column's length).</summary>
    public const int PreparedByMaximumLength = 128;

    /// <summary>
    /// Üretim U2-d (URT-D2): who prepared the plan's CURRENT version — the actor who recorded its last Draft revision, else
    /// the actor who saved it — or null for a plan stored before U2-d and never revised (no preparer was kept) and for a plan built in memory
    /// only. The approval and the rejection refuse this user (<see cref="ManufacturingRuleCodes.PlanSelfApproval"/>).
    /// Not part of the plan's content and not a change of the plan: its version does not move.
    /// </summary>
    public string? PreparedBy { get; private set; }

    /// <summary>
    /// Üretim U2-d: records who prepared the current version (the store calls it on save and after a revision, and when the
    /// plan is rebuilt). The identity is the server actor id, trimmed, at most <see cref="PreparedByMaximumLength"/>.
    /// </summary>
    public void RecordPreparer(string preparedBy)
    {
        var text = preparedBy?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > PreparedByMaximumLength || text.Any(char.IsControl))
        {
            throw new ArgumentException("Planı hazırlayan kullanıcı kimliği geçersiz.", nameof(preparedBy));
        }

        PreparedBy = text;
    }

    /// <summary>
    /// Üretim U2-d: true when <paramref name="userId"/> prepared the current version (a plan without a recorded preparer
    /// answers false).
    /// </summary>
    public bool IsPreparedBy(Guid userId) =>
        PreparedBy is { } preparedBy && Guid.TryParse(preparedBy, out var preparer) && preparer == userId;

    /// <summary>
    /// Üretim U2-d (URT-D2 "hazırlayan ≠ onaylayan"): refuses the decision of the user who prepared the current version.
    /// A plan without a recorded preparer (stored before U2-d and never revised) is not refused.
    /// </summary>
    private void RequireDeciderIsNotPreparer(Guid deciderUserId)
    {
        if (IsPreparedBy(deciderUserId))
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.PlanSelfApproval,
                "Planı hazırlayan kullanıcı aynı planı onaylayamaz veya reddedemez; başka bir kullanıcı karar vermelidir.");
        }
    }

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
        CutToLengthSpec? cutToLengthSpec = null,
        PlanParameterSnapshotV1? parameterSnapshot = null,
        decimal? lineSpeedMetresPerMinute = null,
        IEnumerable<PlanRejection>? rejections = null,
        PlanCancellation? cancellation = null,
        Guid? orderId = null,
        PlanProcessSpec? processSpec = null,
        string? preparedBy = null,
        PlanSalesOrderLink? salesOrderLink = null,
        long salesOrderLinkRevision = 0)
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

        ProductionPlan plan;
        try
        {
            plan = new ProductionPlan(id, companyId, workCenterId, process, inputs, target, spec, cutToLengthSpec,
                lineSpeedMetresPerMinute, processSpec);
        }
        catch (ArgumentException)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                "Kayıtlı plan içeriği planın kurallarını karşılamıyor.");
        }

        // U2-a: the Draft revisions and rejections interleave; each advanced the version by one, and a stored step names
        // the version it produced. They are replayed in that order (a rejection whose version is next goes first).
        var pendingRejections = new Queue<PlanRejection>((rejections ?? []).OrderBy(rejection => rejection.PlanVersion));
        void ReplayRejectionsUpTo(long? beforeVersion)
        {
            while (pendingRejections.Count > 0 && pendingRejections.Peek().PlanVersion == plan.Version + 1
                   && (beforeVersion is null || pendingRejections.Peek().PlanVersion < beforeVersion))
            {
                var rejection = pendingRejections.Dequeue();
                try
                {
                    plan.Reject(plan.Version, rejection.RejectedByUserId, rejection.RejectedAtUtc, rejection.Reason);
                }
                catch (Exception exception) when (exception is ManufacturingRuleException or ArgumentException)
                {
                    throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                        "Kayıtlı plan reddi planın kurallarını karşılamıyor.");
                }

                if (plan._rejections[^1].Number != rejection.Number)
                {
                    throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                        "Kayıtlı plan retleri eksiksiz numaralanmamış.");
                }
            }
        }

        foreach (var revision in revisions ?? [])
        {
            ArgumentNullException.ThrowIfNull(revision);
            ReplayRejectionsUpTo(revision.PlanVersion);
            try
            {
                plan.Revise(plan.Version, revision.WorkCenterId, process, revision.Inputs, revision.Target,
                    revision.CutToLengthSpec, revision.LineSpeedMetresPerMinute, revision.ProcessSpec);
            }
            catch (Exception exception) when (exception is ManufacturingRuleException or ArgumentException)
            {
                // A stored revision the rules would refuse is refused, not repaired.
                var code = exception is ManufacturingRuleException rule ? rule.Code : "argument";
                throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                    $"Kayıtlı plan revizyonu planın kurallarını karşılamıyor ({code}).");
            }

            if (revision.PlanVersion is { } produced && produced != plan.Version)
            {
                throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                    "Kayıtlı plan revizyonunun sürümü planın adımlarıyla uyuşmuyor.");
            }
        }

        ReplayRejectionsUpTo(beforeVersion: null);
        if (pendingRejections.Count > 0)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                "Kayıtlı plan reddinin sürümü planın adımlarıyla uyuşmuyor.");
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

        if (parameterSnapshot is not null)
        {
            if (plan.Status != PlanStatus.Approved || parameterSnapshot.Process != process)
            {
                throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                    "Kayıtlı parametre snapshot'ı yalnız aynı süreçteki onaylı plana ait olabilir.");
            }

            // U2-a: a plan-entered value is in the snapshot exactly when the plan carries it, with the plan's number.
            if (!plan.MatchesPlanValues(parameterSnapshot))
            {
                throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                    "Kayıtlı parametre snapshot'ı planda girilen değerle uyuşmuyor.");
            }

            plan.ParameterSnapshot = parameterSnapshot;
        }

        if (orderId is { } order)
        {
            if (order == Guid.Empty || plan.Status != PlanStatus.Approved)
            {
                throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                    "Kayıtlı emir yalnız onaylı plana bağlı olabilir.");
            }

            plan.OrderId = order;
        }

        if (cancellation is not null)
        {
            try
            {
                plan.Cancel(plan.Version, cancellation.CancelledByUserId, cancellation.CancelledAtUtc, cancellation.Reason);
            }
            catch (Exception exception) when (exception is ManufacturingRuleException or ArgumentException)
            {
                throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                    "Kayıtlı plan iptali planın kurallarını karşılamıyor.");
            }
        }

        if (plan.Version != version)
        {
            throw new ManufacturingRuleException(
                ManufacturingRuleCodes.StoredRecordInvalid, "Kayıtlı plan sürümü planın kendi değişiklikleriyle uyuşmuyor.");
        }

        // U2-d: the preparer is set after the replay, so a stored step recorded before U2-d is never refused on rebuild.
        if (preparedBy is not null)
        {
            try
            {
                plan.RecordPreparer(preparedBy);
            }
            catch (ArgumentException)
            {
                throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                    "Kayıtlı plan hazırlayanı geçersiz.");
            }
        }

        // U2-b / API-16: the link is not a step of the plan (its version does not move); it is set after the replay and must
        // still meet the rules a new link meets: a revision for every stored link, the inputs' party.
        if (salesOrderLinkRevision < 0 || (salesOrderLink is not null && salesOrderLinkRevision < 1))
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                "Kayıtlı satış siparişi bağının revizyonu geçersiz.");
        }

        if (salesOrderLink is not null)
        {
            if (!salesOrderLink.IsWellFormed() || !plan.InputsMatchParty(salesOrderLink.CustomerPartyId))
            {
                throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                    "Kayıtlı satış siparişi bağı planın kurallarını karşılamıyor.");
            }

            plan.SalesOrderLink = salesOrderLink;
        }

        plan.SalesOrderLinkRevision = salesOrderLinkRevision;
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
        SlittingPlanParameters? parameters) =>
        Approve(expectedVersion, approvedByUserId, approvedAtUtc, parameters, parameterSnapshot: null);

    /// <summary>
    /// Üretim U1-b: the approval with the Settings parameter profile it captures (DEC-PRDCT-008). The snapshot must be of
    /// the plan's own process; the slitting gate runs on the parameters the caller derived from it. Nothing is applied
    /// on a refusal.
    /// </summary>
    public void Approve(long expectedVersion, Guid approvedByUserId, DateTimeOffset approvedAtUtc,
        SlittingPlanParameters? parameters, PlanParameterSnapshotV1? parameterSnapshot)
    {
        RequireApprovable(expectedVersion);
        RequireDeciderIsNotPreparer(approvedByUserId);
        if (parameterSnapshot is not null && parameterSnapshot.Process != Process)
        {
            throw new ArgumentException("Parametre profili planın sürecine ait olmalıdır.", nameof(parameterSnapshot));
        }

        if (parameterSnapshot?.Values.Any(value => value.Source is not null) == true)
        {
            throw new ArgumentException("Ayarlar profili planda girilen değer taşıyamaz.", nameof(parameterSnapshot));
        }

        SlittingApprovalGate.Require(this, parameters);
        ApplyApproval(expectedVersion, approvedByUserId, approvedAtUtc);
        // U2-a (URT-D9): the plan-entered line speed replaces the profile's value in the captured snapshot.
        ParameterSnapshot = parameterSnapshot is null ? null : CaptureWithPlanValues(parameterSnapshot);
    }

    /// <summary>
    /// Üretim U2-a (URT-D2, API-10): the approver's rejection of a Draft instead of its approval. The plan stays a Draft
    /// (there is no separate "Kontrolde" step), the rejection with its reason is recorded and the version advances by
    /// one, so the planner's next revision or a later approval names the version after it. In order, and nothing is
    /// applied on a refusal: the version fence; a cancelled plan (<see cref="ManufacturingRuleCodes.PlanCancelled"/>);
    /// only a Draft (<see cref="ManufacturingRuleCodes.PlanNotDraft"/>); a reason.
    /// </summary>
    public void Reject(long expectedVersion, Guid rejectedByUserId, DateTimeOffset rejectedAtUtc, string reason)
    {
        RequireFence(expectedVersion);
        if (Status == PlanStatus.Cancelled)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.PlanCancelled, "İptal edilmiş plan reddedilmez.");
        }

        if (Status != PlanStatus.Draft)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.PlanNotDraft, "Yalnız taslak plan reddedilebilir.");
        }

        var rejectedBy = RequireId(rejectedByUserId, nameof(rejectedByUserId));
        RequireDeciderIsNotPreparer(rejectedBy);
        var text = RequireReason(reason, nameof(reason));
        Version++;
        _rejections.Add(new PlanRejection(_rejections.Count + 1, text, rejectedBy, rejectedAtUtc.ToUniversalTime(),
            Version));
    }

    /// <summary>
    /// Üretim U2-a (API-11): cancels a Draft, or an approved plan that opened no order. The plan is kept with its reason
    /// (never deleted) and takes no further decision; the version advances by one. In order, and nothing is applied on a
    /// refusal: the version fence; already cancelled (<see cref="ManufacturingRuleCodes.PlanCancelled"/>); an order
    /// exists (<see cref="ManufacturingRuleCodes.PlanHasOrder"/>); a reason.
    /// </summary>
    public void Cancel(long expectedVersion, Guid cancelledByUserId, DateTimeOffset cancelledAtUtc, string reason)
    {
        RequireFence(expectedVersion);
        if (Status == PlanStatus.Cancelled)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.PlanCancelled, "Plan zaten iptal edilmiş.");
        }

        if (OrderId is not null)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.PlanHasOrder,
                "Emir açmış plan iptal edilmez; emrin kendi iptal/ters zinciri ayrıdır.");
        }

        var cancelledBy = RequireId(cancelledByUserId, nameof(cancelledByUserId));
        var text = RequireReason(reason, nameof(reason));
        Cancellation = new PlanCancellation(text, cancelledBy, cancelledAtUtc.ToUniversalTime());
        Status = PlanStatus.Cancelled;
        Version++;
    }

    /// <summary>
    /// Üretim U2-a (URT-D1): records that this approved plan opened <paramref name="orderId"/>. One plan opens exactly one
    /// order: a second is refused (<see cref="ManufacturingRuleCodes.PlanOrderExists"/>). Only
    /// <see cref="ProductionOrder.FromApprovedPlan"/> calls it; the plan's version does not move.
    /// </summary>
    internal void RecordOrder(Guid orderId)
    {
        if (Status != PlanStatus.Approved)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.PlanNotApproved,
                "Üretim emri yalnız onaylı plandan oluşturulabilir.");
        }

        if (OrderId is not null)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.PlanOrderExists,
                "Onaylı plan emrini zaten açtı; bir plandan tek emir açılır.");
        }

        OrderId = RequireId(orderId, nameof(orderId));
    }

    /// <summary>
    /// Üretim U2-b / API-16 (SAT-D219): links this plan to an approved sales order line, moves the link to another line, or
    /// removes it (<paramref name="link"/> null). The plan's version does not move; the link revision advances by one.
    /// In order, and nothing is applied on a refusal: the version fence; cancelled
    /// (<see cref="ManufacturingRuleCodes.PlanCancelled"/>); the link revision the caller read
    /// (<see cref="ManufacturingRuleCodes.PlanSalesOrderLinkChanged"/>); the plan's order is closed
    /// (<see cref="ManufacturingRuleCodes.PlanSalesOrderLinkOrderClosed"/>); no change
    /// (<see cref="ManufacturingRuleCodes.PlanSalesOrderLinkNoChange"/>); the customer-owned inputs' party
    /// (<see cref="ManufacturingRuleCodes.PlanSalesOrderPartyMismatch"/>).
    /// </summary>
    public void ChangeSalesOrderLink(long expectedVersion, long expectedLinkRevision, PlanSalesOrderLink? link,
        bool orderClosed)
    {
        RequireFence(expectedVersion);
        if (Status == PlanStatus.Cancelled)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.PlanCancelled, "İptal edilmiş planın bağı değişmez.");
        }

        if (expectedLinkRevision != SalesOrderLinkRevision)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.PlanSalesOrderLinkChanged,
                "Planın satış siparişi bağı siz okuduktan sonra değişti; planı yeniden okuyun.");
        }

        if (orderClosed)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.PlanSalesOrderLinkOrderClosed,
                "Planın emri kapanmış; satış siparişi bağı artık değişmez.");
        }

        if (link is not null && !link.IsWellFormed())
        {
            throw new ArgumentException("Satış siparişi bağı eksik veya geçersiz.", nameof(link));
        }

        if (link is null ? SalesOrderLink is null : SalesOrderLink?.SalesOrderLineId == link.SalesOrderLineId)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.PlanSalesOrderLinkNoChange,
                "İstek planın satış siparişi bağında hiçbir şeyi değiştirmiyor.");
        }

        if (link is not null && !InputsMatchParty(link.CustomerPartyId))
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.PlanSalesOrderPartyMismatch,
                "Plandaki müşteri malı girdiler sipariş satırının müşterisine ait değil.");
        }

        SalesOrderLink = link;
        SalesOrderLinkRevision++;
    }

    /// <summary>
    /// U2-b / API-16: true when no customer-owned input names another party than <paramref name="partyId"/>. Inputs
    /// without commercial provenance (company-owned or legacy) never conflict.
    /// </summary>
    private bool InputsMatchParty(Guid partyId) => InputsMatchParty(_inputs, partyId);

    private static bool InputsMatchParty(IEnumerable<PlannedInput> inputs, Guid partyId) =>
        inputs.All(input => input.CommercialContext is not { } context || context.PartyId == partyId);

    /// <summary>
    /// The numbers this plan entered itself for its engines: the line speed (U2-a) and the process spec's values (U2-b1).
    /// </summary>
    public IReadOnlyList<PlanEnteredValue> PlanEnteredValues()
    {
        var values = new List<PlanEnteredValue>();
        if (LineSpeedMetresPerMinute is { } speed)
        {
            values.Add(new PlanEnteredValue(LineSpeedParameter, LineSpeedUnit, speed));
        }

        if (ProcessSpec is { } processSpec)
        {
            values.AddRange(processSpec.EngineValues(Process));
        }

        return values;
    }

    /// <summary>
    /// The profile snapshot as this plan captures it: each plan-entered value replaces the profile's (keeping the
    /// profile's unit for that key when it has one) or is added.
    /// </summary>
    private PlanParameterSnapshotV1 CaptureWithPlanValues(PlanParameterSnapshotV1 snapshot) =>
        PlanEnteredValues().Aggregate(snapshot, (captured, value) =>
            captured.WithPlanValue(value.Key, value.Unit, value.Number));

    /// <summary>A stored snapshot carries exactly this plan's entered values (by key and number), each with source Plan.</summary>
    private bool MatchesPlanValues(PlanParameterSnapshotV1 snapshot)
    {
        var stored = snapshot.Values.Where(value => value.Source is not null)
            .OrderBy(value => value.Key, StringComparer.Ordinal)
            .Select(value => (value.Key, value.Number))
            .ToArray();
        var entered = PlanEnteredValues()
            .OrderBy(value => value.Key, StringComparer.Ordinal)
            .Select(value => (value.Key, (decimal?)value.Number))
            .ToArray();
        return stored.SequenceEqual(entered);
    }

    /// <summary>
    /// MFG-PLAN-EDIT: the edit of a plan that has no downstream effect yet. Only a Draft is revised: an approved plan
    /// has recorded a decision and may have opened its order (one plan opens one order, URT-D1), and an order carries its
    /// own snapshot from which inputs are issued — so an approved plan is never edited here; a post-approval revision
    /// with snapshot and difference (PR-09) is a separate flow. A cancelled plan is not a Draft either.
    /// A revision replaces the work center, the planned inputs, the target and (U2-a) the plan-entered line speed AS A
    /// WHOLE and advances the version by one. In order, and nothing is applied on a refusal:
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
        IEnumerable<PlannedInput> inputs, ProductionTarget? target, CutToLengthSpec? cutToLengthSpec = null,
        decimal? lineSpeedMetresPerMinute = null, PlanProcessSpec? processSpec = null)
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
        RequireLineSpeed(Process, lineSpeedMetresPerMinute, nameof(lineSpeedMetresPerMinute));
        processSpec?.RequireAllowedFor(Process, nameof(processSpec));
        if (SalesOrderLink is { } salesLink && !InputsMatchParty(newInputs, salesLink.CustomerPartyId))
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.PlanSalesOrderPartyMismatch,
                "Plan bir satış siparişi satırına bağlı; müşteri malı girdiler o satırın müşterisine ait olmalıdır.");
        }


        if (newWorkCenter == WorkCenterId && Equals(target, Target) && newInputs.SequenceEqual(_inputs)
            && CutSpecsEqual(cutToLengthSpec, CutToLengthSpec) && lineSpeedMetresPerMinute == LineSpeedMetresPerMinute
            && Equals(processSpec, ProcessSpec))
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.PlanRevisionNoChange,
                "Düzenleme planda hiçbir şeyi değiştirmiyor.");
        }

        WorkCenterId = newWorkCenter;
        Target = target;
        CutToLengthSpec = cutToLengthSpec;
        LineSpeedMetresPerMinute = lineSpeedMetresPerMinute;
        ProcessSpec = processSpec;
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
        RequireFence(expectedVersion);
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
        RequireFence(expectedVersion);
        if (Status != PlanStatus.Draft)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.PlanNotDraft, "Yalnız taslak plan onaylanabilir.");
        }
    }

    private void RequireFence(long expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ManufacturingVersionConflictException(expectedVersion, Version);
        }
    }

    private static Guid RequireId(Guid value, string parameterName) =>
        value == Guid.Empty ? throw new ArgumentException("Kimlik boş olamaz.", parameterName) : value;

    private static string RequireReason(string? reason, string parameterName)
    {
        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > DecisionReasonMaximumLength || text.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"Gerekçe 1–{DecisionReasonMaximumLength} karakter olmalı ve kontrol karakteri taşımamalıdır.", parameterName);
        }

        return text;
    }

    /// <summary>
    /// U2-a (URT-D9): a plan-entered line speed only on a Slitting or Tube / Profile plan, positive, with at most the
    /// Settings value scale. A caller error otherwise (the API refuses it as an invalid field first).
    /// </summary>
    private static void RequireLineSpeed(ManufacturingProcess process, decimal? lineSpeed, string parameterName)
    {
        if (lineSpeed is not { } speed)
        {
            return;
        }

        if (process is not (ManufacturingProcess.Slitting or ManufacturingProcess.TubeProfile))
        {
            throw new ArgumentException("Hat hızı planda yalnız Dilme ve Boru/Profil süreçlerinde girilir.", parameterName);
        }

        if (speed <= 0m || decimal.Round(speed, LineSpeedScale) != speed)
        {
            throw new ArgumentOutOfRangeException(parameterName, lineSpeed,
                $"Hat hızı sıfırdan büyük ve en fazla {LineSpeedScale} ondalık olmalıdır.");
        }
    }

    /// <summary>
    /// U2-a: the net usable width entered on a slitting spec cannot exceed any planned parent coil's width (the Product
    /// snapshot captured for that roll). A roll without a snapshot (legacy) has no captured width to compare.
    /// </summary>
    private static void RequireNetWidthWithinParents(SlittingSpec? spec, IEnumerable<PlannedInput> inputs)
    {
        if (spec?.NetUsableWidthMm is not { } netWidth)
        {
            return;
        }

        if (inputs.Any(input => input.TechnicalSnapshot is { } snapshot && netWidth > snapshot.WidthMm))
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.PlanNetWidthExceedsParent,
                "Net kullanılabilir en ana rulonun eninden büyük olamaz.");
        }
    }

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
    CutToLengthSpec? CutToLengthSpec = null, decimal? LineSpeedMetresPerMinute = null, long? PlanVersion = null,
    PlanProcessSpec? ProcessSpec = null);

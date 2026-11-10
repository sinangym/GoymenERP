using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Domain.RiskExceptions;

/// <summary>
/// The approval axis of a single-shipment risk exception (API-07; SAT-D153…D162, EK56 SAT-D286). Born <see cref="Requested"/>
/// and moves once: <c>Requested → Approved</c>, <c>Requested → Rejected</c> or, by its requester, <c>Requested → Withdrawn</c>.
/// All three are terminal; nothing is deleted.
/// Whether an approved exception still releases its shipment is a question of the day (<see cref="SalesShipmentRiskException.IsValidOn"/>),
/// never a stored state change.
/// </summary>
public enum SalesShipmentRiskExceptionState
{
    Requested = 1,
    Approved = 2,
    Rejected = 3,

    /// <summary>EK56 SAT-D286: the requester withdrew the pending request; it is neither approved nor rejected afterwards.</summary>
    Withdrawn = 4,
}

/// <summary>Who decided the exception and when; the reason is a rejection's (required there, absent on an approval).</summary>
public sealed record SalesShipmentRiskExceptionDecision(string DecidedBy, DateTimeOffset DecidedAtUtc, string? RejectionReason);

/// <summary>
/// A single-shipment risk exception of an approved sales order (API-07; SAT-D153…D162, DEC-FIN-009):
/// <list type="bullet">
/// <item><b>One shipment (SAT-D154):</b> bound to exactly one loading instruction id. Sales records the id as an opaque
/// reference and never reads or writes the Logistics owner's tables (SAT-D209); the exception releases only that id.</item>
/// <item><b>Separate approval record (SAT-D161):</b> it opens no order revision and no order re-approval; the order is
/// not changed by it.</item>
/// <item><b>Maker≠checker (SAT-D153/D159):</b> the requester never approves or rejects their own request.</item>
/// <item><b>Validity (SAT-D160):</b> an approved exception is valid only on the approval's business date (Türkiye time),
/// fixed at approval as <see cref="ValidOn"/>; on any later date it has lapsed. Cancellation lapse (implementation detail,
/// reversible) is the Logistics gate's concern: instruction ids are never reused, so an exception of a cancelled
/// instruction releases nothing else.</item>
/// <item><b>Audit (SAT-D154):</b> who requested, who decided, which shipment, the reason and the times are facts of the
/// record itself and of its audit events.</item>
/// </list>
/// </summary>
public sealed class SalesShipmentRiskException
{
    public const int MaximumReasonLength = SalesOrderDraft.MaximumNoteLength;
    public const int MaximumActorLength = SalesOrderDraft.MaximumActorLength;

    private bool restored;

    private SalesShipmentRiskException(Guid id, Guid companyId, Guid orderId, int exceptionNo, Guid loadingInstructionId,
        string reason, string requestedBy, DateTimeOffset now)
    {
        Id = Identity.Require(id, nameof(id));
        CompanyId = Identity.Require(companyId, nameof(companyId));
        OrderId = Identity.Require(orderId, nameof(orderId));
        ExceptionNo = exceptionNo >= 1 ? exceptionNo : throw new ArgumentOutOfRangeException(nameof(exceptionNo));
        LoadingInstructionId = Identity.Require(loadingInstructionId, nameof(loadingInstructionId));
        Reason = reason;
        RequestedBy = RequireActor(requestedBy);
        RequestedAtUtc = now.ToUniversalTime();
        UpdatedAtUtc = RequestedAtUtc;
    }

    public Guid Id { get; }

    /// <summary>The server-resolved company; never taken from the request and never changed.</summary>
    public Guid CompanyId { get; }

    /// <summary>The approved sales order the shipment belongs to.</summary>
    public Guid OrderId { get; }

    /// <summary>1-based number of the exception within its order; the route key of the decisions.</summary>
    public int ExceptionNo { get; }

    /// <summary>The single shipment (loading instruction) this exception releases; an opaque reference (SAT-D209).</summary>
    public Guid LoadingInstructionId { get; }

    /// <summary>Why the exception is asked (required; audited).</summary>
    public string Reason { get; }

    public SalesShipmentRiskExceptionState State { get; private set; } = SalesShipmentRiskExceptionState.Requested;

    public string RequestedBy { get; }

    public DateTimeOffset RequestedAtUtc { get; }

    /// <summary>The decision; null while the exception is <see cref="SalesShipmentRiskExceptionState.Requested"/>.</summary>
    public SalesShipmentRiskExceptionDecision? Decision { get; private set; }

    /// <summary>The approval's business date (SAT-D160); null unless approved.</summary>
    public DateOnly? ValidOn { get; private set; }

    /// <summary>Optimistic concurrency: 1 at creation, +1 on the decision; a refused command moves nothing.</summary>
    public long Version { get; private set; } = 1;

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Records a request: the reason is required and bounded (never cut).</summary>
    public static SalesShipmentRiskException Request(Guid id, Guid companyId, Guid orderId, int exceptionNo,
        Guid loadingInstructionId, string? reason, string requestedBy, DateTimeOffset now) =>
        new(id, companyId, orderId, exceptionNo, loadingInstructionId, RequireReason(reason), requestedBy, now);

    /// <summary>SAT-D153/D160: one approver who is not the requester; valid until the end of <paramref name="businessDate"/>.</summary>
    public void Approve(long expectedVersion, string actorId, DateTimeOffset now, DateOnly businessDate)
    {
        RequireVersion(expectedVersion);
        var actor = RequireDecider(actorId);
        Decide(SalesShipmentRiskExceptionState.Approved, new SalesShipmentRiskExceptionDecision(actor, now.ToUniversalTime(), null), now);
        ValidOn = businessDate;
    }

    /// <summary>SAT-D153: one decider who is not the requester; the rejection carries its reason.</summary>
    public void Reject(long expectedVersion, string actorId, string? reason, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireDecider(actorId);
        var text = RequireReason(reason);
        Decide(SalesShipmentRiskExceptionState.Rejected, new SalesShipmentRiskExceptionDecision(actor, now.ToUniversalTime(), text), now);
    }

    /// <summary>
    /// EK56 SAT-D286: the requester withdraws their own pending request (another actor's request is not opened). No decision is
    /// recorded: the withdrawer is the requester by rule, and the instant is <see cref="UpdatedAtUtc"/> and the audit event.
    /// </summary>
    public void Withdraw(long expectedVersion, string actorId, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireActor(actorId);
        if (State != SalesShipmentRiskExceptionState.Requested)
        {
            throw new SalesRuleException(SalesRuleCodes.StateConflict, $"Yalnız onay bekleyen risk istisnası geri çekilir (durum: {State}).");
        }

        if (!string.Equals(actor, RequestedBy, StringComparison.Ordinal))
        {
            throw new SalesRuleException(SalesRuleCodes.RiskExceptionNotRequester, "Risk istisnasını yalnız isteyen kişi geri çekebilir.");
        }

        State = SalesShipmentRiskExceptionState.Withdrawn;
        Decision = null;
        UpdatedAtUtc = now.ToUniversalTime();
        Version = checked(Version + 1);
    }

    /// <summary>SAT-D160: an approved exception releases its shipment only on its approval's business date.</summary>
    public bool IsValidOn(DateOnly businessDate) =>
        State == SalesShipmentRiskExceptionState.Approved && ValidOn == businessDate;

    /// <summary>
    /// Restores the stored decision onto an exception rebuilt through <see cref="Request"/>: the facts must match the state
    /// (Requested and Withdrawn: none; Approved: a decision without a reason and a validity date; Rejected: a decision with a
    /// reason and no validity date), the decider is not the requester and the version is at least the transitions it implies.
    /// A corrupt row is refused, never repaired. Runs once.
    /// </summary>
    public void Restore(SalesShipmentRiskExceptionState state, SalesShipmentRiskExceptionDecision? decision, DateOnly? validOn,
        long storedVersion, DateTimeOffset updatedAtUtc)
    {
        if (restored || Version != 1 || State != SalesShipmentRiskExceptionState.Requested)
        {
            throw new InvalidOperationException("Risk istisnasının kayıtlı durumu yalnız bir kez ve yeni kurulmuş kayda geri yüklenir.");
        }

        var consistent = state switch
        {
            SalesShipmentRiskExceptionState.Requested or SalesShipmentRiskExceptionState.Withdrawn => decision is null && validOn is null,
            SalesShipmentRiskExceptionState.Approved => decision is { RejectionReason: null } && validOn is not null,
            SalesShipmentRiskExceptionState.Rejected =>
                decision is { RejectionReason: { Length: > 0 and <= MaximumReasonLength } } && validOn is null,
            _ => false,
        };
        if (!consistent || decision is { DecidedBy: var by }
            && (string.IsNullOrWhiteSpace(by) || by.Length > MaximumActorLength || string.Equals(by, RequestedBy, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Kayıtlı risk istisnası kararı kayıtlı durumuyla uyuşmuyor.");
        }

        var minimumVersion = state == SalesShipmentRiskExceptionState.Requested ? 1L : 2L;
        if (storedVersion < minimumVersion)
        {
            throw new InvalidOperationException("Kayıtlı risk istisnası sürümü, kararların hesabıyla uyuşmuyor.");
        }

        State = state;
        Decision = decision;
        ValidOn = validOn;
        Version = storedVersion;
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
        restored = true;
    }

    private string RequireDecider(string actorId)
    {
        var actor = RequireActor(actorId);
        if (State != SalesShipmentRiskExceptionState.Requested)
        {
            throw new SalesRuleException(SalesRuleCodes.StateConflict, $"Yalnız onay bekleyen risk istisnası karara bağlanır (durum: {State}).");
        }

        return !string.Equals(actor, RequestedBy, StringComparison.Ordinal)
            ? actor
            : throw new SalesRuleException(SalesRuleCodes.RiskExceptionSelfDecision, "Risk istisnasını isteyen kendi talebini onaylayamaz ya da reddedemez.");
    }

    private void Decide(SalesShipmentRiskExceptionState state, SalesShipmentRiskExceptionDecision decision, DateTimeOffset now)
    {
        State = state;
        Decision = decision;
        UpdatedAtUtc = now.ToUniversalTime();
        Version = checked(Version + 1);
    }

    private void RequireVersion(long expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new SalesVersionConflictException(expectedVersion, Version);
        }
    }

    private static string RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new SalesRuleException(SalesRuleCodes.RiskExceptionReasonRequired, "Risk istisnası gerekçe ister.");
        }

        return reason.Length <= MaximumReasonLength
            ? reason
            : throw new SalesRuleException(SalesRuleCodes.RiskExceptionReasonTooLong,
                $"Gerekçe en fazla {MaximumReasonLength} karakterdir; fazlası kesilmez, reddedilir.");
    }

    private static string RequireActor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Değer boş olamaz.", nameof(value));
        }

        var text = value.Trim();
        return text.Length <= MaximumActorLength
            ? text
            : throw new ArgumentException($"Değer en fazla {MaximumActorLength} karakterdir.", nameof(value));
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// Who asked, under which permission and in which conversation — the ADR-009 audit envelope of one
/// draft write, the same shape Inventory and Accounting use. Every value comes from the caller's
/// server-side context (the HTTP slice); this module never makes one up. The company is not here:
/// it is always the server-resolved company context, and a draft of any other company is refused.
/// <see cref="IdempotencyKey"/> is the client's <c>Idempotency-Key</c> (R3): with
/// <see cref="SalesDraftRequestHash"/> it is the replay identity of a create.
/// </summary>
public sealed record SalesDraftWriteEnvelope(
    string ActorId,
    string PermissionCode,
    IReadOnlyCollection<string> RoleCodes,
    string CorrelationId,
    string? CausationId,
    string IdempotencyKey,
    string SourceChannel,
    string? ClientIpAddress = null,
    string? UserAgent = null);

/// <summary>What a draft write did: a new record (201), or the same accepted attempt again with no second effect (200, replay).</summary>
public enum SalesDraftWriteOutcome
{
    Recorded = 1,
    Replayed = 2,
}

/// <summary>The versioned command names the Sales attempt record is keyed by (ADR-011 §2).</summary>
public static class SalesDraftCommands
{
    /// <summary>The create of an order draft (#257 POST /api/v1/sales/drafts).</summary>
    public const string CreateV1 = "sales.order-draft.create.v1";

    /// <summary>SALES-A1: POST /api/v1/sales/drafts/{id}/submission — Preparing/Rejected → PendingApproval.</summary>
    public const string SubmitV1 = "sales.order-draft.submit.v1";

    /// <summary>SALES-A1: POST /api/v1/sales/drafts/{id}/approval — PendingApproval → Approved.</summary>
    public const string ApproveV1 = "sales.order-draft.approve.v1";

    /// <summary>SALES-A1: POST /api/v1/sales/drafts/{id}/rejection — PendingApproval → Rejected.</summary>
    public const string RejectV1 = "sales.order-draft.reject.v1";

    /// <summary>Q9: POST /api/v1/sales/drafts/{id}/customer-acceptance.</summary>
    public const string RecordCustomerAcceptanceV1 = "sales.order-draft.customer-acceptance.record.v1";

    /// <summary>K3 Head B: POST /api/v1/sales/drafts/{id}/commercial-revision — the commercial fields of every line replaced; an approved draft is archived and advances a revision.</summary>
    public const string ReviseCommercialV1 = "sales.order-draft.revise-commercial.v1";

    /// <summary>Reasoned maker-checker approval of every current below-floor commercial snapshot.</summary>
    public const string ApprovePriceFloorExceptionsV1 = "sales.order-draft.approve-price-floor-exceptions.v1";

    /// <summary>D2 (Sinan 5749046401): POST /api/v1/sales/drafts/{id}/withdrawal — Preparing/PendingApproval/Rejected → Withdrawn (terminal; nothing deleted).</summary>
    public const string WithdrawV1 = "sales.order-draft.withdraw.v1";

    /// <summary>DEC-SAL-003: the delivery-schedule update (dates only, no re-approval, audited).</summary>
    public const string RescheduleDeliveryV1 = "sales.order-draft.reschedule-delivery.v1";

    /// <summary>DEC-SAL-002: POST /api/v1/sales/drafts/{id}/quantity-revision — the ordered quantity of every line replaced with a reason; an approved draft is archived and advances a revision.</summary>
    public const string ReviseQuantityV1 = "sales.order-draft.revise-quantity.v1";

    /// <summary>DEC-SAL-005: POST /api/v1/sales/drafts/{id}/cancellation — an approved order's lifecycle Draft → Cancelled after its impact preview.</summary>
    public const string CancelV1 = "sales.order-draft.cancel.v1";

    /// <summary>Partial delivery: POST /api/v1/sales/drafts/{id}/remainder-changes — a remainder cancel/revise is requested (nothing on the line moves).</summary>
    public const string RequestRemainderChangeV1 = "sales.order-draft.request-remainder-change.v1";

    /// <summary>Partial delivery: POST …/remainder-changes/{changeNo}/approval — the pending change is applied on the floor read afresh.</summary>
    public const string ApproveRemainderChangeV1 = "sales.order-draft.approve-remainder-change.v1";

    /// <summary>Partial delivery: POST …/remainder-changes/{changeNo}/rejection — the pending change is closed; nothing changes.</summary>
    public const string RejectRemainderChangeV1 = "sales.order-draft.reject-remainder-change.v1";

    public static bool IsRemainderCommand(string commandName) =>
        commandName is RequestRemainderChangeV1 or ApproveRemainderChangeV1 or RejectRemainderChangeV1;

    public static bool IsStateCommand(string commandName) =>
        commandName is SubmitV1 or ApproveV1 or RejectV1 or WithdrawV1 or RecordCustomerAcceptanceV1;
}

/// <summary>The stable refusal codes of the Sales draft store, beside the domain's own rule codes.</summary>
public static class SalesDraftStoreCodes
{
    /// <summary>The draft's company is not the server-resolved company context (or there is none).</summary>
    public const string CompanyContextMismatch = "sales_company_context_mismatch";

    /// <summary>
    /// The same idempotency key was offered again with another request content OR by another actor
    /// (#257: 409 same-key different-payload; ADR-011 §3). One code for both, as the platform
    /// Idempotency module's single Conflict kind: the answer must not disclose that the key exists
    /// for someone else, nor anything of the stored result.
    /// </summary>
    public const string SameKeyPayloadMismatch = "sales_same_key_payload_mismatch";

    /// <summary>Another write took the same key, draft id or document number first; nothing of this write was stored, the same request may be retried.</summary>
    public const string ConcurrentChange = "sales_concurrent_change";

    /// <summary>A text does not fit its stored column; it is refused, never cut.</summary>
    public const string ValueDoesNotFit = "sales_value_does_not_fit";

    /// <summary>The list cursor is not this company's, not this ordering's, or malformed (#257: 400).</summary>
    public const string InvalidCursor = "sales_invalid_cursor";
}

/// <summary>A stored draft could not be written or read; <see cref="Code"/> says why. Nothing was stored.</summary>
public sealed class SalesDraftStoreException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}

/// <summary>The stored draft, whether this call recorded it or found the same attempt already recorded, and the correlation of the first attempt.</summary>
public sealed record SalesDraftStoreResult(SalesOrderDraft Draft, SalesDraftWriteOutcome Outcome, string OriginalCorrelationId);

/// <summary>
/// A terminal refusal of a create (ADR-011 §7: the authorized <c>VersionConflict</c> / <c>NotFound</c>
/// kind), recorded under the attempt key so that the same key always answers the same result. The
/// status, the code and the field errors are exactly what the first attempt was answered.
/// </summary>
public sealed record SalesDraftTerminalRefusal(int Status, string Code, IReadOnlyList<SalesDraftFieldError> Errors);

/// <summary>What the attempt key already holds: the stored draft, or the recorded terminal refusal — never both.</summary>
public sealed record SalesDraftReplay(SalesOrderDraft? Draft, SalesDraftTerminalRefusal? Refusal, string OriginalCorrelationId);

/// <summary>One page of the company's drafts, newest first (createdAtUtc desc, id desc); <see cref="NextCursor"/> null is the end (#257).</summary>
public sealed record SalesDraftPage(IReadOnlyList<SalesOrderDraft> Items, string? NextCursor);

/// <summary>
/// The audit facts of one internal-approval command (SALES-A1, ADR-009): the versioned event type
/// (<c>sales.order-draft.submitted.v1</c> / <c>approved.v1</c> / <c>rejected.v1</c>), the audit
/// operation and the user-facing reason. Written by the store in the same save as the draft's
/// state, with the entity version before and after.
/// </summary>
public sealed record SalesDraftStateAudit(string EventType, string Operation, string Reason);

/// <summary>
/// What the store did for a state command: the draft after the transition (or the same stored
/// result again, <see cref="SalesDraftWriteOutcome.Replayed"/>) and the correlation of the attempt
/// that first recorded it. A replay answers the draft as it is NOW (the persisted record), marked
/// as a replay with its first correlation — the Counterparty / Manufacturing pattern.
/// </summary>
public sealed record SalesDraftStateResult(SalesOrderDraft Draft, SalesDraftWriteOutcome Outcome, string OriginalCorrelationId);

/// <summary>
/// The persistent draft store of the server-resolved company (SALES-P2 implements it; SALES-P1
/// only declares it). One call is one save: the header, the lines with their owner snapshots, the
/// delivery slices, the attempt record and the audit event commit together or not at all.
/// <list type="bullet">
/// <item><b>Replay (ADR-011, R3):</b> (company, <see cref="SalesDraftCommands.CreateV1"/>, uppercase
/// SHA-256 of the trimmed idempotency key) is the attempt identity; the raw key is never stored. The
/// same key with the same <see cref="SalesDraftRequestHash"/> and the same actor answers the first
/// stored draft with <see cref="SalesDraftWriteOutcome.Replayed"/> and the original correlation;
/// another hash or another actor is <see cref="SalesDraftStoreCodes.SameKeyPayloadMismatch"/> — one
/// definite 409 that discloses nothing of the stored result nor that the key exists for someone else.
/// Authority is checked by the caller before the replay is read. A key of
/// another company is invisible: a company change is not a rollback (R3).</item>
/// <item><b>Document number:</b> assigned by the store on first commit through
/// <paramref name="build"/>, company-scoped and technical; not an official document (Ö8); no
/// gap-free guarantee is given.</item>
/// <item><b>Fail-closed:</b> the caller obtains every owner snapshot (Product eligibility, Settings
/// unit and money profiles, Cari account) BEFORE building the draft; a missing one is that
/// owner's refusal, never a stored draft. Today the approved Settings provider answers Unavailable
/// for money, so no real draft can be stored until it does.</item>
/// </list>
/// </summary>
public interface ISalesOrderDraftStore
{
    /// <summary>
    /// Stores a new draft, or answers the same attempt already stored. <paramref name="build"/>
    /// receives the document number the store assigns and returns the complete draft (it must be
    /// storable: at least one line).
    /// </summary>
    Task<SalesDraftStoreResult> StoreDraftAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        Func<string, SalesOrderDraft> build, CancellationToken cancellationToken = default);

    /// <summary>
    /// ADR-011 §4/§7: what the attempt key already holds for this actor and content — the stored
    /// draft or the recorded terminal refusal — or null when the key is new. The same key with another
    /// content or another actor is <see cref="SalesDraftStoreCodes.SameKeyPayloadMismatch"/>. The
    /// caller checks authority before calling (§5) and reads no owner when a replay is found.
    /// <paramref name="commandName"/> is the attempt identity's command (<see cref="SalesDraftCommands"/>);
    /// the create by default.
    /// </summary>
    Task<SalesDraftReplay?> FindReplayAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        CancellationToken cancellationToken = default, string commandName = SalesDraftCommands.CreateV1);

    /// <summary>
    /// ADR-011 §7: records a terminal refusal under the attempt key, so a retry with the same key,
    /// content and actor answers the same refusal instead of asking the owners again. A key already
    /// holding an attempt is <see cref="SalesDraftStoreCodes.ConcurrentChange"/> (the retry replays it).
    /// <paramref name="draftId"/> identifies the existing draft for a mutation refusal; create-time
    /// refusals leave it null.
    /// </summary>
    Task RecordRefusalAsync(SalesDraftWriteEnvelope envelope, string requestHash, SalesDraftTerminalRefusal refusal,
        CancellationToken cancellationToken = default, string commandName = SalesDraftCommands.CreateV1, Guid? draftId = null);

    /// <summary>
    /// SALES-A1: one internal-approval transition of the company's stored draft in ONE save — the
    /// draft's approval state, facts and version, the attempt record under
    /// (<paramref name="commandName"/>, key hash) and the audit event commit together or not at all.
    /// The caller has already measured its authority; <paramref name="mutate"/> is the domain
    /// transition (it names the expected version and throws the rule refusal — the caller records
    /// terminal refusals itself, ADR-011 §7). Another company's draft, or none, is null (never 403).
    /// A same-key attempt already stored answers <see cref="SalesDraftWriteOutcome.Replayed"/> with the
    /// draft as it is now; another content or actor is <see cref="SalesDraftStoreCodes.SameKeyPayloadMismatch"/>;
    /// a lost race (attempt key, draft version) is <see cref="SalesDraftStoreCodes.ConcurrentChange"/>
    /// with nothing stored.
    /// </summary>
    Task<SalesDraftStateResult?> UpdateDraftStateAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        Guid draftId, Action<SalesOrderDraft, DateTimeOffset> mutate, SalesDraftStateAudit audit,
        CancellationToken cancellationToken = default);

    /// <summary>The company's stored draft, or null (another company's draft is null too, never 403).</summary>
    /// <summary>
    /// K3 Head B: the commercial revision, one save — the same key rules as a state command (§3/§4/§7/§8);
    /// <paramref name="mutate"/> is the domain's <c>ReviseCommercial</c> and returns the archived approved
    /// snapshot when one was made. The store writes that snapshot's rows, the header (revision, approval
    /// axis, version) and every line's commercial columns together with the attempt record and the audit
    /// event, or nothing at all. <paramref name="beforeWrite"/> revalidates the caller's narrow
    /// authority after every awaited store read and before any domain, attempt or audit mutation.
    /// The command name must be <see cref="SalesDraftCommands.ReviseCommercialV1"/>.
    /// </summary>
    Task<SalesDraftStateResult?> ReviseDraftCommercialAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        Guid draftId, Func<SalesOrderDraft, DateTimeOffset, ApprovedRevisionSnapshot?> mutate, SalesDraftStateAudit audit,
        Func<CancellationToken, Task> beforeWrite, CancellationToken cancellationToken = default);

    /// <summary>
    /// DEC-SAL-003: the delivery-schedule update, one save. The same key rules as a state command; the domain decides
    /// (expected version, state, every line and slice, the no-change refusal) and returns the recorded schedule, which
    /// is APPENDED (schedule rows) beside the header's version, the attempt record and the audit event with the dates
    /// before and after — or nothing at all. No existing line or slice row changes. <paramref name="beforeWrite"/> is
    /// the caller's last fence, run after the draft is read and before the mutation.
    /// </summary>
    Task<SalesDraftStateResult?> RescheduleDeliveryAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        Guid draftId, Func<SalesOrderDraft, DateTimeOffset, DeliveryScheduleSnapshot> mutate, SalesDraftStateAudit audit,
        Func<CancellationToken, Task> beforeWrite, CancellationToken cancellationToken = default);

    /// <summary>
    /// DEC-SAL-005: the cancellation of an approved order, one save. The same key rules as a state command (§3/§4/§7/§8);
    /// <paramref name="beforeWrite"/> receives the draft as read (tracked header) and is the caller's last word before the
    /// mutation — its fences and the impact preview read afresh from the owners, answered as the snapshot to keep; any
    /// refusal it throws leaves nothing written. <paramref name="mutate"/> is the domain's <c>Cancel</c>. The store APPENDS the
    /// cancellation row (who, when, why, the revision, the version produced, the impact the user confirmed) and moves the
    /// header's lifecycle state and version together with the attempt record and the audit event — or nothing at all. No
    /// line, revision, schedule or approval column changes. The command name must be <see cref="SalesDraftCommands.CancelV1"/>.
    /// </summary>
    Task<SalesDraftStateResult?> CancelDraftAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        Guid draftId, Func<SalesOrderDraft, CancellationImpactSnapshot, DateTimeOffset, CancellationFacts> mutate, SalesDraftStateAudit audit,
        Func<SalesOrderDraft, CancellationToken, Task<CancellationImpactSnapshot>> beforeWrite, CancellationToken cancellationToken = default);

    /// <summary>
    /// DEC-SAL-002: the quantity revision, one save — the commercial revision's key rules and shape (§3/§4/§7/§8).
    /// <paramref name="beforeWrite"/> receives the draft as read (tracked header and lines) and is the caller's last word before
    /// the mutation — its fences, the domain's own preconditions and the owners' links read afresh; any refusal it throws
    /// leaves nothing written. <paramref name="mutate"/> is the domain's <c>ReviseQuantities</c> and returns the archived
    /// approved snapshot (with its reason) when one was made. The store writes that snapshot's rows, the header (revision,
    /// approval axis, version) and every line's quantity columns together with the attempt record and the audit event, or
    /// nothing at all. No slice, schedule or commercial column changes. The command name must be
    /// <see cref="SalesDraftCommands.ReviseQuantityV1"/>.
    /// </summary>
    Task<SalesDraftStateResult?> ReviseDraftQuantityAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        Guid draftId, Func<SalesOrderDraft, DateTimeOffset, ApprovedRevisionSnapshot?> mutate, SalesDraftStateAudit audit,
        Func<SalesOrderDraft, CancellationToken, Task> beforeWrite, CancellationToken cancellationToken = default);

    /// <summary>
    /// Partial delivery (user decision 2026-09-30): one remainder change step, one save — the state command's key rules
    /// (§3/§4/§7/§8). <paramref name="beforeWrite"/> receives the draft as read (tracked header and lines) and is the caller's
    /// last word before the mutation — its fences, the domain's own preconditions and the owners' floor read afresh; any refusal
    /// it throws leaves nothing written. <paramref name="mutate"/> is the domain's request / approval / rejection and returns the
    /// change as recorded. The store APPENDS the request row (request) or the decision row (approval, rejection), writes an
    /// approved change's line quantity columns, the header's version, the attempt record and the audit event — or nothing at
    /// all. The approval axis, the revision, the slices and the commercial columns never move (checked). The command must be one
    /// of <see cref="SalesDraftCommands.IsRemainderCommand"/>.
    /// </summary>
    Task<SalesDraftStateResult?> ChangeRemainderAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        Guid draftId, Func<SalesOrderDraft, DateTimeOffset, RemainderChangeFacts> mutate, SalesDraftStateAudit audit,
        Func<SalesOrderDraft, CancellationToken, Task> beforeWrite, CancellationToken cancellationToken = default);

    Task<SalesOrderDraft?> FindDraftAsync(Guid draftId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The company's drafts, newest first, by opaque company-bound cursor (#257: pageSize 1–100,
    /// default 50); <paramref name="approvalState"/> narrows to one internal approval state (SALES-A1).
    /// </summary>
    Task<SalesDraftPage> ListDraftsAsync(int pageSize, string? cursor, CancellationToken cancellationToken = default,
        InternalApprovalState? approvalState = null);
}

/// <summary>
/// The canonical content of a create request for the replay identity (R3, #257): the request as
/// the strict wire shape serialises it, hashed. Two attempts are "the same" only when their line
/// identities, quantities text, dates, references and every optional field agree exactly; line
/// order matters, because the client keeps it across retries.
/// </summary>
public static class SalesDraftRequestHash
{
    public static string Compute(CreateSalesOrderDraftRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Hash(JsonSerializer.Serialize(request, SalesDraftJson.Options));
    }

    /// <summary>
    /// SALES-A1: the canonical content of a state command — the command name, the draft, the expected
    /// version and (for a rejection) the reason. The same key with another draft, version, command or
    /// reason is another content (one 409, ADR-011 §3).
    /// </summary>
    public static string Compute(string commandName, Guid draftId, SalesDraftStateCommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        return Hash(JsonSerializer.Serialize(new { command = commandName, draftId, request.ExpectedVersion, request.Reason }, SalesDraftJson.Options));
    }

    /// <summary>K3 Head B: the canonical content of a commercial revision — the command, the draft, the expected version and every line's fields in the order given.</summary>
    public static string Compute(string commandName, Guid draftId, SalesDraftCommercialRevisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        return Hash(JsonSerializer.Serialize(new { command = commandName, draftId, request.ExpectedVersion, request.Lines }, SalesDraftJson.Options));
    }

    /// <summary>Price-floor approval replay identity: command, draft, expected version and every line/reason in request order.</summary>
    public static string Compute(string commandName, Guid draftId, SalesDraftPriceFloorExceptionApprovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        return Hash(JsonSerializer.Serialize(
            new { command = commandName, draftId, request.ExpectedVersion, request.Lines }, SalesDraftJson.Options));
    }

    /// <summary>DEC-SAL-002: the canonical content of a quantity revision — the command, the draft, the expected version, the reason as sent and every line's quantity text in the order given.</summary>
    public static string Compute(string commandName, Guid draftId, SalesDraftQuantityRevisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        return Hash(JsonSerializer.Serialize(
            new { command = commandName, draftId, request.ExpectedVersion, request.Reason, request.Lines }, SalesDraftJson.Options));
    }

    /// <summary>DEC-SAL-003: the canonical content of a delivery-schedule update (command, draft, version, header date and every line's dates, in order).</summary>
    public static string Compute(string commandName, Guid draftId, SalesDraftDeliveryScheduleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        return Hash(JsonSerializer.Serialize(
            new { command = commandName, draftId, request.ExpectedVersion, request.RequestedDeliveryDate, request.Lines }, SalesDraftJson.Options));
    }

    /// <summary>DEC-SAL-005: the canonical content of a cancellation (command, draft, version, reason as sent, the preview token).</summary>
    public static string Compute(string commandName, Guid draftId, SalesDraftCancelRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        return Hash(JsonSerializer.Serialize(
            new { command = commandName, draftId, request.ExpectedVersion, request.Reason, request.ImpactToken }, SalesDraftJson.Options));
    }

    /// <summary>Partial delivery: the canonical content of a remainder change request (command, draft, version, line, kind, quantity text, reason as sent).</summary>
    public static string Compute(string commandName, Guid draftId, SalesDraftRemainderChangeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        return Hash(JsonSerializer.Serialize(
            new { command = commandName, draftId, request.ExpectedVersion, request.LineId, request.Kind, request.Quantity, request.Reason },
            SalesDraftJson.Options));
    }

    /// <summary>Partial delivery: the canonical content of a remainder decision (command, draft, change number, version, reason as sent).</summary>
    public static string Compute(string commandName, Guid draftId, int changeNo, SalesDraftRemainderDecisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        return Hash(JsonSerializer.Serialize(
            new { command = commandName, draftId, changeNo, request.ExpectedVersion, request.Reason }, SalesDraftJson.Options));
    }

    private static string Hash(string canonical) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
}

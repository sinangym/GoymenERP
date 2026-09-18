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
    /// </summary>
    Task<SalesDraftReplay?> FindReplayAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ADR-011 §7: records a terminal refusal under the attempt key, so a retry with the same key,
    /// content and actor answers the same refusal instead of asking the owners again. A key already
    /// holding an attempt is <see cref="SalesDraftStoreCodes.ConcurrentChange"/> (the retry replays it).
    /// </summary>
    Task RecordRefusalAsync(SalesDraftWriteEnvelope envelope, string requestHash, SalesDraftTerminalRefusal refusal,
        CancellationToken cancellationToken = default);

    /// <summary>The company's stored draft, or null (another company's draft is null too, never 403).</summary>
    Task<SalesOrderDraft?> FindDraftAsync(Guid draftId, CancellationToken cancellationToken = default);

    /// <summary>The company's drafts, newest first, by opaque company-bound cursor (#257: pageSize 1–100, default 50).</summary>
    Task<SalesDraftPage> ListDraftsAsync(int pageSize, string? cursor, CancellationToken cancellationToken = default);
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
        var canonical = JsonSerializer.Serialize(request, SalesDraftJson.Options);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

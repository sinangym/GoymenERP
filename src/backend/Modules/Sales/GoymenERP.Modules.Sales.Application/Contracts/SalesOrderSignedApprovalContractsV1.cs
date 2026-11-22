using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Sales.Domain.SignedApprovals;

namespace GoymenERP.Modules.Sales.Application.Contracts;

// WP13 (YOL-D11, DALGA_PLANI_v2 1.14): the customer's wet-signed approval of a sales order. The file is uploaded to the order in the
// Document Vault (POST /api/v1/documents, ownerKind "sales.order": the vault's size, type, signature and scan rules, versioned, never
// deleted); this contract records WHICH vault document version is the order's signed approval ("imzalı onay alındı") under
// /api/v1/sales/drafts/{id}/signed-approvals, and withdraws a wrong record with a reason. Nothing is deleted.

public static class SalesOrderSignedApprovalApiV1
{
    public const string Segment = "signed-approvals";

    public const string DeactivationSegment = "deactivation";

    /// <summary>A technical bound of the history answered with one order (payload size), not a business policy.</summary>
    public const int MaximumListed = 50;
}

public static class SalesOrderSignedApprovalCommands
{
    public const string RecordV1 = "sales.order-signed-approval.record.v1";

    public const string DeactivateV1 = "sales.order-signed-approval.deactivate.v1";

    public static bool IsCommand(string? commandName) => commandName is RecordV1 or DeactivateV1;
}

/// <summary>The problem codes this contract adds; the rest are the draft routes' (<see cref="SalesDraftProblemCodesV1"/>).</summary>
public static class SalesOrderSignedApprovalProblemCodesV1
{
    /// <summary>422: the document is not a clean PDF/PNG/JPEG uploaded to this order (unknown, another record's, unscanned).</summary>
    public const string DocumentInvalid = "sales_signed_approval_document_invalid";

    /// <summary>409 (recorded): this exact document version is already the order's active signed approval.</summary>
    public const string AlreadyCurrent = "sales_signed_approval_already_current";

    /// <summary>409 (recorded): the order is not an open order whose current revision is internally approved.</summary>
    public const string OrderNotApproved = "sales_signed_approval_order_not_approved";

    /// <summary>503: the Document Vault could not answer for the document (nothing is guessed).</summary>
    public const string DocumentUnavailable = "sales_signed_approval_document_unavailable";
}

/// <summary>
/// Records a vault document as the order's signed approval. <see cref="ExpectedOrderVersion"/> is the order version the person saw
/// (a changed order is a 409 version conflict: the signature may not cover the revision on screen).
/// </summary>
public sealed record RecordSalesOrderSignedApprovalRequest(Guid DocumentId, string ExpectedOrderVersion);

/// <summary>Withdraws the active signed approval record (its expected version, a reason ≤ 4000); the record and the file stay.</summary>
public sealed record DeactivateSalesOrderSignedApprovalRequest(string ExpectedVersion, string Reason);

/// <summary>One signed approval record as stored: the order revision it covers, the pinned vault document version and its state.</summary>
public sealed record SalesOrderSignedApprovalViewV1(
    Guid Id, Guid OrderId, string OrderRevision, Guid DocumentId, int DocumentVersionNo, string DocumentSha256, string State,
    string Version, string RecordedBy, DateTimeOffset RecordedAtUtc, string? DeactivationKind, string? DeactivatedBy,
    DateTimeOffset? DeactivatedAtUtc, string? DeactivationReason, Guid? SupersededById);

/// <summary>
/// The order's signed approval status. <see cref="SignedApprovalReceived"/> is true only while an active record covers the order's
/// CURRENT revision; after a commercial revision the old record stays active but no longer covers the order (false, "yeni revizyon
/// için imzalı onay bekleniyor"). <see cref="Items"/> is the history, newest first.
/// </summary>
public sealed record SalesOrderSignedApprovalsResponseV1(
    Guid OrderId, string OrderVersion, string OrderRevision, bool SignedApprovalReceived, SalesOrderSignedApprovalViewV1? Active,
    IReadOnlyList<SalesOrderSignedApprovalViewV1> Items);

public sealed record SalesOrderSignedApprovalCommandResultV1(
    SalesOrderSignedApprovalsResponseV1 Detail, bool IsReplay, string OriginalCorrelationId);

public interface ISalesOrderSignedApprovalServiceV1
{
    Task<SalesOrderSignedApprovalCommandResultV1> RecordAsync(Guid orderId, RecordSalesOrderSignedApprovalRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<SalesOrderSignedApprovalCommandResultV1> DeactivateAsync(Guid orderId, Guid approvalId,
        DeactivateSalesOrderSignedApprovalRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<SalesOrderSignedApprovalsResponseV1> GetAsync(Guid orderId, CancellationToken cancellationToken = default);
}

/// <summary>The order facts a signed approval needs (Sales' own row, company-filtered).</summary>
public sealed record SalesOrderSignedApprovalOrderFactsV1(Guid OrderId, long Version, int Revision, bool IsOpen, bool IsApproved);

/// <summary>What a signed approval key already holds: the order whose state it answers, or the recorded terminal refusal.</summary>
public sealed record SalesOrderSignedApprovalReplay(Guid? OrderId, SalesDraftTerminalRefusal? Refusal, string OriginalCorrelationId);

public sealed record SalesOrderSignedApprovalStoreResult(Guid OrderId, SalesDraftWriteOutcome Outcome, string OriginalCorrelationId);

/// <summary>
/// The signed approval half of the Sales store (ADR-011 §2–§8, its own attempt table). One save writes the new record, the
/// supersession of the previous active one (if any), the attempt and the audit events. UX_OrderSignedApprovals_Active refuses a
/// second active record of an order as the save's concurrent change. Sales writes only its own tables.
/// </summary>
public interface ISalesOrderSignedApprovalStore
{
    Task<SalesOrderSignedApprovalReplay?> FindSignedApprovalReplayAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        string commandName, CancellationToken cancellationToken = default);

    Task<SalesOrderSignedApprovalOrderFactsV1?> FindSignedApprovalOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records <paramref name="approval"/> after re-reading the order: its version must still be <paramref name="expectedOrderVersion"/>
    /// and its revision the approval's (else <see cref="Domain.SalesVersionConflictException"/>); the active record is superseded.
    /// </summary>
    Task<SalesOrderSignedApprovalStoreResult> StoreSignedApprovalAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        long expectedOrderVersion, SalesOrderSignedApproval approval, SalesDraftStateAudit audit, SalesDraftStateAudit supersededAudit,
        CancellationToken cancellationToken = default);

    /// <summary>Withdraws the order's record <paramref name="approvalId"/>; null when no such record of the order exists.</summary>
    Task<SalesOrderSignedApprovalStoreResult?> DeactivateSignedApprovalAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        Guid orderId, Guid approvalId, Action<SalesOrderSignedApproval, DateTimeOffset> mutate, SalesDraftStateAudit audit,
        CancellationToken cancellationToken = default);

    Task RecordSignedApprovalRefusalAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName, Guid orderId,
        SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken = default);

    /// <summary>The order's records, newest first, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<SalesOrderSignedApproval>> ListSignedApprovalsAsync(Guid orderId, int limit,
        CancellationToken cancellationToken = default);
}

public enum SalesSignedApprovalDocumentOutcomeV1
{
    Accepted = 1,
    NotFound = 2,
    OwnerMismatch = 3,
    KindNotAllowed = 4,
    NotDownloadable = 5,
    Denied = 6,
    Unavailable = 7,
}

/// <summary>The Documents owner's answer: on <see cref="SalesSignedApprovalDocumentOutcomeV1.Accepted"/> the current version and hash.</summary>
public sealed record SalesSignedApprovalDocumentCheckV1(SalesSignedApprovalDocumentOutcomeV1 Outcome, int? VersionNo, string? Sha256);

/// <summary>
/// WP13 host port over the Document Vault (Sales never reads document tables). The host answers with the actor's own Documents read
/// authority whether <paramref name="documentId"/> is a clean, downloadable PDF, PNG or JPEG uploaded to THIS sales order
/// (owner kind <c>sales.order</c>, owner id <paramref name="orderId"/>) in <paramref name="companyId"/>, and which version/hash it is
/// now. Any doubt answers a non-Accepted outcome (fail-closed); no registration answers Unavailable.
/// </summary>
public interface ISalesOrderSignedApprovalDocumentPortV1
{
    Task<SalesSignedApprovalDocumentCheckV1> CheckAsync(Guid companyId, Guid orderId, Guid documentId,
        CancellationToken cancellationToken = default);
}

public static class SalesOrderSignedApprovalHash
{
    public static string Compute(Guid orderId, RecordSalesOrderSignedApprovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Hash(JsonSerializer.Serialize(new
        {
            command = SalesOrderSignedApprovalCommands.RecordV1,
            orderId,
            request.DocumentId,
            request.ExpectedOrderVersion,
        }, SalesDraftJson.Options));
    }

    public static string Compute(Guid orderId, Guid approvalId, DeactivateSalesOrderSignedApprovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Hash(JsonSerializer.Serialize(new
        {
            command = SalesOrderSignedApprovalCommands.DeactivateV1,
            orderId,
            approvalId,
            request.ExpectedVersion,
            request.Reason,
        }, SalesDraftJson.Options));
    }

    private static string Hash(string canonical) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
}

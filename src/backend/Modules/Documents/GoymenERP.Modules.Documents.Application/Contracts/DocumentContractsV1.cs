namespace GoymenERP.Modules.Documents.Application.Contracts;

/// <summary>The document vault's public routes (v1).</summary>
public static class DocumentsApiV1
{
    public const string DocumentsPath = "/api/v1/documents";
    public const string IdempotencyHeader = "Idempotency-Key";
    public const int CreatedStatus = 201;

    /// <summary>The multipart field names of an upload; any other field is refused.</summary>
    public const string OwnerKindField = "ownerKind";
    public const string OwnerIdField = "ownerId";
    public const string FileField = "file";
    public const string ExpectedCurrentVersionField = "expectedCurrentVersion";
}

/// <summary>The command names under which an attempt (ADR-011) is recorded.</summary>
public static class DocumentCommands
{
    public const string UploadV1 = "documents.upload.v1";
    public const string AppendVersionV1 = "documents.append-version.v1";

    public static bool IsKnown(string commandName) => commandName is UploadV1 or AppendVersionV1;
}

/// <summary>The problem codes of the vault's routes. Platform 401/403/404 and every 503 stay code-less.</summary>
public static class DocumentProblemCodesV1
{
    public const string ValidationFailed = "validation_failed";
    public const string CommandMetadataInvalid = "command_metadata_invalid";
    public const string SameKeyPayloadMismatch = "same_key_payload_mismatch";
    public const string ConcurrentChange = "concurrent_change";

    /// <summary>The scanner found malware; nothing was stored.</summary>
    public const string ContentRejected = "document_content_rejected";

    /// <summary>The version was not scanned (no scanner configured or it could not answer): it is not served.</summary>
    public const string NotScanned = "document_not_scanned";
}

/// <summary>What the upload route read from the multipart body; validated by the service.</summary>
public sealed record UploadDocumentRequest(string? OwnerKind, Guid OwnerId, string? FileName, string? DeclaredContentType, byte[] Content);

/// <summary>A new immutable version of an existing logical document.</summary>
public sealed record AppendDocumentVersionRequest(int ExpectedCurrentVersion, string? FileName, string? DeclaredContentType, byte[] Content);

/// <summary>The command metadata the route read from the headers (the Logistics shape).</summary>
public sealed record DocumentCommandMetadata(string IdempotencyKey, string CorrelationId, string? CausationId);

/// <summary>
/// One document as a list row and an upload answer: its owner link, its (latest) version's safe file name, canonical
/// content type, size, server-computed SHA-256, scan state and whether it may be downloaded now. Identities, names and
/// facts; the object key and the storage location are never on the wire.
/// </summary>
public sealed record DocumentView(
    Guid Id,
    Guid CompanyId,
    string OwnerKind,
    Guid OwnerId,
    int VersionNo,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    string ScanState,
    bool Downloadable,
    string UploadedBy,
    DateTimeOffset UploadedAtUtc);

/// <summary>
/// AT-057: an earlier document of the SAME company with the same content hash, shown as a warning (DEC-DOC-002: the
/// user sees the existing records; authority and business context are judged separately). Only documents whose owner
/// record the actor may read are listed — a duplicate elsewhere is not disclosed.
/// </summary>
public sealed record DocumentDuplicateView(Guid DocumentId, string FileName, string OwnerKind, Guid OwnerId, DateTimeOffset UploadedAtUtc);

/// <summary>An upload's answer: the stored document, the readable duplicates, and whether this is a replayed attempt.</summary>
public sealed record DocumentUploadResponse(DocumentView Document, IReadOnlyList<DocumentDuplicateView> Duplicates, bool IsReplay,
    string OriginalCorrelationId);

/// <summary>An append answer; a replay returns the exact version created by the first attempt.</summary>
public sealed record DocumentVersionWriteResponse(DocumentView Document, bool IsReplay, string OriginalCorrelationId);

/// <summary>A deterministic immutable-history page, newest version first.</summary>
public sealed record DocumentVersionHistoryResponse(Guid DocumentId, IReadOnlyList<DocumentView> Versions, string? NextCursor);

/// <summary>The upload bounds the client may pre-check against; the server re-checks every upload regardless.</summary>
public sealed record DocumentUploadLimitsView(long MaximumBytes, IReadOnlyList<string> Extensions, IReadOnlyList<string> ContentTypes);

/// <summary>An owner record's documents, newest first, and whether the actor may upload to it now.</summary>
public sealed record DocumentListResponse(IReadOnlyList<DocumentView> Items, bool CanUpload, DocumentUploadLimitsView Limits);

/// <summary>A downloadable version's verified content (its hash re-checked against the stored one).</summary>
public sealed record DocumentContentV1(Guid DocumentId, string FileName, string ContentType, byte[] Content, string Sha256);

public sealed record DocumentFieldError(string Field, string Code);

/// <summary>A refusal with its HTTP status, code and field errors; the route maps it (the Logistics shape).</summary>
public sealed class DocumentServiceException : Exception
{
    public DocumentServiceException(int status, string code, IReadOnlyList<DocumentFieldError>? errors = null)
        : base(code)
    {
        Status = status;
        Code = code;
        Errors = errors ?? [];
    }

    public int Status { get; }

    public string Code { get; }

    public IReadOnlyList<DocumentFieldError> Errors { get; }
}

/// <summary>The vault's use cases (ERP-000G core; AT-065 first owner: the Cari party).</summary>
public interface IDocumentService
{
    Task<DocumentUploadResponse> UploadAsync(UploadDocumentRequest request, DocumentCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<DocumentListResponse> ListAsync(string? ownerKind, Guid ownerId, CancellationToken cancellationToken = default);

    Task<DocumentContentV1> GetContentAsync(Guid documentId, CancellationToken cancellationToken = default);

    Task<DocumentVersionWriteResponse> AppendVersionAsync(Guid documentId, AppendDocumentVersionRequest request,
        DocumentCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<DocumentVersionHistoryResponse> ListVersionsAsync(Guid documentId, int? pageSize, string? cursor,
        CancellationToken cancellationToken = default);

    Task<DocumentContentV1> GetVersionContentAsync(Guid documentId, int versionNo, CancellationToken cancellationToken = default);
}

/// <summary>
/// The host's bindings (the Logistics bindings shape): the upload bound in bytes (OPEN-015 — capacity is not decided,
/// so the bound is configuration with a conservative default, never a business rule).
/// </summary>
public sealed record DocumentsServiceBindings(long MaximumUploadBytes)
{
    /// <summary>ASSUMPTION-DOC-1: 20 MiB until OPEN-015 decides document volume and size.</summary>
    public const long DefaultMaximumUploadBytes = 20L * 1024 * 1024;

    public static DocumentsServiceBindings Default { get; } = new(DefaultMaximumUploadBytes);
}

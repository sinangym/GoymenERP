using GoymenERP.Modules.Documents.Domain;

namespace GoymenERP.Modules.Documents.Application.Contracts;

/// <summary>
/// The vault's metadata store (Infrastructure implements it over the <c>documents</c> schema). Every read is of the
/// context company only; every write is append-only and happens in ONE save with its attempt and its audit event.
/// </summary>
public interface IDocumentStore
{
    /// <summary>The recorded attempt of (context company, command, key), answered for this actor and request, or null.</summary>
    Task<DocumentStoreResult?> FindReplayAsync(DocumentWriteEnvelope envelope, string requestHash, string commandName,
        CancellationToken cancellationToken = default);

    Task<DocumentStoreResult?> FindVersionReplayAsync(DocumentWriteEnvelope envelope, string requestHash, Guid documentId,
        int resultVersionNo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a new logical document (version 1, the owner link and — when the company does not hold this content yet —
    /// the blob row), the recorded attempt and the audit event in one save. <paramref name="beforeWrite"/> runs after
    /// every store read and immediately before the sole save; a lost same-key race runs it before returning the winner.
    /// F-026 (PLT-S9): an upload carrying <see cref="NewDocumentUpload.Template"/> is a GENERATED PDF — the same single
    /// save, its attempt recorded under <see cref="DocumentCommands.StoreGeneratedV1"/> and its audit event carrying the
    /// template snapshot (DEC-DOC-010); a replay answers version 1 of the first attempt either way.
    /// </summary>
    Task<DocumentStoreResult> StoreUploadAsync(DocumentWriteEnvelope envelope, string requestHash, NewDocumentUpload upload,
        Func<CancellationToken, Task> beforeWrite, CancellationToken cancellationToken = default);

    Task<DocumentStoreResult> StoreVersionAsync(DocumentWriteEnvelope envelope, string requestHash, int expectedCurrentVersion,
        NewDocumentVersion version, Func<CancellationToken, Task> beforeWrite, CancellationToken cancellationToken = default);

    /// <summary>
    /// B-26: the recorded link attempt of (context company, <see cref="DocumentCommands.LinkV1"/>, key), answered for
    /// this actor, request and document with the document as it is now, or null.
    /// </summary>
    Task<DocumentStoreResult?> FindLinkReplayAsync(DocumentWriteEnvelope envelope, string requestHash, Guid documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// B-26 / DEC-DOC-002: links one EXISTING document to one more owner record (no new blob, no new version) with the
    /// recorded attempt and the audit event in one save. The document's current version must still be
    /// <see cref="NewDocumentLink.ExpectedCurrentVersion"/> (else <see cref="DocumentStoreCodes.ConcurrentChange"/>); an
    /// existing link to the same owner is <see cref="DocumentStoreCodes.AlreadyLinked"/>.
    /// </summary>
    Task<DocumentStoreResult> StoreLinkAsync(DocumentWriteEnvelope envelope, string requestHash, NewDocumentLink link,
        Func<CancellationToken, Task> beforeWrite, CancellationToken cancellationToken = default);

    Task<StoredDocument?> FindDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);

    Task<StoredDocument?> FindDocumentVersionAsync(Guid documentId, int versionNo, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredDocument>> ListVersionsAsync(Guid documentId, int limit, int? beforeVersionNo,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredDocument>> ListByOwnerAsync(string ownerKind, Guid ownerId, int limit, CancellationToken cancellationToken = default);

    /// <summary>AT-057: other documents of the context company whose latest version has this content hash, newest first.</summary>
    Task<IReadOnlyList<StoredDocument>> FindByContentAsync(string sha256, Guid excludingDocumentId, int limit,
        CancellationToken cancellationToken = default);
}

/// <summary>Who wrote, under which name and roles, with which correlation and key, from which channel (ADR-009).</summary>
public sealed record DocumentWriteEnvelope(
    string ActorId,
    string PermissionCode,
    IReadOnlyCollection<string> RoleCodes,
    string CorrelationId,
    string? CausationId,
    string IdempotencyKey,
    string SourceChannel,
    string? ClientIpAddress,
    string? UserAgent);

/// <summary>
/// A new upload, already classified, hashed, scanned and written to the object store. F-026 (PLT-S9):
/// <see cref="Template"/> is set only for a PDF a producing module generated (DEC-DOC-010 snapshot); null is a user upload.
/// KLN-D59: <see cref="ProducerCommand"/> names the producing command whose own permission gated the write; it is set
/// exactly when <see cref="Template"/> is. KLN-D60: <see cref="DocumentScanState.SystemGenerated"/> is accepted only
/// together with a template (the server-internal producer path).
/// </summary>
public sealed record NewDocumentUpload(
    Guid DocumentId,
    DocumentOwnerLink Owner,
    DocumentFileName FileName,
    DocumentFileKindInfo Kind,
    long SizeBytes,
    string Sha256,
    string ObjectKey,
    DocumentScanState ScanState,
    string? ScannerId,
    DateTimeOffset UploadedAtUtc,
    GeneratedDocumentTemplate? Template = null,
    string? ProducerCommand = null);

/// <summary>A classified, scanned immutable version to append to one existing logical document.</summary>
public sealed record NewDocumentVersion(
    Guid DocumentId,
    DocumentFileName FileName,
    DocumentFileKindInfo Kind,
    long SizeBytes,
    string Sha256,
    string ObjectKey,
    DocumentScanState ScanState,
    string? ScannerId,
    DateTimeOffset UploadedAtUtc);

/// <summary>B-26: one more owner link for an existing document, measured against its current version.</summary>
public sealed record NewDocumentLink(
    Guid DocumentId,
    int ExpectedCurrentVersion,
    DocumentOwnerLink Owner,
    DateTimeOffset LinkedAtUtc);

/// <summary>One owner link of a stored document.</summary>
public sealed record StoredDocumentLink(string OwnerKind, Guid OwnerId);

/// <summary>
/// A stored document with its latest version and its PRIMARY owner link (the earliest: the upload's). B-26:
/// <see cref="Links"/> carries every owner link of the document, primary first; null means the primary link only.
/// </summary>
public sealed record StoredDocument(
    Guid Id,
    Guid CompanyId,
    string OwnerKind,
    Guid OwnerId,
    int VersionNo,
    string FileName,
    DocumentFileKind Kind,
    long SizeBytes,
    string Sha256,
    string ObjectKey,
    DocumentScanState ScanState,
    string? ScannerId,
    string UploadedBy,
    DateTimeOffset UploadedAtUtc,
    IReadOnlyList<StoredDocumentLink>? Links = null)
{
    /// <summary>Every owner link, primary first; at least the primary one.</summary>
    public IReadOnlyList<StoredDocumentLink> OwnerLinks =>
        Links is { Count: > 0 } links ? links : [new StoredDocumentLink(OwnerKind, OwnerId)];
}

/// <summary>The stored document and whether it is a replay of an earlier attempt (then with that attempt's correlation).</summary>
public sealed record DocumentStoreResult(StoredDocument Document, bool IsReplay, string OriginalCorrelationId);

public static class DocumentStoreCodes
{
    public const string SameKeyPayloadMismatch = "same_key_payload_mismatch";
    public const string ConcurrentChange = "concurrent_change";
    public const string CompanyContextMismatch = "company_context_mismatch";
    public const string AlreadyLinked = "document_already_linked";
}

public sealed class DocumentStoreException(string code, string message, Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}

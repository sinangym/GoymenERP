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

    /// <summary>
    /// Stores a new logical document (version 1, the owner link and — when the company does not hold this content yet —
    /// the blob row), the recorded attempt and the audit event in one save. <paramref name="beforeWrite"/> runs after
    /// every store read and immediately before the sole save; a lost same-key race runs it before returning the winner.
    /// </summary>
    Task<DocumentStoreResult> StoreUploadAsync(DocumentWriteEnvelope envelope, string requestHash, NewDocumentUpload upload,
        Func<CancellationToken, Task> beforeWrite, CancellationToken cancellationToken = default);

    Task<StoredDocument?> FindDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);

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

/// <summary>A new upload, already classified, hashed, scanned and written to the object store.</summary>
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
    DateTimeOffset UploadedAtUtc);

/// <summary>A stored document with its latest version and its owner link.</summary>
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
    DateTimeOffset UploadedAtUtc);

/// <summary>The stored document and whether it is a replay of an earlier attempt (then with that attempt's correlation).</summary>
public sealed record DocumentStoreResult(StoredDocument Document, bool IsReplay, string OriginalCorrelationId);

public static class DocumentStoreCodes
{
    public const string SameKeyPayloadMismatch = "same_key_payload_mismatch";
    public const string ConcurrentChange = "concurrent_change";
    public const string CompanyContextMismatch = "company_context_mismatch";
}

public sealed class DocumentStoreException(string code, string message, Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}

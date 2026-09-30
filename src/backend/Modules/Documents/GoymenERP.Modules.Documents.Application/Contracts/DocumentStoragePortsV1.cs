using GoymenERP.Modules.Documents.Domain;

namespace GoymenERP.Modules.Documents.Application.Contracts;

/// <summary>
/// The object-storage abstraction (AGENTS.md "Files: S3-compatible object storage abstraction"; ADR-005 object store +
/// DB metadata/hash). Objects are immutable and content-addressed by <see cref="DocumentObjectKeys"/>: writing a key
/// that already holds the same bytes is a no-op, writing different bytes under an existing key is refused, and nothing
/// is ever deleted or overwritten (legal retention 10 years, no deletion; archive/legal hold later, OPEN-018/021).
/// Production (user decision 2026-09-30): the company's own server directory (local disk or NAS, inside the existing
/// backups; no cloud); development/test: a local file-system stand-in. An S3-compatible adapter could implement the same
/// port later. A store that cannot answer throws <see cref="DocumentObjectStoreUnavailableException"/>.
/// </summary>
public interface IDocumentObjectStoreV1
{
    /// <summary>Stores the bytes under the key unless the key already holds exactly these bytes.</summary>
    Task PutAsync(string key, ReadOnlyMemory<byte> content, string sha256, CancellationToken cancellationToken = default);

    /// <summary>The bytes under the key, or null when the key holds nothing.</summary>
    Task<byte[]?> ReadAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>The object store is not configured, unreachable, or holds something else under a content key.</summary>
public sealed class DocumentObjectStoreUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
/// The object key of a company's content: <c>companies/{companyId:N}/sha256/{hash}</c>. Physical dedup is WITHIN a
/// company only (DEC-DOC-002; cross-company dedup, a shared key scope and its existence oracle stay closed): the same
/// bytes uploaded in two companies are two objects. Built only from a Guid and 64 hex characters, so a user-supplied
/// name can never become a path.
/// </summary>
public static class DocumentObjectKeys
{
    public static string For(Guid companyId, string sha256)
    {
        if (companyId == Guid.Empty || !DocumentContentHash.IsWellFormed(sha256))
        {
            throw new ArgumentException("Nesne anahtarı şirket kimliği ve 64 karakterlik içerik özetinden oluşur.");
        }

        return $"companies/{companyId:N}/sha256/{sha256}";
    }
}

/// <summary>
/// DEC-DOC-009's malware scan. The verdict is the scanner's; the vault never turns "no answer" into "clean". The
/// production scanner is Windows Defender on the server (user decision 2026-09-30), registered only when configured;
/// without a scanner, or when it cannot answer, every upload is stored as <see cref="DocumentScanState.NotScanned"/>
/// and is not downloadable.
/// </summary>
public interface IDocumentContentScannerV1
{
    /// <summary>A stable identity of the scanner and its signature set, recorded on every version it cleared.</summary>
    string ScannerId { get; }

    Task<DocumentScanVerdictV1> ScanAsync(ReadOnlyMemory<byte> content, DocumentFileKind kind, CancellationToken cancellationToken = default);
}

public enum DocumentScanVerdictV1
{
    Clean = 1,
    Infected = 2,
    Unavailable = 3,
}

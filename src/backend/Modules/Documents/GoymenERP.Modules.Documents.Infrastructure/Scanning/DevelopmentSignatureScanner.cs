using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Domain;

namespace GoymenERP.Modules.Documents.Infrastructure.Scanning;

/// <summary>
/// A DEVELOPMENT/TEST stand-in for DEC-DOC-009's malware scanner, never a production scanner: it knows exactly one
/// signature, the industry-standard EICAR test string, so that the refusal path is exercisable end to end without a
/// scanner product. Everything else it calls clean, and it says so in its identity — every version it clears carries
/// <see cref="Id"/>, so no such version can pass for a production scan. The module registers it only where the host
/// allows development storage (Development/Testing); the scanner product is undecided (NEEDS-SINAN), and without one
/// production stores every upload as NotScanned (not downloadable).
/// </summary>
public sealed class DevelopmentSignatureScanner : IDocumentContentScannerV1
{
    public const string Id = "development-eicar-signature-only-v1";

    private static readonly byte[] Eicar = "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"u8.ToArray();

    public string ScannerId => Id;

    public Task<DocumentScanVerdictV1> ScanAsync(ReadOnlyMemory<byte> content, DocumentFileKind kind, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(content.Span.IndexOf(Eicar) >= 0 ? DocumentScanVerdictV1.Infected : DocumentScanVerdictV1.Clean);
    }
}

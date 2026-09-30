namespace GoymenERP.Modules.Documents.Domain;

/// <summary>
/// DEC-DOC-009's allowlist and MIME validation, as one pure decision over what the client sent: the (already safe)
/// file name, the media type the client declared, and the bytes. The KIND is decided by the extension being on the
/// allowlist AND the bytes carrying that kind's signature; the client's declared type may only agree with it (or be
/// the browser's "unknown" answer) and is never stored — the stored and served type is the kind's canonical one. A
/// file that says PDF but is not, an image renamed to .pdf, an allowed name over a disallowed declared type, an empty
/// file or one over the configured bound is refused with a stable code before anything is hashed, scanned or stored.
/// </summary>
public static class DocumentContentPolicy
{
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];

    /// <summary>What a browser sends when it does not know the type; it neither confirms nor contradicts the kind.</summary>
    private static readonly HashSet<string> UnknownDeclaredTypes = new(["", "application/octet-stream"], StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<DocumentFileKindInfo> AllowedKinds { get; } =
    [
        new DocumentFileKindInfo(DocumentFileKind.Pdf, "application/pdf", [".pdf"]),
        new DocumentFileKindInfo(DocumentFileKind.Png, "image/png", [".png"]),
        new DocumentFileKindInfo(DocumentFileKind.Jpeg, "image/jpeg", [".jpg", ".jpeg"]),
    ];

    public static DocumentFileKindInfo Info(DocumentFileKind kind) =>
        AllowedKinds.SingleOrDefault(info => info.Kind == kind) ?? throw new ArgumentOutOfRangeException(nameof(kind));

    /// <summary>Classifies an upload or refuses it with one of <see cref="DocumentRuleCodes"/>.</summary>
    public static DocumentContentDecision Classify(DocumentFileName fileName, string? declaredContentType, ReadOnlySpan<byte> content,
        long maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 1);
        if (content.IsEmpty)
        {
            return DocumentContentDecision.Refused(DocumentRuleCodes.Empty);
        }

        if (content.Length > maximumBytes)
        {
            return DocumentContentDecision.Refused(DocumentRuleCodes.TooLarge);
        }

        var extension = fileName.Extension;
        var info = AllowedKinds.SingleOrDefault(candidate => candidate.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
        if (info is null)
        {
            return DocumentContentDecision.Refused(DocumentRuleCodes.TypeNotAllowed);
        }

        var declared = MediaTypeOnly(declaredContentType);
        if (!UnknownDeclaredTypes.Contains(declared) && !IsDeclaredAs(info.Kind, declared))
        {
            return DocumentContentDecision.Refused(DocumentRuleCodes.ContentMismatch);
        }

        if (!content.StartsWith(Signature(info.Kind)))
        {
            return DocumentContentDecision.Refused(DocumentRuleCodes.ContentMismatch);
        }

        return DocumentContentDecision.Accepted(info);
    }

    private static bool IsDeclaredAs(DocumentFileKind kind, string declared) => kind switch
    {
        DocumentFileKind.Pdf => string.Equals(declared, "application/pdf", StringComparison.OrdinalIgnoreCase),
        DocumentFileKind.Png => string.Equals(declared, "image/png", StringComparison.OrdinalIgnoreCase),
        // Older browsers name a progressive JPEG image/pjpeg; it is the same signature.
        DocumentFileKind.Jpeg => string.Equals(declared, "image/jpeg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(declared, "image/pjpeg", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    private static ReadOnlySpan<byte> Signature(DocumentFileKind kind) => kind switch
    {
        DocumentFileKind.Pdf => PdfSignature,
        DocumentFileKind.Png => PngSignature,
        DocumentFileKind.Jpeg => JpegSignature,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>The media type without parameters ("application/pdf; name=x" → "application/pdf"), trimmed.</summary>
    private static string MediaTypeOnly(string? declared)
    {
        if (string.IsNullOrWhiteSpace(declared))
        {
            return string.Empty;
        }

        var semicolon = declared.IndexOf(';', StringComparison.Ordinal);
        return (semicolon < 0 ? declared : declared[..semicolon]).Trim();
    }
}

/// <summary>An accepted kind, or the refusal code; never both.</summary>
public sealed record DocumentContentDecision(DocumentFileKindInfo? Kind, string? RefusalCode)
{
    public bool IsAccepted => Kind is not null;

    public static DocumentContentDecision Accepted(DocumentFileKindInfo kind) => new(kind, null);

    public static DocumentContentDecision Refused(string code) => new(null, code);
}

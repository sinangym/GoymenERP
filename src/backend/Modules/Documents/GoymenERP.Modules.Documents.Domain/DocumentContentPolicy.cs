namespace GoymenERP.Modules.Documents.Domain;

/// <summary>
/// DEC-DOC-009's allowlist and MIME validation, as one pure decision over what the client sent: the (already safe)
/// file name, the media type the client declared, and the bytes. The KIND is decided by the extension being on the
/// allowlist AND the bytes carrying that kind's signature (or, for the structured bank-statement kinds, passing
/// <see cref="DocumentContentInspection"/>); the client's declared type may only agree with it (or be the browser's
/// "unknown" answer) and is never stored — the stored and served type is the kind's canonical one. A file that says PDF
/// but is not, an image renamed to .pdf, an allowed name over a disallowed declared type, an empty file or one over the
/// configured bound is refused with a stable code before anything is hashed, scanned or stored. Which kinds an upload
/// may have is the owner record's: <see cref="StandardKinds"/> unless its owner port opts in to more (FNS-D52).
/// </summary>
public static class DocumentContentPolicy
{
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];

    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string PlainTextContentType = "text/plain";
    private const string CsvContentType = "text/csv";

    /// <summary>What a browser sends when it does not know the type; it neither confirms nor contradicts the kind.</summary>
    private static readonly HashSet<string> UnknownDeclaredTypes = new(["", "application/octet-stream"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The types browsers and operating systems declare for a .csv file: the registered text/csv, its older aliases,
    /// plain text, and the spreadsheet type Windows associates with .csv when a spreadsheet program is installed. The
    /// declaration only has to not contradict the kind; the bytes still decide.
    /// </summary>
    private static readonly HashSet<string> CsvDeclaredTypes = new(
        [CsvContentType, "application/csv", "text/x-csv", "text/comma-separated-values", PlainTextContentType, "application/vnd.ms-excel"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>Every kind the vault knows, in a stable order, with its canonical media type and extensions.</summary>
    public static IReadOnlyList<DocumentFileKindInfo> KnownKinds { get; } =
    [
        new DocumentFileKindInfo(DocumentFileKind.Pdf, "application/pdf", [".pdf"]),
        new DocumentFileKindInfo(DocumentFileKind.Png, "image/png", [".png"]),
        new DocumentFileKindInfo(DocumentFileKind.Jpeg, "image/jpeg", [".jpg", ".jpeg"]),
        new DocumentFileKindInfo(DocumentFileKind.Xlsx, XlsxContentType, [".xlsx"]),
        // Banks name MT940 exports .sta, .mt940, .940 or plain .txt; the content check decides, not the name.
        new DocumentFileKindInfo(DocumentFileKind.Mt940, PlainTextContentType, [".sta", ".mt940", ".940", ".txt"]),
        new DocumentFileKindInfo(DocumentFileKind.Csv, CsvContentType, [".csv"]),
        new DocumentFileKindInfo(DocumentFileKind.UblInvoiceXml, "application/xml", [".xml"]),
    ];

    /// <summary>The kinds every owner record accepts (DEC-DOC-009 as it stood before FNS-D52); the default of an owner port.</summary>
    public static IReadOnlyList<DocumentFileKind> StandardKinds { get; } = [DocumentFileKind.Pdf, DocumentFileKind.Png, DocumentFileKind.Jpeg];

    /// <summary>
    /// FNS-D52: the kinds a bank-statement import accepts. The owner record that holds a statement (the Finance record,
    /// FIN-S21) opts in to them through its owner port; no existing owner accepts them.
    /// </summary>
    public static IReadOnlyList<DocumentFileKind> BankStatementImportKinds { get; } =
        [DocumentFileKind.Xlsx, DocumentFileKind.Mt940, DocumentFileKind.Csv];

    /// <summary>ALS-D7: source imports only. Existing owners and their type lists are unchanged.</summary>
    public static IReadOnlyList<DocumentFileKind> PurchaseInvoiceImportKinds { get; } =
        [DocumentFileKind.UblInvoiceXml, DocumentFileKind.Xlsx, DocumentFileKind.Csv];

    public static DocumentFileKindInfo Info(DocumentFileKind kind) =>
        KnownKinds.SingleOrDefault(info => info.Kind == kind) ?? throw new ArgumentOutOfRangeException(nameof(kind));

    /// <summary>The known kinds among <paramref name="acceptedKinds"/>, in the stable <see cref="KnownKinds"/> order.</summary>
    public static IReadOnlyList<DocumentFileKindInfo> InfosOf(IReadOnlyCollection<DocumentFileKind> acceptedKinds)
    {
        ArgumentNullException.ThrowIfNull(acceptedKinds);
        return [.. KnownKinds.Where(info => acceptedKinds.Contains(info.Kind))];
    }

    /// <summary>Classifies an upload to a record that accepts the <see cref="StandardKinds"/>.</summary>
    public static DocumentContentDecision Classify(DocumentFileName fileName, string? declaredContentType, ReadOnlySpan<byte> content,
        long maximumBytes) => Classify(fileName, declaredContentType, content, maximumBytes, StandardKinds);

    /// <summary>
    /// Classifies an upload or refuses it with one of <see cref="DocumentRuleCodes"/>. A known kind the owner record does
    /// not accept is <see cref="DocumentRuleCodes.TypeNotAllowed"/>, exactly like a kind the vault does not know.
    /// </summary>
    public static DocumentContentDecision Classify(DocumentFileName fileName, string? declaredContentType, ReadOnlySpan<byte> content,
        long maximumBytes, IReadOnlyCollection<DocumentFileKind> acceptedKinds)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(acceptedKinds);
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
        var info = KnownKinds.SingleOrDefault(candidate => candidate.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
        if (info is null || !acceptedKinds.Contains(info.Kind))
        {
            return DocumentContentDecision.Refused(DocumentRuleCodes.TypeNotAllowed);
        }

        var declared = MediaTypeOnly(declaredContentType);
        if (!UnknownDeclaredTypes.Contains(declared) && !IsDeclaredAs(info.Kind, declared))
        {
            return DocumentContentDecision.Refused(DocumentRuleCodes.ContentMismatch);
        }

        if (!HasContentOf(info.Kind, content))
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
        DocumentFileKind.Xlsx => string.Equals(declared, XlsxContentType, StringComparison.OrdinalIgnoreCase),
        DocumentFileKind.Mt940 => string.Equals(declared, PlainTextContentType, StringComparison.OrdinalIgnoreCase),
        DocumentFileKind.Csv => CsvDeclaredTypes.Contains(declared),
        DocumentFileKind.UblInvoiceXml => string.Equals(declared, "application/xml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(declared, "text/xml", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    /// <summary>Whether the bytes prove the kind: its magic number, or (bank-statement kinds) its structural check.</summary>
    private static bool HasContentOf(DocumentFileKind kind, ReadOnlySpan<byte> content) => kind switch
    {
        DocumentFileKind.Pdf => content.StartsWith(PdfSignature),
        DocumentFileKind.Png => content.StartsWith(PngSignature),
        DocumentFileKind.Jpeg => content.StartsWith(JpegSignature),
        DocumentFileKind.Xlsx => DocumentContentInspection.IsPassiveWorkbook(content),
        DocumentFileKind.Mt940 => DocumentContentInspection.IsMt940Statement(content),
        DocumentFileKind.Csv => DocumentContentInspection.IsDelimitedTable(content),
        DocumentFileKind.UblInvoiceXml => DocumentUblInvoiceInspection.IsPassiveInvoice(content),
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

namespace GoymenERP.Modules.Documents.Domain;

/// <summary>
/// The content kinds the vault knows (DEC-DOC-009: an allowlist, and the bytes must prove the kind). Every kind is a
/// passive format whose signature or structure the server checks itself; nothing that executes, renders script or
/// unpacks (executables, scripts, HTML, SVG, general archives, office macros) is on the list. Which further kinds a company
/// may upload is not decided (the open part of DEC-DOC-009's type list, NEEDS-SINAN); widening the list is a reviewed code
/// change that adds a kind with its own content check, never a configuration string.
/// <para>
/// <see cref="Pdf"/>, <see cref="Png"/> and <see cref="Jpeg"/> are the standard kinds every owner record accepts.
/// <see cref="Xlsx"/>, <see cref="Mt940"/> and <see cref="Csv"/> are the bank-statement import kinds (FNS-D52, B-26): an
/// owner record accepts them only when its owner port opts in, so the open type list of the existing owners is not
/// widened by them.
/// </para>
/// </summary>
public enum DocumentFileKind
{
    Pdf = 1,
    Png = 2,
    Jpeg = 3,

    /// <summary>An Office Open XML workbook without macros, embedded objects or encryption (FNS-D52).</summary>
    Xlsx = 4,

    /// <summary>A SWIFT MT940 customer statement as plain text (FNS-D52).</summary>
    Mt940 = 5,

    /// <summary>A delimited text table (FNS-D52); its column mapping is a per-bank setting outside this module.</summary>
    Csv = 6,

    /// <summary>A passive UBL invoice XML source. Only the incoming purchase invoice owner opts in (ALS-D7).</summary>
    UblInvoiceXml = 7,
}

/// <summary>One allowlisted kind: its canonical media type (the only one the server ever serves) and its extensions.</summary>
public sealed record DocumentFileKindInfo(DocumentFileKind Kind, string ContentType, IReadOnlyList<string> Extensions);

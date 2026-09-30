namespace GoymenERP.Modules.Documents.Domain;

/// <summary>
/// The content kinds the vault accepts (DEC-DOC-009: an allowlist, and the bytes must prove the kind). Every kind is a
/// passive format whose signature the server checks itself; nothing that executes, renders script or unpacks
/// (executables, scripts, HTML, SVG, archives, office macros) is on the list. Which further kinds a company may upload
/// is not decided (the open part of DEC-DOC-009's type list, NEEDS-SINAN); widening the list is a reviewed code change
/// that adds a kind with its own signature check, never a configuration string.
/// </summary>
public enum DocumentFileKind
{
    Pdf = 1,
    Png = 2,
    Jpeg = 3,
}

/// <summary>One allowlisted kind: its canonical media type (the only one the server ever serves) and its extensions.</summary>
public sealed record DocumentFileKindInfo(DocumentFileKind Kind, string ContentType, IReadOnlyList<string> Extensions);

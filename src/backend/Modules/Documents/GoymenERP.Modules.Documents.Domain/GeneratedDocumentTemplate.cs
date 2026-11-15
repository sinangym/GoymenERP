using System.Text.RegularExpressions;

namespace GoymenERP.Modules.Documents.Domain;

/// <summary>
/// DEC-DOC-010: the template (and asset) version a generated PDF was rendered with, kept as the document's snapshot so the
/// stored file can always be traced to the exact template that produced it. The code names the template the producing
/// module declared ("sales.order-pdf"); the version is the producer's own template/asset version ("v1", "2026.11.1").
/// Neither is a business rule of the vault: they are recorded, never interpreted.
/// </summary>
public sealed partial record GeneratedDocumentTemplate
{
    public const int MaximumCodeLength = 64;
    public const int MaximumVersionLength = 64;

    private GeneratedDocumentTemplate(string code, string version)
    {
        Code = code;
        Version = version;
    }

    public string Code { get; }

    public string Version { get; }

    public static GeneratedDocumentTemplate? TryCreate(string? code, string? version) =>
        code is not null && code.Length <= MaximumCodeLength && CodePattern().IsMatch(code)
        && version is not null && version.Length <= MaximumVersionLength && VersionPattern().IsMatch(version)
            ? new GeneratedDocumentTemplate(code, version)
            : null;

    [GeneratedRegex("^[a-z][a-z0-9-]*(\\.[a-z][a-z0-9-]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}

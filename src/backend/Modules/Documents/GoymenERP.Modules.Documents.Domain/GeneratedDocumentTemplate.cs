using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using System.Globalization;

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

    private GeneratedDocumentTemplate(string code, string version, GeneratedDocumentPublicationSnapshot? publication)
    {
        Code = code;
        Version = version;
        Publication = publication;
    }

    public string Code { get; }

    public string Version { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeneratedDocumentPublicationSnapshot? Publication { get; }

    public static GeneratedDocumentTemplate? TryCreate(string? code, string? version, GeneratedDocumentPublicationSnapshot? publication = null) =>
        code is not null && code.Length <= MaximumCodeLength && CodePattern().IsMatch(code)
        && version is not null && version.Length <= MaximumVersionLength && VersionPattern().IsMatch(version)
        && (publication is null || publication.IsWellFormed() && publication.TemplateCode == code
            && version == "pub." + publication.PublicationVersion.ToString(CultureInfo.InvariantCulture))
            ? new GeneratedDocumentTemplate(code, version, publication)
            : null;

    [GeneratedRegex("^[a-z][a-z0-9-]*(\\.[a-z][a-z0-9-]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}

using System.Text;

namespace GoymenERP.Modules.Documents.Domain;

/// <summary>
/// The name a user sees for an uploaded file (DEC-DOC-004: the user-visible name is metadata, independent of the
/// technical identity; forbidden file characters are converted safely). Only the last path segment of what the client
/// sent is kept, in NFC; control characters, bidirectional overrides (the right-to-left "fdp.exe" trick) and the
/// characters no common file system accepts become "_"; Turkish letters stay as they are. Leading/trailing blanks and
/// dots are trimmed. The name is never a storage path: objects are addressed by company and content hash only.
/// </summary>
public sealed class DocumentFileName
{
    public const int MaximumLength = 200;
    private const int MaximumKeptExtensionLength = 16;
    private const string Forbidden = "<>:\"/\\|?*";

    private DocumentFileName(string value)
    {
        Value = value;
        var dot = value.LastIndexOf('.');
        Extension = dot <= 0 || dot == value.Length - 1 ? string.Empty : value[dot..].ToLowerInvariant();
    }

    public string Value { get; }

    /// <summary>The lower-case extension with its dot (".pdf"), or empty when the name has none.</summary>
    public string Extension { get; }

    public override string ToString() => Value;

    /// <summary>The safe name, or null when nothing usable is left (<see cref="DocumentRuleCodes.FileNameInvalid"/>).</summary>
    public static DocumentFileName? TryCreate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var lastSeparator = raw.LastIndexOfAny(['/', '\\']);
        var segment = (lastSeparator < 0 ? raw : raw[(lastSeparator + 1)..]).Normalize(NormalizationForm.FormC);
        var builder = new StringBuilder(segment.Length);
        foreach (var character in segment)
        {
            builder.Append(char.IsControl(character) || IsBidiControl(character) || Forbidden.Contains(character, StringComparison.Ordinal)
                ? '_'
                : character);
        }

        var cleaned = builder.ToString().Trim().Trim('.').Trim();
        if (cleaned.Length == 0 || cleaned.All(character => character == '_'))
        {
            return null;
        }

        if (cleaned.Length > MaximumLength)
        {
            // Keep the extension: it decides the kind. The stem is cut, never the extension.
            var dot = cleaned.LastIndexOf('.');
            var extension = dot > 0 && cleaned.Length - dot <= MaximumKeptExtensionLength ? cleaned[dot..] : string.Empty;
            cleaned = cleaned[..(MaximumLength - extension.Length)].TrimEnd() + extension;
        }

        return new DocumentFileName(cleaned);
    }

    private static bool IsBidiControl(char character) =>
        character is >= '‪' and <= '‮' or >= '⁦' and <= '⁩' or '‎' or '‏' or '؜';
}

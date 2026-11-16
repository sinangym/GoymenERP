using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>
/// API-98/B-14: the deterministic input rules of a reference list entry, shared by the administration, the reader and the
/// tests. Every rule is closed: a value is accepted as written (the name only trimmed) or refused, never folded or replaced
/// by a default. Which entries a list holds is the company's Settings data; nothing is seeded here.
/// </summary>
public static class ReferenceListRulesV1
{
    /// <summary>
    /// A code: 1…<see cref="SettingsReferenceListInputLimitsV1.CodeMaximumLength"/> characters of 'A'–'Z', '0'–'9', '.', '_'
    /// or '-', starting with a letter or digit, exactly as written (no trimming, no case folding).
    /// </summary>
    public static bool IsCode(string? code)
    {
        if (string.IsNullOrEmpty(code) || code.Length > SettingsReferenceListInputLimitsV1.CodeMaximumLength
            || !IsCodeStart(code[0]))
        {
            return false;
        }

        foreach (var character in code)
        {
            if (!IsCodeStart(character) && character is not ('.' or '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A display name: trimmed, 1…<see cref="SettingsReferenceListInputLimitsV1.NameMaximumLength"/> characters, no control
    /// characters.
    /// </summary>
    public static bool TryNormalizeName(string? name, out string normalized)
    {
        normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > SettingsReferenceListInputLimitsV1.NameMaximumLength
            || normalized.Any(char.IsControl))
        {
            normalized = string.Empty;
            return false;
        }

        return true;
    }

    /// <summary>A stored name is well formed when it is already in its normalized form.</summary>
    public static bool IsStoredName(string? name) =>
        TryNormalizeName(name, out var normalized) && string.Equals(normalized, name, StringComparison.Ordinal);

    private static bool IsCodeStart(char character) => character is >= 'A' and <= 'Z' or >= '0' and <= '9';
}

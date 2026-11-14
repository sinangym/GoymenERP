using System.Text;

namespace GoymenERP.Modules.Identity.Domain.Credentials;

/// <summary>
/// Local account user name rules (ID-1). The stored unique key is the normalized form: trimmed,
/// Unicode NFC and invariant upper case, so "ali", " ALI " and a decomposed form are one account.
/// </summary>
public static class LocalUserName
{
    /// <summary>Column length of <c>UserName</c> and <c>NormalizedUserName</c>.</summary>
    public const int MaxLength = 256;

    /// <summary>
    /// Returns the normalized user name, or <see langword="null"/> when the value is empty or longer than
    /// <see cref="MaxLength"/> after trimming. Callers treat <see langword="null"/> as "no such account".
    /// </summary>
    public static string? TryNormalize(string? userName)
    {
        if (userName is null)
        {
            return null;
        }

        var trimmed = userName.Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxLength)
        {
            return null;
        }

        if (HasLoneSurrogate(trimmed))
        {
            // Not valid UTF-16; such a name never matches a stored account (ICU may not refuse it itself).
            return null;
        }

        string composed;
        try
        {
            composed = trimmed.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            // Lone surrogates cannot be normalized; such a name never matches a stored account.
            return null;
        }

        var normalized = composed.ToUpperInvariant();
        return normalized.Length > MaxLength ? null : normalized;
    }

    private static bool HasLoneSurrogate(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
            {
                index++;
            }
            else if (char.IsSurrogate(value[index]))
            {
                return true;
            }
        }

        return false;
    }
}

using System.Security.Cryptography;
using System.Text;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

/// <summary>
/// SAT-D119 review P2-1: the opaque identity half of the e-belge profile's command fence. The numeric PROFILE version
/// alone cannot tell two profiles apart (ABA): after a party's VKN changes (OPEN-030) from X to Y, X's and Y's profiles
/// may both be at version 1. The token binds the fence to WHICH profile the read showed — the party's VKN and the
/// profile's surrogate id, or "none" when no profile existed for that VKN — so a stale command against another
/// taxpayer's record is refused as 409 <c>version_conflict</c>.
/// <para>
/// It is a SHA-256 over a versioned, domain-separated text and carries nothing beyond what the reader's own card
/// already shows (the reader can only obtain the token of a party its own company holds). It is never parsed, only
/// recomputed and compared.
/// </para>
/// </summary>
internal static class CounterpartyEDocumentProfileToken
{
    private const string Domain = "goymen-erp/counterparty/customer-e-document-profile/v1";

    /// <summary>The token of the profile resolved for <paramref name="taxIdentityNumber"/> (null id = no profile yet).</summary>
    public static string Compute(string taxIdentityNumber, Guid? profileId)
    {
        ArgumentException.ThrowIfNullOrEmpty(taxIdentityNumber);
        var text = $"{Domain}\n{taxIdentityNumber}\n{(profileId is { } id ? id.ToString("N") : "none")}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>The token's lexical shape: 64 lower-case hex digits.</summary>
    public static bool IsWellFormed(string? token) =>
        token is { Length: 64 } && token.All(character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}

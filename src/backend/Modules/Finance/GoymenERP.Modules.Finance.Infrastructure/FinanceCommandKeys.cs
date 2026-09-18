using System.Security.Cryptography;
using System.Text;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// ADR-011 §2: the raw Idempotency-Key is never stored; the command record keeps the upper-case
/// SHA-256 of the trimmed key. Finance refuses a non-canonical key before any read, so the trimmed
/// key is the key itself.
/// </summary>
internal static class FinanceCommandKeys
{
    public static string Hash(string idempotencyKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey.Trim())));
}

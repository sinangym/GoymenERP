using System.Security.Cryptography;
using System.Text;

namespace GoymenERP.Modules.Organization.Infrastructure.Seeding;

internal static class DeterministicGuid
{
    public static Guid Create(Guid companyId, string entityType, string code)
    {
        var value = $"{companyId:N}|{entityType}|{code.Trim().ToUpperInvariant()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        return new Guid(guidBytes);
    }
}

using System.Security.Cryptography;
using System.Text.Json;

namespace GoymenERP.Modules.Audit.Domain.Events;

public static class AuditEventIntegrity
{
    public static string Compute(AuditEventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(envelope)));
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GoymenERP.Modules.Idempotency.Domain.Executions;

public static class IdempotencyHash
{
    public static string ComputeKey(string key)
    {
        var canonicalKey = RequireText(key, nameof(key));
        return ComputeBytes(Encoding.UTF8.GetBytes(canonicalKey));
    }

    public static string ComputePayload<T>(T payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return ComputeBytes(JsonSerializer.SerializeToUtf8Bytes(payload));
    }

    private static string ComputeBytes(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static string RequireText(string? value, string parameterName)
    {
        var canonical = value?.Trim();
        return string.IsNullOrWhiteSpace(canonical)
            ? throw new ArgumentException("Değer boş olamaz.", parameterName)
            : canonical;
    }
}

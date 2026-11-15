using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GoymenERP.BuildingBlocks.Security.SecretStore;

namespace GoymenERP.BuildingBlocks.Security.Events;

/// <summary>
/// A keyed hash of a login identifier (AI_KARAR_PAKETI_2026-11 E8). It lets the security channel correlate failed
/// logins for one identifier without storing the identifier. The key id travels with the value so a rotated key can be
/// told apart; the raw identifier and the key never leave the hasher.
/// </summary>
public sealed record LoginIdentifierHashV1(string KeyId, string Value, string PolicyRef);

/// <summary>
/// HMAC-SHA256 hasher for login identifiers (E8). The key lives only in the secret store; the key owner and the
/// rotation period are human decisions (OPEN-020 / E8). Until a key id, a policy reference and a key of at least the
/// hash size are all present, <see cref="TryCreate"/> returns <c>null</c> and no identifier hash is written
/// (fail-closed: nothing is hashed with an invented or weak key).
/// </summary>
public sealed partial class LoginIdentifierHasherV1 : IDisposable
{
    private readonly byte[] key;
    private bool disposed;

    private LoginIdentifierHasherV1(string keyId, byte[] key, string policyRef)
    {
        KeyId = keyId;
        PolicyRef = policyRef;
        this.key = key;
    }

    public string KeyId { get; }

    public string PolicyRef { get; }

    /// <summary>The shortest key accepted: the HMAC-SHA256 output size.</summary>
    public static int MinimumKeyBytes => HMACSHA256.HashSizeInBytes;

    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9_.-]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex KeyIdPattern();

    /// <summary>Accepts a base64 key from the secret store. Returns <c>null</c> when anything is missing or too weak.</summary>
    public static LoginIdentifierHasherV1? TryCreate(string? keyId, ErpSecretV1? key, string? policyRef)
    {
        if (keyId is null || !KeyIdPattern().IsMatch(keyId) || string.IsNullOrWhiteSpace(policyRef) || key is null)
        {
            return null;
        }

        var encoded = key.Reveal();
        var buffer = new byte[(encoded.Length * 3 / 4) + 3];
        if (!Convert.TryFromBase64String(encoded, buffer, out var written) || written < MinimumKeyBytes)
        {
            CryptographicOperations.ZeroMemory(buffer);
            return null;
        }

        var keyBytes = buffer.AsSpan(0, written).ToArray();
        CryptographicOperations.ZeroMemory(buffer);
        return new LoginIdentifierHasherV1(keyId, keyBytes, policyRef.Trim());
    }

    /// <summary>
    /// Hashes an identifier that the caller has already normalized with the identity module's own normalizer. The hasher
    /// does not normalize, so two modules cannot disagree on what "the same identifier" means.
    /// </summary>
    public LoginIdentifierHashV1 Hash(string normalizedIdentifier)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(normalizedIdentifier);
        var input = Encoding.UTF8.GetBytes(normalizedIdentifier);
        try
        {
            var mac = HMACSHA256.HashData(key, input);
            return new LoginIdentifierHashV1(KeyId, Convert.ToHexStringLower(mac), PolicyRef);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        CryptographicOperations.ZeroMemory(key);
        disposed = true;
    }

    public override string ToString() => $"{nameof(LoginIdentifierHasherV1)}({KeyId}, redacted)";
}

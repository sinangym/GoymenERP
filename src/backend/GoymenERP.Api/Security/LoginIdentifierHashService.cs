using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.BuildingBlocks.Security.SecretStore;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.Security;

/// <summary>
/// E8 configuration (section <c>SecurityEvents:LoginIdentifierHash</c>). It names the key; it never holds it. The
/// key itself is read from the secret store under <see cref="KeySecretName"/>. All three values are unset by default,
/// so no identifier hash is produced until the key owner and rotation are decided (E8 human gate).
/// </summary>
public sealed class LoginIdentifierHashOptions
{
    public const string SectionName = "SecurityEvents:LoginIdentifierHash";

    /// <summary>Identifies the key generation so a rotated key can be told apart.</summary>
    public string? KeyId { get; set; }

    /// <summary>Secret store reference name of the base64 key (for example an environment variable name).</summary>
    public string? KeySecretName { get; set; }

    /// <summary>Reviewed decision reference that enabled the key (traceability, not authority).</summary>
    public string? PolicyRef { get; set; }
}

/// <summary>Produces E8 login identifier hashes, or nothing while the key is not configured.</summary>
public interface ILoginIdentifierHashServiceV1
{
    bool IsConfigured { get; }

    /// <summary>Returns <c>null</c> while E8 is closed; callers then write no identifier hash at all.</summary>
    LoginIdentifierHashV1? TryHash(string normalizedIdentifier);
}

public sealed class LoginIdentifierHashService(
    IOptionsMonitor<LoginIdentifierHashOptions> options,
    IErpSecretReaderV1 secrets) : ILoginIdentifierHashServiceV1
{
    public bool IsConfigured
    {
        get
        {
            using var hasher = CreateHasher();
            return hasher is not null;
        }
    }

    public LoginIdentifierHashV1? TryHash(string normalizedIdentifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(normalizedIdentifier);
        // The key is read per call and the hasher is disposed at once: a rotated secret takes effect without a restart
        // and the key bytes do not stay in memory between calls.
        using var hasher = CreateHasher();
        return hasher?.Hash(normalizedIdentifier);
    }

    private LoginIdentifierHasherV1? CreateHasher()
    {
        var current = options.CurrentValue;
        if (string.IsNullOrWhiteSpace(current.KeySecretName) || !ErpSecretNamesV1.IsValid(current.KeySecretName))
        {
            return null;
        }

        return LoginIdentifierHasherV1.TryCreate(current.KeyId, secrets.TryRead(current.KeySecretName), current.PolicyRef);
    }
}

using System.Text.RegularExpressions;

namespace GoymenERP.BuildingBlocks.Security.SecretStore;

/// <summary>
/// One secret value read from the process secret store (environment or a mounted secret store; AI_KARAR_PAKETI_2026-11
/// A and E8). The value never appears in <see cref="ToString"/>, logs, audit, problem details or configuration files.
/// Callers keep it only for the one call that needs it.
/// </summary>
public sealed class ErpSecretV1
{
    private readonly string value;

    public ErpSecretV1(string name, string value)
    {
        if (!ErpSecretNamesV1.IsValid(name)) throw new ArgumentException("Invalid secret name.", nameof(name));
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A secret value cannot be empty.", nameof(value));
        Name = name;
        this.value = value;
    }

    /// <summary>The reference name of the secret (for example an environment variable name). Not a secret itself.</summary>
    public string Name { get; }

    /// <summary>Returns the raw value. Call it only where the value is handed to its single consumer.</summary>
    public string Reveal() => value;

    public override string ToString() => $"{nameof(ErpSecretV1)}({Name}, redacted)";
}

/// <summary>
/// Reads a named secret. Implementations read only the secret store, never <c>appsettings*.json</c>, so a secret value
/// cannot be committed with configuration. A missing or empty secret returns <c>null</c>; consumers stay closed.
/// </summary>
public interface IErpSecretReaderV1
{
    ErpSecretV1? TryRead(string secretName);
}

/// <summary>Secret reference names: upper-case ASCII letters, digits and underscore, starting with a letter.</summary>
public static partial class ErpSecretNamesV1
{
    [GeneratedRegex(@"\A[A-Z][A-Z0-9_]{0,127}\z", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    public static bool IsValid(string? name) => name is not null && NamePattern().IsMatch(name);
}

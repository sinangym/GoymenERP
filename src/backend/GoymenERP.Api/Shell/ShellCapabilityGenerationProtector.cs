using System.Security.Cryptography;
using System.Text;

namespace GoymenERP.Api.Shell;

public sealed class ShellCapabilityGenerationProtector : IDisposable
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    public string Protect(params IEnumerable<string> components)
    {
        ArgumentNullException.ThrowIfNull(components);

        var canonical = new StringBuilder();
        foreach (var component in components)
        {
            ShellCapabilityRegistry.Append(
                canonical,
                component ?? throw new InvalidOperationException(
                    "Shell generation girdisi null olamaz."));
        }

        var digest = HMACSHA256.HashData(
            _key,
            Encoding.UTF8.GetBytes(canonical.ToString()));
        return $"hmac-sha256:{ShellCapabilityRegistry.Base64Url(digest)}";
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_key);
    }
}

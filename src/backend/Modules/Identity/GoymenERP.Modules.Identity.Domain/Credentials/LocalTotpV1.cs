using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GoymenERP.Modules.Identity.Domain.Credentials;

/// <summary>RFC 6238 authenticator interoperability: SHA1, six digits, 30 seconds. The bounded skew is one step.
/// Optional enrollment is the KIM-D3 decision; these protocol parameters never grant a role or require MFA.</summary>
public static class LocalTotpV1
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    public static string NewSecret() => Encode(RandomNumberGenerator.GetBytes(20));

    public static long? Match(string secret, string? code, DateTimeOffset now, long? lastAcceptedStep)
    {
        if (code is null || code.Length != 6 || !code.All(char.IsAsciiDigit)) return null;
        var key = Decode(secret);
        var step = now.ToUnixTimeSeconds() / 30;
        long? matched = null;
        for (var offset = -1; offset <= 1; offset++)
        {
            var candidate = step + offset;
            if (candidate >= 0 && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(AtStep(key, candidate)), Encoding.ASCII.GetBytes(code))
                && (lastAcceptedStep is null || candidate > lastAcceptedStep)) matched = candidate;
        }
        return matched;
    }

    internal static string AtStep(byte[] secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        var hash = HMACSHA1.HashData(secret, counter);
        var offset = hash[^1] & 15;
        var number = BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(offset, 4)) & 0x7fffffff;
        return (number % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static string Encode(byte[] bytes)
    {
        var output = new StringBuilder(); var buffer = 0; var bits = 0;
        foreach (var value in bytes)
        {
            buffer = (buffer << 8) | value; bits += 8;
            while (bits >= 5) { bits -= 5; output.Append(Alphabet[(buffer >> bits) & 31]); }
        }
        if (bits > 0) output.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return output.ToString();
    }
    private static byte[] Decode(string text)
    {
        if (text.Length is < 26 or > 128 || text.Any(value => !Alphabet.Contains(value))) throw new ArgumentException("Authenticator secret is invalid.");
        var output = new List<byte>(); var buffer = 0; var bits = 0;
        foreach (var value in text)
        {
            buffer = (buffer << 5) | Alphabet.IndexOf(value); bits += 5;
            if (bits >= 8) { bits -= 8; output.Add((byte)(buffer >> bits)); }
        }
        if (Encode(output.ToArray()) != text) throw new ArgumentException("Authenticator secret is not canonical.");
        return output.ToArray();
    }

    public static IReadOnlyList<string> NewRecoveryCodes() => Enumerable.Range(0, 10)
        .Select(_ => string.Join('-', Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).Chunk(4).Select(part => new string(part)))).ToArray();
    public static string RecoveryHash(string code)
    {
        var normalized = code.Replace("-", "", StringComparison.Ordinal).Trim().ToUpperInvariant();
        if (normalized.Length != 32 || normalized.Any(value => !char.IsAsciiHexDigit(value))) return string.Empty;
        return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(normalized)));
    }
}

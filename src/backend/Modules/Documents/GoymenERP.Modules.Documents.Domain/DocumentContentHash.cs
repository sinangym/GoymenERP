using System.Security.Cryptography;

namespace GoymenERP.Modules.Documents.Domain;

/// <summary>
/// The content identity of an uploaded file: SHA-256 over the exact bytes the server received, as 64 lower-case hex
/// characters. The server computes it; a hash the client sends is never trusted (AT-057 compares only server hashes).
/// </summary>
public static class DocumentContentHash
{
    public const int Length = 64;

    public static string Compute(ReadOnlySpan<byte> content) => Convert.ToHexStringLower(SHA256.HashData(content));

    public static bool IsWellFormed(string? value) =>
        value is { Length: Length } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

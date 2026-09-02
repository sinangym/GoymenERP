using System.Security.Cryptography;

namespace GoymenERP.DocumentGeneration.Pdf;

internal static class NotoSansRegularFont
{
    internal const string FamilyName = "Noto Sans";
    internal const string FaceName = "NotoSans-Regular";
    internal static readonly string AssetPath = Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "Fonts",
        "NotoSans-Regular.ttf");
    internal const string ExpectedSha256 =
        "B85C38ECEA8A7CFB39C24E395A4007474FA5A4FC864F6EE33309EB4948D232D5";

    private static readonly Lazy<byte[]> FontBytes = new(LoadAndVerify);

    internal static void EnsureIntegrity() => _ = FontBytes.Value;

    internal static byte[] GetBytes() => FontBytes.Value;

    private static byte[] LoadAndVerify()
    {
        if (!File.Exists(AssetPath))
        {
            throw new InvalidOperationException("The vendored Noto Sans Regular font asset was not found.");
        }

        var bytes = File.ReadAllBytes(AssetPath);
        var actualHash = Convert.ToHexString(SHA256.HashData(bytes));

        if (!string.Equals(actualHash, ExpectedSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The vendored Noto Sans Regular font asset hash does not match its provenance: {actualHash}.");
        }

        return bytes;
    }
}

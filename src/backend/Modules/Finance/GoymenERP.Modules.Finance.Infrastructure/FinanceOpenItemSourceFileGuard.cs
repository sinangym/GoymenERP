using System.Security.Cryptography;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// Binds the exact bytes of an already approved external invoice-installment file to the
/// immutable Finance source reference. It deliberately does not define a CSV/Excel layout,
/// approve a mapping, parse rows, grant authority or admit open items.
/// </summary>
public static class FinanceOpenItemSourceFileGuard
{
    public const string FileEmptyCode = "finance_open_item_source_file_empty";
    public const string ChecksumMismatchCode = "finance_open_item_batch_checksum_mismatch";

    public static string ComputeSha256(ReadOnlySpan<byte> content) =>
        Convert.ToHexStringLower(SHA256.HashData(content));

    public static IReadOnlyList<ValidationIssue> ValidateContent(
        FinanceOpenItemSourceReference source,
        ReadOnlySpan<byte> content)
    {
        ArgumentNullException.ThrowIfNull(source);

        var issues = FinanceOpenItemSourcePolicy.ValidateReference(source);
        if (issues.Count > 0)
        {
            return issues;
        }
        if (content.IsEmpty)
        {
            return [new ValidationIssue("sourceFile", FileEmptyCode)];
        }

        var expected = Convert.FromHexString(source.BatchChecksum);
        Span<byte> actual = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(content, actual);
        return CryptographicOperations.FixedTimeEquals(expected, actual)
            ? []
            : [new ValidationIssue("batchChecksum", ChecksumMismatchCode)];
    }
}

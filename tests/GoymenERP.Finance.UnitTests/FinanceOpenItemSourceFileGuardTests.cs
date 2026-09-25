using System.Text;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinanceOpenItemSourceFileGuardTests
{
    private static readonly Guid BatchId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public void Exact_approved_bytes_bind_to_a_lowercase_sha256_without_selecting_a_file_layout()
    {
        var content = Encoding.UTF8.GetBytes("SENTETIK approved invoice-installment bytes\n");
        var checksum = FinanceOpenItemSourceFileGuard.ComputeSha256(content);
        var source = new FinanceOpenItemSourceReference(BatchId, "synthetic-approved-v1", checksum);

        var issues = FinanceOpenItemSourceFileGuard.ValidateContent(source, content);

        Assert.Empty(issues);
        Assert.Matches("^[0-9a-f]{64}$", checksum);
    }

    [Fact]
    public void One_changed_byte_is_refused_before_any_row_mapping_or_admission()
    {
        var approved = Encoding.UTF8.GetBytes("SENTETIK approved invoice-installment bytes\n");
        var changed = Encoding.UTF8.GetBytes("SENTETIK approved invoice-installment bytes!\n");
        var source = new FinanceOpenItemSourceReference(
            BatchId,
            "synthetic-approved-v1",
            FinanceOpenItemSourceFileGuard.ComputeSha256(approved));

        var issue = Assert.Single(FinanceOpenItemSourceFileGuard.ValidateContent(source, changed));

        Assert.Equal("batchChecksum", issue.Field);
        Assert.Equal(FinanceOpenItemSourceFileGuard.ChecksumMismatchCode, issue.Code);
    }

    [Fact]
    public void Empty_file_and_noncanonical_reference_fail_closed()
    {
        var empty = new FinanceOpenItemSourceReference(
            BatchId,
            "synthetic-approved-v1",
            FinanceOpenItemSourceFileGuard.ComputeSha256([]));
        var malformed = empty with { BatchVersion = " synthetic-approved-v1 " };

        Assert.Equal(
            FinanceOpenItemSourceFileGuard.FileEmptyCode,
            Assert.Single(FinanceOpenItemSourceFileGuard.ValidateContent(empty, [])).Code);
        Assert.Equal(
            "finance_open_item_batch_version_invalid",
            Assert.Single(FinanceOpenItemSourceFileGuard.ValidateContent(malformed, [1])).Code);
    }
}

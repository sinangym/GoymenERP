using System.Security.Cryptography;
using System.Text;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinanceOfficialInvoiceImportParserV1Tests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Batch = Guid.Parse("11000000-0000-0000-0000-000000000001");

    [Fact]
    public void Exact_v1_csv_becomes_canonical_batch_with_raw_byte_checksum()
    {
        var bytes = Csv(Row("1250.50", "2026-10-31"));

        var preview = new FinanceOfficialInvoiceImportParserV1().Preview(
            Context(), new(Batch, "official-2026-09-v1", bytes));

        Assert.True(preview.IsValid);
        Assert.Empty(preview.Issues);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), preview.SourceSha256);
        var batch = Assert.IsType<ApprovedFinanceOpenItemSourceBatch>(preview.Batch);
        Assert.Equal((Batch, Company, "official-2026-09-v1", preview.SourceSha256),
            (batch.BatchId, batch.CompanyId, batch.BatchVersion, batch.BatchChecksum));
        var row = Assert.Single(batch.Rows);
        Assert.Equal(("1250.50", new DateOnly(2026, 10, 31),
                FinanceOpenItemSourceKinds.OfficialInvoiceInstallmentDocument),
            (row.Amount, row.DueOn, row.DocumentKind));
    }

    [Fact]
    public void Header_shape_amount_and_date_errors_are_listed_without_a_batch()
    {
        var badHeader = Encoding.UTF8.GetBytes("wrong\n");
        var headerResult = new FinanceOfficialInvoiceImportParserV1().Preview(
            Context(), new(Batch, "v1", badHeader));
        var rowResult = new FinanceOfficialInvoiceImportParserV1().Preview(
            Context(), new(Batch, "v1", Csv(Row("0", "31.10.2026"))));

        Assert.Contains(headerResult.Issues,
            issue => issue.Code == FinanceOfficialInvoiceImportCodesV1.HeaderInvalid && issue.RowNumber == 1);
        Assert.Contains(rowResult.Issues,
            issue => issue.Code == FinanceOfficialInvoiceImportCodesV1.DateInvalid && issue.RowNumber == 2);
        Assert.Null(headerResult.Batch);
        Assert.Null(rowResult.Batch);
    }

    [Fact]
    public void Duplicate_source_identity_is_reported_by_canonical_policy()
    {
        var row = Row("1", "");
        var preview = new FinanceOfficialInvoiceImportParserV1().Preview(
            Context(), new(Batch, "v1", Csv(row, row)));

        Assert.Contains(preview.Issues,
            issue => issue.Code == "finance_open_item_source_duplicate"
                && issue.RowNumber == 3 && issue.Field == "rows[1]");
        Assert.Null(preview.Batch);
    }

    [Fact]
    public void Canonical_dotted_row_issue_keeps_the_exact_leaf_field()
    {
        var preview = new FinanceOfficialInvoiceImportParserV1().Preview(
            Context(), new(Batch, "v1", Csv(Row("0", "2026-10-31"))));

        Assert.Contains(preview.Issues,
            issue => issue.Code == "finance_open_item_amount_invalid"
                && issue.RowNumber == 2 && issue.Field == "amount");
        Assert.Null(preview.Batch);
    }

    [Fact]
    public void Company_and_document_kind_are_not_columns_the_file_can_override()
    {
        Assert.DoesNotContain("companyId", FinanceOfficialInvoiceImportTemplateV1.Header, StringComparison.Ordinal);
        Assert.DoesNotContain("documentKind", FinanceOfficialInvoiceImportTemplateV1.Header, StringComparison.Ordinal);
    }

    private static FinanceContext Context() => new(Company, "actor", "correlation", null);

    private static byte[] Csv(params string[] rows) => Encoding.UTF8.GetBytes(
        FinanceOfficialInvoiceImportTemplateV1.Header + "\n" + string.Join("\n", rows) + "\n");

    private static string Row(string amount, string dueOn) => string.Join(',',
        "21000000-0000-0000-0000-000000000001",
        "22000000-0000-0000-0000-000000000001",
        "23000000-0000-0000-0000-000000000001",
        "invoice-v1",
        "24000000-0000-0000-0000-000000000001",
        "25000000-0000-0000-0000-000000000001",
        "26000000-0000-0000-0000-000000000001",
        amount,
        dueOn);
}

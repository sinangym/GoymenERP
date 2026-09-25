using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinanceOpenItemSourcePolicyTests
{
    [Fact]
    public void Valid_batch_preserves_canonical_source_identity_and_lossless_amount()
    {
        var company = Guid.NewGuid();
        var reference = Reference();
        var row = Row("999999999999999999999999999999.50");
        var batch = new ApprovedFinanceOpenItemSourceBatch(
            reference.BatchId, company, reference.BatchVersion, reference.BatchChecksum, [row]);

        Assert.Empty(FinanceOpenItemSourcePolicy.ValidateReference(reference));
        Assert.Empty(FinanceOpenItemSourcePolicy.ValidateBatch(
            new FinanceContext(company, "actor", "correlation", null), reference, batch));

        var persisted = new FinanceOpenItemRow
        {
            Id = row.OpenItemId,
            SourceKind = FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment,
            SourceId = row.SourceDocumentId,
            SourceLineId = row.SourceInstallmentId,
            SourceVersion = row.SourceVersion,
            PartyId = row.PartyId,
            CompanyAccountId = row.CompanyAccountId,
            CurrencyId = row.CurrencyId,
            OriginalAmountText = row.Amount,
            DueOn = row.DueOn,
        };
        Assert.True(FinanceOpenItemSourcePolicy.MatchesExisting(row, persisted));
    }

    [Fact]
    public void Invalid_checksum_duplicate_identity_and_nonpositive_amount_are_refused()
    {
        var company = Guid.NewGuid();
        var reference = Reference() with { BatchChecksum = "ABC" };
        var row = Row("0");
        var batch = new ApprovedFinanceOpenItemSourceBatch(
            reference.BatchId, company, reference.BatchVersion, reference.BatchChecksum, [row, row]);

        Assert.Contains(FinanceOpenItemSourcePolicy.ValidateReference(reference),
            issue => issue.Code == "finance_open_item_batch_checksum_invalid");
        var issues = FinanceOpenItemSourcePolicy.ValidateBatch(
            new FinanceContext(company, "actor", "correlation", null), reference, batch);
        Assert.Contains(issues, issue => issue.Code == "finance_open_item_source_duplicate");
        Assert.Contains(issues, issue => issue.Code == "finance_open_item_amount_invalid");
    }

    [Theory]
    [InlineData("order")]
    [InlineData("delivery-note")]
    public void Order_or_delivery_note_is_not_an_open_item_source(string documentKind)
    {
        var company = Guid.NewGuid();
        var reference = Reference();
        var row = Row("1") with { DocumentKind = documentKind };
        var batch = new ApprovedFinanceOpenItemSourceBatch(
            reference.BatchId, company, reference.BatchVersion, reference.BatchChecksum, [row]);
        var issues = FinanceOpenItemSourcePolicy.ValidateBatch(
            new FinanceContext(company, "actor", "correlation", null), reference, batch);
        Assert.Contains(issues, issue => issue.Code == "finance_open_item_source_document_kind_invalid");
    }

    private static FinanceOpenItemSourceReference Reference() =>
        new(Guid.NewGuid(), "official-2026-09-v1", new string('a', 64));

    private static ApprovedFinanceOpenItemSourceRow Row(string amount) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "invoice-v1",
            FinanceOpenItemSourceKinds.OfficialInvoiceInstallmentDocument,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), amount, new DateOnly(2026, 10, 31));
}

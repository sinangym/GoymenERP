using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// W1-A (AI-D31, AI-D32): the Work inbox source over Finance's manual official sales invoices waiting for approval
/// (FNS-D17…FNS-D21, FNS-D39), on SYNTHETIC rows. Only PendingApproval records are listed; the recorder is the maker; the
/// one rule carries the stored payable amount.
/// </summary>
public sealed class FinanceOfficialSalesInvoiceWorkInboxSourceV1Tests : FinanceWorkInboxSourceContractTests
{
    private static readonly Guid Try = Guid.Parse("88888888-8888-4888-8888-888888888888");

    private protected override string ExpectedKind => "finance.official-sales-invoice.v1";

    private protected override string ExpectedPermission => FinanceOfficialSalesInvoicePermissionNamesV1.Approve;

    private protected override IWorkInboxSourceV1 Create(FinanceDbContext db, FinanceWorkInboxFence fence) =>
        new FinanceOfficialSalesInvoiceWorkInboxSourceV1(db, fence);

    private protected override Guid AddPending(FinanceDbContext db, Guid company, DateTimeOffset at, string maker)
    {
        var row = Invoice(company, at, maker, FinanceOfficialSalesInvoiceStatusV1.PendingApproval);
        db.OfficialSalesInvoices.Add(row);
        return row.Id;
    }

    private protected override void AddNotPending(FinanceDbContext db, Guid company, DateTimeOffset at) =>
        db.OfficialSalesInvoices.AddRange(
            Invoice(company, at, Maker, FinanceOfficialSalesInvoiceStatusV1.Approved),
            Invoice(company, at, Maker, FinanceOfficialSalesInvoiceStatusV1.Rejected),
            Invoice(company, at, Maker, FinanceOfficialSalesInvoiceStatusV1.Withdrawn));

    private protected override void AddInvalid(FinanceDbContext db, Guid company, DateTimeOffset at)
    {
        var row = Invoice(company, at, Maker, FinanceOfficialSalesInvoiceStatusV1.PendingApproval);
        row.PayableAmountText = "SENTETIK-not-a-number";
        db.OfficialSalesInvoices.Add(row);
    }

    [Fact]
    public async Task The_rule_carries_the_stored_payable_amount_the_label_is_the_official_number_and_the_link_is_the_approval_screen()
    {
        var invoice = Invoice(Company, Start, Maker, FinanceOfficialSalesInvoiceStatusV1.PendingApproval, "1250.5000");

        var result = await ListAsync(db => db.OfficialSalesInvoices.Add(invoice));

        var item = Assert.Single(result.Items!);
        Assert.Equal(invoice.OfficialNo, item.DocumentLabel);
        Assert.Equal("/finans/resmi-satis-faturasi-onaylari", item.Link);
        Assert.Equal(
            [new WorkApprovalRuleResultV1(FinanceOfficialSalesInvoiceWorkInboxSourceV1.ApprovalRequiredRule, null, "1250.5", null, null)],
            item.Rules);
        Assert.Contains(item.References, reference => reference.Role == "customer" && reference.RecordId == invoice.CustomerPartyId);
        Assert.Contains(item.References, reference => reference.Role == "currency" && reference.RecordId == Try);
        Assert.Contains(item.References, reference => reference.Role == "pdf-document" && reference.RecordId == invoice.PdfDocumentId);
    }

    private static FinanceOfficialSalesInvoiceRow Invoice(Guid company, DateTimeOffset at, string recordedBy,
        FinanceOfficialSalesInvoiceStatusV1 status, string amount = "100") =>
        new()
        {
            Id = Guid.NewGuid(),
            CompanyId = company,
            OfficialNo = "SENTETIK-" + Guid.NewGuid().ToString("N")[..8],
            Ettn = Guid.NewGuid().ToString("D"),
            InvoiceDate = new DateOnly(2026, 11, 20),
            CustomerPartyId = Guid.NewGuid(),
            CurrencyId = Try,
            PayableAmountText = amount,
            PayableFunctionalAmountText = amount,
            TaxBaseFunctionalAmountText = amount,
            VatFunctionalAmountText = "0",
            WithholdingFunctionalAmountText = "0",
            PdfDocumentId = Guid.NewGuid(),
            PdfDocumentVersionNo = 1,
            PdfSha256 = new string('a', 64),
            Status = (int)status,
            Version = status == FinanceOfficialSalesInvoiceStatusV1.PendingApproval ? 1 : 2,
            RecordedBy = recordedBy,
            RecordedAtUtc = at,
            CorrelationId = "SENTETIK-correlation",
        };
}

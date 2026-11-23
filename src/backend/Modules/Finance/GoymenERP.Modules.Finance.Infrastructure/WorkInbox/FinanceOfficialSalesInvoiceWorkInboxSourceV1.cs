using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32, YOL-D18): the manual official sales invoices (F-005 D1, FNS-D17…FNS-D21, FNS-D39) waiting for
/// approval, as Finance lists them for the Work inbox. A record waits while its status is PendingApproval (the approve and
/// reject commands accept only that status) from the moment it was recorded; there is no edit or resubmission (FNS-D20).
/// It is decided on the official sales invoice approval screen by a holder of
/// <c>finance.official-sales-invoices.approve</c> other than its recorder (the approve command refuses only the recorder:
/// <c>approver_must_differ_from_recorder</c>). The owner's authority answers only a company-wide grant, so scoped approvers
/// are not accepted. Every pending record of the company is returned, the caller's own included: Work hides a maker's own
/// item.
/// <para>
/// The one rule carries the stored payable amount as the actual value; Finance holds no threshold for this approval.
/// Finance stores the currency identity, not its code, so the unit is left empty and the currency is a reference.
/// </para>
/// </summary>
internal sealed class FinanceOfficialSalesInvoiceWorkInboxSourceV1(
    FinanceDbContext dbContext,
    FinanceWorkInboxFence fence) : IWorkInboxSourceV1
{
    public const string ModuleName = "finance";
    public const string KindName = "finance.official-sales-invoice.v1";
    public const string ScreenPath = "/finans/resmi-satis-faturasi-onaylari";

    /// <summary>Every manually recorded official sales invoice needs one approval by someone other than its recorder.</summary>
    public const string ApprovalRequiredRule = "finance.official-sales-invoice-approval-required";

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => FinanceOfficialSalesInvoicePermissionNamesV1.Approve;

    public Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken) =>
        FinanceWorkInboxRead.ListAsync(fence, context, ApproverPermission, maxCount, ReadAsync, Item, cancellationToken);

    private async Task<(List<FinanceOfficialSalesInvoiceRow> Rows, bool Lookup)> ReadAsync(
        Guid companyId, int maxCount, CancellationToken cancellationToken)
    {
        const int pending = (int)FinanceOfficialSalesInvoiceStatusV1.PendingApproval;
        var rows = await dbContext.OfficialSalesInvoices.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Status == pending)
            .OrderBy(row => row.RecordedAtUtc).ThenBy(row => row.Id)
            .Take(maxCount + 1)
            .ToListAsync(cancellationToken);
        return (rows, true);
    }

    private static WorkInboxItemV1? Item(FinanceOfficialSalesInvoiceRow row, bool _)
    {
        if (row.Id == Guid.Empty || row.CustomerPartyId == Guid.Empty || row.CurrencyId == Guid.Empty
            || row.PdfDocumentId == Guid.Empty || string.IsNullOrWhiteSpace(row.RecordedBy)
            || FinanceWorkInboxRead.Amount(row.PayableAmountText) is not { } amount)
        {
            return null;
        }

        return new WorkInboxItemV1(ModuleName, KindName, row.Id, FinanceWorkInboxRead.Label(row.OfficialNo, row.Id),
            row.RecordedAtUtc.ToUniversalTime(), ScreenPath, row.RecordedBy,
            [new WorkApprovalRuleResultV1(ApprovalRequiredRule, null, amount, null, null)],
            [
                FinanceWorkInboxRead.Reference("customer", row.CustomerPartyId),
                FinanceWorkInboxRead.Reference("currency", row.CurrencyId),
                FinanceWorkInboxRead.Reference("pdf-document", row.PdfDocumentId),
            ]);
    }
}

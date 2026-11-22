using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32, YOL-D18): the R47 manual outgoing bank payments waiting for approval, as Finance lists them for
/// the Work inbox. A payment waits while its status is Draft (the approve command accepts only a draft); it is approved on
/// the payment's own screen by a holder of <c>finance.manual-bank-payment.approve</c> other than the preparer (the inbox
/// decides nothing). Every draft of the company is returned, the caller's own included: Work hides a maker's own item.
/// <para>
/// Finance holds no approval matrix, threshold or limit for this payment (R47: every payment needs one approval), so the
/// one rule carries the stored amount as the actual value and nothing else. Finance stores the currency identity, not its
/// code, so the unit is left empty and the currency is carried as a reference.
/// </para>
/// </summary>
internal sealed class FinanceManualBankPaymentWorkInboxSourceV1(
    FinanceDbContext dbContext,
    FinanceWorkInboxFence fence) : IWorkInboxSourceV1
{
    public const string ModuleName = "finance";
    public const string KindName = "finance.manual-bank-payment.v1";
    public const string ScreenPath = "/finans/odemeler";

    /// <summary>Every manual outgoing bank payment needs one approval by someone other than its preparer (R47).</summary>
    public const string ApprovalRequiredRule = "finance.manual-bank-payment-approval-required";

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => FinancePermissionNames.ManualBankPaymentApprove;


    public async Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        cancellationToken.ThrowIfCancellationRequested();
        if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is { } refused)
        {
            return refused;
        }

        List<ManualBankPaymentRow> drafts;
        Dictionary<Guid, string> banks;
        try
        {
            drafts = await dbContext.ManualBankPayments.AsNoTracking()
                .Where(row => row.CompanyId == context.CompanyId && row.Status == ManualBankPaymentStatuses.Draft)
                .OrderBy(row => row.PreparedAtUtc).ThenBy(row => row.Id)
                .Take(maxCount + 1)
                .ToListAsync(cancellationToken);
            var bankIds = drafts.Take(maxCount).Select(row => row.TreasuryAccountId).Distinct().ToArray();
            banks = await dbContext.BankAccounts.AsNoTracking()
                .Where(row => row.CompanyId == context.CompanyId && bankIds.Contains(row.Id))
                .ToDictionaryAsync(row => row.Id, row => row.DisplayName, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await fence.CheckAsync(context, ApproverPermission, cancellationToken)
                ?? WorkInboxSourceResultV1.Unavailable();
        }

        if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }

        var items = new List<WorkInboxItemV1>(Math.Min(drafts.Count, maxCount));
        foreach (var row in drafts.Take(maxCount))
        {
            if (!TryItem(row, banks, out var item))
            {
                // A record Finance cannot read as its own invariants say: unavailable, never a partial list.
                return WorkInboxSourceResultV1.Unavailable();
            }

            items.Add(item);
        }

        return WorkInboxSourceResultV1.Found(context.CompanyId, items, drafts.Count > maxCount);
    }

    private static bool TryItem(ManualBankPaymentRow row, IReadOnlyDictionary<Guid, string> banks, out WorkInboxItemV1 item)
    {
        item = null!;
        if (row.Id == Guid.Empty || row.CurrencyId == Guid.Empty || row.PartyId == Guid.Empty
            || row.CompanyAccountId == Guid.Empty || row.TreasuryAccountId == Guid.Empty
            || string.IsNullOrWhiteSpace(row.PreparedBy)
            || !FinanceWorkInboxFence.TryAmount(row.AmountText, out var amount))
        {
            return false;
        }

        var references = new List<WorkApprovalReferenceV1>(5)
        {
            new("party", FinanceWorkInboxFence.ShortReference(row.PartyId), row.PartyId, null),
            new("company-account", FinanceWorkInboxFence.ShortReference(row.CompanyAccountId), row.CompanyAccountId, null),
            new("bank-account", BankLabel(row.TreasuryAccountId, banks), row.TreasuryAccountId, null),
            new("currency", FinanceWorkInboxFence.ShortReference(row.CurrencyId), row.CurrencyId, null),
        };
        if (row.LinkedDocumentId is { } document && document != Guid.Empty)
        {
            references.Add(new("document", FinanceWorkInboxFence.ShortReference(document), document, null));
        }

        // A draft carries no document number yet (it is reserved at the final record); the screens show the short id.
        var label = string.IsNullOrWhiteSpace(row.DocumentNumber)
            ? FinanceWorkInboxFence.ShortReference(row.Id)
            : row.DocumentNumber;
        if (label.Length > WorkInboxRulesV1.MaxDocumentLabelLength)
        {
            return false;
        }

        item = new WorkInboxItemV1(ModuleName, KindName, row.Id, label, row.PreparedAtUtc.ToUniversalTime(),
            FinanceWorkInboxFence.Link(ScreenPath, row.Id), row.PreparedBy,
            [new WorkApprovalRuleResultV1(ApprovalRequiredRule, null, WorkInboxRulesV1.Decimal(amount), null, null)],
            references);
        return true;
    }

    private static string BankLabel(Guid id, IReadOnlyDictionary<Guid, string> banks) =>
        banks.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name)
            && name.Length <= WorkInboxRulesV1.MaxReferenceLabelLength
            ? name
            : FinanceWorkInboxFence.ShortReference(id);
}

using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32, YOL-D18): the proposals to reverse a recorded manual bank payment (API-23) waiting for a
/// decision, as Finance lists them for the Work inbox. A proposal waits while its status is Pending (the approve and
/// reject commands accept only a pending proposal) from the moment it was proposed; it is never edited. It is decided on
/// the payment's own screen by a holder of <c>finance.manual-bank-payment.reverse.approve</c> other than the proposer (the
/// approve command refuses only the proposer: <c>finance_approver_is_proposer</c>). The owner's permission port answers
/// only a company-wide grant, so scoped approvers are not accepted. Every pending proposal of the company is returned, the
/// caller's own included: Work hides a maker's own item.
/// <para>
/// The one rule carries the reversed payment's stored amount as the actual value; no threshold exists. A proposal whose
/// payment Finance does not find breaks the owner's invariant (the approve command fails on it), so the answer is
/// Unavailable.
/// </para>
/// </summary>
internal sealed class FinanceManualBankPaymentReversalWorkInboxSourceV1(
    FinanceDbContext dbContext,
    FinanceWorkInboxFence fence) : IWorkInboxSourceV1
{
    public const string ModuleName = "finance";
    public const string KindName = "finance.manual-bank-payment-reversal.v1";

    /// <summary>The payment's own screen (<c>/finans/odemeler/{paymentId}</c>), where the reversal panel decides.</summary>
    public const string ScreenPath = "/finans/odemeler";

    /// <summary>Every reversal of a recorded manual bank payment needs one approval by someone other than its proposer.</summary>
    public const string ApprovalRequiredRule = "finance.manual-bank-payment-reversal-approval-required";

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => FinancePermissionNames.PaymentReverseApprove;

    public Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken) =>
        FinanceWorkInboxRead.ListAsync(fence, context, ApproverPermission, maxCount, ReadAsync, Item, cancellationToken);

    private async Task<(List<PaymentReversalProposalRow> Rows, Lookup Lookup)> ReadAsync(
        Guid companyId, int maxCount, CancellationToken cancellationToken)
    {
        var rows = await dbContext.PaymentReversalProposals.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Status == ReversalProposalStatuses.Pending)
            .OrderBy(row => row.ProposedAtUtc).ThenBy(row => row.Id)
            .Take(maxCount + 1)
            .ToListAsync(cancellationToken);
        var paymentIds = rows.Take(maxCount).Select(row => row.PaymentId).Distinct().ToArray();
        var payments = await dbContext.ManualBankPayments.AsNoTracking()
            .Where(row => row.CompanyId == companyId && paymentIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var bankIds = payments.Values.Select(row => row.TreasuryAccountId).Distinct().ToArray();
        var banks = await dbContext.BankAccounts.AsNoTracking()
            .Where(row => row.CompanyId == companyId && bankIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.DisplayName, cancellationToken);
        return (rows, new Lookup(payments, banks));
    }

    private static WorkInboxItemV1? Item(PaymentReversalProposalRow row, Lookup lookup)
    {
        if (row.Id == Guid.Empty || row.PaymentId == Guid.Empty || string.IsNullOrWhiteSpace(row.ProposedBy)
            || !lookup.Payments.TryGetValue(row.PaymentId, out var payment)
            || payment.PartyId == Guid.Empty || payment.TreasuryAccountId == Guid.Empty || payment.CurrencyId == Guid.Empty
            || FinanceWorkInboxRead.Amount(payment.AmountText) is not { } amount)
        {
            return null;
        }

        var paymentLink = FinanceWorkInboxFence.Link(ScreenPath, payment.Id);
        return new WorkInboxItemV1(ModuleName, KindName, row.Id, FinanceWorkInboxRead.Label(payment.DocumentNumber, row.Id),
            row.ProposedAtUtc.ToUniversalTime(), paymentLink, row.ProposedBy,
            [new WorkApprovalRuleResultV1(ApprovalRequiredRule, null, amount, null, null)],
            [
                FinanceWorkInboxRead.Reference("manual-bank-payment", payment.Id,
                    FinanceWorkInboxRead.Label(payment.DocumentNumber, payment.Id), paymentLink),
                FinanceWorkInboxRead.Reference("party", payment.PartyId),
                FinanceWorkInboxRead.Reference("bank-account", payment.TreasuryAccountId,
                    FinanceWorkInboxRead.Name(lookup.Banks.GetValueOrDefault(payment.TreasuryAccountId), payment.TreasuryAccountId)),
                FinanceWorkInboxRead.Reference("currency", payment.CurrencyId),
            ]);
    }

    private sealed record Lookup(
        IReadOnlyDictionary<Guid, ManualBankPaymentRow> Payments,
        IReadOnlyDictionary<Guid, string> Banks);
}

using System.Globalization;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32, YOL-D18): the proposed credit accounts (FIN-SC47, FNS-D87/FNS-D88/FNS-D93/FNS-D94) waiting for a
/// decision, as Finance lists them for the Work inbox. An account waits while its status is Proposed (the approve and
/// reject commands accept only that status) since it was proposed; a proposal is not edited. It is decided on the Work
/// purpose route by a holder of <c>finance.credits.approve</c> other than the proposer (the approve command refuses only the
/// proposer). The owner's permission port answers only a company-wide grant, so scoped approvers are not accepted. Every
/// proposed account of the company is returned, the caller's own included: Work hides a maker's own item.
/// <para>
/// The one rule carries the stored principal amount as the actual value; no threshold exists. The label is the stored
/// contract reference.
/// </para>
/// </summary>
internal sealed class FinanceCreditAccountWorkInboxSourceV1(
    FinanceDbContext dbContext,
    FinanceWorkInboxFence fence) : IWorkApprovalDecisionSourceV1
{
    public const string ModuleName = "finance";
    public const string KindName = "finance.credit-account.v1";
    public const string ScreenPath = "/finans/krediler";

    /// <summary>Every new credit account needs one approval by someone other than its proposer.</summary>
    public const string ApprovalRequiredRule = "finance.credit-account-approval-required";

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => ErpPermissions.FinanceCreditsApprove;

    public Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken) =>
        FinanceWorkInboxRead.ListAsync(fence, context, ApproverPermission, maxCount, ReadAsync, Item, cancellationToken);

    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId,
        CancellationToken cancellationToken)
    {
        if (itemId == Guid.Empty || await fence.CheckAsync(context, ApproverPermission, cancellationToken) is not null) return null;
        var row = await dbContext.CreditAccounts.AsNoTracking().SingleOrDefaultAsync(
            row => row.CompanyId == context.CompanyId && row.Id == itemId, cancellationToken);
        if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is not null || row is null
            || row.Version < 1 || row.Status is not (FinanceCreditAccountService.Proposed or FinanceCreditAccountService.Approved or FinanceCreditAccountService.Rejected)) return null;
        return Item(row, false) is { } item
            ? new(context.CompanyId, item, row.Version.ToString(CultureInfo.InvariantCulture), row.Status == FinanceCreditAccountService.Proposed) : null;
    }

    private async Task<(List<CreditAccountRow> Rows, bool Lookup)> ReadAsync(
        Guid companyId, int maxCount, CancellationToken cancellationToken)
    {
        var rows = await dbContext.CreditAccounts.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Status == FinanceCreditAccountService.Proposed)
            .OrderBy(row => row.ProposedAtUtc).ThenBy(row => row.Id)
            .Take(maxCount + 1)
            .ToListAsync(cancellationToken);
        return (rows, true);
    }

    private static WorkInboxItemV1? Item(CreditAccountRow row, bool _)
    {
        if (row.Id == Guid.Empty || row.CurrencyId == Guid.Empty || string.IsNullOrWhiteSpace(row.ProposedBy)
            || string.IsNullOrWhiteSpace(row.LenderName)
            || FinanceWorkInboxRead.Amount(row.PrincipalAmountText) is not { } principal)
        {
            return null;
        }

        var references = new List<WorkApprovalReferenceV1>(4)
        {
            new("lender", row.LenderName.Length <= WorkInboxRulesV1.MaxReferenceLabelLength
                ? row.LenderName
                : FinanceWorkInboxFence.ShortReference(row.Id), row.LenderPartyId is { } party && party != Guid.Empty ? party : null, null),
        };
        if (row.LenderCompanyPartyAccountId is { } account && account != Guid.Empty)
        {
            references.Add(FinanceWorkInboxRead.Reference("company-account", account));
        }

        references.Add(FinanceWorkInboxRead.Reference("currency", row.CurrencyId));
        return new WorkInboxItemV1(ModuleName, KindName, row.Id, FinanceWorkInboxRead.Label(row.ContractReference, row.Id),
            row.ProposedAtUtc.ToUniversalTime(), $"/onaylarim/{KindName}/{row.Id:D}", row.ProposedBy,
            [new WorkApprovalRuleResultV1(ApprovalRequiredRule, null, principal, null, null)],
            references);
    }
}

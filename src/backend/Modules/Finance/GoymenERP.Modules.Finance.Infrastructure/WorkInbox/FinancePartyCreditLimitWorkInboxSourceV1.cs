using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32, YOL-D18): the pending party credit limit proposals (API-85a, FNS-D27a) as Finance lists them for
/// the Work inbox. A proposal waits while its status is Proposed; it is decided on the credit limit screen by a holder of
/// <c>finance.credit-limits.approve</c> other than the proposer (the inbox decides nothing). Every pending proposal of the
/// company is returned, the caller's own included: Work hides a maker's own item.
/// <para>
/// The rule results come only from stored records: the proposed amount, the account's current approved limit and, when
/// both are in the same currency, their difference. Finance stores the currency identity, not its code, so the unit is
/// left empty and the currency is carried as a reference. No threshold exists (FNS-D56/FNS-D57: no threshold, no second
/// approval).
/// </para>
/// </summary>
internal sealed class FinancePartyCreditLimitWorkInboxSourceV1(
    FinanceDbContext dbContext,
    FinanceWorkInboxFence fence) : IWorkInboxSourceV1
{
    public const string ModuleName = "finance";
    public const string KindName = "finance.party-credit-limit.v1";
    public const string ScreenPath = "/finans/kredi-limitleri";

    /// <summary>A Set proposal on an account with an approved Set limit in the same currency.</summary>
    public const string ChangeRule = "finance.credit-limit-change";

    /// <summary>A Set proposal on an account with no approved limit (never set, or an approved Remove).</summary>
    public const string NewRule = "finance.credit-limit-new";

    /// <summary>A Set proposal whose currency differs from the current approved limit's (no difference is computed).</summary>
    public const string CurrencyChangeRule = "finance.credit-limit-currency-change";

    /// <summary>A Remove proposal: the account would have no limit.</summary>
    public const string RemovalRule = "finance.credit-limit-removal";

    /// <summary>The account's limit moved since the proposal was made; approval would be refused (credit_limit_revision_stale).</summary>
    public const string RevisionStaleRule = "finance.credit-limit-revision-stale";

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => ErpPermissions.FinanceCreditLimitsApprove;


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

        List<PartyCreditLimitRow> pending;
        List<PartyCreditLimitRow> effective;
        Dictionary<Guid, long> revisions;
        try
        {
            pending = await dbContext.PartyCreditLimits.AsNoTracking()
                .Where(row => row.CompanyId == context.CompanyId && row.Status == FinancePartyCreditLimitService.Proposed)
                .OrderBy(row => row.ProposedAtUtc).ThenBy(row => row.Id)
                .Take(maxCount + 1)
                .ToListAsync(cancellationToken);
            var accountIds = pending.Take(maxCount).Select(row => row.CompanyAccountId).Distinct().ToArray();
            effective = await dbContext.PartyCreditLimits.AsNoTracking()
                .Where(row => row.CompanyId == context.CompanyId && accountIds.Contains(row.CompanyAccountId)
                    && row.Status == FinancePartyCreditLimitService.Approved)
                .ToListAsync(cancellationToken);
            revisions = await dbContext.PartyCreditLimits.AsNoTracking()
                .Where(row => row.CompanyId == context.CompanyId && accountIds.Contains(row.CompanyAccountId)
                    && row.AccountRevision != null)
                .GroupBy(row => row.CompanyAccountId)
                .Select(group => new { CompanyAccountId = group.Key, Revision = group.Max(row => row.AccountRevision) })
                .ToDictionaryAsync(item => item.CompanyAccountId, item => item.Revision ?? 0, cancellationToken);
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

        var items = new List<WorkInboxItemV1>(Math.Min(pending.Count, maxCount));
        foreach (var row in pending.Take(maxCount))
        {
            var current = effective.Where(candidate => candidate.CompanyAccountId == row.CompanyAccountId).ToArray();
            if (current.Length > 1 || !TryItem(row, current.SingleOrDefault(), revisions.GetValueOrDefault(row.CompanyAccountId),
                    out var item))
            {
                // A record Finance cannot read as its own invariants say: unavailable, never a partial list.
                return WorkInboxSourceResultV1.Unavailable();
            }

            items.Add(item);
        }

        return WorkInboxSourceResultV1.Found(context.CompanyId, items, pending.Count > maxCount);
    }

    private static bool TryItem(PartyCreditLimitRow row, PartyCreditLimitRow? current, long revision,
        out WorkInboxItemV1 item)
    {
        item = null!;
        if (row.Id == Guid.Empty || row.CompanyAccountId == Guid.Empty || row.PartyId == Guid.Empty
            || string.IsNullOrWhiteSpace(row.ProposedBy))
        {
            return false;
        }

        var rules = new List<WorkApprovalRuleResultV1>(2);
        var references = new List<WorkApprovalReferenceV1>(4)
        {
            new("company-account", FinanceWorkInboxFence.ShortReference(row.CompanyAccountId), row.CompanyAccountId, null),
            new("party", FinanceWorkInboxFence.ShortReference(row.PartyId), row.PartyId, null),
        };
        var currentSet = current is { Kind: FinancePartyCreditLimitService.SetKind } ? current : null;
        decimal currentAmount = 0;
        if (currentSet is not null
            && (currentSet.CurrencyId is not { } currentCurrency || currentCurrency == Guid.Empty
                || !FinanceWorkInboxFence.TryAmount(currentSet.AmountText, out currentAmount)))
        {
            return false;
        }

        switch (row.Kind)
        {
            case FinancePartyCreditLimitService.SetKind:
                if (row.CurrencyId is not { } currency || currency == Guid.Empty
                    || !FinanceWorkInboxFence.TryAmount(row.AmountText, out var proposed))
                {
                    return false;
                }

                references.Add(new("currency", FinanceWorkInboxFence.ShortReference(currency), currency, null));
                if (currentSet is null)
                {
                    rules.Add(new(NewRule, null, WorkInboxRulesV1.Decimal(proposed), null, null));
                }
                else if (currentSet.CurrencyId == currency)
                {
                    rules.Add(new(ChangeRule, WorkInboxRulesV1.Decimal(currentAmount), WorkInboxRulesV1.Decimal(proposed),
                        WorkInboxRulesV1.Decimal(proposed - currentAmount), null));
                }
                else
                {
                    rules.Add(new(CurrencyChangeRule, null, WorkInboxRulesV1.Decimal(proposed), null, null));
                    references.Add(new("current-currency", FinanceWorkInboxFence.ShortReference(currentSet.CurrencyId!.Value),
                        currentSet.CurrencyId, null));
                }

                break;
            case FinancePartyCreditLimitService.RemoveKind:
                if (row.CurrencyId is not null || row.AmountText is not null)
                {
                    return false;
                }

                rules.Add(currentSet is null
                    ? new(RemovalRule, null, null, null, null)
                    : new(RemovalRule, WorkInboxRulesV1.Decimal(currentAmount), null, null, null));
                if (currentSet is not null)
                {
                    references.Add(new("current-currency", FinanceWorkInboxFence.ShortReference(currentSet.CurrencyId!.Value),
                        currentSet.CurrencyId, null));
                }

                break;
            default:
                return false;
        }

        if (row.BaseRevision != revision)
        {
            rules.Add(new(RevisionStaleRule, null, null, null, null));
        }

        item = new WorkInboxItemV1(ModuleName, KindName, row.Id, FinanceWorkInboxFence.ShortReference(row.CompanyAccountId),
            row.ProposedAtUtc.ToUniversalTime(), ScreenPath, row.ProposedBy, rules, references);
        return true;
    }
}

using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Inventory.Infrastructure.WorkInbox;

/// <summary>
/// WP6 (AI-D31, AI-D32, YOL-D41; SAT-D304, SAT-D372): the opening-stock cost submissions waiting for a decision, as Inventory
/// lists them for the Work inbox. A submission waits while it has no decision (the decision table allows exactly one) — the
/// state <c>DecideInventoryOpeningCostV1</c> accepts — and is decided by a company-wide holder of
/// <c>inventory.opening-cost.approve</c>. YOL-D41: no owner screen is built for it; the Work inbox decision (WP8) covers it,
/// so the item links to the inbox filtered to its kind and the explanation card carries the detail.
/// <para>
/// Every pending submission of the company is returned, the submitter's own included: Work hides the owner's "may not decide"
/// set, which is the owner's rule word for word (<c>InventoryStore.OpeningCosts</c>, <c>opening_cost_self_approval</c>): the
/// submitter never decides their own submission. No one else is excluded (YOL-D23). The rule's number is the stored unit
/// cost per stock kilogram in the company's base currency (the ledger's unit; no currency code is stored on the row, so the
/// unit is left empty and the sentence names it). The free-text reason is not copied. The scope is the location (D11); the
/// decision accepts only a company-wide grant (YOL-D36 pattern), so a scoped holder is not asked.
/// </para>
/// </summary>
internal sealed class InventoryOpeningCostWorkInboxSourceV1 : IWorkInboxSourceV1
{
    public const string ModuleName = "inventory";
    public const string KindName = "inventory.opening-cost.v1";

    /// <summary>An opening cost was submitted: actual = the unit cost per stock kilogram (base currency).</summary>
    public const string SubmittedRule = "inventory.opening-cost-submitted";

    private readonly InventoryWorkInboxRows rows;
    private readonly InventoryWorkInboxFence fence;

    public InventoryOpeningCostWorkInboxSourceV1(Persistence.InventoryDbContext dbContext, InventoryWorkInboxFence fence)
        : this(new InventoryWorkInboxDbRows(dbContext), fence)
    {
    }

    internal InventoryOpeningCostWorkInboxSourceV1(InventoryWorkInboxRows rows, InventoryWorkInboxFence fence)
    {
        this.rows = rows;
        this.fence = fence;
    }

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => InventoryPermissionManifestV1.OpeningCostApprove;

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

        var companyId = context.CompanyId;
        List<Pending> pending;
        try
        {
            var read = await rows.ListAsync(
                (from submission in rows.OpeningCosts
                 where submission.CompanyId == companyId
                     && !rows.OpeningCostDecisions.Any(decision => decision.CompanyId == companyId
                         && decision.OpeningCostId == submission.Id)
                 orderby submission.RecordedAtUtc, submission.Id
                 select new
                 {
                     submission.Id,
                     submission.LocationId,
                     submission.ProductId,
                     submission.UnitCostPerKg,
                     submission.RecordedBy,
                     submission.RecordedAtUtc,
                 })
                .Take(maxCount + 1),
                cancellationToken);
            pending = [.. read.Select(row => new Pending(row.Id, row.LocationId, row.ProductId, row.UnitCostPerKg,
                row.RecordedBy, row.RecordedAtUtc))];
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await fence.UnavailableAsync(context, ApproverPermission, cancellationToken);
        }

        if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }

        var items = new List<WorkInboxItemV1>(Math.Min(pending.Count, maxCount));
        foreach (var row in pending.Take(maxCount))
        {
            if (row.Id == Guid.Empty || row.LocationId == Guid.Empty || row.ProductId == Guid.Empty
                || row.UnitCostPerKg <= 0m || string.IsNullOrWhiteSpace(row.RecordedBy))
            {
                // A record Inventory cannot read as its own invariants say: unavailable, never a partial list.
                return WorkInboxSourceResultV1.Unavailable();
            }

            var label = InventoryWorkInboxFence.Id(row.Id);
            items.Add(new WorkInboxItemV1(ModuleName, KindName, row.Id, label, row.RecordedAtUtc.ToUniversalTime(),
                InventoryWorkInboxFence.WorkInboxKindLink(KindName), row.RecordedBy.Trim(),
                [new WorkApprovalRuleResultV1(SubmittedRule, null, WorkInboxRulesV1.Decimal(row.UnitCostPerKg), null, null)],
                [
                    new WorkApprovalReferenceV1("product", InventoryWorkInboxFence.Id(row.ProductId), row.ProductId, null),
                    new WorkApprovalReferenceV1("location", InventoryWorkInboxFence.Id(row.LocationId), row.LocationId, null),
                ],
                null,
                new WorkInboxItemScopeV1(null, row.LocationId, null)));
        }

        return WorkInboxSourceResultV1.Found(companyId, items, pending.Count > maxCount);
    }

    private sealed record Pending(Guid Id, Guid LocationId, Guid ProductId, decimal UnitCostPerKg, string RecordedBy,
        DateTimeOffset RecordedAtUtc);
}

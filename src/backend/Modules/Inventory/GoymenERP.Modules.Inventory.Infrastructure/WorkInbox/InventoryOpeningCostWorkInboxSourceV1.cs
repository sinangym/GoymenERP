using GoymenERP.BuildingBlocks.Work;
using GoymenERP.BuildingBlocks.Security;
using System.Globalization;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Inventory.Infrastructure.WorkInbox;

/// <summary>
/// WP5 (YOL-D41, AI-D32; SAT-D304, SAT-D372): the opening-stock cost submissions waiting for a decision, as Inventory lists
/// them for the Work inbox. A submission waits while it has no decision (the owner allows exactly one per submission) since
/// it was recorded. It has no screen of its own (YOL-D41): the decision is given from the inbox (WP8) and the detail is the
/// explanation card, so the link opens the inbox's own approval detail (<c>/onaylarim/{kind}/{id}</c>).
/// <para>
/// The decision needs <c>inventory.opening-cost.approve</c> company-wide (the owner's command measures it that way, so a
/// scoped holder is not asked). The owner refuses only the submitter, either way (SAT-D304 (1): giren ≠ onaylayan), so the
/// submitter is the maker and no one else is excluded (YOL-D23). Every pending submission of the company is returned, the
/// caller's own included. The unit cost is a cost figure: it is not copied into the inbox (cost visibility is its own
/// permission); the card names the position (location and product) and why it waits. The free-text reason is not copied.
/// </para>
/// </summary>
internal sealed class InventoryOpeningCostWorkInboxSourceV1 : IWorkApprovalDecisionSourceV1
{
    public const string ModuleName = "inventory";
    public const string KindName = "inventory.opening-cost.v1";

    /// <summary>The Work inbox's own approval detail route (no owner screen exists, YOL-D41).</summary>
    public const string InboxDetailPath = "/onaylarim";

    /// <summary>An opening unit cost was entered for a position and waits for another person's decision (SAT-D304).</summary>
    public const string ApprovalRequiredRule = "inventory.opening-cost-approval-required";

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
                rows.OpeningCosts
                    .Where(submission => submission.CompanyId == companyId
                        && !rows.OpeningCostDecisions.Any(decision => decision.CompanyId == companyId
                            && decision.OpeningCostId == submission.Id))
                    .OrderBy(submission => submission.RecordedAtUtc).ThenBy(submission => submission.Id)
                    .Select(submission => new
                    {
                        submission.Id,
                        submission.LocationId,
                        submission.ProductId,
                        submission.RecordedBy,
                        submission.RecordedAtUtc,
                    })
                    .Take(maxCount + 1),
                cancellationToken);
            pending = [.. read.Select(row => new Pending(row.Id, row.LocationId, row.ProductId, row.RecordedBy, row.RecordedAtUtc))];
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
                || string.IsNullOrWhiteSpace(row.RecordedBy))
            {
                // A record Inventory cannot read as its own invariants say: unavailable, never a partial list.
                return WorkInboxSourceResultV1.Unavailable();
            }

            items.Add(new WorkInboxItemV1(ModuleName, KindName, row.Id, InventoryWorkInboxFence.Id(row.Id)[..8],
                row.RecordedAtUtc.ToUniversalTime(), InboxDetailPath + "/" + KindName + "/" + InventoryWorkInboxFence.Id(row.Id),
                row.RecordedBy.Trim(),
                [new WorkApprovalRuleResultV1(ApprovalRequiredRule, null, null, null, null)],
                [
                    new WorkApprovalReferenceV1("location", InventoryWorkInboxFence.Id(row.LocationId)[..8], row.LocationId, null),
                    new WorkApprovalReferenceV1("product", InventoryWorkInboxFence.Id(row.ProductId)[..8], row.ProductId, null),
                ],
                null,
                new WorkInboxItemScopeV1(null, row.LocationId, null)));
        }

        return WorkInboxSourceResultV1.Found(companyId, items, pending.Count > maxCount);
    }

    private sealed record Pending(Guid Id, Guid LocationId, Guid ProductId, string RecordedBy, DateTimeOffset RecordedAtUtc);

    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId,
        CancellationToken cancellationToken)
    {
        if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is not null) return null;
        var costVisible = await fence.CheckAsync(context, ErpPermissions.InventoryAverageCostRead, cancellationToken) is null;
        var found = await rows.ListAsync(rows.OpeningCosts.Where(row => row.CompanyId == context.CompanyId && row.Id == itemId)
            .Select(row => new { row.Id, row.LocationId, row.ProductId, row.RecordedBy, row.RecordedAtUtc,
                Cost = costVisible ? (decimal?)row.UnitCostPerKg : null,
                Pending = !rows.OpeningCostDecisions.Any(decision => decision.CompanyId == context.CompanyId && decision.OpeningCostId == row.Id) }), cancellationToken);
        if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is not null) return null;
        var value = found.SingleOrDefault();
        if (value is null) return null;
        costVisible = costVisible && await fence.CheckAsync(context, ErpPermissions.InventoryAverageCostRead, cancellationToken) is null;
        var rules = new List<WorkApprovalRuleResultV1> { new(ApprovalRequiredRule, null, null, null, null) };
        if (costVisible && value.Cost is { } cost)
            rules.Add(new("inventory.opening-cost-unit-cost", null, cost.ToString("0.############################", CultureInfo.InvariantCulture), null, null));
        var item = new WorkInboxItemV1(ModuleName, KindName, value.Id, InventoryWorkInboxFence.Id(value.Id)[..8],
            value.RecordedAtUtc.ToUniversalTime(), InboxDetailPath + "/" + KindName + "/" + InventoryWorkInboxFence.Id(value.Id), value.RecordedBy,
            rules,
            [new("location", InventoryWorkInboxFence.Id(value.LocationId)[..8], value.LocationId, null),
             new("product", InventoryWorkInboxFence.Id(value.ProductId)[..8], value.ProductId, null)],
            null, new(null, value.LocationId, null));
        // Owner submissions are append-only; the exact submission ID is already the decision's immutable CAS key.
        return new(context.CompanyId, item, "submission/" + value.Id.ToString("D"), value.Pending);
    }
}

using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32, YOL-D18): the purchase orders waiting for approval, as Purchasing lists them for the Work inbox. An
/// order waits while it is a Draft (<c>ApprovePurchaseOrderV1</c> accepts only a Draft, ALS-D3); it is approved on the order's
/// own screen by a holder of <c>purchasing.orders.approve</c> other than its preparer (the inbox decides nothing). Every draft
/// of the company is returned, the caller's own included: Work hides a maker's own item.
/// <para>
/// Purchasing stores no approval matrix or amount threshold on an order (API-96 is A2a-5's, not built), so the rules carry no
/// numbers: every order needs one approval, and an order reopened by a supplier revision (ALS-D115) needs it again. The order
/// has no submission step: it waits from its creation, or from its latest supplier revision when it was reopened. The preparer
/// is the one the approve command checks (the revision's preparer, else the creator).
/// </para>
/// </summary>
internal sealed class PurchaseOrderWorkInboxSourceV1(
    PurchasingDbContext dbContext,
    PurchasingWorkInboxFence fence) : IWorkInboxSourceV1
{
    public const string ModuleName = "purchasing";
    public const string KindName = "purchasing.order.v1";

    /// <summary>Every purchase order needs one approval by someone other than its preparer (ALS-D3).</summary>
    public const string ApprovalRequiredRule = "purchasing.order-approval-required";

    /// <summary>The order was approved, then reopened as a draft by a supplier revision and waits for approval again (ALS-D115).</summary>
    public const string SupplierRevisedRule = "purchasing.order-supplier-revised";

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => PurchasingPermissionManifestV1.OrdersApprove;

    public WorkApprovalDurationClassV1 DurationClass => WorkApprovalDurationClassV1.BusinessDays;

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

        List<Pending> pending;
        try
        {
            var draft = (int)PurchaseOrderStatus.Draft;
            var drafts = await dbContext.Orders.AsNoTracking()
                .Where(row => row.CompanyId == context.CompanyId && row.Status == draft)
                .ToListAsync(cancellationToken);

            // The waiting time of a reopened order lives in its revision history, so the order is taken after it is known.
            pending = drafts.Select(Read).ToList();
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

        var ordered = pending.OrderBy(item => item.WaitingSinceUtc).ThenBy(item => item.Row.Id).ToList();
        var items = new List<WorkInboxItemV1>(Math.Min(ordered.Count, maxCount));
        foreach (var entry in ordered.Take(maxCount))
        {
            if (!TryItem(context, entry, out var item))
            {
                // A record Purchasing cannot read as its own invariants say: unavailable, never a partial list.
                return WorkInboxSourceResultV1.Unavailable();
            }

            items.Add(item);
        }

        return WorkInboxSourceResultV1.Found(context.CompanyId, items, ordered.Count > maxCount);
    }

    private static Pending Read(PurchaseOrderRow row)
    {
        // An inconsistent history throws (the store's own rule) and makes the whole read unavailable.
        var history = PurchaseSupplierRevisionHistory.Read(row);
        var waitingSince = history is null ? row.CreatedAtUtc : history.Revisions[^1].RecordedAtUtc;
        return new(row, waitingSince.ToUniversalTime(), row.CurrentPreparedBy ?? row.CreatedBy, history is not null);
    }

    private static bool TryItem(WorkInboxSourceContextV1 context, Pending entry, out WorkInboxItemV1 item)
    {
        item = null!;
        var row = entry.Row;
        if (row.Id == Guid.Empty || row.CompanyId != context.CompanyId || row.SupplierPartyId == Guid.Empty
            || string.IsNullOrWhiteSpace(entry.PreparedBy) || row.SourceRequestId == Guid.Empty)
        {
            return false;
        }

        var label = PurchasingWorkInboxFence.OrderLabel(row.Id, row.DocumentNumber);
        if (label.Length > WorkInboxRulesV1.MaxDocumentLabelLength)
        {
            return false;
        }

        var rules = new List<WorkApprovalRuleResultV1>(2) { new(ApprovalRequiredRule, null, null, null, null) };
        if (entry.Revised)
        {
            rules.Add(new(SupplierRevisedRule, null, null, null, null));
        }

        var references = new List<WorkApprovalReferenceV1>(2)
        {
            new("supplier", PurchasingWorkInboxFence.ShortReference(row.SupplierPartyId), row.SupplierPartyId, null),
        };
        if (row.SourceRequestId is { } request)
        {
            references.Add(new("purchase-request", PurchasingWorkInboxFence.ShortReference(request), request,
                PurchasingWorkInboxFence.Link(PurchasingWorkInboxFence.RequestScreenPath, request)));
        }

        item = new WorkInboxItemV1(ModuleName, KindName, row.Id, label, entry.WaitingSinceUtc,
            PurchasingWorkInboxFence.Link(PurchasingWorkInboxFence.OrderScreenPath, row.Id), entry.PreparedBy,
            rules, references);
        return true;
    }

    private sealed record Pending(PurchaseOrderRow Row, DateTimeOffset WaitingSinceUtc, string PreparedBy, bool Revised);
}

using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Inventory.Infrastructure.WorkInbox;

/// <summary>
/// WP6 (AI-D31, AI-D32, DALGA_PLANI_v2 1.4; KLN-D23 (c), KLN-D92, KLN-D97, KLN-D108): the transit loss requests waiting for a
/// decision, as Inventory lists them for the Work inbox. A numbered loss request waits while it has neither a decision nor a
/// withdrawal — exactly the set the owner's candidate search lists for the <c>loss-decision</c> purpose — and is decided on the
/// transfer loss approval screen by a company-wide holder of <c>inventory.stock-transfer.loss-approve</c>
/// (<c>DecideInventoryTransferLossV1</c>; the inbox decides nothing). It waits since it was recorded; a new request after a
/// rejection is a new number and waits anew.
/// <para>
/// Every pending request of the company is returned, the caller's own included: Work hides the owner's "may not decide" set,
/// which is the owner's rule word for word (<c>DecideLossAsync</c>): the requester never approves or rejects their own loss
/// request, a renewed one included. No one else is excluded (YOL-D23). The rule says how many kilograms the dispatch put in
/// transit on the line (the stored dispatch kilograms, never recomputed) and, for a request numbered above one, that an
/// earlier request was rejected or withdrawn. The request's free-text explanation is not copied. The item's scope is the
/// destination location (D11); the owner's decision accepts only a company-wide grant, so a scoped holder is not asked.
/// </para>
/// </summary>
internal sealed partial class InventoryTransferLossWorkInboxSourceV1 : IWorkApprovalDecisionSourceV1
{
    public const string ModuleName = "inventory";
    public const string KindName = "inventory.transfer-loss.v1";

    /// <summary>A transit loss was requested for the line: actual = the kilograms its dispatch put in transit.</summary>
    public const string LossRequestRule = "inventory.transfer-loss-request";

    /// <summary>The request follows a rejected or withdrawn earlier request of the same line (request number above one).</summary>
    public const string RenewedRequestRule = "inventory.transfer-loss-renewed";

    private const int FirstRequestNumber = 1;
    private const string KilogramUnit = "kg";

    private readonly InventoryWorkInboxRows rows;
    private readonly InventoryWorkInboxFence fence;

    public InventoryTransferLossWorkInboxSourceV1(Persistence.InventoryDbContext dbContext, InventoryWorkInboxFence fence)
        : this(new InventoryWorkInboxDbRows(dbContext), fence)
    {
    }

    internal InventoryTransferLossWorkInboxSourceV1(InventoryWorkInboxRows rows, InventoryWorkInboxFence fence)
    {
        this.rows = rows;
        this.fence = fence;
    }

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => InventoryPermissionManifestV1.StockTransferLossApprove;

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
                (from request in rows.TransferLossRequests
                 join transfer in rows.Transfers
                     on new { request.CompanyId, Id = request.TransferId } equals new { transfer.CompanyId, transfer.Id }
                 join line in rows.TransferLines
                     on new { request.CompanyId, request.TransferId, request.LineId }
                     equals new { line.CompanyId, line.TransferId, line.LineId }
                 join label in rows.Labels on new { line.CompanyId, Id = line.LabelId } equals new { label.CompanyId, label.Id }
                 where request.CompanyId == companyId && transfer.CompanyId == companyId
                     && !rows.TransferLossDecisions.Any(decision => decision.CompanyId == companyId
                         && decision.TransferId == request.TransferId && decision.LineId == request.LineId
                         && decision.RequestNumber == request.RequestNumber)
                     && !rows.TransferLossWithdrawals.Any(withdrawal => withdrawal.CompanyId == companyId
                         && withdrawal.TransferId == request.TransferId && withdrawal.LineId == request.LineId
                         && withdrawal.RequestNumber == request.RequestNumber)
                 orderby request.RecordedAtUtc, request.TransferId, request.LineId
                 select new
                 {
                     request.TransferId,
                     request.LineId,
                     request.RequestNumber,
                     transfer.DestinationLocationId,
                     line.LabelId,
                     LabelCode = label.Code,
                     DispatchedKg = rows.TransferDispatchLines
                         .Where(dispatch => dispatch.CompanyId == companyId && dispatch.TransferId == request.TransferId
                             && dispatch.LineId == request.LineId)
                         .Select(dispatch => (decimal?)dispatch.ActualKg)
                         .FirstOrDefault(),
                     request.RecordedBy,
                     request.RecordedAtUtc,
                 })
                .Take(maxCount + 1),
                cancellationToken);
            pending = [.. read.Select(row => new Pending(row.TransferId, row.LineId, row.RequestNumber,
                row.DestinationLocationId, row.LabelId, row.LabelCode, row.DispatchedKg, row.RecordedBy, row.RecordedAtUtc))];
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await fence.UnavailableAsync(context, ApproverPermission, cancellationToken);
        }

        if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }

        if (pending.Select(row => (row.TransferId, row.LineId)).Distinct().Count() != pending.Count)
        {
            // A line waits for one loss request at a time (a new number follows a closed one): two answers break the owner's rule.
            return WorkInboxSourceResultV1.Unavailable();
        }

        var items = new List<WorkInboxItemV1>(Math.Min(pending.Count, maxCount));
        foreach (var row in pending.Take(maxCount))
        {
            if (!TryItem(row, out var item))
            {
                // A record Inventory cannot read as its own invariants say: unavailable, never a partial list.
                return WorkInboxSourceResultV1.Unavailable();
            }

            items.Add(item);
        }

        return WorkInboxSourceResultV1.Found(companyId, items, pending.Count > maxCount);
    }

    private static bool TryItem(Pending row, out WorkInboxItemV1 item)
    {
        item = null!;
        if (row.TransferId == Guid.Empty || row.LineId == Guid.Empty || row.LabelId == Guid.Empty
            || row.DestinationLocationId == Guid.Empty || row.RequestNumber < FirstRequestNumber
            || string.IsNullOrWhiteSpace(row.RecordedBy) || string.IsNullOrWhiteSpace(row.LabelCode)
            || row.LabelCode.Length > WorkInboxRulesV1.MaxDocumentLabelLength || row.LabelCode.Any(char.IsControl)
            || row.DispatchedKg is { } negative && negative < 0m)
        {
            return false;
        }

        // A loss is requested only for a dispatched line; without its dispatch kilograms the rule says so without a number.
        var rules = new List<WorkApprovalRuleResultV1>(2)
        {
            row.DispatchedKg is { } dispatched
                ? new(LossRequestRule, null, WorkInboxRulesV1.Decimal(dispatched), null, KilogramUnit)
                : new(LossRequestRule, null, null, null, null),
        };
        if (row.RequestNumber > FirstRequestNumber)
        {
            rules.Add(new(RenewedRequestRule, null, null, null, null));
        }

        var itemId = InventoryWorkInboxFence.TransferLineItemId(KindName, row.TransferId, row.LineId);
        item = new WorkInboxItemV1(ModuleName, KindName, itemId, row.LabelCode,
            row.RecordedAtUtc.ToUniversalTime(), "/onaylarim/" + KindName + "/" + InventoryWorkInboxFence.Id(itemId), row.RecordedBy.Trim(), rules,
            [
                new WorkApprovalReferenceV1("inventory-label", row.LabelCode, row.LabelId,
                    InventoryWorkInboxFence.LabelLink(row.LabelId)),
                new WorkApprovalReferenceV1("inventory-transfer", InventoryWorkInboxFence.Id(row.TransferId), row.TransferId,
                    InventoryWorkInboxFence.TransferLossApprovalPath),
            ],
            null,
            new WorkInboxItemScopeV1(null, row.DestinationLocationId, null));
        return true;
    }

    private sealed record Pending(
        Guid TransferId,
        Guid LineId,
        int RequestNumber,
        Guid DestinationLocationId,
        Guid LabelId,
        string LabelCode,
        decimal? DispatchedKg,
        string RecordedBy,
        DateTimeOffset RecordedAtUtc);
}

using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.GoodsReceipts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32, YOL-D18): the bulk goods receipts waiting for a kg variance decision (ALC-S16, KLN-D45), company-wide,
/// as Purchasing lists them for the Work inbox. A batch waits while its status is PendingApproval — exactly what
/// <c>ApprovePurchaseGoodsReceiptBatchV1</c> accepts. Its purpose screen invokes the native decision command for a holder of
/// <c>purchasing.goods-receipts.approve-variance</c> (the permission the decision command measures, KLN-D45; the separate
/// overdelivery name is not this decision's) other than its preparer. Every pending batch of the company is returned, the
/// caller's own included: Work hides a maker's own item.
/// <para>
/// The rules come from the submission snapshot stored on the batch: the outcome (outside the Ayarlar tolerance, with the
/// tolerance percent in force as the threshold; or no tolerance set), and the kg comparison — the received total as the actual
/// value and its signed difference from the expected kg (the difference the domain's <see cref="GoodsReceiptKgVariance"/>
/// compared). A batch carries no document number of its own: the label is its order's.
/// </para>
/// </summary>
internal sealed class PurchaseGoodsReceiptBatchWorkInboxSourceV1(
    PurchasingDbContext dbContext,
    PurchasingWorkInboxFence fence) : IWorkApprovalDecisionSourceV1
{
    public const string ModuleName = "purchasing";
    public const string KindName = "purchasing.goods-receipt-batch.v1";

    /// <summary>The kg difference is outside the Ayarlar tolerance in force at submission.</summary>
    public const string OutsideToleranceRule = "purchasing.goods-receipt-kg-outside-tolerance";

    /// <summary>No kg tolerance was set in Ayarlar at submission: every difference waits for an approver (KLN-D45).</summary>
    public const string ToleranceNotSetRule = "purchasing.goods-receipt-kg-tolerance-not-set";

    /// <summary>A pending batch whose stored outcome names no difference (not expected): it still waits for the decision.</summary>
    public const string ApprovalRequiredRule = "purchasing.goods-receipt-batch-approval-required";

    /// <summary>The received kg total against the expected kg of the order line at submission.</summary>
    public const string KgDifferenceRule = "purchasing.goods-receipt-kg-difference";

    /// <summary>The unit code of the kg comparison.</summary>
    public const string KilogramUnit = "kg";

    /// <summary>The order reference plus at most this many label references (the contract's reference limit).</summary>
    private const int MaxLabelReferences = WorkInboxRulesV1.MaxReferencesPerItem - 1;

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => PurchasingPermissionManifestV1.GoodsReceiptsApproveVariance;


    /// <summary>Approval-purpose read only: no order browsing or price permission is granted.
    /// Terminal batches remain available so their native immutable receipt can be replayed.</summary>
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (itemId == Guid.Empty || await fence.CheckAsync(context, ApproverPermission, cancellationToken) is not null) return null;
        var row = await dbContext.GoodsReceiptBatches.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.CompanyId == context.CompanyId && candidate.BatchId == itemId, cancellationToken);
        if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is not null || row is null) return null;
        var orders = await dbContext.Orders.AsNoTracking().Where(candidate => candidate.CompanyId == context.CompanyId && candidate.Id == row.OrderId)
            .ToDictionaryAsync(candidate => candidate.Id, candidate => candidate.DocumentNumber, cancellationToken);
        if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is not null) return null;
        var labels = await dbContext.GoodsReceiptBatchLabels.AsNoTracking()
            .Where(candidate => candidate.CompanyId == context.CompanyId && candidate.BatchId == itemId)
            .OrderBy(candidate => candidate.LineNo).ToListAsync(cancellationToken);
        if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is not null) return null;
        // Reuse the native read projection for label order, stored kg and piece facts; no second calculator.
        _ = PurchaseGoodsReceiptBatchStore.Restore(row, context.CompanyId, labels);
        var pending = row.Status == (int)PurchaseGoodsReceiptBatchStatus.PendingApproval;
        // Native submission may already post an inside-tolerance/piece batch at version 1
        // without a separate decision. Its terminal refusal still belongs to the owner.
        var postedAtSubmission = row.Status == (int)PurchaseGoodsReceiptBatchStatus.Posted && row.Version == 1;
        if (row.Version < 1 || !Enum.IsDefined((PurchaseGoodsReceiptBatchStatus)row.Status)
            || (pending || postedAtSubmission ? row.DecidedBy is not null || row.DecidedAtUtc is not null || row.DecisionReason is not null
                : row.Version < 2 || string.IsNullOrWhiteSpace(row.DecidedBy) || row.DecidedAtUtc is null || row.DecidedBy == row.PreparedBy
                  || (row.Status == (int)PurchaseGoodsReceiptBatchStatus.Rejected ? string.IsNullOrWhiteSpace(row.DecisionReason) : row.DecisionReason is not null))
            || !TryItem(context, row, orders, labels, out var item))
            throw new InvalidOperationException("Mal kabul partisi karar kaynağı geçersiz.");
        return new(context.CompanyId, item, row.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), pending);
    }

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

        List<PurchaseGoodsReceiptBatchRow> batches;
        Dictionary<Guid, string?> orderNumbers;
        ILookup<Guid, PurchaseGoodsReceiptBatchLabelRow> labels;
        try
        {
            var pendingStatus = (int)PurchaseGoodsReceiptBatchStatus.PendingApproval;
            batches = await dbContext.GoodsReceiptBatches.AsNoTracking()
                .Where(row => row.CompanyId == context.CompanyId && row.Status == pendingStatus)
                .OrderBy(row => row.PreparedAtUtc).ThenBy(row => row.BatchId)
                .Take(maxCount + 1)
                .ToListAsync(cancellationToken);
            var shown = batches.Take(maxCount).ToArray();
            var orderIds = shown.Select(row => row.OrderId).Distinct().ToArray();
            var batchIds = shown.Select(row => row.BatchId).ToArray();
            orderNumbers = await dbContext.Orders.AsNoTracking()
                .Where(row => row.CompanyId == context.CompanyId && orderIds.Contains(row.Id))
                .ToDictionaryAsync(row => row.Id, row => row.DocumentNumber, cancellationToken);
            labels = (await dbContext.GoodsReceiptBatchLabels.AsNoTracking()
                    .Where(row => row.CompanyId == context.CompanyId && batchIds.Contains(row.BatchId))
                    .ToListAsync(cancellationToken))
                .OrderBy(row => row.LineNo)
                .ToLookup(row => row.BatchId);
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

        var items = new List<WorkInboxItemV1>(Math.Min(batches.Count, maxCount));
        foreach (var row in batches.Take(maxCount))
        {
            if (!TryItem(context, row, orderNumbers, labels[row.BatchId], out var item))
            {
                return WorkInboxSourceResultV1.Unavailable();
            }

            items.Add(item);
        }

        return WorkInboxSourceResultV1.Found(context.CompanyId, items, batches.Count > maxCount);
    }

    private static bool TryItem(WorkInboxSourceContextV1 context, PurchaseGoodsReceiptBatchRow row,
        IReadOnlyDictionary<Guid, string?> orderNumbers, IEnumerable<PurchaseGoodsReceiptBatchLabelRow> labels,
        out WorkInboxItemV1 item)
    {
        item = null!;
        if (row.BatchId == Guid.Empty || row.OrderId == Guid.Empty || row.CompanyId != context.CompanyId
            || string.IsNullOrWhiteSpace(row.PreparedBy) || !orderNumbers.TryGetValue(row.OrderId, out var orderNumber))
        {
            return false;
        }

        var label = PurchasingWorkInboxFence.OrderLabel(row.OrderId, orderNumber);
        if (label.Length > WorkInboxRulesV1.MaxDocumentLabelLength)
        {
            return false;
        }

        var outcome = (GoodsReceiptKgVarianceOutcome)row.VarianceOutcome;
        WorkApprovalRuleResultV1 reason;
        switch (outcome)
        {
            case GoodsReceiptKgVarianceOutcome.OutsideTolerance when row.TolerancePercent is { } tolerance:
                reason = new(OutsideToleranceRule, WorkInboxRulesV1.Decimal(tolerance), null, null, "%");
                break;
            case GoodsReceiptKgVarianceOutcome.ToleranceNotSet when row.TolerancePercent is null:
                reason = new(ToleranceNotSetRule, null, null, null, null);
                break;
            case GoodsReceiptKgVarianceOutcome.Exact or GoodsReceiptKgVarianceOutcome.WithinTolerance:
                reason = new(ApprovalRequiredRule, null, null, null, null);
                break;
            default:
                return false;
        }

        var orderLink = PurchasingWorkInboxFence.Link(PurchasingWorkInboxFence.OrderScreenPath, row.OrderId);
        var references = new List<WorkApprovalReferenceV1>(WorkInboxRulesV1.MaxReferencesPerItem)
        {
            new("purchase-order", label, row.OrderId, orderLink),
        };
        foreach (var labelRow in labels.Where(labelRow => !labelRow.Released).Take(MaxLabelReferences))
        {
            if (string.IsNullOrWhiteSpace(labelRow.LabelCode) || labelRow.LabelId == Guid.Empty
                || labelRow.LabelCode.Length > WorkInboxRulesV1.MaxReferenceLabelLength)
            {
                return false;
            }

            references.Add(new("goods-receipt-label", labelRow.LabelCode, labelRow.LabelId, null));
        }

        item = new WorkInboxItemV1(ModuleName, KindName, row.BatchId, label, row.PreparedAtUtc.ToUniversalTime(),
            "/onaylarim/" + KindName + "/" + row.BatchId.ToString("D"), row.PreparedBy,
            [
                reason,
                new(KgDifferenceRule, null, WorkInboxRulesV1.Decimal(row.ReceivedQuantityKg),
                    WorkInboxRulesV1.Decimal(row.ReceivedQuantityKg - row.ExpectedQuantityKg), KilogramUnit),
            ],
            references);
        return true;
    }
}

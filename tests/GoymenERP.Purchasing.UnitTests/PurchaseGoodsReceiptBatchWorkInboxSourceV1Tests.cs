using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.GoodsReceipts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.WorkInbox;
using static GoymenERP.Purchasing.UnitTests.PurchasingWorkInboxTestWorld;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// W1-A (AI-D31/AI-D32, KLN-D45): the bulk goods receipt Work inbox source lists every PendingApproval batch of the company
/// (not per order), oldest first, the preparer's own included, measured with the variance approval permission the decision
/// command uses; the rule numbers are the stored submission snapshot. SENTETIK / NOT_ENV.
/// </summary>
public sealed class PurchaseGoodsReceiptBatchWorkInboxSourceV1Tests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 3, 7, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Only_pending_batches_of_the_caller_company_are_listed_across_orders_oldest_first()
    {
        var world = new PurchasingWorkInboxTestWorld();
        var firstOrder = Order(CompanyA, "SENTETIK-PO-1");
        var secondOrder = Order(CompanyA, "SENTETIK-PO-2");
        var older = Batch(CompanyA, secondOrder.Id, PurchaseGoodsReceiptBatchStatus.PendingApproval, Start);
        var newer = Batch(CompanyA, firstOrder.Id, PurchaseGoodsReceiptBatchStatus.PendingApproval, Start.AddHours(1));
        await world.SeedAsync(CompanyA, db =>
        {
            db.Orders.AddRange(firstOrder, secondOrder);
            db.GoodsReceiptBatches.AddRange(newer, older,
                Decided(Batch(CompanyA, firstOrder.Id, PurchaseGoodsReceiptBatchStatus.Posted, Start.AddMinutes(-10))),
                Decided(Batch(CompanyA, firstOrder.Id, PurchaseGoodsReceiptBatchStatus.Rejected, Start.AddMinutes(-20))));
        });
        var foreignOrder = Order(CompanyB, "SENTETIK-PO-B");
        await world.SeedAsync(CompanyB, db =>
        {
            db.Orders.Add(foreignOrder);
            db.GoodsReceiptBatches.Add(Batch(CompanyB, foreignOrder.Id, PurchaseGoodsReceiptBatchStatus.PendingApproval, Start.AddDays(-1)));
        });

        var result = await Source(world).ListPendingAsync(world.Caller(CompanyA), 10, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
        Assert.False(result.HasMore);
        Assert.Equal([older.BatchId, newer.BatchId], result.Items!.Select(item => item.ItemId));
        var first = result.Items![0];
        AssertContractShape("purchasing", first);
        Assert.Equal("purchasing.goods-receipt-batch.v1", first.Kind);
        Assert.Equal("SENTETIK-PO-2", first.DocumentLabel);
        Assert.Equal("/satinalma/siparisler/" + secondOrder.Id.ToString("D"), first.Link);
        Assert.Equal(Start, first.WaitingSinceUtc);
        var order = first.References[0];
        Assert.Equal(("purchase-order", secondOrder.Id), (order.Role, order.RecordId));
        Assert.Equal(PurchasingPermissionManifestV1.GoodsReceiptsApproveVariance, world.LastPermission);
    }

    [Fact]
    public async Task The_rules_carry_the_stored_tolerance_and_kg_snapshot_and_the_preparers_own_batch_is_returned()
    {
        var world = new PurchasingWorkInboxTestWorld { UserId = Maker };
        var order = Order(CompanyA, "SENTETIK-PO-KG");
        var outside = Batch(CompanyA, order.Id, PurchaseGoodsReceiptBatchStatus.PendingApproval, Start);
        outside.ExpectedQuantityKg = 1000m;
        outside.ReceivedQuantityKg = 1062.5m;
        outside.VarianceOutcome = (int)GoodsReceiptKgVarianceOutcome.OutsideTolerance;
        outside.TolerancePercent = 2.5m;
        var notSet = Batch(CompanyA, order.Id, PurchaseGoodsReceiptBatchStatus.PendingApproval, Start.AddMinutes(1));
        notSet.ExpectedQuantityKg = 500m;
        notSet.ReceivedQuantityKg = 480m;
        notSet.VarianceOutcome = (int)GoodsReceiptKgVarianceOutcome.ToleranceNotSet;
        notSet.TolerancePercent = null;
        await world.SeedAsync(CompanyA, db =>
        {
            db.Orders.Add(order);
            db.GoodsReceiptBatches.AddRange(outside, notSet);
            db.GoodsReceiptBatchLabels.Add(new PurchaseGoodsReceiptBatchLabelRow
            {
                CompanyId = CompanyA, BatchId = outside.BatchId, LineNo = 1, AcceptanceId = Guid.NewGuid(), LotId = Guid.NewGuid(),
                LabelId = Guid.NewGuid(), MovementId = Guid.NewGuid(), LabelCode = "SENTETIK-LBL-1", MeasuredQuantityKg = 1062.5m,
            });
        });

        var items = (await Source(world).ListPendingAsync(world.Caller(CompanyA), 10, CancellationToken.None)).Items!;

        Assert.All(items, item => Assert.Equal(Maker, item.PreparedBy));
        Assert.Equal(
            [
                new WorkApprovalRuleResultV1(PurchaseGoodsReceiptBatchWorkInboxSourceV1.OutsideToleranceRule, "2.5", null, null, "%"),
                new WorkApprovalRuleResultV1(PurchaseGoodsReceiptBatchWorkInboxSourceV1.KgDifferenceRule, null, "1062.5", "62.5", "kg"),
            ],
            items[0].Rules);
        Assert.Equal(("goods-receipt-label", "SENTETIK-LBL-1"), (items[0].References[1].Role, items[0].References[1].Label));
        Assert.Equal(
            [
                new WorkApprovalRuleResultV1(PurchaseGoodsReceiptBatchWorkInboxSourceV1.ToleranceNotSetRule, null, null, null, null),
                new WorkApprovalRuleResultV1(PurchaseGoodsReceiptBatchWorkInboxSourceV1.KgDifferenceRule, null, "480", "-20", "kg"),
            ],
            items[1].Rules);
    }

    [Fact]
    public async Task More_pending_batches_than_requested_set_has_more()
    {
        var world = new PurchasingWorkInboxTestWorld();
        var order = Order(CompanyA, "SENTETIK-PO-MANY");
        var rows = Enumerable.Range(0, 3)
            .Select(index => Batch(CompanyA, order.Id, PurchaseGoodsReceiptBatchStatus.PendingApproval, Start.AddMinutes(index))).ToArray();
        await world.SeedAsync(CompanyA, db =>
        {
            db.Orders.Add(order);
            db.GoodsReceiptBatches.AddRange(rows);
        });

        var result = await Source(world).ListPendingAsync(world.Caller(CompanyA), 2, CancellationToken.None);

        Assert.True(result.HasMore);
        Assert.Equal([rows[0].BatchId, rows[1].BatchId], result.Items!.Select(item => item.ItemId));
    }

    [Fact]
    public async Task Without_the_variance_approval_permission_the_source_is_denied_without_items()
    {
        var world = new PurchasingWorkInboxTestWorld { Granted = false };
        var order = Order(CompanyA, "SENTETIK-PO");
        await world.SeedAsync(CompanyA, db =>
        {
            db.Orders.Add(order);
            db.GoodsReceiptBatches.Add(Batch(CompanyA, order.Id, PurchaseGoodsReceiptBatchStatus.PendingApproval, Start));
        });

        var result = await Source(world).ListPendingAsync(world.Caller(CompanyA), 5, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, result.Outcome);
        Assert.Null(result.Items);
    }

    private static PurchaseGoodsReceiptBatchWorkInboxSourceV1 Source(PurchasingWorkInboxTestWorld world) =>
        new(world.Context(CompanyA), world.Fence(CompanyA));

    private static PurchaseOrderRow Order(Guid company, string number) => new()
    {
        Id = Guid.NewGuid(), CompanyId = company, SupplierPartyId = Guid.NewGuid(), CompanyAccountId = Guid.NewGuid(),
        Status = (int)PurchaseOrderStatus.Approved, Version = 2, RowVersion = [1], EffectiveOn = new DateOnly(2026, 11, 1),
        CreatedAtUtc = Start.AddDays(-2), UpdatedAtUtc = Start.AddDays(-2), CreatedBy = "SENTETIK-buyer",
        CorrelationId = "SENTETIK-correlation", DocumentNumber = number,
    };

    private static PurchaseGoodsReceiptBatchRow Batch(Guid company, Guid orderId, PurchaseGoodsReceiptBatchStatus status,
        DateTimeOffset preparedAt) => new()
    {
        CompanyId = company, BatchId = Guid.NewGuid(), OrderId = orderId, LineId = Guid.NewGuid(), ProductId = Guid.NewGuid(),
        ProductVersion = "1", UnitId = Guid.NewGuid(), LocationId = Guid.NewGuid(), OccurredAtUtc = preparedAt, Status = (int)status,
        Version = 1, RowVersion = [1], ExpectedQuantityKg = 100m, ReceivedQuantityKg = 110m,
        VarianceOutcome = (int)GoodsReceiptKgVarianceOutcome.ToleranceNotSet, PreparedBy = Maker, PreparedAtUtc = preparedAt,
        CorrelationId = "SENTETIK-correlation",
    };

    private static PurchaseGoodsReceiptBatchRow Decided(PurchaseGoodsReceiptBatchRow row)
    {
        row.DecidedBy = Approver;
        row.DecidedAtUtc = row.PreparedAtUtc.AddMinutes(1);
        row.DecisionReason = row.Status == (int)PurchaseGoodsReceiptBatchStatus.Rejected ? "SENTETIK red" : null;
        return row;
    }
}

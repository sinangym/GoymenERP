using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchaseReceiptAdjustmentPersistenceTests
{
    [Fact]
    public void A_net_zero_batch_rebuilds_once_at_its_final_version_and_missing_child_history_is_refused()
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now, "maker");
        var line = order.AddLine(order.Version, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Kg(100), Now);
        order.Approve(order.Version, "checker", Now);
        var original = Guid.NewGuid(); order.RecordAcceptance(order.Version, original, line.Id, Kg(40), Now);
        order.Cancel(order.Version, "canceller", "Cancelled", Now);
        order.AdjustReceipts(order.Version,
            [new(Guid.NewGuid(), original, Kg(10), false, false, Guid.NewGuid(), Kg(20)),
             new(Guid.NewGuid(), original, Kg(20), false, false, Guid.NewGuid(), Kg(10))],
            "operator", "Net zero batch", Now);
        var (header, lines, shares, rows, cancellations) = Snapshot(order);
        var batchId = Guid.NewGuid();
        for (var index = 0; index < rows.Length; index++) { rows[index].BatchId = batchId; rows[index].BatchLineNo = index + 1; }
        var batch = new PurchaseReceiptAdjustmentBatchRow { CompanyId = order.CompanyId, BatchId = batchId, OrderId = order.Id,
            LineCount = rows.Length, OrderVersionAfter = order.Version, RecordedBy = "operator", Reason = "Net zero batch",
            RecordedAtUtc = Now, CorrelationId = "correlation", ResultJson = "{}" };

        var rebuilt = PurchasingStore.RestoreOrder(header, lines, shares, receiptAdjustments: rows,
            cancellationHistory: cancellations, receiptBatches: [batch]);
        Assert.Equal((order.Version, PurchaseOrderStatus.Cancelled, 40m, 60m, 0m),
            (rebuilt.Version, rebuilt.Status, rebuilt.Lines[0].AcceptedQuantity.Value,
                rebuilt.Lines[0].CancelledQuantity.Value, rebuilt.Lines[0].RemainingQuantity.Value));
        Assert.Equal(2, rebuilt.ReceiptAdjustments.Count);
        Assert.All(rebuilt.ReceiptAdjustments, item => Assert.Equal(order.Version, item.OrderVersionAfter));
        Assert.Throws<InvalidOperationException>(() => PurchasingStore.RestoreOrder(header, lines, shares,
            receiptAdjustments: [rows[0]], cancellationHistory: cancellations, receiptBatches: [batch]));
        Assert.Throws<InvalidOperationException>(() => PurchasingStore.RestoreOrder(header, lines, shares,
            receiptAdjustments: rows, cancellationHistory: cancellations));
    }

    [Fact]
    public void Batch_headers_are_company_scoped_and_append_only_and_outbox_positions_are_database_generated()
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        using var database = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer("Server=(local);Database=ModelOnly;Integrated Security=true;TrustServerCertificate=true").Options, company);
        var batch = database.Model.FindEntityType(typeof(PurchaseReceiptAdjustmentBatchRow))!;
        Assert.Single(batch.GetDeclaredQueryFilters());
        Assert.Contains(batch.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name)
            .SequenceEqual(new[] { "CompanyId", "OrderId", "OrderVersionAfter" }));
        var detail = database.Model.FindEntityType(typeof(PurchaseReceiptAdjustmentRow))!;
        Assert.Contains(detail.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(PurchaseReceiptAdjustmentBatchRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(new[] { "CompanyId", "OrderId", "BatchId" }));
        var position = database.Model.FindEntityType(typeof(PurchasingOutboxRow))!.FindProperty(nameof(PurchasingOutboxRow.Position))!;
        Assert.True(position.IsConcurrencyToken);
        Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate, position.ValueGenerated);
        var row = new PurchaseReceiptAdjustmentBatchRow { CompanyId = company.CompanyId, BatchId = Guid.NewGuid(), OrderId = Guid.NewGuid(),
            OrderVersionAfter = 2, LineCount = 1, RecordedBy = "operator", Reason = "Immutable", CorrelationId = "correlation", ResultJson = "{}" };
        database.Attach(row); row.Reason = "Overwritten";
        Assert.Throws<InvalidOperationException>(() => database.SaveChanges());
        database.ChangeTracker.Clear(); database.Attach(row); database.Remove(row);
        Assert.Throws<InvalidOperationException>(() => database.SaveChanges());
    }
}

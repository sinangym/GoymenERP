using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchaseReceiptAdjustmentPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 8, 0, 0, TimeSpan.Zero);
    private static Quantity Kg(decimal value) => Quantity.Of(value, QuantityUnit.Kilogram);

    [Fact]
    public void Down_refuses_history_loss_before_any_schema_change_and_up_backfills_only_recorded_cancellations()
    {
        var migration = new AddPurchaseReceiptAdjustments();
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51949", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("PurchaseOrderCancellationHistory", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("UndoQuantity", guard.Sql, StringComparison.Ordinal);
        var backfill = Assert.Single(migration.UpOperations.OfType<SqlOperation>());
        Assert.Contains("WHERE [Status] = 3", backfill.Sql, StringComparison.Ordinal);
        Assert.Contains("[CancelledBy], [CancellationReason], [CancelledAtUtc]", backfill.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Batch_Down_preserves_grouped_replay_and_any_published_cursor_history_before_schema_changes()
    {
        var migration = new AddPurchaseReceiptAdjustmentBatches();
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Contains("THROW 51956", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[PurchaseReceiptAdjustmentBatches]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[ReceiptAdjustmentBatchId] IS NOT NULL", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("SELECT 1 FROM [purchasing].[OutboxMessages]", guard.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Supplier_return_snapshot_Down_refuses_original_response_loss_before_schema_changes()
    {
        var migration = new AddPurchaseSupplierReturnResponseSnapshots();
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51953", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[ResultJson] IS NOT NULL", guard.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Cancel_undo_cancel_and_definitive_return_rebuild_original_and_net_shares_without_double_opening()
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now, "maker");
        var line = order.AddLine(order.Version, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Kg(100), Now);
        order.Approve(order.Version, "checker", Now);
        var acceptanceId = Guid.NewGuid();
        order.RecordAcceptance(order.Version, acceptanceId, line.Id, Kg(40), Now);
        order.Cancel(order.Version, "first", "First cancellation", Now);
        order.AdjustReceipt(order.Version, Guid.NewGuid(), acceptanceId, Kg(10), false, false, "operator", "Undo", Now);
        order.Cancel(order.Version, "second", "Second cancellation", Now);
        order.AdjustReceipt(order.Version, Guid.NewGuid(), acceptanceId, Kg(5), true, false, "operator", "Definitive return", Now);

        var (row, lines, acceptances, adjustments, cancellations) = Snapshot(order);
        var restored = PurchasingStore.RestoreOrder(row, lines, acceptances, receiptAdjustments: adjustments, cancellationHistory: cancellations);
        var rebuilt = Assert.Single(restored.Lines);
        Assert.Equal((order.Version, PurchaseOrderStatus.Cancelled), (restored.Version, restored.Status));
        Assert.Equal((25m, 70m, 5m, 0m), (rebuilt.AcceptedQuantity.Value, rebuilt.CancelledQuantity.Value,
            rebuilt.DefinitiveReturnQuantity.Value, rebuilt.RemainingQuantity.Value));
        Assert.Equal(40m, Assert.Single(restored.Acceptances).Quantity.Value);
        Assert.Equal(2, restored.CancellationHistory.Count);
        Assert.Equal(2, restored.ReceiptAdjustments.Count);
        Assert.Equal("first", restored.CancellationHistory[0].Cancellation.CancelledBy);
        Assert.Equal("second", restored.Cancellation!.CancelledBy);
    }

    [Fact]
    public void Replacement_receipt_replays_inside_the_single_net_adjustment_version()
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now, "maker");
        var line = order.AddLine(order.Version, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Kg(100), Now);
        order.Approve(order.Version, "checker", Now);
        var original = Guid.NewGuid(); order.RecordAcceptance(order.Version, original, line.Id, Kg(40), Now);
        order.Cancel(order.Version, "canceller", "Cancelled", Now);
        order.AdjustReceipt(order.Version, Guid.NewGuid(), original, Kg(10), false, false,
            "operator", "Net zero correction", Now, Guid.NewGuid(), Kg(10));
        var (row, lines, acceptances, adjustments, cancellations) = Snapshot(order);
        var restored = PurchasingStore.RestoreOrder(row, lines, acceptances, receiptAdjustments: adjustments, cancellationHistory: cancellations);
        Assert.Equal(order.Version, restored.Version);
        Assert.Equal(PurchaseOrderStatus.Cancelled, restored.Status);
        Assert.Equal(2, restored.Acceptances.Count);
        Assert.Equal(40m, Assert.Single(restored.Lines).AcceptedQuantity.Value);
        Assert.False(Assert.Single(restored.ReceiptAdjustments).Revived);
    }

    [Fact]
    public void Stored_net_total_corruption_is_refused_instead_of_repaired()
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now, "maker");
        var line = order.AddLine(order.Version, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Kg(100), Now);
        order.Approve(order.Version, "checker", Now);
        var original = Guid.NewGuid(); order.RecordAcceptance(order.Version, original, line.Id, Kg(40), Now);
        order.AdjustReceipt(order.Version, Guid.NewGuid(), original, Kg(10), false, false, "operator", "Undo", Now);
        var (row, lines, acceptances, adjustments, cancellations) = Snapshot(order);
        lines[0].DefinitiveReturned = 1m;
        Assert.Throws<InvalidOperationException>(() => PurchasingStore.RestoreOrder(row, lines, acceptances,
            receiptAdjustments: adjustments, cancellationHistory: cancellations));
    }

    [Fact]
    public void Adjustment_model_scopes_original_acceptance_by_company_order_and_line_and_shares_the_attempt_key()
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        using var database = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer("Server=(local);Database=ModelOnly;Integrated Security=true;TrustServerCertificate=true").Options, company);
        var adjustment = database.Model.FindEntityType(typeof(PurchaseReceiptAdjustmentRow))!;
        Assert.Single(adjustment.GetDeclaredQueryFilters());
        Assert.Contains(adjustment.GetForeignKeys(), key => key.Properties.Select(property => property.Name)
            .SequenceEqual(new[] { "CompanyId", "OrderId", "LineId", "AcceptanceId" }));
        var attempt = database.Model.FindEntityType(typeof(PurchaseAttemptRow))!;
        Assert.Equal(new[] { "CompanyId", "CommandName", "IdempotencyKeyHash" }, attempt.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(attempt.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(PurchaseReceiptAdjustmentRow));
        var history = database.Model.FindEntityType(typeof(PurchaseOrderCancellationHistoryRow))!;
        Assert.Single(history.GetDeclaredQueryFilters());
    }

    [Fact]
    public void Adjustment_history_cannot_be_modified_or_deleted_before_any_database_io()
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        using var database = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer("Server=(local);Database=ModelOnly;Integrated Security=true;TrustServerCertificate=true").Options, company);
        var row = new PurchaseReceiptAdjustmentRow { CompanyId = company.CompanyId, AdjustmentId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(), AcceptanceId = Guid.NewGuid(), LineId = Guid.NewGuid(), UndoQuantity = 1,
            OrderVersionAfter = 2, RecordedBy = "operator", Reason = "Original", CorrelationId = "correlation", ResultJson = "{}" };
        database.Attach(row); row.Reason = "Overwritten";
        Assert.Throws<InvalidOperationException>(() => database.SaveChanges());
        database.ChangeTracker.Clear(); database.Attach(row); database.Remove(row);
        Assert.Throws<InvalidOperationException>(() => database.SaveChanges());
    }

    private static (PurchaseOrderRow, PurchaseOrderLineRow[], PurchaseAcceptanceRow[], PurchaseReceiptAdjustmentRow[], PurchaseOrderCancellationHistoryRow[]) Snapshot(PurchaseOrder order)
    {
        var header = new PurchaseOrderRow { CompanyId = order.CompanyId, Id = order.Id, SupplierPartyId = order.SupplierPartyId,
            CompanyAccountId = Guid.NewGuid(), CreatedAtUtc = Now, UpdatedAtUtc = order.UpdatedAtUtc, CreatedBy = "maker",
            Status = (int)order.Status, Version = order.Version, ApprovedBy = "checker", ApprovedAtUtc = Now,
            CancelledBy = order.Cancellation?.CancelledBy, CancelledAtUtc = order.Cancellation?.CancelledAtUtc,
            CancellationReason = order.Cancellation?.Reason };
        var lines = order.Lines.Select((line, index) => new PurchaseOrderLineRow { CompanyId = order.CompanyId, OrderId = order.Id,
            LineNo = index + 1, LineId = line.Id, Kind = (int)line.Kind, ParentLineId = line.ParentLineId, ProductId = line.ProductId,
            ProductVersion = "1", UnitId = line.UnitId, UnitIsKilogram = true, Ordered = line.Quantity.Value,
            Technical = line.TechnicalQuantity.Value, Accepted = line.AcceptedQuantity.Value, Cancelled = line.CancelledQuantity.Value,
            Closed = line.ClosedQuantity.Value, DefinitiveReturned = line.DefinitiveReturnQuantity.Value,
            ReceiptClosureRevived = line.ReceiptClosureRevived }).ToArray();
        var originalVersion = 4L; // Create + one line + approval + original acceptance.
        var shares = order.Acceptances.Select(share => new PurchaseAcceptanceRow { CompanyId = order.CompanyId, OrderId = order.Id,
            AcceptanceId = share.AcceptanceId, LineId = share.LineId, Quantity = share.Quantity.Value, RecordedAtUtc = Now,
            OrderVersionAfter = order.ReceiptAdjustments.SingleOrDefault(item => item.NewAcceptanceId == share.AcceptanceId)?.OrderVersionAfter ?? originalVersion,
            IdempotencyKeyHash = new string('a', 64), RequestHash = new string('b', 64), ActorId = "operator", CorrelationId = "correlation", ResultJson = "{}" }).ToArray();
        var adjustments = order.ReceiptAdjustments.Select(change => new PurchaseReceiptAdjustmentRow { CompanyId = order.CompanyId,
            OrderId = order.Id, AdjustmentId = change.AdjustmentId, AcceptanceId = change.AcceptanceId, LineId = change.LineId,
            UndoQuantity = change.UndoQuantity.Value, IsSupplierReturn = change.IsSupplierReturn, ReplacementExpected = change.ReplacementExpected,
            NewAcceptanceId = change.NewAcceptanceId, NewAcceptedQuantity = change.NewAcceptedQuantity.Value, OrderVersionAfter = change.OrderVersionAfter,
            Revived = change.Revived, NetOpenedQuantity = change.NetOpenedQuantity.Value, RecordedBy = change.Actor, Reason = change.Reason,
            RecordedAtUtc = change.OccurredAtUtc, CorrelationId = "correlation", ResultJson = "{}" }).ToArray();
        return (header, lines, shares, adjustments, order.CancellationHistory.Select(change => PurchasingStore.CancellationRow(order.CompanyId, order.Id, change)).ToArray());
    }
}

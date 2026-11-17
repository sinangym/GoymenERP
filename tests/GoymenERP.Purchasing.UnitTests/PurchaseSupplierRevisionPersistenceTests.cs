using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseSupplierRevisionPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Repeated_supplier_revisions_rebuild_each_approval_and_price_without_replacing_the_original_maker(bool approveLast)
    {
        var order = Revised(approveLast);
        var (row, lines) = Snapshot(order);
        var rebuilt = PurchasingStore.RestoreOrder(row, lines);
        Assert.Equal((order.Version, order.Status, order.SupplierPartyId, order.PreparedBy, order.ApprovalVersion),
            (rebuilt.Version, rebuilt.Status, rebuilt.SupplierPartyId, rebuilt.PreparedBy, rebuilt.ApprovalVersion));
        Assert.Equal(2, rebuilt.SupplierRevision);
        Assert.Equal(new[] { 12.5m, 18m }, rebuilt.SupplierRevisions.Select(item => item.PreviousLines[0].Price!.UnitPrice));
        Assert.Equal(new[] { "SENTETIK-first-checker", "SENTETIK-original-maker" }, rebuilt.SupplierRevisions.Select(item => item.PreviousApproval.ApprovedBy));
        Assert.Equal(20m, rebuilt.Lines[0].Price!.UnitPrice);
        Assert.Equal("SENTETIK-original-maker", row.CreatedBy);
        Assert.Equal(2, rebuilt.SupplierRevisionPrices.Count);
        Assert.Equal(row.SupplierRevisionHistoryJson, PurchaseSupplierRevisionHistory.Write(rebuilt));
    }

    [Fact]
    public void Receipt_after_the_last_reapproval_replays_after_the_supplier_and_price_history()
    {
        var order = Revised(true); var receiptId = Guid.NewGuid();
        order.RecordAcceptance(order.Version, receiptId, order.Lines[0].Id, Quantity.Of(3, QuantityUnit.Kilogram), Now.AddHours(1));
        var (row, lines) = Snapshot(order);
        var receipt = new PurchaseAcceptanceRow { CompanyId = order.CompanyId, OrderId = order.Id, LineId = lines[0].LineId,
            AcceptanceId = receiptId, Quantity = 3, OrderVersionAfter = order.Version, RecordedAtUtc = Now.AddHours(1),
            IdempotencyKeyHash = new string('a', 64), RequestHash = new string('b', 64), ActorId = "SENTETIK-receiver",
            CorrelationId = "SENTETIK-receipt", ResultJson = "{}" };
        var rebuilt = PurchasingStore.RestoreOrder(row, lines, [receipt]);
        Assert.Equal(3m, rebuilt.Lines[0].AcceptedQuantity.Value);
        Assert.Equal(7m, rebuilt.Lines[0].RemainingQuantity.Value);
        Assert.Equal(2, rebuilt.SupplierRevision);
    }

    [Fact]
    public void Line_closure_before_a_later_supplier_revision_does_not_break_the_ordered_history()
    {
        var order = Revised(true);
        var line = order.Lines[0];
        order.CloseLine(order.Version, line.Id, "SENTETIK-closer", "SENTETIK closed", Now.AddMinutes(10));
        var closure = new PurchaseOrderLineClosureEventRow { CompanyId = order.CompanyId, OrderId = order.Id,
            LineId = line.Id, OrderVersionAfter = order.Version, Closed = true, RecordedBy = "SENTETIK-closer",
            Reason = "SENTETIK closed", RecordedAtUtc = Now.AddMinutes(10), CorrelationId = "SENTETIK-closure" };
        order.ReviseSupplier(order.Version, Guid.NewGuid(), PurchaseOrderInvoiceHistory.None, "SENTETIK-third-maker", "SENTETIK third", Now.AddMinutes(11));
        order.SetSupplierRevisionPrice(order.Version, line.Id, new(25m, line.UnitId), "SENTETIK-third-maker", Now.AddMinutes(12));
        order.Approve(order.Version, "SENTETIK-third-checker", Now.AddMinutes(13));
        var (row, lines) = Snapshot(order); lines[0].Closed = line.ClosedQuantity.Value;
        var rebuilt = PurchasingStore.RestoreOrder(row, lines, closureEvents: [closure]);
        Assert.Equal((order.Version, 3, 10m, 25m), (rebuilt.Version, rebuilt.SupplierRevision, rebuilt.Lines[0].ClosedQuantity.Value, rebuilt.Lines[0].Price!.UnitPrice));
    }

    [Theory]
    [InlineData("company")]
    [InlineData("order")]
    [InlineData("price")]
    [InlineData("history-price")]
    [InlineData("approval-version")]
    [InlineData("actor")]
    [InlineData("history-missing")]
    public void Corrupt_or_cross_company_history_is_refused_without_guessing(string fault)
    {
        var (row, lines) = Snapshot(Revised(true));
        var history = PurchaseSupplierRevisionHistory.Read(row)!;
        switch (fault)
        {
            case "company": history = history with { CompanyId = Guid.NewGuid() }; break;
            case "order": history = history with { OrderId = Guid.NewGuid() }; break;
            case "price": lines[0].UnitPrice = 21m; break;
            case "history-price": history.Prices[0] = history.Prices[0] with { PreviousPrice = new(99m, lines[0].UnitId) }; break;
            case "approval-version": history = history with { CurrentApprovalVersion = history.Revisions[0].OrderVersionAfter }; break;
            case "actor": row.CurrentPreparedBy = "SENTETIK-wrong"; break;
        }
        row.SupplierRevisionHistoryJson = fault == "history-missing" ? null : JsonSerializer.Serialize(history);
        Assert.ThrowsAny<InvalidOperationException>(() => PurchasingStore.RestoreOrder(row, lines));
    }

    [Fact]
    public void Revision_history_and_original_prices_cannot_be_rewritten_or_erased_before_any_database_io()
    {
        var (row, _) = Snapshot(Revised(true));
        using var database = Context(row.CompanyId);
        database.Attach(row);
        var history = PurchaseSupplierRevisionHistory.Read(row)!;
        history.Revisions[0] = history.Revisions[0] with { Reason = "SENTETIK overwritten" };
        row.SupplierRevisionHistoryJson = JsonSerializer.Serialize(history);
        Assert.Throws<InvalidOperationException>(() => database.SaveChanges());
        database.ChangeTracker.Clear();
        (row, _) = Snapshot(Revised(true));
        using var other = Context(row.CompanyId);
        other.Attach(row); row.SupplierRevisionHistoryJson = null; row.CurrentPreparedBy = null;
        Assert.Throws<InvalidOperationException>(() => other.SaveChanges());
    }

    [Fact]
    public void A_price_or_supplier_cannot_change_without_a_matching_parent_revision_before_any_database_io()
    {
        var (row, lines) = Snapshot(Revised(true));
        using var database = Context(row.CompanyId);
        database.Attach(lines[0]); lines[0].UnitPrice = 100m;
        Assert.Throws<InvalidOperationException>(() => database.SaveChanges());
        database.ChangeTracker.Clear();
        database.Attach(row); row.CompanyAccountId = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => database.SaveChanges());
    }

    [Fact]
    public void Invoice_bindings_are_tenant_filtered_immutable_and_bound_to_the_same_company_order_line()
    {
        var company = Guid.NewGuid(); using var database = Context(company);
        var model = database.GetService<IDesignTimeModel>().Model;
        var binding = model.FindEntityType(typeof(PurchaseInvoiceOrderBindingRow))!;
        Assert.Single(binding.GetDeclaredQueryFilters());
        Assert.Contains(binding.GetForeignKeys(), key => key.Properties.Select(item => item.Name)
            .SequenceEqual(new[] { "CompanyId", "OrderId", "OrderLineId" }));
        var header = model.FindEntityType(typeof(PurchaseOrderRow))!;
        Assert.True(header.FindProperty(nameof(PurchaseOrderRow.SupplierRevisionHistoryJson))!.IsNullable);
        Assert.True(header.FindProperty(nameof(PurchaseOrderRow.CurrentPreparedBy))!.IsNullable);
        Assert.Equal(PurchasingDbContext.SupplierRevisionHistoryCheck,
            header.GetCheckConstraints().Single(check => check.Name == "CK_PurchaseOrders_SupplierRevisionHistory").Sql);
        Assert.Equal(PurchasingDbContext.InvoiceOrderBindingScopeCheck,
            binding.GetCheckConstraints().Single(check => check.Name == "CK_PurchaseInvoiceOrderBindings_Scope").Sql);
        Assert.Contains(header.GetCheckConstraints(), check => check.Name == "CK_PurchaseOrders_Approval"
            && check.Sql.Contains("COALESCE([CurrentPreparedBy], [CreatedBy])", StringComparison.Ordinal));
        var row = new PurchaseInvoiceOrderBindingRow { CompanyId = company, InvoiceId = Guid.NewGuid(), InvoiceLineId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(), OrderLineId = Guid.NewGuid(), SupplierPartyId = Guid.NewGuid(), OrderVersion = 3, RecordedAtUtc = Now };
        database.Attach(row); row.OrderVersion = 4;
        Assert.Throws<InvalidOperationException>(() => database.SaveChanges());
        database.ChangeTracker.Clear(); database.Attach(row); database.Remove(row);
        Assert.Throws<InvalidOperationException>(() => database.SaveChanges());
    }

    [Fact]
    public void Supplier_revision_migration_is_additive_and_refuses_rollback_before_dropping_any_history()
    {
        var migration = new AddPurchaseSupplierRevisions();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Contains("THROW 51977", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[SupplierRevisionHistoryJson] IS NOT NULL", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[CurrentPreparedBy] IS NOT NULL", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[purchasing].[PurchaseInvoiceOrderBindings]", guard.Sql, StringComparison.Ordinal);
    }

    private static PurchaseOrder Revised(bool approveLast)
    {
        var unit = Guid.NewGuid(); var money = PurchaseOrderMoney.Of(Guid.NewGuid(), "TRY", Guid.NewGuid(), 1, 2, 4);
        var order = PurchaseOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), money, Now, "SENTETIK-original-maker");
        order.AddLine(order.Version, Guid.NewGuid(), Guid.NewGuid(), unit, Quantity.Of(10, QuantityUnit.Kilogram), null, new(12.5m, unit), Now);
        order.Approve(order.Version, "SENTETIK-first-checker", Now);
        order.ReviseSupplier(order.Version, Guid.NewGuid(), PurchaseOrderInvoiceHistory.None, "SENTETIK-first-checker", "SENTETIK first", Now.AddMinutes(1));
        order.SetSupplierRevisionPrice(order.Version, order.Lines[0].Id, new(18m, unit), "SENTETIK-first-checker", Now.AddMinutes(2));
        order.Approve(order.Version, "SENTETIK-original-maker", Now.AddMinutes(3));
        order.ReviseSupplier(order.Version, Guid.NewGuid(), PurchaseOrderInvoiceHistory.None, "SENTETIK-next-maker", "SENTETIK next", Now.AddMinutes(4));
        order.SetSupplierRevisionPrice(order.Version, order.Lines[0].Id, new(20m, unit), "SENTETIK-price-operator", Now.AddMinutes(5));
        if (approveLast) order.Approve(order.Version, "SENTETIK-next-checker", Now.AddMinutes(6));
        return order;
    }

    private static (PurchaseOrderRow, PurchaseOrderLineRow[]) Snapshot(PurchaseOrder order)
    {
        var row = new PurchaseOrderRow { CompanyId = order.CompanyId, Id = order.Id, SupplierPartyId = order.SupplierPartyId,
            CompanyAccountId = Guid.NewGuid(), CreatedAtUtc = Now, UpdatedAtUtc = order.UpdatedAtUtc, CreatedBy = "SENTETIK-original-maker",
            CurrentPreparedBy = order.PreparedBy, Status = (int)order.Status, Version = order.Version,
            ApprovedBy = order.Approval?.ApprovedBy, ApprovedAtUtc = order.Approval?.ApprovedAtUtc,
            CurrencyId = order.Money!.CurrencyId, CurrencyCode = order.Money.CurrencyCode, MoneyProfileId = order.Money.ProfileId,
            MoneyProfileVersion = order.Money.ProfileVersion, MoneyScale = order.Money.Scale, UnitPriceScale = order.Money.UnitPriceScale,
            SupplierRevisionHistoryJson = PurchaseSupplierRevisionHistory.Write(order) };
        return (row, order.Lines.Select((line, index) => new PurchaseOrderLineRow { CompanyId = order.CompanyId, OrderId = order.Id,
            LineId = line.Id, LineNo = index + 1, Kind = (int)line.Kind, ProductId = line.ProductId, ProductVersion = "1", UnitId = line.UnitId,
            UnitIsKilogram = true, Ordered = line.Quantity.Value, Accepted = line.AcceptedQuantity.Value,
            UnitPrice = line.Price?.UnitPrice, PriceUnitId = line.Price?.PriceUnitId,
            DiscountPercent = line.Price?.DiscountPercent, DiscountAmount = line.Price?.DiscountAmount,
            DeferralPercent = line.Price?.DeferralPercent, DeferralAmount = line.Price?.DeferralAmount }).ToArray());
    }

    private static PurchasingDbContext Context(Guid companyId)
    {
        var company = new CompanyContext(); company.Set(companyId);
        return new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer("Server=model-only;Database=model-only;Trusted_Connection=True;TrustServerCertificate=True").Options, company);
    }
}

using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Returns;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// AK-8 (SAT-D433/SAT-D441/SAT-D445) on real SQL Server: a supplier return keeps its foreign source label and mode together
/// (CK_PurchaseSupplierReturns_Lineage), they round-trip through the store, and AddPurchaseSupplierReturnLineageSource's Down
/// refuses (52071) while one exists. SENTETIK owners.
/// </summary>
public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task The_supplier_return_lineage_migrations_Down_passes_with_only_receipt_label_returns_and_refuses_while_a_source_label_return_exists()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        var order = Order(owners, null, null, 10m);
        await using (StoreScope scope = run.Store(CompanyA))
        {
            await scope.Store.StoreOrderAsync(run.Envelope("lineage-order"), Hash("lineage-order"), owners.Account, Day, () => order,
                VersionsOf(order));
        }

        string guardSql = Assert.IsType<SqlOperation>(new AddPurchaseSupplierReturnLineageSource().DownOperations[0]).Sql;
        var plain = Draft(order.Id, order.Lines[0].Id, null, null);
        await WithReturnStore(run, store => store.SaveNewAsync(plain, run.Envelope("lineage-plain"), Hash("lineage-plain"),
            PurchaseSupplierReturnCommandsV1.CreateV1));
        await run.ExecuteAsync(guardSql);

        var halfPair = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE purchasing.PurchaseSupplierReturns SET SourceLabelId = NEWID() WHERE ReturnId = @id", ("@id", plain.ReturnId)));
        Assert.Equal(547, halfPair.Number);
        Assert.Contains("CK_PurchaseSupplierReturns_Lineage", halfPair.Message, StringComparison.Ordinal);

        var label = Guid.NewGuid();
        var sourced = Draft(order.Id, order.Lines[0].Id, label, PurchaseSupplierReturnLineageModeV1.Lineage);
        await WithReturnStore(run, store => store.SaveNewAsync(sourced, run.Envelope("lineage-sourced"), Hash("lineage-sourced"),
            PurchaseSupplierReturnCommandsV1.CreateV1));
        await WithReturnStore(run, async store =>
        {
            var found = await store.FindAsync(order.Id, sourced.ReturnId);
            Assert.Equal((label, PurchaseSupplierReturnLineageModeV1.Lineage), (found!.Return.SourceLabelId, found.Return.LineageMode));
            return found;
        });

        var guard = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guardSql));
        Assert.Equal(52071, guard.Number);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.PurchaseSupplierReturns WHERE SourceLabelId IS NOT NULL"));
    }

    /// <summary>
    /// AK-8b on real SQL Server: the per-receipt parts and the piece count of a lineage return round-trip through the store, exist
    /// only with a source label (CK_PurchaseSupplierReturns_LineageParts), and AddPurchaseSupplierReturnLineageParts' Down refuses
    /// (52072) while one exists.
    /// </summary>
    [SqlServerFact]
    public async Task The_supplier_return_lineage_parts_migrations_Down_passes_without_parts_and_refuses_while_a_return_with_per_receipt_parts_exists()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        var order = Order(owners, null, null, 10m);
        await using (StoreScope scope = run.Store(CompanyA))
        {
            await scope.Store.StoreOrderAsync(run.Envelope("parts-order"), Hash("parts-order"), owners.Account, Day, () => order,
                VersionsOf(order));
        }

        string guardSql = Assert.IsType<SqlOperation>(new AddPurchaseSupplierReturnLineageParts().DownOperations[0]).Sql;
        var sourced = Draft(order.Id, order.Lines[0].Id, Guid.NewGuid(), PurchaseSupplierReturnLineageModeV1.Lineage);
        await WithReturnStore(run, store => store.SaveNewAsync(sourced, run.Envelope("parts-none"), Hash("parts-none"),
            PurchaseSupplierReturnCommandsV1.CreateV1));
        await run.ExecuteAsync(guardSql);

        var plain = Draft(order.Id, order.Lines[0].Id, null, null);
        await WithReturnStore(run, store => store.SaveNewAsync(plain, run.Envelope("parts-plain"), Hash("parts-plain"),
            PurchaseSupplierReturnCommandsV1.CreateV1));
        var orphan = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE purchasing.PurchaseSupplierReturns SET PieceCount = 3 WHERE ReturnId = @id", ("@id", plain.ReturnId)));
        Assert.Equal(547, orphan.Number);
        Assert.Contains("CK_PurchaseSupplierReturns_LineageParts", orphan.Message, StringComparison.Ordinal);

        var withParts = Draft(order.Id, order.Lines[0].Id, Guid.NewGuid(), PurchaseSupplierReturnLineageModeV1.Lineage);
        var otherAcceptance = Guid.NewGuid();
        withParts = withParts with
        {
            QuantityKg = 3m, PieceCount = 5,
            LineageParts =
            [
                new(withParts.GoodsAcceptanceId, order.Id, order.Lines[0].Id, withParts.ReturnLineId, withParts.InventoryMovementId, 1.8m,
                    PurchaseSupplierReturnCostBasis.ManualCostWithException, 2m, 3, "0.8", [new(Guid.NewGuid(), "1")]),
                new(otherAcceptance, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1.2m,
                    PurchaseSupplierReturnCostBasis.ManualCostWithException, 2.5m, 2),
            ],
        };
        await WithReturnStore(run, store => store.SaveNewAsync(withParts, run.Envelope("parts-two"), Hash("parts-two"),
            PurchaseSupplierReturnCommandsV1.CreateV1));
        await WithReturnStore(run, async store =>
        {
            var found = await store.FindAsync(order.Id, withParts.ReturnId);
            Assert.Equal((5, 2, 3m), (found!.Return.PieceCount, found.Return.LineageParts!.Count, found.Return.QuantityKg));
            Assert.Equal([(withParts.GoodsAcceptanceId, 1.8m, 3, "0.8"), (otherAcceptance, 1.2m, 2, (string?)null)],
                found.Return.LineageParts.Select(part => (part.GoodsAcceptanceId, part.QuantityKg, part.PieceCount!.Value, part.PendingKg)));
            Assert.Equal("1", Assert.Single(found.Return.LineageParts[0].Invoiced!).QuantityKg);
            return found;
        });

        var guard = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guardSql));
        Assert.Equal(52072, guard.Number);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.PurchaseSupplierReturns WHERE LineagePartsJson IS NOT NULL AND PieceCount = 5"));
    }

    private static PurchaseSupplierReturnDraft Draft(Guid orderId, Guid lineId, Guid? label, PurchaseSupplierReturnLineageModeV1? mode) =>
        new(Guid.NewGuid(), Guid.NewGuid(), orderId, lineId, Guid.NewGuid(), Guid.NewGuid(), PurchaseSupplierReturnStatus.PendingCostException,
            "Free", 1m, PurchaseSupplierReturnCostBasis.ManualCostWithException, 2m, false, "SENTETIK iade", T0,
            SourceLabelId: label, LineageMode: mode);

    private static async Task<T> WithReturnStore<T>(Run run, Func<PurchaseSupplierReturnStore, Task<T>> action)
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        var collector = new AuditEventCollector();
        await using var context = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema))
            .AddInterceptors(new AuditSaveChangesInterceptor(collector, company)).Options, company);
        return await action(new PurchaseSupplierReturnStore(context, company, collector, TimeProvider.System));
    }
}

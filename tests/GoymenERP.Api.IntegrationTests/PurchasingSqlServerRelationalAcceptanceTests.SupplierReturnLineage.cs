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

using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.GoodsReceipts;
using GoymenERP.Modules.Purchasing.Domain.Returns;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Receipt_and_return_identity_probes_do_not_disclose_other_companies_and_global_collisions_still_refuse()
    {
        await using Run run = await Run.StartAsync();
        var orderIds = new Dictionary<Guid, (Guid Order, Guid Line)>();
        foreach (var company in new[] { CompanyA, CompanyB })
        {
            var owners = Owners.For(company);
            var order = Order(owners, null, null, 10m);
            await using var scope = run.Store(company);
            await scope.Store.StoreOrderAsync(run.Envelope($"identity-order-{company:N}"), Hash($"identity-order-{company:N}"),
                owners.Account, Day, () => order, VersionsOf(order));
            orderIds.Add(company, (order.Id, order.Lines[0].Id));
        }

        var batchId = Guid.NewGuid();
        var returnId = Guid.NewGuid();
        PurchaseGoodsReceiptBatchDraft Batch(Guid company) => new(batchId, orderIds[company].Order, orderIds[company].Line,
            Product, "7", KgUnit, Guid.NewGuid(), T0, PurchaseGoodsReceiptBatchStatus.PendingApproval, 10m, 11m,
            GoodsReceiptKgVarianceOutcome.ToleranceNotSet, null, false,
            [new(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), $"SENTETIK-{company:N}",
                GoodsReceiptLabelCodeSource.Entered, null, null, 11m, null, null)]);
        PurchaseSupplierReturnDraft Return(Guid company) => new(returnId, Guid.NewGuid(), orderIds[company].Order,
            orderIds[company].Line, Guid.NewGuid(), Guid.NewGuid(), PurchaseSupplierReturnStatus.PendingCostException,
            "Free", 1m, PurchaseSupplierReturnCostBasis.ManualCostWithException, 2m, false, "SENTETIK iade", T0);

        async Task WithStores(Guid company, Func<PurchaseGoodsReceiptBatchStore, PurchaseSupplierReturnStore, Task> action)
        {
            var contextCompany = new CompanyContext();
            contextCompany.Set(company);
            var collector = new AuditEventCollector();
            await using var context = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
                .UseSqlServer(run.Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, contextCompany)).Options, contextCompany);
            await action(new(context, contextCompany, collector, TimeProvider.System),
                new(context, contextCompany, collector, TimeProvider.System));
        }

        await WithStores(CompanyA, async (batches, returns) =>
        {
            Assert.False(await batches.BatchIdExistsAsync(batchId));
            Assert.False(await returns.ReturnIdExistsAsync(returnId));
            await batches.SaveNewAsync(Batch(CompanyA), run.Envelope("identity-batch-a"), Hash("identity-batch-a"),
                PurchaseGoodsReceiptBatchCommandsV1.SubmitV1);
            await returns.SaveNewAsync(Return(CompanyA), run.Envelope("identity-return-a"), Hash("identity-return-a"),
                PurchaseSupplierReturnCommandsV1.CreateV1);
        });
        await WithStores(CompanyA, async (batches, returns) =>
        {
            Assert.True(await batches.BatchIdExistsAsync(batchId));
            Assert.True(await returns.ReturnIdExistsAsync(returnId));
        });
        await WithStores(CompanyB, async (batches, returns) =>
        {
            Assert.False(await batches.BatchIdExistsAsync(batchId));
            Assert.False(await returns.ReturnIdExistsAsync(returnId));
            Assert.Null(await batches.FindAsync(orderIds[CompanyA].Order, batchId));
            Assert.Null(await returns.FindAsync(orderIds[CompanyA].Order, returnId));
        });

        // A collision remains an ordinary concurrent change. Neither row nor attempt of company B commits.
        await WithStores(CompanyB, async (batches, _) =>
        {
            var refusal = await Assert.ThrowsAsync<PurchasingStoreException>(() => batches.SaveNewAsync(Batch(CompanyB),
                run.Envelope("identity-batch-b"), Hash("identity-batch-b"), PurchaseGoodsReceiptBatchCommandsV1.SubmitV1));
            Assert.Equal(PurchasingStoreCodes.ConcurrentChange, refusal.Code);
        });
        await WithStores(CompanyB, async (_, returns) =>
        {
            var refusal = await Assert.ThrowsAsync<PurchasingStoreException>(() => returns.SaveNewAsync(Return(CompanyB),
                run.Envelope("identity-return-b"), Hash("identity-return-b"), PurchaseSupplierReturnCommandsV1.CreateV1));
            Assert.Equal(PurchasingStoreCodes.ConcurrentChange, refusal.Code);
        });
        await WithStores(CompanyB, async (batches, returns) =>
        {
            Assert.Null(await batches.FindReplayAsync(run.Envelope("identity-batch-b"), Hash("identity-batch-b"),
                PurchaseGoodsReceiptBatchCommandsV1.SubmitV1));
            Assert.Null(await returns.FindReplayAsync(run.Envelope("identity-return-b"), Hash("identity-return-b"),
                PurchaseSupplierReturnCommandsV1.CreateV1));
        });
    }
}

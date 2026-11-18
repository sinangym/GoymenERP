using System.Data.Common;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.PurchasingRuntime;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// STK-S17 / KLN-D65/66/67, ALS-D84: real Purchasing target/acceptance, Inventory allocation/conversion, transaction-owned
/// Sales+purchase locks and SQL persistence. Product/unit/open-demand snapshots and authority fences are explicitly synthetic.
/// No fact claims browser or production acceptance. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public Task Receipt_waits_for_the_same_sales_lock_as_a_cross_purchase_creator_paused_between_the_two_cap_reads() =>
        CheckReceiptCapRaceAsync(physical: false);

    [SqlServerFact]
    public Task Receipt_waits_for_the_same_sales_lock_as_a_physical_creator_paused_between_the_two_cap_reads() =>
        CheckReceiptCapRaceAsync(physical: true);

    private static async Task CheckReceiptCapRaceAsync(bool physical)
    {
        await using var run = await Run.StartAsync();
        var seed = await SeedGoodsReceiptPurchaseOrderAsync(run);
        var supply = new FutureSupplyIds(seed.OrderId, seed.LineId, GoodsReceiptUnit, GoodsReceiptProduct, Guid.NewGuid(), Guid.NewGuid());
        await SeedReceiptFutureAllocationAsync(run, supply, 10m);
        var otherPurchase = supply with { PurchaseOrder = Guid.NewGuid(), PurchaseLine = Guid.NewGuid() };
        var pause = new ReceiptCapReadPause();
        var company = Company(CompanyA);
        var creatorAudit = new AuditEventCollector();
        await using var creatorContext = run.Context(CompanyA, pause, new AuditSaveChangesInterceptor(creatorAudit, company));
        var creatorMetadata = ReceiptMetadata(run, "SENTETIK-cross-purchase", "SENTETIK-cross-purchase");
        // The physical cap must refuse before it reaches the store's label lookup. This synthetic id is deliberately absent.
        Task creator = physical
            ? new InventoryPhysicalAllocationDemandCapWriter(new InventoryStore(creatorContext, company, creatorAudit,
                    new InventoryKilogramScale(GoodsReceiptSettings(run)), TimeProvider.System), creatorContext,
                new ReceiptPrelockDemand(supply), new ReceiptPrelockKilograms(), new SalesOrderLineSerializationLockHostBinder(company))
                .CreateAsync(new(Guid.NewGuid(), supply.DemandOrder, supply.DemandLine, supply.Product, "product-v1",
                    Guid.NewGuid(), "1", "10", "SENTETIK cap race", null), creatorMetadata)
            : ReceiptFutureWriter(creatorContext, otherPurchase, creatorAudit).CreateAsync(
                ReceiptFutureRequest(otherPurchase, 10m), creatorMetadata, new ReceiptPrelockFence());
        await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var observed = new ReceiptObservedSalesLocks(new SalesOrderLineSerializationLockHostBinder(Company(CompanyA)));
        await using var host = ReceiptPrelockHarness.Create(run, supply, observed);
        var callbackCount = 0;
        var receipt = Receipt(seed, Guid.NewGuid(), "10", await run.LocationOfAsync(CompanyA)) with { InitialBlock = BlockState.Free };
        var envelope = GoodsReceiptPurchaseEnvelope(run, "SENTETIK-receipt-race", "SENTETIK-receipt-race");
        var posting = host.Batch.ExecuteAsync(CompanyA, new(seed.OrderId, seed.LineId, "3"), async (participants, token) =>
        {
            Interlocked.Increment(ref callbackCount);
            return await PostReceiptThroughHostAsync(participants, seed, receipt, envelope, token);
        }, _ => Task.CompletedTask);
        try
        {
            await observed.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            // The creator owns the Sales lock, even though it is writing another purchase line. No receipt write can start.
            Assert.Equal(0, Volatile.Read(ref callbackCount));
            pause.Resume.TrySetResult();
            var refused = await Assert.ThrowsAsync<InventoryAllocationException>(() => creator);
            Assert.Equal((409, InventoryFutureAllocationCodesV1.ExceedsDemandOpenQuantity), (refused.Status, refused.Code));
            var posted = await posting.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.False(posted.IsReplay);
            Assert.Equal(1, callbackCount);
        }
        finally { pause.Resume.TrySetResult(); }
        Assert.Equal(10m, await run.ScalarAsync<decimal>(
            "SELECT COALESCE((SELECT SUM(RemainingKg) FROM inventory.Allocations WHERE CompanyId=@company AND DemandLineId=@line AND Status=1),0)"
            + "+COALESCE((SELECT SUM(RemainingQuantity) FROM inventory.FutureSupplyAllocations WHERE CompanyId=@company AND DemandLineId=@line AND Status=1),0)",
            ("@company", CompanyA), ("@line", supply.DemandLine)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.FutureSupplyConversions WHERE AcceptanceId=@acceptance",
            ("@acceptance", receipt.AcceptanceId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.FutureSupplyAllocations WHERE PurchaseOrderLineId=@line",
            ("@line", otherPurchase.PurchaseLine)));
    }

    [SqlServerFact]
    public async Task A_growing_receipt_prelock_set_discards_the_first_transaction_and_invokes_the_batch_callback_once()
    {
        await using var run = await Run.StartAsync();
        var seed = await SeedGoodsReceiptPurchaseOrderAsync(run);
        var supply = new FutureSupplyIds(seed.OrderId, seed.LineId, GoodsReceiptUnit, GoodsReceiptProduct, Guid.NewGuid(), Guid.NewGuid());
        await SeedReceiptFutureAllocationAsync(run, supply, 1m);
        var added = supply with { DemandOrder = Guid.NewGuid(), DemandLine = Guid.NewGuid() };
        var company = Company(CompanyA);
        var reads = new ReceiptReadSetPort(new InventoryFutureAllocationReleasePort(company,
            new PurchaseLineSerializationLockHostBinder(company), TimeProvider.System), async () =>
            await SeedReceiptFutureAllocationAsync(run, added, 1m));
        await using var host = ReceiptPrelockHarness.Create(run, supply, releasePort: reads);
        var callbacks = 0;
        var fences = 0;
        var answer = await host.Batch.ExecuteAsync(CompanyA, new(seed.OrderId, seed.LineId, "3"), (_, _) =>
        { callbacks++; return Task.FromResult("SENTETIK-once"); }, _ => { fences++; return Task.CompletedTask; });
        Assert.Equal(("SENTETIK-once", 1, 1), (answer, callbacks, fences));
        Assert.Equal(2, reads.Transactions.Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAcceptanceShares WHERE OrderId=@order",
            ("@order", seed.OrderId)));
    }

    [SqlServerFact]
    public async Task A_perpetually_expanding_prelock_read_stops_after_bounded_prewrite_attempts_without_invoking_business_work()
    {
        await using var run = await Run.StartAsync();
        var seed = await SeedGoodsReceiptPurchaseOrderAsync(run);
        var supply = new FutureSupplyIds(seed.OrderId, seed.LineId, GoodsReceiptUnit, GoodsReceiptProduct, Guid.NewGuid(), Guid.NewGuid());
        await SeedReceiptFutureAllocationAsync(run, supply, 1m);
        var company = Company(CompanyA);
        // Explicit synthetic owner race injection over real transaction-owned locks; no fake mutation is counted as success.
        var reads = new ReceiptReadSetPort(new InventoryFutureAllocationReleasePort(company,
            new PurchaseLineSerializationLockHostBinder(company), TimeProvider.System), () => Task.CompletedTask, growEveryPreparation: true);
        await using var host = ReceiptPrelockHarness.Create(run, supply, releasePort: reads);
        var calls = 0;
        var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() => host.Batch.ExecuteAsync(CompanyA,
            new(seed.OrderId, seed.LineId, "3"), (_, _) => { calls++; return Task.FromResult(0); }, _ => Task.CompletedTask));
        Assert.Equal((409, PurchaseAcceptanceParticipantCodes.ConcurrentChange), (refused.Status, refused.Code));
        Assert.Equal(0, calls);
        Assert.Equal(3, reads.Transactions.Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAcceptanceShares WHERE OrderId=@order",
            ("@order", seed.OrderId)));
    }

    [SqlServerFact]
    public async Task Receipt_business_failure_is_not_retried_and_rolls_back_conversion_then_the_same_key_can_succeed_and_replay()
    {
        await using var run = await Run.StartAsync();
        var seed = await SeedGoodsReceiptPurchaseOrderAsync(run);
        var supply = new FutureSupplyIds(seed.OrderId, seed.LineId, GoodsReceiptUnit, GoodsReceiptProduct, Guid.NewGuid(), Guid.NewGuid());
        await SeedReceiptFutureAllocationAsync(run, supply, 2m);
        await using var host = ReceiptPrelockHarness.Create(run, supply);
        var receipt = Receipt(seed, Guid.NewGuid(), "2", await run.LocationOfAsync(CompanyA)) with { InitialBlock = BlockState.Free };
        var envelope = GoodsReceiptPurchaseEnvelope(run, "SENTETIK-receipt-retry", "SENTETIK-receipt-retry");
        var callbacks = 0;
        await Assert.ThrowsAsync<ReceiptAfterStageFault>(() => host.Single.ExecuteAsync<PurchaseReceiptStockResultV1>(CompanyA,
            new(seed.OrderId, seed.LineId, "3"), receipt.AcceptanceId, envelope, async (participants, token) =>
            {
                callbacks++;
                await PostReceiptThroughHostAsync(participants, seed, receipt, envelope, token);
                throw new ReceiptAfterStageFault();
            }, _ => Task.CompletedTask));
        Assert.Equal(1, callbacks);
        Assert.Equal(3L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@order", ("@order", seed.OrderId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.FutureSupplyConversions WHERE AcceptanceId=@acceptance",
            ("@acceptance", receipt.AcceptanceId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Labels WHERE Id=@label", ("@label", receipt.LabelId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId=@correlation",
            ("@correlation", envelope.CorrelationId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OutboxMessages WHERE CorrelationId=@correlation",
            ("@correlation", envelope.CorrelationId)));
        Assert.Equal(2m, await run.ScalarAsync<decimal>("SELECT ActiveAllocated FROM inventory.FutureSupplyLines WHERE PurchaseOrderLineId=@line",
            ("@line", seed.LineId)));

        var posted = await host.Single.ExecuteAsync(CompanyA, new(seed.OrderId, seed.LineId, "3"), receipt.AcceptanceId, envelope,
            (participants, token) => PostReceiptThroughHostAsync(participants, seed, receipt, envelope, token), _ => Task.CompletedTask);
        Assert.False(posted.IsReplay);
        // Owner data can change after the original receipt. A replay must use immutable owner key/actor/payload facts.
        var company = Company(CompanyA);
        var audit = new AuditEventCollector();
        await using (var changed = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString).AddInterceptors(new AuditSaveChangesInterceptor(audit, company)).Options, company))
        {
            var actor = new FixedCurrentUser(run.Actor, CompanyA);
            var authority = new GoodsReceiptPurchaseNumberAuthority();
            await using var numbers = new PurchaseDocumentNumberSqlTestOwner(run.Factory.Services, CompanyA, actor, authority);
            var store = new PurchasingStore(changed, company, audit, TimeProvider.System, numbers.Numbers, actor, authority);
            await store.MutateOrderAsync(GoodsReceiptPurchaseEnvelope(run, "SENTETIK-after-receipt-cancel", "SENTETIK-after-receipt-cancel")
                    with { PermissionCode = ErpPermissions.PurchasingOrdersCancel },
                GoodsReceiptHash("SENTETIK-after-receipt-cancel"), PurchasingCommands.OrderCancelV1, seed.OrderId,
                (order, now) => order.Cancel(order.Version, run.Actor.ToString("D"), "SENTETIK kalan iptal", now),
                new PurchasingMutationAudit("purchasing.purchase-order.cancelled.v1", "Cancel", "SENTETIK"));
        }
        var replayed = await host.Single.ExecuteAsync(CompanyA, new(seed.OrderId, seed.LineId, "3"), receipt.AcceptanceId, envelope,
            (participants, token) => PostReceiptThroughHostAsync(participants, seed, receipt, envelope, token), _ => Task.CompletedTask);
        Assert.True(replayed.IsReplay);
        Assert.Equal(posted.LabelVersion, replayed.LabelVersion);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.FutureSupplyConversions WHERE AcceptanceId=@acceptance",
            ("@acceptance", receipt.AcceptanceId)));
        var wrongActor = await Assert.ThrowsAsync<PurchasingServiceException>(() => host.Single.ExecuteAsync(CompanyA,
            new(seed.OrderId, seed.LineId, "3"), receipt.AcceptanceId, envelope with { ActorId = "SENTETIK-other" },
            (_, _) => Task.FromResult(0), _ => Task.CompletedTask));
        Assert.Equal((409, InventoryGoodsReceiptCodesV1.IdempotencyConflict), (wrongActor.Status, wrongActor.Code));
    }

    [SqlServerFact]
    public async Task Cancellation_in_receipt_preparation_stages_nothing_and_the_order_target_is_company_scoped()
    {
        await using var run = await Run.StartAsync();
        var seed = await SeedGoodsReceiptPurchaseOrderAsync(run);
        var supply = new FutureSupplyIds(seed.OrderId, seed.LineId, GoodsReceiptUnit, GoodsReceiptProduct, Guid.NewGuid(), Guid.NewGuid());
        await SeedReceiptFutureAllocationAsync(run, supply, 1m);
        using var cancel = new CancellationTokenSource();
        var locks = new ReceiptObservedSalesLocks(new SalesOrderLineSerializationLockHostBinder(Company(CompanyA)), cancel);
        await using var host = ReceiptPrelockHarness.Create(run, supply, locks);
        var calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Batch.ExecuteAsync(CompanyA, new(seed.OrderId, seed.LineId, "3"),
            (_, _) => { calls++; return Task.FromResult(0); }, _ => Task.CompletedTask, cancel.Token));
        Assert.Equal(0, calls);
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using var owner = await PurchaseAcceptanceParticipant.EnlistAsync(connection, transaction, CompanyA);
        Assert.Equal(new PurchaseAcceptanceReceiptTargetV1(CompanyA, seed.OrderId, seed.LineId, seed.LineId, "3"),
            await owner.ResolveReceiptTargetAsync(seed.OrderId, seed.LineId));
        await using var foreign = await PurchaseAcceptanceParticipant.EnlistAsync(connection, transaction, CompanyB);
        Assert.Null(await foreign.ResolveReceiptTargetAsync(seed.OrderId, seed.LineId));
        await transaction.RollbackAsync();
    }

    private static async Task SeedReceiptFutureAllocationAsync(Run run, FutureSupplyIds supply, decimal quantity)
    {
        var audit = new AuditEventCollector();
        await using var context = run.Context(CompanyA, new AuditSaveChangesInterceptor(audit, Company(CompanyA)));
        await ReceiptFutureWriter(context, supply, audit).CreateAsync(ReceiptFutureRequest(supply, quantity),
            ReceiptMetadata(run, $"SENTETIK-seed-{Guid.NewGuid():N}", "SENTETIK-prelock-seed"), new ReceiptPrelockFence());
    }
    private static CreateFutureSupplyAllocationV1 ReceiptFutureRequest(FutureSupplyIds supply, decimal quantity) =>
        new(Guid.NewGuid(), supply.DemandOrder, supply.DemandLine, supply.PurchaseOrder, supply.PurchaseLine,
            PurchaseAcceptanceParticipantContractV1.FormatQuantity(quantity), supply.Unit, "SENTETIK receipt prelock");
    private static InventoryFutureAllocationWriter ReceiptFutureWriter(InventoryDbContext context, FutureSupplyIds supply,
        AuditEventCollector audit)
    {
        var company = Company(CompanyA);
        return new(context, company, audit, new SalesOrderLineSerializationLockHostBinder(company),
            new PurchaseLineSerializationLockHostBinder(company), new ReceiptPrelockSupply(supply), new ReceiptPrelockDemand(supply),
            new ReceiptPrelockKilograms(), TimeProvider.System);
    }
    private static async Task<PurchaseReceiptStockResultV1> PostReceiptThroughHostAsync(IPurchaseGoodsReceiptBatchParticipantsV1 batch,
        GoodsReceiptAcceptanceSeed seed, InventoryGoodsReceiptRequestV1 receipt, PurchasingWriteEnvelope envelope, CancellationToken token) =>
        await PostReceiptThroughHostAsync(await batch.EnlistLabelAsync(0, token), seed, receipt, envelope, token);
    private static async Task<PurchaseReceiptStockResultV1> PostReceiptThroughHostAsync(IPurchaseGoodsReceiptParticipantsV1 participant,
        GoodsReceiptAcceptanceSeed seed, InventoryGoodsReceiptRequestV1 receipt, PurchasingWriteEnvelope envelope, CancellationToken token)
    {
        var purchase = await participant.StagePurchaseAsync(GoodsReceiptPurchaseRequest(seed, receipt.AcceptanceId, receipt.QuantityKg), envelope, token);
        return await participant.StageInventoryAsync(new(CompanyA, receipt.AcceptanceId, seed.OrderId, seed.LineId,
            purchase.PurchaseOrderVersion, receipt.ProductId, receipt.ExpectedProductVersion, receipt.PurchaseUnitId,
            receipt.LotId, receipt.LabelId, receipt.LabelCode, receipt.HeatNumber, receipt.CoilNumber, receipt.RequiredDocuments,
            "Company", CompanyA, "Company", CompanyA, receipt.LocationId, receipt.MovementId, receipt.QuantityKg,
            receipt.OccurredAtUtc) { InitialBlock = "Free" }, envelope, token);
    }
    private sealed class ReceiptPrelockHarness(PurchasingDbContext context, ServiceProvider services,
        ReceiptPrelockLocations locations) : IAsyncDisposable
    {
        public PurchaseGoodsReceiptBatchHost Batch { get; } = new(context, locations, services, TimeProvider.System);
        // The optional read-only screen adapters are not used by ExecuteAsync in these transaction facts.
        public PurchaseGoodsReceiptHost Single { get; } = new(context, null!, locations, null!, null!, services, TimeProvider.System);
        public static ReceiptPrelockHarness Create(Run run, FutureSupplyIds supply,
            ISalesOrderLineSerializationLockBinderV1? sales = null, IInventoryFutureAllocationReleasePortV1? releasePort = null)
        {
            var company = Company(CompanyA);
            var purchase = new PurchaseLineSerializationLockHostBinder(company);
            var services = new ServiceCollection()
                .AddSingleton<ISettingsPrerequisiteProvider>(GoodsReceiptSettings(run))
                .AddSingleton<ISalesOrderLineSerializationLockBinderV1>(sales ?? new SalesOrderLineSerializationLockHostBinder(company))
                .AddSingleton<IPurchaseLineSerializationLockBinderV1>(purchase)
                .AddSingleton<IInventoryFutureAllocationReleasePortV1>(releasePort ?? new InventoryFutureAllocationReleasePort(company, purchase, TimeProvider.System))
                .AddSingleton<IInventoryGoodsReceiptFutureSupplyPortV1>(new InventoryGoodsReceiptFutureSupplyPort(company, purchase,
                    new ReceiptPrelockDemand(supply), new ReceiptPrelockKilograms(), TimeProvider.System)).BuildServiceProvider();
            var context = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
                .UseSqlServer(run.Factory.DatabaseConnectionString).Options, company);
            return new(context, services, new ReceiptPrelockLocations());
        }
        public async ValueTask DisposeAsync() { await context.DisposeAsync(); await services.DisposeAsync(); }
    }
    private sealed class ReceiptPrelockLocations : IOrganizationLocationStatusReaderV1
    {
        public Task<OrganizationLocationStatusV1?> ReadAsync(Guid companyId, Guid locationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OrganizationLocationStatusV1?>(new(locationId, true, false));
    }
    private sealed class ReceiptPrelockSupply(FutureSupplyIds supply) : IInventoryFutureSupplySourceV1
    {
        public Task<InventoryFutureSupplySourceAnswerV1> GetAllocatableLineAsync(Guid orderId, Guid lineId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InventoryFutureSupplySourceAnswerV1(InventoryFutureSupplySourceOutcomeV1.Allocatable,
                new(supply.PurchaseOrder, supply.PurchaseLine, supply.Product, "product-v1", supply.Unit, "10", 3)));
    }
    private sealed class ReceiptPrelockDemand(FutureSupplyIds supply) : IInventoryFutureDemandSourceV1
    {
        public Task<InventoryFutureDemandSourceAnswerV1> GetDemandLineAsync(Guid orderId, Guid lineId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InventoryFutureDemandSourceAnswerV1(InventoryFutureDemandSourceOutcomeV1.Open,
                new(orderId, lineId, supply.Product, "product-v1", supply.Unit, "10")));
    }
    private sealed class ReceiptPrelockKilograms : IInventoryFutureUnitConverterV1
    {
        public Task<InventoryFutureUnitConversionV1> ConvertAsync(Guid productId, string productVersion, decimal quantity,
            Guid fromUnitId, Guid toUnitId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InventoryFutureUnitConversionV1(InventoryFutureUnitConversionOutcomeV1.Converted, quantity));
        public Task<InventoryFutureUnitConversionV1> ConvertToKilogramsAsync(Guid productId, string productVersion, decimal quantity,
            Guid unitId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InventoryFutureUnitConversionV1(InventoryFutureUnitConversionOutcomeV1.Converted, quantity));
    }
    private sealed class ReceiptPrelockFence : IInventoryFutureAllocationAuthorityFenceV1
    { public Task RequireStillAsync(CancellationToken cancellationToken = default) => Task.CompletedTask; }
    private sealed class ReceiptAfterStageFault : Exception;
    private sealed class ReceiptCapReadPause : DbCommandInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool paused;
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!paused && command.CommandText.Contains("[inventory].[Allocations]", StringComparison.Ordinal)
                && command.CommandText.Contains("SUM(", StringComparison.OrdinalIgnoreCase))
            {
                paused = true; Entered.TrySetResult();
                await Resume.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }
    private sealed class ReceiptObservedSalesLocks(ISalesOrderLineSerializationLockBinderV1 inner,
        CancellationTokenSource? cancel = null) : ISalesOrderLineSerializationLockBinderV1
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ISalesOrderLineSerializationLockV1 Bind(DbConnection connection, DbTransaction transaction) => new Observed(inner.Bind(connection, transaction), this);
        private void Observe() { Entered.TrySetResult(); cancel?.Cancel(); }
        private sealed class Observed(ISalesOrderLineSerializationLockV1 inner, ReceiptObservedSalesLocks owner) : ISalesOrderLineSerializationLockV1
        {
            public Task AcquireAsync(Guid lineId, CancellationToken cancellationToken = default)
            { owner.Observe(); return inner.AcquireAsync(lineId, cancellationToken); }
        }
    }
    private sealed class ReceiptReadSetPort(IInventoryFutureAllocationReleasePortV1 inner, Func<Task> afterFirstRead,
        bool growEveryPreparation = false)
        : IInventoryFutureAllocationReleasePortV1
    {
        public List<DbTransaction> Transactions { get; } = [];
        private bool changed;
        private bool GrowEveryPreparation => growEveryPreparation;
        private async Task AfterReadAsync() { if (!changed) { changed = true; await afterFirstRead(); } }
        public async Task<IInventoryFutureAllocationReleaseSessionV1> EnlistAsync(DbConnection connection, DbTransaction transaction,
            CancellationToken cancellationToken = default)
        {
            Transactions.Add(transaction);
            return new ReadSet(await inner.EnlistAsync(connection, transaction, cancellationToken), this);
        }
        private sealed class ReadSet(IInventoryFutureAllocationReleaseSessionV1 inner, ReceiptReadSetPort owner)
            : IInventoryFutureAllocationReleaseSessionV1
        {
            private int reads;
            public async Task<IReadOnlyList<InventoryFutureAllocationViewV1>> ListActiveByPurchaseLineAsync(Guid lineId, CancellationToken token = default)
            {
                var rows = await inner.ListActiveByPurchaseLineAsync(lineId, token);
                await owner.AfterReadAsync();
                if (owner.GrowEveryPreparation && ++reads == 2)
                    return [.. rows, rows[0] with { AllocationId = Guid.NewGuid(), SalesOrderLineId = Guid.NewGuid() }];
                return rows;
            }
            public Task<IReadOnlyList<InventoryFutureAllocationViewV1>> ListActiveByDemandLineAsync(Guid lineId, CancellationToken token = default) =>
                inner.ListActiveByDemandLineAsync(lineId, token);
            public Task<InventoryFutureAllocationViewV1> ReleaseAsync(InventoryFutureAllocationReleaseRequestV1 request, InventoryCommandMetadata metadata,
                CancellationToken token = default) => inner.ReleaseAsync(request, metadata, token);
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
}

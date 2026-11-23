using System.Data;
using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>SAT-S8: real Sales store + host participant + Inventory writer/session. Owner product/unit/supply/delivery
/// snapshots are explicitly synthetic. No test below claims a Purchasing business workflow or browser acceptance.</summary>
public sealed partial class SalesDraftApprovalSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Accepted_revision_and_real_inventory_reduction_commit_together_and_replay_writes_no_second_release()
    {
        await using var run = await Run.StartAsync();
        var (draftId, _) = await PrepareFutureDemandAcceptanceAsync(run);
        var seed = await SeedFutureDemandHostAsync(run, draftId);
        var called = 0;
        await using (var sales = run.Store(CompanyA))
        {
            var envelope = FutureDemandHostEnvelope(run, "host-accept");
            var result = await sales.Store.UpdateDraftStateWithTransactionAsync(envelope, Hash("host-accept"),
                SalesDraftCommands.RecordCustomerAcceptanceV1, draftId, (draft, now) => draft.RecordCustomerAcceptance(8, run.Actor, now),
                CustomerAccepted, async (draft, _, transaction, token) =>
                {
                    called++;
                    await FutureDemandHost(run).ReduceForAcceptedRevisionAsync(transaction, FutureDemandHostRequest(draft, envelope), token);
                });
            Assert.Equal((9L, 1), (result!.Draft.Version, result.Draft.CustomerAcceptance!.Revision));
            var replay = await sales.Store.UpdateDraftStateWithTransactionAsync(envelope, Hash("host-accept"),
                SalesDraftCommands.RecordCustomerAcceptanceV1, draftId, (_, _) => throw new InvalidOperationException("SENTETIK replay"),
                CustomerAccepted, (_, _, _, _) => throw new InvalidOperationException("SENTETIK duplicate owner call"));
            Assert.Equal(SalesDraftWriteOutcome.Replayed, replay!.Outcome);
        }
        Assert.Equal(1, called);
        Assert.Equal((1m, 0.25m, 2L), (await run.ScalarAsync<decimal>(
                "SELECT RemainingQuantity FROM inventory.FutureSupplyAllocations WHERE Id=@id", ("@id", seed.AllocationId)),
            await run.ScalarAsync<decimal>("SELECT ReleasedQuantity FROM inventory.FutureSupplyAllocations WHERE Id=@id", ("@id", seed.AllocationId)),
            await run.ScalarAsync<long>("SELECT Version FROM inventory.FutureSupplyAllocations WHERE Id=@id", ("@id", seed.AllocationId))));
        Assert.Equal(1m, await run.ScalarAsync<decimal>("SELECT ActiveAllocated FROM inventory.FutureSupplyLines WHERE PurchaseOrderLineId=@line",
            ("@line", seed.PurchaseLineId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OutboxMessages WHERE EventType=@event",
            ("@event", InventoryFutureAllocationReleasedOutboxV1.EventType)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=@event AND PermissionCode=@permission",
            ("@id", seed.AllocationId.ToString("D")), ("@event", InventoryFutureAllocationReleasedOutboxV1.EventType),
            ("@permission", SalesPermissionManifestV1.OrdersCustomerAcceptanceRecord)));
    }

    [SqlServerFact]
    public async Task A_sales_save_failure_rolls_back_real_inventory_allocation_ledger_release_audit_and_outbox()
    {
        await using var run = await Run.StartAsync();
        var (draftId, _) = await PrepareFutureDemandAcceptanceAsync(run);
        var seed = await SeedFutureDemandHostAsync(run, draftId);
        var envelope = FutureDemandHostEnvelope(run, "host-accept-fail");
        await using (var sales = run.Store(CompanyA, new FailAfterFutureDemandSave()))
            await Assert.ThrowsAsync<FutureDemandSaveFault>(() => sales.Store.UpdateDraftStateWithTransactionAsync(envelope,
                Hash("host-accept-fail"), SalesDraftCommands.RecordCustomerAcceptanceV1, draftId,
                (draft, now) => draft.RecordCustomerAcceptance(8, run.Actor, now), CustomerAccepted,
                (draft, _, transaction, token) => FutureDemandHost(run).ReduceForAcceptedRevisionAsync(transaction,
                    FutureDemandHostRequest(draft, envelope), token)));
        Assert.Equal((1.25m, 0m, 1L), (await run.ScalarAsync<decimal>(
                "SELECT RemainingQuantity FROM inventory.FutureSupplyAllocations WHERE Id=@id", ("@id", seed.AllocationId)),
            await run.ScalarAsync<decimal>("SELECT ReleasedQuantity FROM inventory.FutureSupplyAllocations WHERE Id=@id", ("@id", seed.AllocationId)),
            await run.ScalarAsync<long>("SELECT Version FROM inventory.FutureSupplyAllocations WHERE Id=@id", ("@id", seed.AllocationId))));
        Assert.Equal(1.25m, await run.ScalarAsync<decimal>("SELECT ActiveAllocated FROM inventory.FutureSupplyLines WHERE PurchaseOrderLineId=@line",
            ("@line", seed.PurchaseLineId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OutboxMessages WHERE EventType=@event",
            ("@event", InventoryFutureAllocationReleasedOutboxV1.EventType)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=@event",
            ("@id", seed.AllocationId.ToString("D")), ("@event", InventoryFutureAllocationReleasedOutboxV1.EventType)));
        await using var fresh = run.Store(CompanyA);
        var unchanged = (await fresh.Store.FindDraftAsync(draftId))!;
        Assert.Equal((8L, 0), (unchanged.Version, unchanged.CustomerAcceptance!.Revision));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE IdempotencyKeyHash=@key",
            ("@key", Hash("host-accept-fail").ToUpperInvariant())));
    }

    [SqlServerFact]
    public async Task A_racing_future_create_waits_for_accepted_reduction_and_retries_against_the_new_sales_cap()
    {
        await using var run = await Run.StartAsync();
        var (draftId, _) = await PrepareFutureDemandAcceptanceAsync(run);
        var seed = await SeedFutureDemandHostAsync(run, draftId);
        var schedule = new FutureDemandHostSchedule(block: true);
        var envelope = FutureDemandHostEnvelope(run, "host-racing-accept");
        await using var sales = run.Store(CompanyA);
        var accept = sales.Store.UpdateDraftStateWithTransactionAsync(envelope, Hash("host-racing-accept"),
            SalesDraftCommands.RecordCustomerAcceptanceV1, draftId, (draft, now) => draft.RecordCustomerAcceptance(8, run.Actor, now),
            CustomerAccepted, (draft, _, transaction, token) => FutureDemandHost(run, schedule).ReduceForAcceptedRevisionAsync(transaction,
                FutureDemandHostRequest(draft, envelope), token));
        try
        {
            await schedule.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var competingAudit = new AuditEventCollector();
            await using var competingInventory = FutureDemandHostInventory(run, competingAudit);
            await using var competingSales = run.Store(CompanyA);
            var writer = FutureDemandHostWriter(competingInventory, competingAudit, seed,
                new InventoryFutureDemandSource(competingSales.Port, new FutureDemandHostDeliveries()));
            var request = new CreateFutureSupplyAllocationV1(Guid.NewGuid(), draftId, seed.SalesLineId, seed.PurchaseOrderId,
                seed.PurchaseLineId, "0.001", KgUnit, "SENTETIK yarışan ayırma");
            var metadata = FutureDemandHostInventoryMetadata(run, "host-racing-create");
            var busy = await Assert.ThrowsAsync<InventoryAllocationException>(() => writer.CreateAsync(request, metadata, new FutureDemandHostFence()));
            Assert.Equal((409, "sales_order_line_busy"), (busy.Status, busy.Code));
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands WHERE IdempotencyKeyHash=@key",
                ("@key", Hash("host-racing-create").ToUpperInvariant())));
            schedule.Resume.TrySetResult();
            Assert.NotNull(await accept);
            // A fresh owner read under the same demand lock sees accepted revision 1. The same key was not consumed by busy.
            var cap = await Assert.ThrowsAsync<InventoryAllocationException>(() => writer.CreateAsync(request, metadata, new FutureDemandHostFence()));
            Assert.Equal((409, InventoryFutureAllocationCodesV1.ExceedsDemandOpenQuantity), (cap.Status, cap.Code));
            Assert.Equal(1m, await run.ScalarAsync<decimal>("SELECT ActiveAllocated FROM inventory.FutureSupplyLines WHERE PurchaseOrderLineId=@line",
                ("@line", seed.PurchaseLineId)));
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.FutureSupplyAllocations WHERE DemandId=@id", ("@id", draftId)));
        }
        finally
        {
            schedule.Resume.TrySetResult();
            await accept;
        }
    }

    private sealed record FutureDemandHostSeed(Guid AllocationId, Guid PurchaseOrderId, Guid PurchaseLineId, Guid SalesOrderId, Guid SalesLineId);
    private static async Task<FutureDemandHostSeed> SeedFutureDemandHostAsync(Run run, Guid draftId)
    {
        var audit = new AuditEventCollector();
        await using var inventory = FutureDemandHostInventory(run, audit);
        await inventory.Database.MigrateAsync();
        await using var sales = run.Store(CompanyA);
        var draft = (await sales.Store.FindDraftAsync(draftId))!;
        var seed = new FutureDemandHostSeed(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), draftId, draft.Lines[0].Id);
        var writer = FutureDemandHostWriter(inventory, audit, seed, new FutureDemandHostOriginalDemand(seed));
        var created = await writer.CreateAsync(new(seed.AllocationId, seed.SalesOrderId, seed.SalesLineId, seed.PurchaseOrderId,
            seed.PurchaseLineId, "1.25", KgUnit, "SENTETIK önceki kabul ayırması"),
            FutureDemandHostInventoryMetadata(run, "host-future-seed"), new FutureDemandHostFence());
        Assert.Equal("1.25", created.Allocation.RemainingQuantity);
        return seed;
    }
    private static InventoryDbContext FutureDemandHostInventory(Run run, AuditEventCollector audit)
    {
        var company = FutureDemandHostCompany();
        return new(new DbContextOptionsBuilder<InventoryDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", InventoryDbContext.Schema))
            .AddInterceptors(new AuditSaveChangesInterceptor(audit, company)).Options, company);
    }
    private static CompanyContext FutureDemandHostCompany() { var company = new CompanyContext(); company.Set(CompanyA); return company; }
    private static SalesDraftWriteEnvelope FutureDemandHostEnvelope(Run run, string key) => run.Envelope(key) with
        { PermissionCode = SalesPermissionManifestV1.OrdersCustomerAcceptanceRecord };
    private static SalesOrderFutureDemandReductionRequestV1 FutureDemandHostRequest(SalesOrderDraft draft, SalesDraftWriteEnvelope envelope) =>
        new(draft.Id, draft.Revision, draft.Lines.Select(line => new SalesOrderFutureDemandReductionLineV1(line.Id,
            line.Product.ProductId, line.Product.ProductVersion, line.OrderUnit.UnitId, line.QuantityText)).ToArray(),
            SalesOrderFutureDemandReductionTextsV1.AcceptedRevision, envelope);
    private static SalesFutureDemandReductionHostParticipant FutureDemandHost(Run run, FutureDemandHostSchedule? schedule = null)
    {
        var company = FutureDemandHostCompany();
        var purchase = new PurchaseLineSerializationLockHostBinder(company);
        return new(new SalesOrderLineSerializationLockHostBinder(company), purchase,
            new InventoryFutureAllocationReleasePort(company, purchase, TimeProvider.System, new FutureDemandHostKilograms(),
                schedule ?? new FutureDemandHostSchedule()), company, new FixedUser(run.Actor, CompanyA),
            new RecordingAuthority(SalesPermissionManifestV1.OrdersCustomerAcceptanceRecord));
    }
    private static InventoryFutureAllocationWriter FutureDemandHostWriter(InventoryDbContext context, AuditEventCollector audit, FutureDemandHostSeed seed,
        IInventoryFutureDemandSourceV1 demand)
    {
        var company = FutureDemandHostCompany();
        return new(context, company, audit, new SalesOrderLineSerializationLockHostBinder(company),
            new PurchaseLineSerializationLockHostBinder(company), new FutureDemandHostSupply(seed), demand,
            new FutureDemandHostKilograms(), TimeProvider.System);
    }
    private static InventoryCommandMetadata FutureDemandHostInventoryMetadata(Run run, string key) =>
        new(run.Actor, ErpPermissions.InventoryFutureAllocationWrite, ["SENTETIK-INVENTORY"], run.Correlation, null, key, "Web");
    private sealed class FutureDemandHostOriginalDemand(FutureDemandHostSeed seed) : IInventoryFutureDemandSourceV1
    {
        public Task<InventoryFutureDemandSourceAnswerV1> GetDemandLineAsync(Guid orderId, Guid lineId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InventoryFutureDemandSourceAnswerV1(InventoryFutureDemandSourceOutcomeV1.Open,
                new(seed.SalesOrderId, seed.SalesLineId, Product, "7", KgUnit, "1.25")));
    }
    private sealed class FutureDemandHostSupply(FutureDemandHostSeed seed) : IInventoryFutureSupplySourceV1
    {
        public Task<InventoryFutureSupplySourceAnswerV1> GetAllocatableLineAsync(Guid orderId, Guid lineId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InventoryFutureSupplySourceAnswerV1(InventoryFutureSupplySourceOutcomeV1.Allocatable,
                new(seed.PurchaseOrderId, seed.PurchaseLineId, Product, "7", KgUnit, "10", 1)));
    }
    private sealed class FutureDemandHostDeliveries : ISalesOrderDeliveryReadPortV1
    {
        public Task<SalesOrderDeliveryReadAnswerV1> ReadOrderDeliveriesAsync(Guid orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SalesOrderDeliveryReadAnswerV1(CompanyA, orderId, []));
    }
    private sealed class FutureDemandHostFence : IInventoryFutureAllocationAuthorityFenceV1
    { public Task RequireStillAsync(CancellationToken cancellationToken = default) => Task.CompletedTask; }
    private sealed class FutureDemandHostKilograms : IInventoryFutureUnitConverterV1
    {
        public Task<InventoryFutureUnitConversionV1> ConvertAsync(Guid productId, string productVersion, decimal quantity,
            Guid fromUnitId, Guid toUnitId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InventoryFutureUnitConversionV1(InventoryFutureUnitConversionOutcomeV1.Converted, quantity));
        public Task<InventoryFutureUnitConversionV1> ConvertToKilogramsAsync(Guid productId, string productVersion, decimal quantity,
            Guid fromUnitId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InventoryFutureUnitConversionV1(InventoryFutureUnitConversionOutcomeV1.Converted, quantity));
    }
    private sealed class FutureDemandHostSchedule(bool block = false) : IInventoryFutureSupplyScheduleSourceV1
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<InventoryFutureSupplyScheduleV1> GetLineScheduleAsync(Guid orderId, Guid lineId, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            if (block) await Resume.Task.WaitAsync(cancellationToken);
            return new(true, orderId, lineId, new DateOnly(2026, 12, 1));
        }
    }
}

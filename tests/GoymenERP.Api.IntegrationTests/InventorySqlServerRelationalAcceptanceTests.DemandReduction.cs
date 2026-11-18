using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Demand_reduction_updates_the_allocation_ledger_audit_and_outbox_in_the_host_transaction_and_rollback_restores_all()
    {
        await using var run = await Run.StartAsync();
        var supply = FutureSupplyIds.New();
        var allocationId = Guid.NewGuid();
        await run.ExecuteAsync(InsertFutureSupplyLine, supply.Line(CompanyA, 100m, 60m));
        await run.ExecuteAsync(InsertFutureSupplyAllocation, supply.Allocation(CompanyA, allocationId, 60m, 60m, 1));
        await using (var context = run.Context(CompanyA))
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await new SalesOrderLineSerializationLockHostBinder(Company(CompanyA))
                .Bind(context.Database.GetDbConnection(), transaction.GetDbTransaction()).AcquireAsync(supply.DemandLine);
            var port = new InventoryFutureAllocationReleasePort(Company(CompanyA),
                new PurchaseLineSerializationLockHostBinder(Company(CompanyA)), TimeProvider.System,
                new ReductionKilograms(), new ReductionSchedule());
            await using var session = await port.EnlistAsync(context.Database.GetDbConnection(), transaction.GetDbTransaction());
            var result = await session.ReduceDemandAsync(new(supply.DemandOrder, supply.DemandLine, supply.Product, "1",
                supply.Unit, "50", "SENTETIK talep azaltma"), ReductionMetadata());
            var changed = Assert.Single(result.ReleasedAllocations);
            Assert.Equal((allocationId, "50", "10", "2"),
                (changed.AllocationId, changed.RemainingQuantity, changed.ReleasedQuantity, changed.Version));
            Assert.Equal(1, await context.OutboxMessages.CountAsync());
            await transaction.RollbackAsync();
        }
        Assert.Equal(60m, await run.ScalarAsync<decimal>(
            "SELECT RemainingQuantity FROM inventory.FutureSupplyAllocations WHERE Id=@id", ("@id", allocationId)));
        Assert.Equal(60m, await run.ScalarAsync<decimal>(
            "SELECT ActiveAllocated FROM inventory.FutureSupplyLines WHERE PurchaseOrderLineId=@line", ("@line", supply.PurchaseLine)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OutboxMessages"));
    }

    [SqlServerFact]
    public async Task An_unreadable_schedule_wins_over_a_stale_selection_and_writes_no_allocation_or_event()
    {
        await using var run = await Run.StartAsync();
        var supply = FutureSupplyIds.New();
        var allocationId = Guid.NewGuid();
        await run.ExecuteAsync(InsertFutureSupplyLine, supply.Line(CompanyA, 100m, 60m));
        await run.ExecuteAsync(InsertFutureSupplyAllocation, supply.Allocation(CompanyA, allocationId, 60m, 60m, 1));
        await using var context = run.Context(CompanyA);
        await using var transaction = await context.Database.BeginTransactionAsync();
        await new SalesOrderLineSerializationLockHostBinder(Company(CompanyA))
            .Bind(context.Database.GetDbConnection(), transaction.GetDbTransaction()).AcquireAsync(supply.DemandLine);
        var port = new InventoryFutureAllocationReleasePort(Company(CompanyA),
            new PurchaseLineSerializationLockHostBinder(Company(CompanyA)), TimeProvider.System,
            new ReductionKilograms(), new InventoryUnavailableFutureSupplyScheduleSourceV1());
        await using var session = await port.EnlistAsync(context.Database.GetDbConnection(), transaction.GetDbTransaction());
        var refusal = await Assert.ThrowsAsync<InventoryAllocationException>(() => session.ReduceDemandAsync(
            new(supply.DemandOrder, supply.DemandLine, supply.Product, "1", supply.Unit, "50", "SENTETIK azaltma",
                [new(allocationId, 99, "10")]), ReductionMetadata()));
        Assert.Equal((503, InventoryFutureAllocationCodesV1.ServiceUnavailable), (refusal.Status, refusal.Code));
        await transaction.RollbackAsync();
        Assert.Equal(60m, await run.ScalarAsync<decimal>(
            "SELECT RemainingQuantity FROM inventory.FutureSupplyAllocations WHERE Id=@id", ("@id", allocationId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OutboxMessages"));
    }

    private static InventoryCommandMetadata ReductionMetadata() =>
        new("SENTETIK-actor", "sales.order.revise", [], "SENTETIK-reduction", null, "SENTETIK-reduction-key", SourceChannels.Web, null, null);

    private sealed class ReductionKilograms : IInventoryFutureUnitConverterV1
    {
        public Task<InventoryFutureUnitConversionV1> ConvertAsync(Guid productId, string productVersion, decimal quantity,
            Guid fromUnitId, Guid toUnitId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InventoryFutureUnitConversionV1(InventoryFutureUnitConversionOutcomeV1.Converted, quantity));
        public Task<InventoryFutureUnitConversionV1> ConvertToKilogramsAsync(Guid productId, string productVersion, decimal quantity,
            Guid fromUnitId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InventoryFutureUnitConversionV1(InventoryFutureUnitConversionOutcomeV1.Converted, quantity));
    }

    private sealed class ReductionSchedule : IInventoryFutureSupplyScheduleSourceV1
    {
        public Task<InventoryFutureSupplyScheduleV1> GetLineScheduleAsync(Guid purchaseOrderId, Guid purchaseOrderLineId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new InventoryFutureSupplyScheduleV1(true, purchaseOrderId, purchaseOrderLineId, new DateOnly(2026, 12, 1)));
    }
}

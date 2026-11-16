using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Api.Endpoints;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Batch_preflight_reads_the_later_owner_before_refusing_an_earlier_consumed_label_and_never_stages_a_row()
    {
        await using var run = await Run.StartAsync();
        var first = await SeedReceiptReversalAsync(run); var second = await SeedReceiptReversalAsync(run, 'c');
        await using (var setup = run.Context(CompanyA))
        {
            var physical = await setup.Allocations.SingleAsync(row => row.Id == first.PhysicalId);
            physical.Status = (int)StockAllocationStatus.Consumed; physical.RemainingKg = 0m; await setup.SaveChangesAsync();
        }
        var owners = new BatchReversalOwners([first, second], second.SalesLine);
        var refused = await Assert.ThrowsAsync<InventoryGoodsReceiptReversalException>(() => EvaluateReversalBatchAsync(run, [first, second], owners));
        Assert.Equal((503, InventoryGoodsReceiptReversalCodesV1.Unavailable), (refused.Status, refused.Code));
        Assert.Contains(second.SalesLine, owners.DemandReads);
        await using var after = run.Context(CompanyA);
        Assert.Empty(await after.GoodsReceiptReversals.ToListAsync()); Assert.Empty(await after.FutureSupplyConversionReversals.ToListAsync());
        Assert.False(await after.Commands.AnyAsync(row => row.CommandName == InventoryGoodsReceiptReversalCodesV1.CommandName));
        Assert.Equal(4m, await LabelBlockKgAsync(run, first.Receipt.LabelId, first.Receipt.InitialBlock));
        Assert.Equal(3m, (await after.Allocations.SingleAsync(row => row.Id == second.PhysicalId)).RemainingKg);
    }

    [SqlServerFact]
    public async Task A_successful_batch_preflight_preserves_all_physical_future_command_and_audit_state()
    {
        await using var run = await Run.StartAsync();
        var first = await SeedReceiptReversalAsync(run); var second = await SeedReceiptReversalAsync(run, 'c');
        var owners = new BatchReversalOwners([first, second]);
        await EvaluateReversalBatchAsync(run, [first, second], owners);
        await using var after = run.Context(CompanyA);
        Assert.Empty(await after.GoodsReceiptReversals.ToListAsync()); Assert.Empty(await after.FutureSupplyConversionReversals.ToListAsync());
        Assert.False(await after.Commands.AnyAsync(row => row.CommandName == InventoryGoodsReceiptReversalCodesV1.CommandName));
        Assert.All(await after.FutureSupplyAllocations.ToListAsync(), row => Assert.Equal((0m, 1L), (row.RemainingQuantity, row.Version)));
        Assert.All(await after.Allocations.ToListAsync(), row => Assert.Equal((3m, 1L), (row.RemainingKg, row.Version)));
        Assert.Equal(2, owners.DemandReads.Count);
    }

    private static async Task EvaluateReversalBatchAsync(Run run, IReadOnlyList<ReceiptReversalSeed> seeds, BatchReversalOwners owners)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString); await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var company = Company(CompanyA);
        var salesLocks = new SalesOrderLineSerializationLockHostBinder(company).Bind(connection, transaction);
        var purchaseLocks = new PurchaseLineSerializationLockHostBinder(company).Bind(connection, transaction);
        foreach (var id in seeds.Select(seed => seed.SalesLine).Distinct().Order()) await salesLocks.AcquireAsync(id);
        foreach (var id in seeds.Select(seed => seed.Receipt.PurchaseOrderLineId).Distinct().Order()) await purchaseLocks.AcquireAsync(id);
        await using var auditScope = run.Factory.Services.CreateAsyncScope();
        auditScope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        var auditWriter = auditScope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
        await using var participant = await InventoryGoodsReceiptReversalParticipant.EnlistAsync(connection, transaction, CompanyA,
            GoodsReceiptSettings(run), owners, new ReceiptPrelockKilograms(), owners,
            auditScope.ServiceProvider.GetRequiredService<IAuditEventCollector>(), auditWriter);
        try
        {
            await Assert.IsAssignableFrom<IInventoryGoodsReceiptReversalBatchParticipantV1>(participant).EvaluateBatchAsync(seeds.Select(seed =>
                new InventoryGoodsReceiptReversalBatchLineV1(ReceiptReversalRequest(seed.Receipt),
                    new(CompanyA, seed.Receipt.PurchaseOrderId, seed.Receipt.PurchaseOrderLineId, 5, "2", "3"), SupplierReturnMetadata(run))).ToArray());
            Assert.False(auditWriter.HasPendingDrafts); await transaction.CommitAsync();
        }
        catch { await transaction.RollbackAsync(); Assert.False(auditWriter.HasPendingDrafts); throw; }
    }

    private sealed class BatchReversalOwners(IReadOnlyList<ReceiptReversalSeed> seeds, Guid? unavailableDemand = null)
        : IInventoryFutureDemandSourceV1, IInventoryFutureSupplySourceV1
    {
        public List<Guid> DemandReads { get; } = [];
        public Task<InventoryFutureDemandSourceAnswerV1> GetDemandLineAsync(Guid orderId, Guid lineId, CancellationToken cancellationToken = default)
        {
            DemandReads.Add(lineId);
            if (lineId == unavailableDemand) return Task.FromResult(new InventoryFutureDemandSourceAnswerV1(InventoryFutureDemandSourceOutcomeV1.Unavailable, null));
            var seed = seeds.Single(seed => seed.SalesOrder == orderId && seed.SalesLine == lineId);
            return Task.FromResult(new InventoryFutureDemandSourceAnswerV1(InventoryFutureDemandSourceOutcomeV1.Open,
                new(orderId, lineId, seed.Receipt.ProductId, seed.Receipt.ExpectedProductVersion, seed.Receipt.PurchaseUnitId, "2")));
        }
        public Task<InventoryFutureSupplySourceAnswerV1> GetAllocatableLineAsync(Guid orderId, Guid lineId, CancellationToken cancellationToken = default)
        {
            var seed = seeds.Single(seed => seed.Receipt.PurchaseOrderId == orderId && seed.Receipt.PurchaseOrderLineId == lineId);
            return Task.FromResult(new InventoryFutureSupplySourceAnswerV1(InventoryFutureSupplySourceOutcomeV1.Allocatable,
                new(orderId, lineId, seed.Receipt.ProductId, seed.Receipt.ExpectedProductVersion, seed.Receipt.PurchaseUnitId, "3", 5)));
        }
    }
}

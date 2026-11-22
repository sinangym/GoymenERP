using System.Data.Common;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.FutureSupply;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// STK-S19 / 7B: real receipt, conversion/reversal persistence, transaction locks, physical movement, audit/outbox and rollback.
/// The post-adjustment Purchasing supply and current Sales demand snapshots are explicitly synthetic owner answers here;
/// the combined Purchasing adjustment host needs separate SQL facts. SENTETIK / NOT_ENV. Runtime NOT_RUN.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Receipt_reversal_refuses_a_different_current_demand_product_without_effects_and_the_same_key_can_retry()
    {
        await using var run = await Run.StartAsync();
        var seed = await SeedReceiptReversalAsync(run);
        var request = ReceiptReversalRequest(seed.Receipt); var metadata = SupplierReturnMetadata(run);
        var refused = await Assert.ThrowsAsync<InventoryGoodsReceiptReversalException>(() =>
            StageReceiptReversalAsync(run, seed, request, metadata, commit: true, wrongDemandProduct: true));
        Assert.Equal((503, InventoryGoodsReceiptReversalCodesV1.Unavailable), (refused.Status, refused.Code));
        await using (var context = run.Context(CompanyA))
        {
            Assert.Empty(await context.GoodsReceiptReversals.ToListAsync());
            Assert.False(await context.Commands.AnyAsync(row => row.CommandName == InventoryGoodsReceiptReversalCodesV1.CommandName));
            Assert.Equal(3m, (await context.Allocations.SingleAsync(row => row.Id == seed.PhysicalId)).RemainingKg);
        }
        Assert.False((await StageReceiptReversalAsync(run, seed, request, metadata, commit: true)).IsReplay);
    }

    [SqlServerFact]
    public async Task Receipt_reversal_of_a_bucket_without_stock_keeps_a_typed_refusal_and_rolls_back_preceding_deallocations()
    {
        await using var run = await Run.StartAsync();
        var seed = await SeedReceiptReversalAsync(run);
        var request = ReceiptReversalRequest(seed.Receipt) with { SourceBlock = BlockState.Blocked };
        var refused = await Assert.ThrowsAsync<InventoryGoodsReceiptReversalException>(() =>
            StageReceiptReversalAsync(run, seed, request, SupplierReturnMetadata(run), commit: true));
        Assert.Equal((409, InventoryGoodsReceiptReversalCodesV1.StockNotAvailable), (refused.Status, refused.Code));
        await using var context = run.Context(CompanyA);
        Assert.Empty(await context.GoodsReceiptReversals.ToListAsync());
        Assert.Empty(await context.FutureSupplyConversionReversals.ToListAsync());
        Assert.Equal(3m, (await context.Allocations.SingleAsync(row => row.Id == seed.PhysicalId)).RemainingKg);
        Assert.Equal(0m, (await context.FutureSupplyAllocations.SingleAsync(row => row.Id == seed.FutureId)).RemainingQuantity);
    }

    [SqlServerFact]
    public async Task The_receipt_reversal_migrations_Down_passes_with_original_receipts_and_refuses_while_reversal_history_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddInventoryGoodsReceiptReversals().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);
        var seed = await SeedReceiptReversalAsync(run);
        await run.ExecuteAsync(guard); // Original receipt and original conversion alone are harmless to this Down.
        var result = await StageReceiptReversalAsync(run, seed, ReceiptReversalRequest(seed.Receipt), SupplierReturnMetadata(run), commit: true);
        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51950, refused.Number);
        await using var context = run.Context(CompanyA);
        Assert.Equal(result.ReversalId, Assert.Single(await context.GoodsReceiptReversals.ToListAsync()).Id);
        Assert.Single(await context.FutureSupplyConversionReversals.ToListAsync());
    }

    [SqlServerFact]
    public async Task Receipt_reversal_reopens_only_the_fitting_share_keeps_prior_releases_and_original_links_and_replays_once()
    {
        await using var run = await Run.StartAsync();
        var seed = await SeedReceiptReversalAsync(run);
        var metadata = SupplierReturnMetadata(run);
        var request = ReceiptReversalRequest(seed.Receipt);
        var first = await StageReceiptReversalAsync(run, seed, request, metadata, commit: true);
        var link = Assert.Single(first.Conversions);
        Assert.Equal(("2", "1", "1", "0"),
            (link.UndoneQuantity, link.ReopenedQuantity, link.OverflowReleasedQuantity, link.UnheldQuantity));
        Assert.Equal(2m, await LabelBlockKgAsync(run, seed.Receipt.LabelId, BlockState.Free));
        await using (var context = run.Context(CompanyA))
        {
            var allocation = await context.FutureSupplyAllocations.SingleAsync(row => row.Id == seed.FutureId);
            Assert.Equal((1m, 2m, (int)FutureSupplyAllocationStatus.Active),
                (allocation.RemainingQuantity, allocation.ReleasedQuantity, allocation.Status));
            var original = await context.FutureSupplyConversions.SingleAsync(row => row.Id == seed.ConversionId);
            Assert.Equal((3m, 3m, seed.PhysicalId), (original.Quantity, original.PhysicalQuantityKg, original.PhysicalAllocationId));
            Assert.Single(await context.FutureSupplyConversionReversals.ToListAsync());
        }
        var replay = await StageReceiptReversalAsync(run, seed, request,
            metadata with { CorrelationId = "SENTETIK-reversal-replay" }, commit: true, unavailableOwners: true);
        Assert.True(replay.IsReplay);
        Assert.Equal(metadata.CorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(first.InventoryMovementId, replay.InventoryMovementId);
        Assert.Equal(2m, await LabelBlockKgAsync(run, seed.Receipt.LabelId, BlockState.Free));
    }

    [SqlServerFact]
    public async Task Receipt_reversal_host_rollback_removes_every_effect_and_the_same_key_can_then_succeed()
    {
        await using var run = await Run.StartAsync();
        var seed = await SeedReceiptReversalAsync(run);
        var metadata = SupplierReturnMetadata(run);
        var request = ReceiptReversalRequest(seed.Receipt);
        await StageReceiptReversalAsync(run, seed, request, metadata, commit: false);
        await using (var context = run.Context(CompanyA))
        {
            Assert.Empty(await context.GoodsReceiptReversals.ToListAsync());
            Assert.Empty(await context.FutureSupplyConversionReversals.ToListAsync());
            var future = await context.FutureSupplyAllocations.SingleAsync(row => row.Id == seed.FutureId);
            Assert.Equal((0m, 1m, 1L), (future.RemainingQuantity, future.ReleasedQuantity, future.Version));
            Assert.False(await context.Commands.AnyAsync(row => row.CommandName == InventoryGoodsReceiptReversalCodesV1.CommandName));
            Assert.False(await context.OutboxMessages.AnyAsync(row => row.EventType == InventoryGoodsReceiptReversalCodesV1.EventType));
        }
        Assert.Equal(4m, await LabelBlockKgAsync(run, seed.Receipt.LabelId, BlockState.Free));
        Assert.False((await StageReceiptReversalAsync(run, seed, request, metadata, commit: true)).IsReplay);
    }

    [SqlServerFact]
    public async Task Receipt_reversal_refuses_a_partial_release_of_a_piece_holding_allocation_without_effects()
    {
        await using var run = await Run.StartAsync();
        var seed = await SeedReceiptReversalAsync(run);
        await run.ExecuteAsync("UPDATE inventory.Allocations SET AllocatedPieces = 3, RemainingPieces = 3 WHERE Id = @id",
            ("@id", seed.PhysicalId));
        // Undoing 2 of the held 3 kg would release part of the hold; no rule says how many pieces that gives back.
        var refused = await Assert.ThrowsAsync<InventoryGoodsReceiptReversalException>(() => StageReceiptReversalAsync(run, seed,
            ReceiptReversalRequest(seed.Receipt), SupplierReturnMetadata(run), commit: true));
        Assert.Equal((422, InventoryGoodsReceiptReversalCodesV1.PartialQuantityNotRepresentable), (refused.Status, refused.Code));
        await using var context = run.Context(CompanyA);
        Assert.Empty(await context.GoodsReceiptReversals.ToListAsync());
        var physical = await context.Allocations.AsNoTracking().SingleAsync(row => row.Id == seed.PhysicalId);
        Assert.Equal((3m, 3m, 3, 3, 1L), (physical.AllocatedKg, physical.RemainingKg, physical.AllocatedPieces, physical.RemainingPieces,
            physical.Version));
    }

    [SqlServerFact]
    public async Task Receipt_reversal_that_releases_a_piece_holding_allocation_in_full_leaves_it_holding_no_pieces()
    {
        await using var run = await Run.StartAsync();
        var seed = await SeedReceiptReversalAsync(run);
        await run.ExecuteAsync("UPDATE inventory.Allocations SET AllocatedPieces = 3, RemainingPieces = 3 WHERE Id = @id",
            ("@id", seed.PhysicalId));
        var result = await StageReceiptReversalAsync(run, seed, ReceiptReversalRequest(seed.Receipt) with { QuantityKg = "3" },
            SupplierReturnMetadata(run), commit: true);
        Assert.False(result.IsReplay);
        await using var context = run.Context(CompanyA);
        var physical = await context.Allocations.AsNoTracking().SingleAsync(row => row.Id == seed.PhysicalId);
        Assert.Equal((0m, (int)StockAllocationStatus.Released, 3, 0), (physical.RemainingKg, physical.Status,
            physical.AllocatedPieces, physical.RemainingPieces));
    }

    private sealed record ReceiptReversalSeed(InventoryGoodsReceiptRequestV1 Receipt, Guid FutureId,
        Guid ConversionId, Guid PhysicalId, Guid SalesOrder, Guid SalesLine);

    private static async Task<ReceiptReversalSeed> SeedReceiptReversalAsync(Run run, char conversionKey = 'a')
    {
        var receipt = await ReceiveForSupplierReturnAsync(run, BlockState.Free);
        var seed = new ReceiptReversalSeed(receipt, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await using var context = run.Context(CompanyA);
        context.FutureSupplyLines.Add(new() { CompanyId = CompanyA, PurchaseOrderId = receipt.PurchaseOrderId,
            PurchaseOrderLineId = receipt.PurchaseOrderLineId, PurchaseUnitId = receipt.PurchaseUnitId,
            AllocatableSnapshot = 3m, ActiveAllocated = 0m, Version = 1, CreatedAtUtc = GoodsReceiptTime, UpdatedAtUtc = GoodsReceiptTime });
        context.FutureSupplyAllocations.Add(new() { Id = seed.FutureId, CompanyId = CompanyA,
            SourceKind = InventoryFutureAllocationKindsV1.PurchaseOrderLine, PurchaseOrderId = receipt.PurchaseOrderId,
            PurchaseOrderLineId = receipt.PurchaseOrderLineId, PurchaseUnitId = receipt.PurchaseUnitId,
            ProductId = receipt.ProductId, ProductVersion = receipt.ExpectedProductVersion,
            DemandKind = InventoryFutureAllocationKindsV1.SalesOrderLine, DemandId = seed.SalesOrder, DemandLineId = seed.SalesLine,
            AllocatedQuantity = 4m, RemainingQuantity = 0m, ReleasedQuantity = 1m, Status = (int)FutureSupplyAllocationStatus.Consumed,
            Version = 1, Reason = "SENTETIK original allocation", ReleaseReasonCode = "manual-release", ReleaseOrigin = "inventory",
            ReleaseReason = "SENTETIK earlier release", ReleasedAtUtc = GoodsReceiptTime,
            ObservedPurchaseOrderVersion = 3, CreatedAtUtc = GoodsReceiptTime, UpdatedAtUtc = GoodsReceiptTime });
        context.Allocations.Add(new() { Id = seed.PhysicalId, CompanyId = CompanyA, OwnerKind = (int)receipt.OwnerKind,
            OwnerId = receipt.OwnerId, CustodianKind = (int)receipt.CustodianKind, CustodianId = receipt.CustodianId,
            LocationId = receipt.LocationId, LabelId = receipt.LabelId, Block = (int)BlockState.Free,
            DemandKind = InventoryAllocationDemandKindsV1.SalesOrderLine, DemandId = seed.SalesOrder, DemandLineId = seed.SalesLine,
            DemandProductId = receipt.ProductId, DemandProductVersion = receipt.ExpectedProductVersion,
            AllocatedKg = 3m, RemainingKg = 3m, Status = (int)StockAllocationStatus.Active, Version = 1,
            Reason = "SENTETIK original conversion", CreatedAtUtc = GoodsReceiptTime, UpdatedAtUtc = GoodsReceiptTime });
        context.FutureSupplyConversions.Add(new() { Id = seed.ConversionId, CompanyId = CompanyA,
            AcceptanceId = receipt.AcceptanceId, FutureAllocationId = seed.FutureId, LabelId = receipt.LabelId,
            Outcome = (int)InventoryFutureSupplyConversionOutcomeV1.Converted, Quantity = 3m,
            PhysicalAllocationId = seed.PhysicalId, PhysicalQuantityKg = 3m, CreatedAtUtc = GoodsReceiptTime });
        context.Commands.Add(new() { CompanyId = CompanyA,
            CommandName = InventoryGoodsReceiptFutureSupplyV1.CommandName,
            IdempotencyKeyHash = new string(conversionKey, 64), PayloadHash = new string('b', 64),
            ActorId = "SENTETIK-conversion", TargetId = receipt.AcceptanceId,
            ResultJson = JsonSerializer.Serialize(new InventoryGoodsReceiptFutureSupplyResultV1(receipt.AcceptanceId,
                [new(seed.FutureId, nameof(InventoryFutureSupplyConversionOutcomeV1.Converted), "3", seed.PhysicalId, "3", null)], false),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            CorrelationId = "SENTETIK-conversion", RecordedAtUtc = GoodsReceiptTime });
        await context.SaveChangesAsync();
        return seed;
    }

    private static InventoryGoodsReceiptReversalRequestV1 ReceiptReversalRequest(InventoryGoodsReceiptRequestV1 receipt) =>
        new(CompanyA, Guid.NewGuid(), Guid.NewGuid(), receipt.AcceptanceId, Guid.NewGuid(),
            InventoryGoodsReceiptReversalKindV1.AcceptanceUndo, BlockState.Free, "2", null,
            "SENTETIK receipt correction", GoodsReceiptTime.AddHours(1));

    private static async Task<InventoryGoodsReceiptReversalResultV1> StageReceiptReversalAsync(Run run, ReceiptReversalSeed seed,
        InventoryGoodsReceiptReversalRequestV1 request, InventoryCommandMetadata metadata, bool commit, bool unavailableOwners = false,
        bool wrongDemandProduct = false)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var company = Company(CompanyA);
        await new SalesOrderLineSerializationLockHostBinder(company).Bind(connection, transaction).AcquireAsync(seed.SalesLine);
        await new PurchaseLineSerializationLockHostBinder(company).Bind(connection, transaction).AcquireAsync(seed.Receipt.PurchaseOrderLineId);
        var owners = new ReceiptReversalOwners(seed, unavailableOwners, wrongDemandProduct);
        await using var auditScope = run.Factory.Services.CreateAsyncScope();
        auditScope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        await using var participant = await InventoryGoodsReceiptReversalParticipant.EnlistAsync(connection, transaction,
            CompanyA, GoodsReceiptSettings(run), owners, new ReceiptPrelockKilograms(), owners,
            auditScope.ServiceProvider.GetRequiredService<IAuditEventCollector>(),
            auditScope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>());
        var result = await participant.StageAsync(request, new(CompanyA, seed.Receipt.PurchaseOrderId,
            seed.Receipt.PurchaseOrderLineId, 5, "2", "3"), metadata);
        if (commit) await transaction.CommitAsync(); else await transaction.RollbackAsync();
        return result;
    }

    private sealed class ReceiptReversalOwners(ReceiptReversalSeed seed, bool unavailable, bool wrongDemandProduct = false)
        : IInventoryFutureDemandSourceV1, IInventoryFutureSupplySourceV1
    {
        public Task<InventoryFutureDemandSourceAnswerV1> GetDemandLineAsync(Guid orderId, Guid lineId, CancellationToken cancellationToken = default) =>
            unavailable ? throw new InvalidOperationException("SENTETIK replay must not read current owners")
                : Task.FromResult(new InventoryFutureDemandSourceAnswerV1(InventoryFutureDemandSourceOutcomeV1.Open,
                    new(seed.SalesOrder, seed.SalesLine, wrongDemandProduct ? Guid.NewGuid() : seed.Receipt.ProductId, seed.Receipt.ExpectedProductVersion,
                        seed.Receipt.PurchaseUnitId, "2")));
        public Task<InventoryFutureSupplySourceAnswerV1> GetAllocatableLineAsync(Guid orderId, Guid lineId, CancellationToken cancellationToken = default) =>
            unavailable ? throw new InvalidOperationException("SENTETIK replay must not read current owners")
                : Task.FromResult(new InventoryFutureSupplySourceAnswerV1(InventoryFutureSupplySourceOutcomeV1.Allocatable,
                    new(seed.Receipt.PurchaseOrderId, seed.Receipt.PurchaseOrderLineId, seed.Receipt.ProductId,
                        seed.Receipt.ExpectedProductVersion, seed.Receipt.PurchaseUnitId, "3", 5)));
    }
}

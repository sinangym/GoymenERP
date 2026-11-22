using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// K3 düzeltme (tren-1) on real SQL Server. SAT-D463: a piece-holding allocation never moves without its pieces, so a move to a
/// kg-only label is refused with nothing written. KLN-D93 / KLN-D119: a scrap, a supplier-return exit and a receipt reversal
/// leave the active allocations' held pieces covered, not only their kilograms (example: label 3 pieces / 4 kg, allocation
/// 2 kg / 2 pieces; a 1 kg / 2 pieces exit leaves enough kilograms but one piece). K3-B P3-2: the same key with another piece
/// count is a payload mismatch. SENTETIK owners.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task SAT_D463_a_piece_holding_allocation_is_not_moved_to_a_kg_only_label_and_moves_with_its_pieces_to_a_counted_one()
    {
        await using var run = await Run.StartAsync();
        var counted = await SeedPieceSourceAsync(run, "SYN-D463-PIECES", 100m, 10);
        var plain = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-D463-KG-ONLY", 100m);
        var target = await SeedPieceSourceAsync(run, "SYN-D463-TARGET", 100m, 10);
        var create = AllocationRequest(counted, "40") with { PieceCount = 4 };
        InventoryPhysicalAllocationResultV1 created;
        await using (var scope = run.Store(CompanyA)) created = await scope.Store.CreateAsync(create, AllocationMetadata(run));

        ChangePhysicalSalesAllocationV1 MoveTo(PhysicalSource source, int? pieces) => new(created.Allocation.Version,
            create.SalesOrderId, create.SalesOrderLineId, create.ProductId, create.ProductVersion, source.LabelId,
            source.LabelVersion.ToString(), "40", "SENTETIK adetli tahsis tasima.", Guid.NewGuid(), pieces);

        foreach (var (pieces, code) in new[]
                 {
                     ((int?)null, InventoryAllocationCodesV1.PieceCountRequired),
                     (4, InventoryAllocationCodesV1.PieceCountNotTracked),
                 })
        {
            await using var scope = run.Store(CompanyA);
            var refused = await Assert.ThrowsAsync<InventoryAllocationException>(() =>
                scope.Store.ChangeAsync(create.AllocationId, MoveTo(plain, pieces), AllocationMetadata(run)));
            Assert.Equal((422, code), (refused.Status, refused.Code));
        }

        // Nothing moved: one active allocation still holds its 4 pieces on the counted label; the kg-only label holds none.
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations"));
        Assert.Equal(4, await run.ScalarAsync<int>(
            "SELECT RemainingPieces FROM inventory.Allocations WHERE Id = @id AND Status = 1 AND LabelId = @label",
            ("@id", create.AllocationId), ("@label", counted.LabelId)));

        // Without pieces a move to a counted label is refused too; with them it carries the pieces.
        await using (var scope = run.Store(CompanyA))
        {
            var missing = await Assert.ThrowsAsync<InventoryAllocationException>(() =>
                scope.Store.ChangeAsync(create.AllocationId, MoveTo(target, null), AllocationMetadata(run)));
            Assert.Equal((422, InventoryAllocationCodesV1.PieceCountRequired), (missing.Status, missing.Code));
        }

        InventoryPhysicalAllocationChangeResultV1 moved;
        await using (var scope = run.Store(CompanyA))
            moved = await scope.Store.ChangeAsync(create.AllocationId, MoveTo(target, 4), AllocationMetadata(run));
        Assert.Equal((target.LabelId, "40", (int?)4), (moved.Change.Allocation.LabelId, moved.Change.Allocation.RemainingKg,
            moved.Change.Allocation.RemainingPieces));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Allocations WHERE Id = @id AND Status = 3 AND RemainingPieces = 0",
            ("@id", create.AllocationId)));
    }

    [SqlServerFact]
    public async Task KLN_D93_a_scrap_that_would_leave_fewer_pieces_than_the_active_allocations_hold_is_refused()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedPieceSourceAsync(run, "SYN-D93-SCRAP", 100m, 10);
        await using (var scope = run.Store(CompanyA))
            await scope.Store.CreateAsync(AllocationRequest(source, "60") with { PieceCount = 6 }, AllocationMetadata(run));
        var version = await LabelVersionAsync(run, source.LabelId);

        // 30 kg / 5 pieces leaves 70 kg (enough for 60 kg) but only 5 pieces for the 6 held: refused, nothing written.
        await using (var scope = run.Store(CompanyA))
        {
            await AssertScrapRefused(scope.Store.RecordStockScrapAsync(new RecordInventoryStockScrapV1(Guid.NewGuid(), source.LabelId,
                    version.ToString(), "30", At.AddHours(1), "Sentetik adetli hurda.", ScrapPieceCount: 5),
                    Metadata(run) with { IdempotencyKey = $"d93-scrap-{Guid.NewGuid():N}" }, static _ => Task.CompletedTask),
                409, InventoryStockScrapCodesV1.AllocationConflict);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.StockScraps"));

        // 30 kg / 4 pieces leaves 70 kg / 6 pieces: both sides covered, the scrap is recorded.
        await using (var scope = run.Store(CompanyA))
        {
            var recorded = await scope.Store.RecordStockScrapAsync(new RecordInventoryStockScrapV1(Guid.NewGuid(), source.LabelId,
                    version.ToString(), "30", At.AddHours(1), "Sentetik adetli hurda.", ScrapPieceCount: 4),
                Metadata(run) with { IdempotencyKey = $"d93-scrap-{Guid.NewGuid():N}" }, static _ => Task.CompletedTask);
            Assert.Equal((10, 4), (recorded.Scrap.BookPieceCount, recorded.Scrap.ScrapPieceCount));
        }

        // The piece check holds half a fact in SQL too: under three-valued logic `[ScrapPieceCount] >= 0` on NULL was UNKNOWN and
        // passed the CHECK; both counts are now required to be present together.
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE inventory.StockScraps SET ScrapPieceCount = NULL WHERE LabelId = @id", ("@id", source.LabelId)))).Number);
    }

    [SqlServerFact]
    public async Task KLN_D93_a_piece_supplier_return_that_would_leave_fewer_pieces_than_the_allocations_hold_is_refused_and_its_pieces_join_the_hash()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedPieceInvoiceCostReceipt(run);
        await AllocatePieceLabelAsync(run, receipt.LabelId, 2m, 2);

        // 1 kg / 2 pieces leaves 3 kg (enough for 2 kg) but one piece for the two held.
        var request = SupplierReturnRequest(receipt, BlockState.Free, "1", "10") with { PieceCount = 2 };
        await AssertSupplierReturnRefused(run, request, SupplierReturnMetadata(run), 409, InventorySupplierReturnExitCodesV1.StockAllocated);
        Assert.Equal(4m, await LabelBlockKgAsync(run, receipt.LabelId, BlockState.Free));

        // 1 kg / 1 piece leaves 3 kg / 2 pieces: covered, the exit is staged.
        var accepted = request with { PieceCount = 1 };
        var metadata = SupplierReturnMetadata(run);
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventorySupplierReturnExitParticipant.EnlistAsync(connection, transaction, CompanyA,
                GoodsReceiptSettings(run));
            Assert.False((await participant.StageAsync(accepted, metadata)).IsReplay);
            await transaction.CommitAsync();
        }

        Assert.Equal((3m, 2), (await LabelBlockKgAsync(run, receipt.LabelId, BlockState.Free), await run.ScalarAsync<int>(
            "SELECT PieceCount FROM inventory.Balances WHERE LabelId = @id AND Block = @block",
            ("@id", receipt.LabelId), ("@block", (int)BlockState.Free))));
        // K3-B P3-2: the same key with another piece count is a payload mismatch, not a silent replay.
        await AssertSupplierReturnRefused(run, accepted with { PieceCount = 2 }, metadata, 409,
            InventorySupplierReturnExitCodesV1.IdempotencyConflict);
    }

    [SqlServerFact]
    public async Task KLN_D93_a_piece_receipt_reversal_that_would_leave_fewer_pieces_than_the_allocations_hold_is_refused()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedPieceInvoiceCostReceipt(run);
        await AllocatePieceLabelAsync(run, receipt.LabelId, 2m, 2);
        var request = ReceiptReversalRequest(receipt) with { QuantityKg = "1", PieceCount = 2 };

        var refused = await Assert.ThrowsAsync<InventoryGoodsReceiptReversalException>(() =>
            StagePieceReceiptReversalAsync(run, receipt, request, SupplierReturnMetadata(run)));

        Assert.Equal((409, InventoryGoodsReceiptReversalCodesV1.StockAllocated), (refused.Status, refused.Code));
        await using var verify = run.Context(CompanyA);
        Assert.Empty(await verify.GoodsReceiptReversals.ToListAsync());
        Assert.Equal(4m, await LabelBlockKgAsync(run, receipt.LabelId, BlockState.Free));
    }

    /// <summary>An active sales allocation holding <paramref name="pieces"/> of a piece-tracking label's free position.</summary>
    private static async Task AllocatePieceLabelAsync(Run run, Guid labelId, decimal kilograms, int pieces)
    {
        await using var context = run.Context(CompanyA);
        var balance = await context.Balances.AsNoTracking()
            .SingleAsync(row => row.LabelId == labelId && row.Block == (int)BlockState.Free);
        context.Allocations.Add(new AllocationRow
        {
            Id = Guid.NewGuid(), CompanyId = CompanyA,
            OwnerKind = balance.OwnerKind, OwnerId = balance.OwnerId,
            CustodianKind = balance.CustodianKind, CustodianId = balance.CustodianId,
            LocationId = balance.LocationId, LabelId = labelId, Block = (int)BlockState.Free,
            AllocatedKg = kilograms, RemainingKg = kilograms, AllocatedPieces = pieces, RemainingPieces = pieces,
            Status = (int)StockAllocationStatus.Active, Version = 1,
            CreatedAtUtc = GoodsReceiptTime, UpdatedAtUtc = GoodsReceiptTime,
        });
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// A receipt reversal of a receipt without future-supply conversions (no demand or supply owner is read); the participant
    /// alone, in one host transaction, rolled back on refusal.
    /// </summary>
    private static async Task<InventoryGoodsReceiptReversalResultV1> StagePieceReceiptReversalAsync(Run run,
        InventoryGoodsReceiptRequestV1 receipt, InventoryGoodsReceiptReversalRequestV1 request, InventoryCommandMetadata metadata)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await new PurchaseLineSerializationLockHostBinder(Company(CompanyA)).Bind(connection, transaction)
            .AcquireAsync(receipt.PurchaseOrderLineId);
        var owners = new ReceiptReversalOwners(new ReceiptReversalSeed(receipt, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid()), unavailable: true);
        await using var auditScope = run.Factory.Services.CreateAsyncScope();
        auditScope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        await using var participant = await InventoryGoodsReceiptReversalParticipant.EnlistAsync(connection, transaction,
            CompanyA, GoodsReceiptSettings(run), owners, new ReceiptPrelockKilograms(), owners,
            auditScope.ServiceProvider.GetRequiredService<IAuditEventCollector>(),
            auditScope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>());
        try
        {
            var result = await participant.StageAsync(request, new(CompanyA, receipt.PurchaseOrderId, receipt.PurchaseOrderLineId,
                long.Parse(receipt.PurchaseOrderVersionAfter, System.Globalization.CultureInfo.InvariantCulture), "0", "0"), metadata);
            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}

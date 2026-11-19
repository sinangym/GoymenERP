using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// B-19 / SAT-D306: the supplier-return exit port, proven on real SQL Server from a real goods receipt. The receipt
/// brings 4 kg in; Inventory resolves the label from that receipt and never lets the returns exceed it.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Supplier_return_exit_takes_the_received_blocked_kilograms_out_once_replays_and_host_rollback_removes_every_effect()
    {
        await using var run = await Run.StartAsync();
        var receipt = await ReceiveForSupplierReturnAsync(run, BlockState.Blocked);
        var metadata = SupplierReturnMetadata(run);
        var request = SupplierReturnRequest(receipt, BlockState.Blocked, "1.5", "41.5");

        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventorySupplierReturnExitParticipant.EnlistAsync(
                connection, transaction, CompanyA, GoodsReceiptSettings(run));
            Assert.False((await participant.StageAsync(request, metadata)).IsReplay);
            await transaction.RollbackAsync();
        }

        Assert.Equal(4m, await LabelBlockKgAsync(run, receipt.LabelId, BlockState.Blocked));
        Assert.Equal(0, await SupplierReturnExitEffectsAsync(run, metadata.CorrelationId));

        InventorySupplierReturnExitResultV1 first;
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventorySupplierReturnExitParticipant.EnlistAsync(
                connection, transaction, CompanyA, GoodsReceiptSettings(run));
            first = await participant.StageAsync(request, metadata);
            await transaction.CommitAsync();
        }

        Assert.False(first.IsReplay);
        Assert.Equal(receipt.MovementId, first.OriginalReceiptMovementId);
        Assert.Equal(receipt.LabelId, first.LabelId);
        Assert.Equal("1.5", first.QuantityKg);
        Assert.Equal("41.5", first.OriginalUnitCostTryPerKg);
        Assert.Equal(BlockState.Blocked, first.SourceBlock);
        Assert.Equal(2.5m, await LabelBlockKgAsync(run, receipt.LabelId, BlockState.Blocked));
        Assert.Equal(1, await run.ScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM inventory.Movements m
            JOIN inventory.MovementLines l ON l.MovementId = m.Id
            WHERE m.Id = @movement AND m.Kind = @kind AND m.SourceKind = @source
              AND m.SourceId = @receipt AND m.SourceLineId = @line AND m.SourceGrain = 2
              AND l.Direction = 2 AND l.Block = 2 AND l.QuantityKg = 1.5 AND l.LabelId = @label;
            """,
            ("@movement", first.InventoryMovementId), ("@kind", (int)StockMovementKind.SupplierReturnExit),
            ("@source", InventorySupplierReturnExitContractV1.SourceKind), ("@receipt", receipt.MovementId),
            ("@line", request.SupplierReturnLineId), ("@label", receipt.LabelId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.OutboxMessages WHERE Id = @id AND EventType = @event",
            ("@id", first.OutboxMessageId), ("@event", InventorySupplierReturnExitOutboxV1.EventType)));
        Assert.Equal("41.5", await run.ScalarAsync<string>(
            "SELECT JSON_VALUE(PayloadJson, '$.originalUnitCostTryPerKg') FROM inventory.OutboxMessages WHERE Id = @id",
            ("@id", first.OutboxMessageId)));
        Assert.Equal(3, await SupplierReturnExitEffectsAsync(run, metadata.CorrelationId));

        InventorySupplierReturnExitResultV1 replay;
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventorySupplierReturnExitParticipant.EnlistAsync(
                connection, transaction, CompanyA, GoodsReceiptSettings(run));
            replay = await participant.StageAsync(request, metadata with { CorrelationId = "supplier-return-retry" });
            await transaction.CommitAsync();
        }

        Assert.True(replay.IsReplay);
        Assert.Equal(metadata.CorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(first.InventoryMovementId, replay.InventoryMovementId);
        Assert.Equal(2.5m, await LabelBlockKgAsync(run, receipt.LabelId, BlockState.Blocked));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Kind = @kind", ("@kind", (int)StockMovementKind.SupplierReturnExit)));

        await AssertSupplierReturnRefused(run, request with { QuantityKg = "2" }, metadata,
            409, InventorySupplierReturnExitCodesV1.IdempotencyConflict);
    }

    [SqlServerFact]
    public async Task Supplier_return_exit_never_exceeds_the_receipt_the_requested_block_or_the_active_allocations()
    {
        await using var run = await Run.StartAsync();
        var receipt = await ReceiveForSupplierReturnAsync(run, BlockState.Free);

        await StageSupplierReturnAsync(run, SupplierReturnRequest(receipt, BlockState.Free, "3", null));
        Assert.Equal(1m, await LabelBlockKgAsync(run, receipt.LabelId, BlockState.Free));
        Assert.Equal(1, await run.ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM inventory.OutboxMessages
            WHERE EventType = @event AND JSON_VALUE(PayloadJson, '$.originalUnitCostTryPerKg') IS NULL;
            """,
            ("@event", InventorySupplierReturnExitOutboxV1.EventType)));

        await AssertSupplierReturnRefused(run, SupplierReturnRequest(receipt, BlockState.Free, "1.5", null),
            SupplierReturnMetadata(run), 409, InventorySupplierReturnExitCodesV1.ExitExceedsReceipt);
        await AssertSupplierReturnRefused(run, SupplierReturnRequest(receipt, BlockState.Blocked, "0.5", null),
            SupplierReturnMetadata(run), 409, InventorySupplierReturnExitCodesV1.StockNotAvailable);

        // KLN-D93: an exit that would eat into an active allocation is refused with its own code and never releases it.
        await AllocateSupplierReturnLabelAsync(run, receipt.LabelId, 0.5m);
        await AssertSupplierReturnRefused(run, SupplierReturnRequest(receipt, BlockState.Free, "1", null),
            SupplierReturnMetadata(run), 409, InventorySupplierReturnExitCodesV1.StockAllocated);
        Assert.Equal(0.5m, await run.ScalarAsync<decimal>(
            "SELECT SUM(RemainingKg) FROM inventory.Allocations WHERE LabelId = @label AND Status = @active",
            ("@label", receipt.LabelId), ("@active", (int)StockAllocationStatus.Active)));
        Assert.Equal(1m, await LabelBlockKgAsync(run, receipt.LabelId, BlockState.Free));

        await StageSupplierReturnAsync(run, SupplierReturnRequest(receipt, BlockState.Free, "0.5", null));
        Assert.Equal(0.5m, await LabelBlockKgAsync(run, receipt.LabelId, BlockState.Free));
        Assert.Equal(3.5m, await run.ScalarAsync<decimal>(
            """
            SELECT SUM(l.QuantityKg)
            FROM inventory.Movements m JOIN inventory.MovementLines l ON l.MovementId = m.Id
            WHERE m.Kind = @kind AND m.SourceId = @receipt;
            """, ("@kind", (int)StockMovementKind.SupplierReturnExit), ("@receipt", receipt.MovementId)));
    }

    [SqlServerFact]
    public async Task Supplier_return_exit_refuses_a_cost_difference_against_a_readable_average_and_writes_nothing()
    {
        // SAT-D371 (completes SAT-D306 (c)): until the Finance accounting-voucher spec names the account, a return whose
        // original cost would leave a cost difference against the readable moving average is refused; a return without
        // a difference proceeds. The ledger is read only, never written by the exit. SENTETIK figures.
        await using var run = await Run.StartAsync();
        var receipt = await ReceiveForSupplierReturnAsync(run, BlockState.Free);
        Assert.Equal(4m, await run.ScalarAsync<decimal>(
            """
            SELECT SUM(QuantityKg) FROM inventory.Balances
            WHERE LocationId = @location AND ProductId = @product AND OwnerKind = @company AND OwnerId = @owner;
            """,
            ("@location", receipt.LocationId), ("@product", receipt.ProductId),
            ("@company", (int)StockPartyKind.Company), ("@owner", CompanyA)));
        await InsertKnownAverageCostAsync(run, CompanyA, receipt.LocationId, receipt.ProductId, 4m, 10m);

        // 4 kg at 10 hold 40: 1 kg leaving at 50 leaves -10 with stock left; 4 kg at 12 leave -8 at zero stock.
        await AssertSupplierReturnRefused(run, SupplierReturnRequest(receipt, BlockState.Free, "1", "50"),
            SupplierReturnMetadata(run), 409, InventorySupplierReturnExitCodesV1.CostDifferenceUnposted);
        await AssertSupplierReturnRefused(run, SupplierReturnRequest(receipt, BlockState.Free, "4", "12"),
            SupplierReturnMetadata(run), 409, InventorySupplierReturnExitCodesV1.CostDifferenceUnposted);
        Assert.Equal(4m, await LabelBlockKgAsync(run, receipt.LabelId, BlockState.Free));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Kind = @kind", ("@kind", (int)StockMovementKind.SupplierReturnExit)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE CommandName = @name",
            ("@name", InventorySupplierReturnExitContractV1.CommandName)));

        // (40 - 1 * 10) / 3 = 10: no difference, so the return proceeds under the existing rules.
        await StageSupplierReturnAsync(run, SupplierReturnRequest(receipt, BlockState.Free, "1", "10"));
        Assert.Equal(3m, await LabelBlockKgAsync(run, receipt.LabelId, BlockState.Free));
        Assert.Equal(1, await run.ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM inventory.AverageCostLedger
            WHERE CompanyId = @company AND LocationId = @location AND ProductId = @product
              AND Version = 1 AND QuantityKg = 4 AND AverageCostPerKg = 10;
            """,
            ("@company", CompanyA), ("@location", receipt.LocationId), ("@product", receipt.ProductId)));
    }

    [SqlServerFact]
    public async Task Supplier_return_exit_refuses_an_unknown_or_foreign_acceptance_and_writes_nothing()
    {
        await using var run = await Run.StartAsync();
        var receipt = await ReceiveForSupplierReturnAsync(run, BlockState.Free);

        await AssertSupplierReturnRefused(run,
            SupplierReturnRequest(receipt, BlockState.Free, "1", null) with { GoodsAcceptanceId = Guid.NewGuid() },
            SupplierReturnMetadata(run), 409, InventorySupplierReturnExitCodesV1.ReceiptNotRecorded);
        await AssertSupplierReturnRefused(run, SupplierReturnRequest(receipt, BlockState.Free, "1", null),
            SupplierReturnMetadata(run), 404, InventorySupplierReturnExitCodesV1.InvalidReference, enlistAs: CompanyB);
        await AssertSupplierReturnRefused(run,
            SupplierReturnRequest(receipt, BlockState.Free, "1", null) with { CompanyId = CompanyB },
            SupplierReturnMetadata(run), 409, InventorySupplierReturnExitCodesV1.ReceiptNotRecorded,
            enlistAs: CompanyB);

        Assert.Equal(4m, await LabelBlockKgAsync(run, receipt.LabelId, BlockState.Free));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Kind = @kind", ("@kind", (int)StockMovementKind.SupplierReturnExit)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE CommandName = @name",
            ("@name", InventorySupplierReturnExitContractV1.CommandName)));
    }

    [SqlServerFact]
    public async Task Supplier_return_exit_migration_rolls_back_when_empty_and_blocks_after_an_exit()
    {
        const string previousMigration = "20261115191058_AddInventoryTransferReceipts";
        const string currentMigration = "20261115205912_AddInventorySupplierReturnExit";
        await using var run = await Run.StartAsync();
        await using (var context = run.Context(CompanyA))
        {
            var initialHistory = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            await context.GetService<IMigrator>().MigrateAsync(previousMigration);
            Assert.DoesNotContain(currentMigration, await context.Database.GetAppliedMigrationsAsync());
            await context.GetService<IMigrator>().MigrateAsync(currentMigration);
            await context.Database.MigrateAsync();
            Assert.Equal(initialHistory, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
        }

        // Exercise this historical guard without rolling back newer protected physical-validation facts.
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(
            new GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations.AddInventorySupplierReturnExit().DownOperations[0]).Sql;
        await ExecuteAsync(run, guard);

        var receipt = await ReceiveForSupplierReturnAsync(run, BlockState.Free);
        await StageSupplierReturnAsync(run, SupplierReturnRequest(receipt, BlockState.Free, "1", null));

        await using (var context = run.Context(CompanyA))
        {
            var appliedBefore = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            string[] proofQueries =
            [
                "SELECT * FROM inventory.NegativeStockValidationGuards ORDER BY CompanyId FOR JSON PATH",
                "SELECT * FROM inventory.Labels ORDER BY Id FOR JSON PATH",
                "SELECT * FROM inventory.Balances ORDER BY Id FOR JSON PATH",
                "SELECT * FROM inventory.Movements ORDER BY Id FOR JSON PATH",
                "SELECT * FROM inventory.Commands ORDER BY CompanyId, CommandName, IdempotencyKeyHash FOR JSON PATH",
                "SELECT * FROM inventory.OutboxMessages ORDER BY Id FOR JSON PATH",
                "SELECT * FROM audit.AuditEvents ORDER BY Id FOR JSON PATH",
            ];
            var proofBefore = await Task.WhenAll(proofQueries.Select(query => run.ScalarAsync<string>(query)));
            Assert.True(await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.NegativeStockValidationGuards") > 0);
            var rejected = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(run, guard));
            Assert.Equal(51654, rejected.Number);
            Assert.Equal(appliedBefore, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
            Assert.Equal(proofBefore, await Task.WhenAll(proofQueries.Select(query => run.ScalarAsync<string>(query))));
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Kind = @kind", ("@kind", (int)StockMovementKind.SupplierReturnExit)));
    }

    private static async Task<InventoryGoodsReceiptRequestV1> ReceiveForSupplierReturnAsync(Run run, BlockState block)
    {
        var location = await run.LocationOfAsync(CompanyA);
        var seed = await SeedGoodsReceiptPurchaseOrderAsync(run);
        var receipt = Receipt(seed, Guid.NewGuid(), "4", location) with { InitialBlock = block };
        return await StageGoodsReceiptAndCommitAsync(run, seed, receipt, "4",
            $"supplier-return-receipt-{Guid.NewGuid():N}", $"supplier-return-receipt-{Guid.NewGuid():N}");
    }

    private static InventorySupplierReturnExitRequestV1 SupplierReturnRequest(
        InventoryGoodsReceiptRequestV1 receipt, BlockState block, string quantity, string? cost) => new(
        CompanyA,
        Guid.NewGuid(),
        Guid.NewGuid(),
        receipt.AcceptanceId,
        Guid.NewGuid(),
        block,
        quantity,
        cost,
        GoodsReceiptTime.AddHours(1));

    private static InventoryCommandMetadata SupplierReturnMetadata(Run run) => new(
        run.Actor.ToString("D"),
        "purchasing.supplier-returns.record",
        ["SENTETIK-PURCHASING"],
        $"supplier-return-{Guid.NewGuid():N}",
        null,
        $"supplier-return-{Guid.NewGuid():N}",
        "Integration");

    private static async Task StageSupplierReturnAsync(Run run, InventorySupplierReturnExitRequestV1 request)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var participant = await InventorySupplierReturnExitParticipant.EnlistAsync(
            connection, transaction, CompanyA, GoodsReceiptSettings(run));
        Assert.False((await participant.StageAsync(request, SupplierReturnMetadata(run))).IsReplay);
        await transaction.CommitAsync();
    }

    private static async Task AssertSupplierReturnRefused(
        Run run,
        InventorySupplierReturnExitRequestV1 request,
        InventoryCommandMetadata metadata,
        int status,
        string code,
        Guid? enlistAs = null)
    {
        var company = enlistAs ?? CompanyA;
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var participant = await InventorySupplierReturnExitParticipant.EnlistAsync(
            connection, transaction, company, GoodsReceiptSettings(run, company));
        var refused = await Assert.ThrowsAsync<InventorySupplierReturnExitException>(
            () => participant.StageAsync(request, metadata));
        Assert.Equal((status, code), (refused.Status, refused.Code));
        await transaction.RollbackAsync();
    }

    private static async Task AllocateSupplierReturnLabelAsync(Run run, Guid labelId, decimal remainingKg)
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
            AllocatedKg = remainingKg, RemainingKg = remainingKg,
            Status = (int)StockAllocationStatus.Active, Version = 1,
            CreatedAtUtc = GoodsReceiptTime, UpdatedAtUtc = GoodsReceiptTime,
        });
        await context.SaveChangesAsync();
    }

    private static async Task<decimal> LabelBlockKgAsync(Run run, Guid labelId, BlockState block) =>
        await run.ScalarAsync<decimal>(
            "SELECT COALESCE(SUM(QuantityKg), 0) FROM inventory.Balances WHERE LabelId = @label AND Block = @block",
            ("@label", labelId), ("@block", (int)block));

    private static async Task<int> SupplierReturnExitEffectsAsync(Run run, string correlation) =>
        await run.ScalarAsync<int>(
            """
            SELECT
                (SELECT COUNT(*) FROM inventory.OutboxMessages WHERE EventType = @event AND CorrelationId = @correlation)
              + (SELECT COUNT(*) FROM inventory.Commands WHERE CommandName = @name AND CorrelationId = @correlation)
              + (SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = @event AND CorrelationId = @correlation);
            """,
            ("@event", InventorySupplierReturnExitOutboxV1.EventType),
            ("@name", InventorySupplierReturnExitContractV1.CommandName),
            ("@correlation", correlation));
}

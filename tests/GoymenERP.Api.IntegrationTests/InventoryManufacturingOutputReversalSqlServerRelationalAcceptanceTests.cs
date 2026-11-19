using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F-009 D1 (URT-D28, URT-D31; STK-S20): the manufacturing output reversal, the label disposition port and the Free kg
/// port on real SQL Server. The reversal is a new linked movement (kind 10) out of the output's original Free position;
/// the old receipt stays, and only free unallocated kilograms there can be reversed.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Manufacturing_output_reversal_takes_free_output_kg_once_replays_and_never_exceeds_the_output()
    {
        await using var run = await Run.StartAsync();
        var (orderId, output) = await SeedReversibleOutputAsync(run, "SYN-REV-HAPPY", BlockState.Free);
        Assert.Equal("2", output.LabelVersion);

        var request = ReversalRequest(orderId, output, "2", "5");
        var metadata = ManufacturingMetadata(run, "reversal");
        var reversed = await CommitReversalAsync(run, request, metadata);
        Assert.False(reversed.IsReplay);
        Assert.Equal(("3", output.MovementId, "5"), (reversed.LabelVersion, reversed.ReversedReceiptMovementId,
            reversed.QuantityKg));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Id = @movement AND Kind = 10 AND CompanyId = @company "
            + "AND SourceKind = @kind AND SourceId = @order AND SourceLineId = @reversal AND CostStatus = 1",
            ("@movement", request.MovementId), ("@company", CompanyA),
            ("@kind", InventoryManufacturingOutputReversalContractV1.OutputReversalSourceKind),
            ("@order", orderId), ("@reversal", request.ReversalId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.MovementLines WHERE MovementId = @movement AND Direction = 2 "
            + "AND Block = 1 AND QuantityKg = 5", ("@movement", request.MovementId)));
        // The old output receipt stays exactly as it was; net output = receipt − reversals.
        Assert.Equal(20m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.MovementLines WHERE MovementId = @movement",
            ("@movement", output.MovementId)));
        Assert.Equal(15m, await run.ScalarAsync<decimal>(
            "SELECT SUM(QuantityKg) FROM inventory.Balances WHERE LabelId = @label", ("@label", output.LabelId)));

        var replay = await CommitReversalAsync(run, request, metadata with { CorrelationId = "reversal-retry" });
        Assert.True(replay.IsReplay);
        Assert.Equal(metadata.CorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Kind = 10 AND SourceId = @order", ("@order", orderId)));

        await AssertReversalRefusalAsync(run, ReversalRequest(orderId, output, "3", "15.001"),
            ManufacturingMetadata(run, "reversal-over"), 409, InventoryManufacturingOutputReversalCodesV1.ExceedsProduced);
        await AssertReversalRefusalAsync(run, ReversalRequest(orderId, output, "2", "1"),
            ManufacturingMetadata(run, "reversal-stale"), 409, InventoryManufacturingRealizationCodesV1.LabelVersionConflict);
        await AssertReversalRefusalAsync(run, ReversalRequest(Guid.NewGuid(), output, "3", "1"),
            ManufacturingMetadata(run, "reversal-other-order"), 404,
            InventoryManufacturingRealizationCodesV1.InvalidReference);
        await AssertReversalRefusalAsync(run, ReversalRequest(orderId, output, "3", "1") with { OutputId = Guid.NewGuid() },
            ManufacturingMetadata(run, "reversal-other-output"), 404,
            InventoryManufacturingRealizationCodesV1.InvalidReference);
        // The same idempotency key with another payload is a conflict, never a second movement.
        await AssertReversalRefusalAsync(run, ReversalRequest(orderId, output, "3", "1"),
            metadata with { CorrelationId = "reversal-other-payload" }, 409,
            InventoryManufacturingRealizationCodesV1.IdempotencyConflict);

        // The rest of the output is reversed: the label is out of stock and the total equals what was produced.
        var rest = await CommitReversalAsync(run, ReversalRequest(orderId, output, "3", "15"),
            ManufacturingMetadata(run, "reversal-rest"));
        Assert.Equal("4", rest.LabelVersion);
        Assert.Equal(20m, await run.ScalarAsync<decimal>(
            "SELECT SUM(l.QuantityKg) FROM inventory.MovementLines l JOIN inventory.Movements m "
            + "ON m.CompanyId = l.CompanyId AND m.Id = l.MovementId WHERE m.Kind = 10 AND m.SourceId = @order",
            ("@order", orderId)));
        var disposition = await ReadDispositionAsync(run, CompanyA, CompanyA, output.LabelId);
        Assert.Equal(InventoryLabelDispositionStatusV1.Value, disposition.Status);
        Assert.Equal((InventoryLabelStockStateV1.OutOfStock, "0", "4"), (disposition.Disposition!.State,
            disposition.Disposition.PhysicalKg, disposition.Disposition.LabelVersion));
        Assert.Equal(("20", "20", "0"), (disposition.Disposition.Output!.ProducedKg,
            disposition.Disposition.Output.ReversedKg, disposition.Disposition.Output.FreeUnallocatedAtOutputPositionKg));
        await AssertReversalRefusalAsync(run, ReversalRequest(orderId, output, "4", "0.001"),
            ManufacturingMetadata(run, "reversal-after-all"), 409,
            InventoryManufacturingOutputReversalCodesV1.ExceedsProduced);
    }

    [SqlServerFact]
    public async Task Manufacturing_output_reversal_names_allocated_issued_and_blocked_kg_and_writes_nothing()
    {
        await using var run = await Run.StartAsync();

        // Allocated: a Sales allocation holds 5 of the 20 kg, so only 15 are reversible.
        var (allocatedOrder, allocatedOutput) = await SeedReversibleOutputAsync(run, "SYN-REV-ALLOC", BlockState.Free);
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.CreateAsync(AllocationRequest(
                new PhysicalSource(allocatedOutput.LabelId, allocatedOutput.ProductId, 2), "5"),
                Metadata(run));
        }

        var allocatedVersion = (await ReadDispositionAsync(run, CompanyA, CompanyA, allocatedOutput.LabelId))
            .Disposition!.LabelVersion;
        await AssertReversalRefusalAsync(run, ReversalRequest(allocatedOrder, allocatedOutput, allocatedVersion, "15.001"),
            ManufacturingMetadata(run, "reversal-allocated"), 409, InventoryManufacturingOutputReversalCodesV1.Allocated);
        var free = await ReadFreeQuantityAsync(run, CompanyA, CompanyA, allocatedOutput.LabelId);
        Assert.Equal(("15", 1), (free.Value!.FreeKg, free.Value.FreePositionCount));
        var fits = await CommitReversalAsync(run,
            ReversalRequest(allocatedOrder, allocatedOutput, allocatedVersion, "15"),
            ManufacturingMetadata(run, "reversal-allocated-fits"));
        Assert.False(fits.IsReplay);

        // Issued: the output label fed another order, so those kilograms are no longer at the output position.
        var (issuedOrder, issuedOutput) = await SeedReversibleOutputAsync(run, "SYN-REV-ISSUED", BlockState.Free);
        await CommitIssueAsync(run, IssueRequest(Guid.NewGuid(), issuedOutput.LabelId, "2", "8"),
            ManufacturingMetadata(run, "reversal-issue-elsewhere"));
        await AssertReversalRefusalAsync(run, ReversalRequest(issuedOrder, issuedOutput, "3", "12.001"),
            ManufacturingMetadata(run, "reversal-issued"), 409,
            InventoryManufacturingOutputReversalCodesV1.IssuedToManufacturing);

        // Blocked: an output stocked into the quality-blocked bucket cannot be reversed while blocked.
        var (blockedOrder, blockedOutput) = await SeedReversibleOutputAsync(run, "SYN-REV-BLOCKED", BlockState.Blocked);
        await AssertReversalRefusalAsync(run, ReversalRequest(blockedOrder, blockedOutput, "2", "1"),
            ManufacturingMetadata(run, "reversal-blocked"), 409, InventoryManufacturingOutputReversalCodesV1.Blocked);
        var blocked = await ReadDispositionAsync(run, CompanyA, CompanyA, blockedOutput.LabelId);
        Assert.Equal(("20", "20", "0"), (blocked.Disposition!.PhysicalKg, blocked.Disposition.BlockedKg,
            blocked.Disposition.Output!.FreeUnallocatedAtOutputPositionKg));
        Assert.Equal("0", (await ReadFreeQuantityAsync(run, CompanyA, CompanyA, blockedOutput.LabelId)).Value!.FreeKg);

        // Refusals wrote nothing: no reversal movement and no command for the refused orders.
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Kind = 10 AND SourceId IN (@issued, @blocked)",
            ("@issued", issuedOrder), ("@blocked", blockedOrder)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Kind = 10 AND SourceId = @order", ("@order", allocatedOrder)));
    }

    [SqlServerFact]
    public async Task Label_disposition_and_free_quantity_ports_read_only_the_resolved_company()
    {
        await using var run = await Run.StartAsync();
        var (orderId, output) = await SeedReversibleOutputAsync(run, "SYN-REV-PORTS", BlockState.Free);

        var disposition = await ReadDispositionAsync(run, CompanyA, CompanyA, output.LabelId);
        Assert.Equal(InventoryLabelDispositionStatusV1.Value, disposition.Status);
        var value = disposition.Disposition!;
        Assert.Equal((CompanyA, output.LabelId, Product, "2", InventoryLabelStockStateV1.InStock, "20"),
            (value.CompanyId, value.LabelId, value.ProductId, value.LabelVersion, value.State, value.PhysicalKg));
        Assert.Equal((orderId, output.OutputId, output.MovementId, "20", "0", "20"), (value.Output!.ManufacturingOrderId,
            value.Output.OutputId, value.Output.ReceiptMovementId, value.Output.ProducedKg, value.Output.ReversedKg,
            value.Output.FreeUnallocatedAtOutputPositionKg));
        var free = await ReadFreeQuantityAsync(run, CompanyA, CompanyA, output.LabelId);
        Assert.Equal((InventoryLabelDispositionStatusV1.Value, "20", 1, "2"), (free.Status, free.Value!.FreeKg,
            free.Value.FreePositionCount, free.Value.LabelVersion));

        // An unknown label is NotFound, never an empty disposition.
        var unknown = await ReadDispositionAsync(run, CompanyA, CompanyA, Guid.NewGuid());
        Assert.Equal((InventoryLabelDispositionStatusV1.NotFound, null), (unknown.Status, unknown.Disposition));

        // Another company never sees the label, neither by its own context nor by naming company A.
        Assert.Equal(InventoryLabelDispositionStatusV1.NotFound,
            (await ReadDispositionAsync(run, CompanyB, CompanyB, output.LabelId)).Status);
        Assert.Equal(InventoryLabelDispositionStatusV1.NotFound,
            (await ReadDispositionAsync(run, CompanyB, CompanyA, output.LabelId)).Status);
        Assert.Equal(InventoryLabelDispositionStatusV1.NotFound,
            (await ReadFreeQuantityAsync(run, CompanyB, CompanyB, output.LabelId)).Status);
        Assert.Equal(InventoryLabelDispositionStatusV1.NotFound,
            (await ReadFreeQuantityAsync(run, CompanyB, CompanyA, output.LabelId)).Status);

        // A read writes nothing.
        Assert.Equal(2L, await run.ScalarAsync<long>(
            "SELECT Version FROM inventory.Labels WHERE Id = @label", ("@label", output.LabelId)));
    }

    [SqlServerFact]
    public async Task Manufacturing_output_reversal_migration_down_refuses_while_a_reversal_exists()
    {
        await using var run = await Run.StartAsync();
        var (orderId, output) = await SeedReversibleOutputAsync(run, "SYN-REV-ROLLBACK", BlockState.Free);
        await CommitReversalAsync(run, ReversalRequest(orderId, output, "2", "1"),
            ManufacturingMetadata(run, "reversal-rollback"));

        await using var context = run.Context(CompanyA);
        var appliedBefore = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        const string guardProofSql = "SELECT CompanyId, Version FROM inventory.NegativeStockValidationGuards ORDER BY CompanyId FOR JSON PATH";
        const string movementProofSql = "SELECT * FROM inventory.Movements ORDER BY Id FOR JSON PATH";
        const string labelProofSql = "SELECT * FROM inventory.Labels ORDER BY Id FOR JSON PATH";
        var guardProof = await run.ScalarAsync<string>(guardProofSql);
        var movementProof = await run.ScalarAsync<string>(movementProofSql);
        var labelProof = await run.ScalarAsync<string>(labelProofSql);
        Assert.True(await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.NegativeStockValidationGuards") > 0);
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(
            new GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations.AddInventoryManufacturingOutputReversal().DownOperations[0]).Sql;
        // Test this historical guard directly; retain the newer immutable physical-validation proof.
        var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(run, guard));
        Assert.Equal(51420, error.Number);
        Assert.Equal(appliedBefore, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
        Assert.Equal(guardProof, await run.ScalarAsync<string>(guardProofSql));
        Assert.Equal(movementProof, await run.ScalarAsync<string>(movementProofSql));
        Assert.Equal(labelProof, await run.ScalarAsync<string>(labelProofSql));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.__EFMigrationsHistory "
            + "WHERE MigrationId = N'20261115123925_AddInventoryManufacturingOutputReversal'"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Kind = 10 AND SourceId = @order", ("@order", orderId)));
    }

    /// <summary>A 100 kg parent, 30 kg issued to a new order and a 20 kg output declared from it.</summary>
    private static async Task<(Guid OrderId, InventoryManufacturingOutputResultV1 Output)> SeedReversibleOutputAsync(
        Run run, string code, BlockState initialBlock)
    {
        var parent = await SeedManufacturingParentAsync(run, code, 100m);
        var orderId = Guid.NewGuid();
        var issued = await CommitIssueAsync(run, IssueRequest(orderId, parent.Label.Id, "2", "30"),
            ManufacturingMetadata(run, "reversal-seed-issue"));
        var output = await CommitOutputAsync(run,
            OutputRequest(orderId, parent.Label.Id, issued.ParentLabelVersion, parent.LocationId, "20")
                with { InitialBlock = initialBlock },
            ManufacturingMetadata(run, "reversal-seed-output"));
        return (orderId, output);
    }

    private static InventoryManufacturingOutputReversalRequestV1 ReversalRequest(Guid orderId,
        InventoryManufacturingOutputResultV1 output, string labelVersion, string quantity) => new(
        CompanyA, orderId, output.OutputId, Guid.NewGuid(), "5", output.LabelId, labelVersion, Guid.NewGuid(),
        quantity, ManufacturingAt.AddMinutes(4));

    private static async Task<InventoryManufacturingOutputReversalResultV1> CommitReversalAsync(
        Run run, InventoryManufacturingOutputReversalRequestV1 request, InventoryCommandMetadata metadata)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using var participant = await InventoryManufacturingRealizationParticipant.EnlistAsync(
            connection, transaction, CompanyA, ManufacturingSettings(run));
        var result = await participant.StageOutputReversalAsync(request, metadata);
        await transaction.CommitAsync();
        return result;
    }

    private static async Task AssertReversalRefusalAsync(Run run,
        InventoryManufacturingOutputReversalRequestV1 request, InventoryCommandMetadata metadata,
        int status, string code)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using var participant = await InventoryManufacturingRealizationParticipant.EnlistAsync(
            connection, transaction, CompanyA, ManufacturingSettings(run));
        var error = await Assert.ThrowsAsync<InventoryManufacturingRealizationException>(() =>
            participant.StageOutputReversalAsync(request, metadata));
        Assert.Equal((status, code), (error.Status, error.Code));
        await transaction.RollbackAsync();
    }

    private static async Task<InventoryLabelDispositionReadV1> ReadDispositionAsync(Run run, Guid resolvedCompany,
        Guid askedCompany, Guid labelId)
    {
        await using var context = run.Context(resolvedCompany);
        var company = new CompanyContext();
        company.Set(resolvedCompany);
        return await new InventoryLabelDispositionReadPort(context, company).GetAsync(askedCompany, labelId);
    }

    private static async Task<InventoryFreeQuantityReadV1> ReadFreeQuantityAsync(Run run, Guid resolvedCompany,
        Guid askedCompany, Guid labelId)
    {
        await using var context = run.Context(resolvedCompany);
        var company = new CompanyContext();
        company.Set(resolvedCompany);
        return await new InventoryFreeQuantityReadPort(context, company).GetAsync(askedCompany, labelId);
    }
}

using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Customer_return_stages_one_original_label_effect_replays_and_host_rollback_removes_every_effect()
    {
        await using var run = await Run.StartAsync();
        var shipment = await RecordShipmentAsync(run, "SYN-CUSTOMER-RETURN-ATOMIC", "40");
        var metadata = Metadata(run) with
        {
            IdempotencyKey = $"customer-return-{Guid.NewGuid():N}",
            CorrelationId = $"customer-return-{Guid.NewGuid():N}",
        };
        var request = ReturnRequest(shipment, "15");

        InventoryCustomerReturnResultV1 first;
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventoryCustomerReturnParticipant.EnlistAsync(
                connection, transaction, CompanyA, Settings(run, CompanyA));
            first = await participant.StageAsync(request, metadata);
            await transaction.CommitAsync();
        }

        Assert.False(first.IsReplay);
        Assert.Equal(shipment.ShipmentRequest.MovementId, first.OriginalShipmentMovementId);
        Assert.Equal(shipment.ShipmentRequest.LabelId, first.OriginalLabelId);
        Assert.Equal("15", first.QuantityKg);
        Assert.Equal(75m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @label AND Block = 1",
            ("@label", first.OriginalLabelId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM inventory.Movements
            WHERE Id = @movement AND Kind = @kind AND SourceKind = @source
              AND SourceId = @original AND SourceLineId = @return;
            """,
            ("@movement", first.InventoryMovementId), ("@kind", (int)StockMovementKind.Return),
            ("@source", InventoryCustomerReturnContractV1.SourceKind),
            ("@original", first.OriginalShipmentMovementId), ("@return", first.CustomerReturnId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.OutboxMessages WHERE Id = @id", ("@id", first.OutboxMessageId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE CommandName = @name AND CorrelationId = @correlation",
            ("@name", InventoryCustomerReturnContractV1.CommandName), ("@correlation", metadata.CorrelationId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = @event AND CorrelationId = @correlation",
            ("@event", InventoryCustomerReturnOutboxV1.EventType), ("@correlation", metadata.CorrelationId)));

        InventoryCustomerReturnResultV1 replay;
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventoryCustomerReturnParticipant.EnlistAsync(
                connection, transaction, CompanyA, Settings(run, CompanyA));
            replay = await participant.StageAsync(request, metadata);
            await transaction.CommitAsync();
        }

        Assert.True(replay.IsReplay);
        Assert.Equal(first, replay with { IsReplay = false });

        var countsBeforeRollback = await ReturnEffectsAsync(run, first.OriginalLabelId);
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventoryCustomerReturnParticipant.EnlistAsync(
                connection, transaction, CompanyA, Settings(run, CompanyA));
            await participant.StageAsync(ReturnRequest(shipment, "5"), Metadata(run) with
            {
                IdempotencyKey = $"customer-return-rollback-{Guid.NewGuid():N}",
                CorrelationId = $"customer-return-rollback-{Guid.NewGuid():N}",
            });
            await transaction.RollbackAsync();
        }

        Assert.Equal(countsBeforeRollback, await ReturnEffectsAsync(run, first.OriginalLabelId));
    }

    [SqlServerFact]
    public async Task Customer_return_caps_the_exact_shipment_and_changed_replay_or_foreign_source_writes_nothing()
    {
        await using var run = await Run.StartAsync();
        var shipment = await RecordShipmentAsync(run, "SYN-CUSTOMER-RETURN-CAP", "40");
        var metadata = Metadata(run) with
        {
            IdempotencyKey = $"customer-return-cap-{Guid.NewGuid():N}",
            CorrelationId = $"customer-return-cap-{Guid.NewGuid():N}",
        };
        var request = ReturnRequest(shipment, "30");
        await CommitReturnAsync(run, request, metadata);
        var before = await ReturnEffectsAsync(run, shipment.ShipmentRequest.LabelId);

        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventoryCustomerReturnParticipant.EnlistAsync(
                connection, transaction, CompanyA, Settings(run, CompanyA));
            var conflict = await Assert.ThrowsAsync<InventoryCustomerReturnException>(() =>
                participant.StageAsync(request with { QuantityKg = "29" }, metadata));
            Assert.Equal((409, InventoryCustomerReturnCodesV1.IdempotencyConflict),
                (conflict.Status, conflict.Code));
            await transaction.RollbackAsync();
        }

        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventoryCustomerReturnParticipant.EnlistAsync(
                connection, transaction, CompanyA, Settings(run, CompanyA));
            var exceeded = await Assert.ThrowsAsync<InventoryCustomerReturnException>(() => participant.StageAsync(
                ReturnRequest(shipment, "11"), Metadata(run) with
                {
                    IdempotencyKey = $"customer-return-exceed-{Guid.NewGuid():N}",
                }));
            Assert.Equal((409, InventoryCustomerReturnCodesV1.ReturnExceedsShipment),
                (exceeded.Status, exceeded.Code));
            await transaction.RollbackAsync();
        }

        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventoryCustomerReturnParticipant.EnlistAsync(
                connection, transaction, CompanyA, Settings(run, CompanyA));
            var hidden = await Assert.ThrowsAsync<InventoryCustomerReturnException>(() => participant.StageAsync(
                ReturnRequest(shipment, "1") with { SourceShipmentId = Guid.NewGuid() }, Metadata(run) with
                {
                    IdempotencyKey = $"customer-return-hidden-{Guid.NewGuid():N}",
                }));
            Assert.Equal((409, InventoryCustomerReturnCodesV1.ShipmentNotRecorded),
                (hidden.Status, hidden.Code));
            await transaction.RollbackAsync();
        }

        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventoryCustomerReturnParticipant.EnlistAsync(
                connection, transaction, CompanyA, Settings(run, CompanyA));
            var hidden = await Assert.ThrowsAsync<InventoryCustomerReturnException>(() => participant.StageAsync(
                ReturnRequest(shipment, "1") with { CompanyId = CompanyB }, Metadata(run) with
                {
                    IdempotencyKey = $"customer-return-company-{Guid.NewGuid():N}",
                }));
            Assert.Equal((404, InventoryCustomerReturnCodesV1.InvalidReference),
                (hidden.Status, hidden.Code));
            await transaction.RollbackAsync();
        }

        var malformed = await RecordShipmentAsync(run, "SYN-CUSTOMER-RETURN-MALFORMED", "10");
        await run.ExecuteAsync(
            """
            INSERT INTO inventory.MovementLines
                (CompanyId, MovementId, LineNumber, Direction, OwnerKind, OwnerId, CustodianKind,
                 CustodianId, LocationId, LabelId, ProductId, Block, QuantityKg)
            SELECT CompanyId, MovementId, 2, Direction, OwnerKind, OwnerId, CustodianKind,
                   CustodianId, LocationId, LabelId, ProductId, Block, QuantityKg
            FROM inventory.MovementLines
            WHERE CompanyId = @company AND MovementId = @movement AND LineNumber = 1;
            """, ("@company", CompanyA), ("@movement", malformed.ShipmentRequest.MovementId));
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventoryCustomerReturnParticipant.EnlistAsync(
                connection, transaction, CompanyA, Settings(run, CompanyA));
            var invalid = await Assert.ThrowsAsync<InventoryCustomerReturnException>(() => participant.StageAsync(
                ReturnRequest(malformed, "1"), Metadata(run) with
                {
                    IdempotencyKey = $"customer-return-malformed-{Guid.NewGuid():N}",
                }));
            Assert.Equal((503, InventoryCustomerReturnCodesV1.ShipmentTraceInvalid),
                (invalid.Status, invalid.Code));
            await transaction.RollbackAsync();
        }

        Assert.Equal(before, await ReturnEffectsAsync(run, shipment.ShipmentRequest.LabelId));
    }

    [SqlServerFact]
    public async Task Concurrent_customer_returns_leave_only_one_bounded_effect()
    {
        await using var run = await Run.StartAsync();
        var shipment = await RecordShipmentAsync(run, "SYN-CUSTOMER-RETURN-RACE", "40");
        var before = await ReturnEffectsAsync(run, shipment.ShipmentRequest.LabelId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        var ready = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inputs = Enumerable.Range(0, 2).Select(_ => (
            Request: ReturnRequest(shipment, "25"),
            Metadata: Metadata(run) with
            {
                IdempotencyKey = $"customer-return-race-{Guid.NewGuid():N}",
                CorrelationId = $"customer-return-race-{Guid.NewGuid():N}",
            })).ToArray();

        async Task<(InventoryCustomerReturnResultV1? Result, InventoryCustomerReturnException? Error)> AttemptAsync(
            InventoryCustomerReturnRequestV1 request, InventoryCommandMetadata metadata)
        {
            try
            {
                await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
                await connection.OpenAsync(token);
                await using var transaction = await connection.BeginTransactionAsync(token);
                await using var participant = await InventoryCustomerReturnParticipant.EnlistAsync(
                    connection, transaction, CompanyA, Settings(run, CompanyA), cancellationToken: token);
                // Synchronize callers before StageAsync acquires its transaction-owned company mutex.
                // A post-lock rendezvous cannot be reached by both callers while the first holds that lock.
                if (Interlocked.Increment(ref ready) == 2)
                    release.TrySetResult();
                await release.Task.WaitAsync(token);
                try
                {
                    var result = await participant.StageAsync(request, metadata, token);
                    await transaction.CommitAsync(token);
                    return (result, null);
                }
                catch (InventoryCustomerReturnException exception)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    return (null, exception);
                }
            }
            finally
            {
                // Setup or provider failure must also release the other caller; unexpected exceptions
                // remain test failures rather than being fabricated into a domain refusal.
                release.TrySetResult();
            }
        }

        (InventoryCustomerReturnResultV1? Result, InventoryCustomerReturnException? Error)[] attempts;
        try
        {
            attempts = await Task.WhenAll(inputs.Select(input => AttemptAsync(input.Request, input.Metadata)));
        }
        finally
        {
            timeout.Cancel();
            release.TrySetResult();
        }
        var winner = Assert.Single(attempts, attempt => attempt.Result is not null).Result!;
        var refused = Assert.Single(attempts, attempt => attempt.Error is not null).Error!;
        Assert.Equal(409, refused.Status);
        // The loser can observe the committed 25 kg under the mutex, or lose optimistic concurrency.
        // Both are existing participant refusals; raw provider errors are deliberately not caught.
        Assert.Contains(refused.Code, new[]
        {
            InventoryCustomerReturnCodesV1.ReturnExceedsShipment,
            InventoryCustomerReturnCodesV1.ConcurrentChange,
        });
        var after = await ReturnEffectsAsync(run, shipment.ShipmentRequest.LabelId);
        Assert.Equal(before with
        {
            LabelVersion = before.LabelVersion + 1,
            BalanceKg = before.BalanceKg + 25m,
            ReturnMovements = before.ReturnMovements + 1,
            Commands = before.Commands + 1,
            OutboxMessages = before.OutboxMessages + 1,
            AuditEvents = before.AuditEvents + 1,
        }, after);
        var winningInput = inputs.Single(input => input.Request.CustomerReturnId == winner.CustomerReturnId);
        var replay = await CommitReturnAsync(run, winningInput.Request, winningInput.Metadata);
        Assert.True(replay.IsReplay);
        Assert.Equal(winner, replay with { IsReplay = false });
        Assert.Equal(after, await ReturnEffectsAsync(run, shipment.ShipmentRequest.LabelId));
        Assert.Equal(25m, await run.ScalarAsync<decimal>(
            """
            SELECT SUM(line.QuantityKg)
            FROM inventory.Movements movement
            JOIN inventory.MovementLines line
              ON line.CompanyId = movement.CompanyId AND line.MovementId = movement.Id
            WHERE movement.Kind = @kind AND movement.SourceKind = @source
              AND movement.SourceId = @original;
            """,
            ("@kind", (int)StockMovementKind.Return),
            ("@source", InventoryCustomerReturnContractV1.SourceKind),
            ("@original", shipment.ShipmentRequest.MovementId)));
        Assert.Equal(85m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @label AND Block = 1",
            ("@label", shipment.ShipmentRequest.LabelId)));
    }

    private static async Task<CustomerReturnShipment> RecordShipmentAsync(Run run, string code, string quantityKg)
    {
        var state = await SeedAllocationAsync(run, code);
        var request = Request(state, quantityKg);
        var metadata = Metadata(run) with
        {
            IdempotencyKey = $"customer-return-shipment-{Guid.NewGuid():N}",
            CorrelationId = $"customer-return-shipment-{Guid.NewGuid():N}",
        };
        InventoryDefiniteShipmentResultV1 result;
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventoryDefiniteShipmentParticipant.EnlistAsync(
                connection, transaction, CompanyA, Settings(run, CompanyA));
            result = await participant.StageAsync(request, metadata);
            await transaction.CommitAsync();
        }

        return new(request, result);
    }

    private static InventoryCustomerReturnRequestV1 ReturnRequest(CustomerReturnShipment shipment, string quantityKg) =>
        new(CompanyA, Guid.NewGuid(), shipment.ShipmentRequest.ShipmentId,
            shipment.ShipmentRequest.LoadingInstructionLineId, Guid.NewGuid(), quantityKg, At.AddMinutes(1));

    private static async Task<InventoryCustomerReturnResultV1> CommitReturnAsync(
        Run run,
        InventoryCustomerReturnRequestV1 request,
        InventoryCommandMetadata metadata)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var participant = await InventoryCustomerReturnParticipant.EnlistAsync(
            connection, transaction, CompanyA, Settings(run, CompanyA));
        var result = await participant.StageAsync(request, metadata);
        await transaction.CommitAsync();
        return result;
    }

    private static async Task<CustomerReturnEffects> ReturnEffectsAsync(Run run, Guid labelId) => new(
        await run.ScalarAsync<long>("SELECT Version FROM inventory.Labels WHERE Id = @label", ("@label", labelId)),
        await run.ScalarAsync<decimal>("SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @label AND Block = 1",
            ("@label", labelId)),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE Kind = @kind AND SourceKind = @source",
            ("@kind", (int)StockMovementKind.Return), ("@source", InventoryCustomerReturnContractV1.SourceKind)),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands WHERE CommandName = @name",
            ("@name", InventoryCustomerReturnContractV1.CommandName)),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OutboxMessages WHERE EventType = @event",
            ("@event", InventoryCustomerReturnOutboxV1.EventType)),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = @event",
            ("@event", InventoryCustomerReturnOutboxV1.EventType)));

    private sealed record CustomerReturnShipment(
        InventoryDefiniteShipmentRequestV1 ShipmentRequest,
        InventoryDefiniteShipmentResultV1 ShipmentResult);

    private sealed record CustomerReturnEffects(
        long LabelVersion,
        decimal BalanceKg,
        int ReturnMovements,
        int Commands,
        int OutboxMessages,
        int AuditEvents);
}

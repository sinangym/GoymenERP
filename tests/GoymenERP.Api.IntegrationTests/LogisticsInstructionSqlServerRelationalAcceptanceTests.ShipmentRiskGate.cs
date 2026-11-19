using System.Net;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Application.Outbox;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// STK-S27 on real SQL Server (L-1..L-7, FNS-D30, SAT-D310, SAT-D285, LOJ-D19): the hosted definite shipment reads the Sales
/// owner's customer risk gate on every execution. Within the limit it ships and writes, in the SAME save, one
/// <c>logistics.definite-shipment.recorded.v1</c> outbox row (no amount) and consumes no exception. A stop without a valid
/// single-shipment exception is a recorded terminal 409 with no Logistics, Inventory or outbox effect, and its replay never reads
/// the gate again. A valid exception lets one shipment through and is consumed once; a second shipment of the order with the same
/// exception is the terminal <c>logistics_shipment_risk_exception_used</c>, and the database key refuses a second use even from
/// a writer that skipped the store. The migration's Down refuses with 51787 while any outbox event exists. SENTETIK.
/// </summary>
public sealed partial class LogisticsInstructionSqlServerRelationalAcceptanceTests
{
    private const string OutboxCount = "SELECT COUNT(*) FROM logistics.OutboxMessages";
    private const string ExceptionUseCount = "SELECT COUNT(*) FROM logistics.ShipmentRiskExceptionUses";

    [SqlServerFact]
    public async Task A_hosted_shipment_within_the_risk_limit_ships_with_one_outbox_event_in_the_same_save_and_its_replay_writes_nothing_more()
    {
        await using var run = await Run.StartAsync();
        var fixture = await SeedHostedShipmentAsync(run, "SYN-STK27-WITHIN");
        var grant = await GrantRealizationAsync(run, $"SYN-STK27-within-{Guid.NewGuid():N}");
        var gate = new HostedRiskGate();
        await using var host = HostedShipmentHost(run, fixture, new HostedAllocationReads(fixture), grant.Subject, riskGate: gate);
        using var client = HostedClientFor(host, grant.Subject, CompanyA);
        var request = CreateHostedShipmentRequest(fixture, "1", "stk27-within");

        using (var shipped = await client.SendAsync(request.Message))
        {
            Assert.Equal(HttpStatusCode.OK, shipped.StatusCode);
        }

        Assert.Equal((fixture.Owners.Order, fixture.InstructionId), Assert.Single(gate.Requests));
        Assert.Equal(1, await run.ScalarAsync<int>(OutboxCount));
        Assert.Equal(
            $"{LogisticsDefiniteShipmentOutboxV1.RecordedEventType}|{LogisticsDefiniteShipmentOutboxV1.Version}|{request.ShipmentId:D}|{CompanyA:D}|{request.CorrelationId}",
            await run.ScalarAsync<string>("SELECT CONCAT(EventType COLLATE Latin1_General_100_BIN2, N'|', SchemaVersion, N'|', CONVERT(nvarchar(36), SubjectId), N'|', "
                + "CONVERT(nvarchar(36), CompanyId), N'|', CorrelationId COLLATE Latin1_General_100_BIN2) FROM logistics.OutboxMessages"),
            ignoreCase: true);
        var payload = await run.ScalarAsync<string>("SELECT PayloadJson FROM logistics.OutboxMessages");
        Assert.Contains($"\"shipmentId\":\"{request.ShipmentId:D}\"", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"\"salesOrderId\":\"{fixture.Owners.Order:D}\"", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"riskExceptionNo\":null", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("amount", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("kg", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await run.ScalarAsync<int>(ExceptionUseCount));
        // The shipment's audit names what the gate let through (no amount) in the same save as the share and the outbox row.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = @correlation "
            + "AND EventType = N'logistics.loading-instruction.shipment-recorded.v1' AND AfterJson LIKE N'%\"shipmentRiskGate\"%' "
            + "AND AfterJson LIKE N'%\"result\":\"within\"%'", ("@correlation", request.CorrelationId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingShares WHERE ShareId = @id",
            ("@id", request.ShipmentId)));

        var replay = CreateHostedShipmentRequest(fixture, "1", "stk27-within", request.ShipmentId, request.MovementId,
            correlationId: $"SYN-STK27-replay-{Guid.NewGuid():N}");
        using (var replayed = await client.SendAsync(replay.Message))
        {
            Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
            Assert.Equal("true", Assert.Single(replayed.Headers.GetValues("Idempotency-Replayed")));
        }

        Assert.Single(gate.Requests);
        Assert.Equal(1, await run.ScalarAsync<int>(OutboxCount));
    }

    [SqlServerFact]
    public async Task A_hosted_shipment_over_the_risk_limit_without_a_valid_exception_is_a_recorded_terminal_refusal_with_no_effect()
    {
        await using var run = await Run.StartAsync();
        var fixture = await SeedHostedShipmentAsync(run, "SYN-STK27-OVER");
        var grant = await GrantRealizationAsync(run, $"SYN-STK27-over-{Guid.NewGuid():N}");
        var gate = new HostedRiskGate
        {
            Result = SalesShipmentRiskResultV1.Over,
            HoldReason = SalesShipmentRiskHoldReasonV1.OverLimit,
            Status = SalesShipmentRiskExceptionStatusV1.Pending,
        };
        await using var host = HostedShipmentHost(run, fixture, new HostedAllocationReads(fixture), grant.Subject, riskGate: gate);
        using var client = HostedClientFor(host, grant.Subject, CompanyA);
        var request = CreateHostedShipmentRequest(fixture, "1", "stk27-over");
        var before = await HostedEffectsAsync(run, fixture, request);

        using (var refused = await client.SendAsync(request.Message))
        {
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            var body = await refused.Content.ReadAsStringAsync();
            Assert.Contains($"\"code\":\"{LogisticsShipmentRiskGateCodesV1.OverLimit}\"", body, StringComparison.Ordinal);
            Assert.Contains(LogisticsShipmentRiskGateCodesV1.ExceptionErrorField, body, StringComparison.Ordinal);
            Assert.Contains("pending", body, StringComparison.Ordinal);
        }

        var recorded = await HostedEffectsAsync(run, fixture, request);
        Assert.Equal(before, recorded with { ShipmentAttemptCount = before.ShipmentAttemptCount });
        Assert.Equal(before.ShipmentAttemptCount + 1, recorded.ShipmentAttemptCount);
        Assert.Equal(0, await run.ScalarAsync<int>(OutboxCount));
        Assert.Equal(0, await run.ScalarAsync<int>(ExceptionUseCount));

        // The same key replays the recorded refusal with the original correlation and never reads the gate again.
        var replay = CreateHostedShipmentRequest(fixture, "1", "stk27-over", request.ShipmentId, request.MovementId,
            correlationId: $"SYN-STK27-over-replay-{Guid.NewGuid():N}");
        using (var replayed = await client.SendAsync(replay.Message))
        {
            Assert.Equal(HttpStatusCode.Conflict, replayed.StatusCode);
            Assert.Equal("true", Assert.Single(replayed.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(request.CorrelationId, Assert.Single(replayed.Headers.GetValues("Idempotency-Original-Correlation-Id")));
            Assert.Contains($"\"code\":\"{LogisticsShipmentRiskGateCodesV1.OverLimit}\"", await replayed.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }

        Assert.Single(gate.Requests);
        Assert.Equal(recorded, await HostedEffectsAsync(run, fixture, request));
    }

    [SqlServerFact]
    public async Task A_valid_single_shipment_exception_lets_one_shipment_through_and_its_second_use_is_a_terminal_refusal_held_by_the_key()
    {
        await using var run = await Run.StartAsync();
        var fixture = await SeedHostedShipmentAsync(run, "SYN-STK27-EXCEPTION");
        var grant = await GrantRealizationAsync(run, $"SYN-STK27-exception-{Guid.NewGuid():N}");
        var gate = new HostedRiskGate
        {
            Result = SalesShipmentRiskResultV1.Over,
            HoldReason = SalesShipmentRiskHoldReasonV1.OverLimit,
            Status = SalesShipmentRiskExceptionStatusV1.Valid,
            ExceptionNo = 7,
            ValidOn = Day,
        };
        await using var host = HostedShipmentHost(run, fixture, new HostedAllocationReads(fixture), grant.Subject, riskGate: gate);
        using var client = HostedClientFor(host, grant.Subject, CompanyA);
        var first = CreateHostedShipmentRequest(fixture, "1", "stk27-exception-first");

        using (var shipped = await client.SendAsync(first.Message))
        {
            Assert.Equal(HttpStatusCode.OK, shipped.StatusCode);
        }

        Assert.Equal($"7|{first.ShipmentId:D}|{fixture.Owners.Order:D}",
            await run.ScalarAsync<string>("SELECT CONCAT(ExceptionNo, N'|', CONVERT(nvarchar(36), ShipmentId), N'|', "
                + "CONVERT(nvarchar(36), SalesOrderId)) FROM logistics.ShipmentRiskExceptionUses"),
            ignoreCase: true);
        Assert.Contains("\"riskExceptionNo\":7", await run.ScalarAsync<string>("SELECT PayloadJson FROM logistics.OutboxMessages"),
            StringComparison.Ordinal);

        var second = CreateHostedShipmentRequest(
            fixture with
            {
                InstructionVersion = await run.ScalarAsync<long>("SELECT Version FROM logistics.LoadingInstructions WHERE Id = @id",
                    ("@id", fixture.InstructionId)),
            },
            (await run.ScalarAsync<long>("SELECT Version FROM inventory.Allocations WHERE Id = @id", ("@id", fixture.AllocationId))).ToString(),
            "stk27-exception-second",
            quantity: "1");
        var before = await HostedEffectsAsync(run, fixture, second);

        using (var refused = await client.SendAsync(second.Message))
        {
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Contains($"\"code\":\"{LogisticsShipmentRiskGateCodesV1.ExceptionUsed}\"", await refused.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }

        var recorded = await HostedEffectsAsync(run, fixture, second);
        Assert.Equal(before, recorded with { ShipmentAttemptCount = before.ShipmentAttemptCount });
        Assert.Equal(before.ShipmentAttemptCount + 1, recorded.ShipmentAttemptCount);
        Assert.Equal(2, gate.Requests.Count);
        Assert.Equal(1, await run.ScalarAsync<int>(ExceptionUseCount));
        Assert.Equal(1, await run.ScalarAsync<int>(OutboxCount));

        // A writer that skipped the store cannot consume the same exception twice: the primary key refuses it.
        var error = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "INSERT INTO logistics.ShipmentRiskExceptionUses (CompanyId, SalesOrderId, ExceptionNo, ShipmentId, InstructionId, RecordedBy, RecordedAtUtc) "
            + "SELECT CompanyId, SalesOrderId, ExceptionNo, NEWID(), InstructionId, RecordedBy, RecordedAtUtc FROM logistics.ShipmentRiskExceptionUses"));
        Assert.Equal(2627, error.Number);
        Assert.Equal(1, await run.ScalarAsync<int>(ExceptionUseCount));
    }

    [SqlServerFact]
    public async Task The_shipment_risk_gate_migrations_Down_passes_with_an_empty_outbox_and_refuses_with_51787_while_an_outbox_event_exists_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddShipmentRiskGateOutbox().DownOperations[0]).Sql;
        var owners = Owners.For(CompanyA);
        var shipmentId = Guid.NewGuid();
        Guid id;
        await using (var a = run.Store(CompanyA))
        {
            var stored = (await a.Store.StoreInstructionAsync(run.Envelope("k-gate-down-create"), Hash("gate-down-create"), Day,
                () => Instruction(owners))).Instruction;
            id = stored.Instruction.Id;
            var lineId = stored.Instruction.Lines[0].Id;
            await a.Store.MutateInstructionAsync(run.Envelope("k-gate-down-issue"), Hash("gate-down-issue"), LogisticsCommands.IssueV1, id,
                (instruction, now) => instruction.Issue(2, now), Issued);
            await a.Store.MutateInstructionAsync(run.Envelope("k-gate-down-load"), Hash("gate-down-load"), LogisticsCommands.RecordLoadV1, id,
                (instruction, now) => instruction.RecordLoad(3, Guid.NewGuid(), lineId, Kg(4m), MeasurementKind.Measured, now), Loaded);

            // Loaded goods with no outbox event and no consumed exception are harmless: the guard passes.
            await run.ExecuteAsync(guard);

            var gateStore = Assert.IsAssignableFrom<ILogisticsShipmentRiskGateStoreV1>(a.Store);
            await gateStore.MutateInstructionWithShipmentRiskGateAsync(run.Envelope("k-gate-down-ship"), Hash("gate-down-ship"),
                LogisticsCommands.RecordShipmentV1, id, (instruction, now) => instruction.MarkShipped(4, shipmentId, lineId, Kg(2m), now), Shipped,
                new LogisticsShipmentRiskGateFactsV1(shipmentId, owners.Order, id, "within", null, "none", null,
                    SalesShipmentRiskGateContractV1.Version));
        }

        Assert.Equal(1, await run.ScalarAsync<int>(OutboxCount));
        var error = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51787, error.Number);
        Assert.Equal(1, await run.ScalarAsync<int>(OutboxCount));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingShares WHERE ShareId = @id", ("@id", shipmentId)));
    }

    /// <summary>
    /// The Sales owner's gate answer for the asked order and instruction (the hosted tests' default is within the limit with no
    /// exception). Records every question so a test can prove a replay never reads the gate again.
    /// </summary>
    private sealed class HostedRiskGate : ISalesShipmentRiskGateReadPortV1
    {
        private readonly List<(Guid OrderId, Guid InstructionId)> requests = [];
        private readonly Lock gate = new();

        public SalesShipmentRiskResultV1 Result { get; init; } = SalesShipmentRiskResultV1.Within;
        public SalesShipmentRiskHoldReasonV1? HoldReason { get; init; }
        public SalesShipmentRiskExceptionStatusV1 Status { get; init; } = SalesShipmentRiskExceptionStatusV1.None;
        public int? ExceptionNo { get; init; }
        public DateOnly? ValidOn { get; init; }

        public IReadOnlyList<(Guid OrderId, Guid InstructionId)> Requests
        {
            get
            {
                lock (gate)
                {
                    return [.. requests];
                }
            }
        }

        public Task<SalesShipmentRiskGateReadV1> GetAsync(Guid orderId, Guid loadingInstructionId, CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                requests.Add((orderId, loadingInstructionId));
            }

            return Task.FromResult(new SalesShipmentRiskGateReadV1(orderId, loadingInstructionId, Result, HoldReason,
                new SalesShipmentRiskExceptionReadV1(orderId, loadingInstructionId, Status, ExceptionNo, ValidOn),
                SalesShipmentRiskGateContractV1.Version));
        }
    }
}

using System.Data.Common;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task A_real_subcontract_physical_refusal_records_once_after_rollback_and_replays_original_kg_after_stock_changes()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var seed = await SeedSubcontractDeliveryHostAsync(run, "SYN-S3-PHYSICAL");
        var probe = new S3RefusalRecorderProbe();
        await using var scope = SubcontractHostScope.Create(run, seed.Party, seed.Account, recorder: probe.Bind);
        var prepared = await PrepareS3ShipmentAsync(run, scope, seed);
        await ReduceS3PhysicalBalanceForFaultAsync(run, seed.Output.LabelId);
        var refused = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            scope.Host.ShipAsync(prepared.InstructionId, prepared.Request, prepared.Metadata));
        Assert.Equal((409, InventoryDefiniteShipmentCodesV1.PhysicalStockInsufficient, "Sistemde 20 kg var, 80 kg istendi."),
            (refused.Status, refused.Code, refused.Detail));
        Assert.Equal(1, probe.Calls);
        var proof = Assert.IsType<InventoryNegativeStockAttemptEvidenceV1>(probe.Proof);
        Assert.Equal((CompanyA, run.Actor.ToString("D"), ErpPermissions.InventoryPhysicalAllocationWrite, prepared.MovementId),
            (proof.Shortfall.Position.CompanyId, proof.Metadata.ActorId, proof.Metadata.PermissionCode, proof.MovementId));
        Assert.Equal(1, await S3NegativeAttemptsAsync(run, prepared.MovementId));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'inventory.negative-stock-attempt.recorded.v1'"));
        await AssertS3PhysicalWriteAbsentAsync(run, prepared, seed.Output.LabelId);
        Assert.Equal(1, await S3TerminalAttemptsAsync(run, prepared.Metadata.IdempotencyKey));

        // A real later balance change must not rewrite either the terminal response or the immutable proof.
        Assert.Equal(1, await run.ScalarAsync<int>("UPDATE inventory.Balances SET QuantityKg=80 WHERE CompanyId=@company AND LabelId=@label; SELECT @@ROWCOUNT",
            ("@company", CompanyA), ("@label", seed.Output.LabelId)));
        var replay = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => scope.Host.ShipAsync(prepared.InstructionId,
            prepared.Request, prepared.Metadata with { CorrelationId = "SENTETIK-s3-physical-later" }));
        Assert.True(replay.IsReplay); Assert.Equal(prepared.Metadata.CorrelationId, replay.OriginalCorrelationId);
        Assert.Equal((refused.Status, refused.Code, refused.Detail), (replay.Status, replay.Code, replay.Detail));
        Assert.Equal(1, probe.Calls); Assert.Equal(1, await S3NegativeAttemptsAsync(run, prepared.MovementId));
        Assert.Equal(20m, await run.ScalarAsync<decimal>("SELECT AvailableKg FROM inventory.NegativeStockAttempts WHERE CompanyId=@company AND MovementId=@movement",
            ("@company", CompanyA), ("@movement", prepared.MovementId)));
        Assert.Equal(80m, await run.ScalarAsync<decimal>("SELECT RequestedKg FROM inventory.NegativeStockAttempts WHERE CompanyId=@company AND MovementId=@movement",
            ("@company", CompanyA), ("@movement", prepared.MovementId)));
    }

    [SqlServerFact]
    public async Task A_subcontract_refusal_recording_failure_is_503_and_does_not_record_a_terminal_or_physical_effect()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var seed = await SeedSubcontractDeliveryHostAsync(run, "SYN-S3-RECORDER-FAULT");
        var probe = new S3RefusalRecorderProbe { Fail = true };
        await using var scope = SubcontractHostScope.Create(run, seed.Party, seed.Account, recorder: probe.Bind);
        var prepared = await PrepareS3ShipmentAsync(run, scope, seed);
        await ReduceS3PhysicalBalanceForFaultAsync(run, seed.Output.LabelId);
        var refused = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            scope.Host.ShipAsync(prepared.InstructionId, prepared.Request, prepared.Metadata));
        Assert.Equal(503, refused.Status); Assert.Null(refused.Detail); Assert.False(refused.IsReplay);
        Assert.Equal(1, probe.Calls); Assert.NotNull(probe.Proof);
        Assert.Equal(0, await S3NegativeAttemptsAsync(run, prepared.MovementId));
        Assert.Equal(0, await S3TerminalAttemptsAsync(run, prepared.Metadata.IdempotencyKey));
        await AssertS3PhysicalWriteAbsentAsync(run, prepared, seed.Output.LabelId);
    }

    [SqlServerFact]
    public async Task An_uncertain_subcontract_rollback_never_calls_the_refusal_recorder_or_records_a_terminal()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var seed = await SeedSubcontractDeliveryHostAsync(run, "SYN-S3-ROLLBACK-UNKNOWN");
        var probe = new S3RefusalRecorderProbe(); var rollback = new S3RefusalRollbackInterceptor { RefuseRollback = true };
        await using var scope = SubcontractHostScope.Create(run, seed.Party, seed.Account, rollback, recorder: probe.Bind);
        var prepared = await PrepareS3ShipmentAsync(run, scope, seed);
        await ReduceS3PhysicalBalanceForFaultAsync(run, seed.Output.LabelId);
        var refused = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            scope.Host.ShipAsync(prepared.InstructionId, prepared.Request, prepared.Metadata));
        Assert.Equal(503, refused.Status); Assert.Null(refused.Detail);
        Assert.Equal(1, rollback.RollbackAttempts); Assert.Equal(0, probe.Calls);
        Assert.Equal(0, await S3NegativeAttemptsAsync(run, prepared.MovementId));
        Assert.Equal(0, await S3TerminalAttemptsAsync(run, prepared.Metadata.IdempotencyKey));
        await AssertS3PhysicalWriteAbsentAsync(run, prepared, seed.Output.LabelId);
    }

    [SqlServerFact]
    public async Task A_subcontract_rollback_company_actor_or_command_permission_loss_prevents_the_physical_refusal_append()
    {
        foreach (var fault in new[] { "company", "actor", "command-permission" })
        {
            await using var run = await StartManufacturingInventoryRunAsync();
            var seed = await SeedSubcontractDeliveryHostAsync(run, "SYN-S3-FENCE-" + fault);
            var user = new S3RefusalCurrentUser(run.Actor); var authority = new S3RefusalAuthority();
            var probe = new S3RefusalRecorderProbe(); var rollback = new S3RefusalRollbackInterceptor();
            await using var scope = SubcontractHostScope.Create(run, seed.Party, seed.Account, rollback, user, authority, probe.Bind);
            var prepared = await PrepareS3ShipmentAsync(run, scope, seed);
            await ReduceS3PhysicalBalanceForFaultAsync(run, seed.Output.LabelId);
            rollback.AfterRollback = () =>
            {
                if (fault == "company") scope.Company.Set(CompanyB);
                else if (fault == "actor") user.ActorId = Guid.NewGuid().ToString("D");
                else authority.DeniedPermission = LogisticsPermissionManifestV1.ShipmentsRecord;
            };
            var refused = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
                scope.Host.ShipAsync(prepared.InstructionId, prepared.Request, prepared.Metadata));
            Assert.Equal(403, refused.Status); Assert.Null(refused.Detail);
            Assert.Equal(1, rollback.RollbackAttempts); Assert.Equal(0, probe.Calls);
            Assert.Equal(0, await S3NegativeAttemptsAsync(run, prepared.MovementId));
            Assert.Equal(0, await S3TerminalAttemptsAsync(run, prepared.Metadata.IdempotencyKey));
            await AssertS3PhysicalWriteAbsentAsync(run, prepared, seed.Output.LabelId);
        }
    }

    [SqlServerFact]
    public async Task A_subcontract_reservation_shortage_has_no_physical_proof_and_never_calls_the_refusal_recorder()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var seed = await SeedSubcontractDeliveryHostAsync(run, "SYN-S3-RESERVATION-ONLY"); var probe = new S3RefusalRecorderProbe();
        await using var scope = SubcontractHostScope.Create(run, seed.Party, seed.Account, recorder: probe.Bind);
        var command = await scope.CommandAsync(seed);
        command = command with { Lines = [command.Lines[0] with { QuantityKg = "81" }] };
        var refused = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            scope.Host.CreateAsync(command, new("s3-reservation-only", "SENTETIK-s3-reservation-only", null, At)));
        Assert.Equal(409, refused.Status); Assert.Null(refused.Detail); Assert.Equal(0, probe.Calls);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.NegativeStockAttempts"));
    }

    private sealed record S3PreparedShipment(Guid InstructionId, Guid AllocationId, Guid MovementId,
        ShipSubcontractDeliveryV1 Request, LogisticsCommandMetadata Metadata);
    private static async Task<S3PreparedShipment> PrepareS3ShipmentAsync(Run run, SubcontractHostScope scope, SubcontractHostSeed seed)
    {
        var command = await scope.CommandAsync(seed);
        var created = await scope.Host.CreateAsync(command, new("s3-physical-create", "SENTETIK-s3-physical-create", null, At));
        await scope.Store.MutateInstructionAsync(scope.Envelope("s3-physical-issue"), S3Hash("s3-physical-issue"), LogisticsCommands.IssueV1, created.InstructionId,
            (value, now) => value.Issue(long.Parse(created.Version, System.Globalization.CultureInfo.InvariantCulture), now), new("logistics.loading-instruction.issued.v1", "Issue", "SENTETIK"));
        var issued = await scope.Host.ReadAsync(created.InstructionId);
        await scope.Host.LoadAsync(created.InstructionId, command.Lines[0].LineId, new(issued.Version, Guid.NewGuid(), "80", "Measured"),
            new("s3-physical-load", "SENTETIK-s3-physical-load", null, At));
        await scope.Store.MutateInstructionAsync(scope.Envelope("s3-physical-weigh"), S3Hash("s3-physical-weigh"), LogisticsCommands.RecordWeighingV1, created.InstructionId,
            (value, now) => value.RecordWeighing(value.Version, Guid.NewGuid(), WeighingDirection.TareFirst, S3Kg(100), S3Kg(180), run.Actor.ToString("D"), now),
            new("logistics.loading-instruction.weighed.v1", "Weigh", "SENTETIK"));
        var ready = await scope.Host.ReadAsync(created.InstructionId); var line = Assert.Single(ready.Lines); var movement = Guid.NewGuid();
        return new(created.InstructionId, command.Lines[0].AllocationId, movement, new(ready.Version, At.AddHours(2),
            [new(line.LineId, Guid.NewGuid(), movement, line.AllocationVersion!, line.LabelVersion!, line.Source.SalesOrderId,
                line.Source.SalesOrderLineId, line.Source.ManufacturingOrderId, line.Source.OutputFactId, line.Source.LabelId)]),
            new("s3-physical-ship", "SENTETIK-s3-physical-ship", null, At));
    }
    private static async Task ReduceS3PhysicalBalanceForFaultAsync(Run run, Guid label)
    {
        // Explicit isolated SQL fault: a stale 80 kg reservation still exists but only 20 physical kg remain.
        // The real Inventory ledger must reject the 80 kg exit; the fault is not a supported stock mutation.
        Assert.Equal(1, await run.ScalarAsync<int>("UPDATE inventory.Balances SET QuantityKg=20 WHERE CompanyId=@company AND LabelId=@label; SELECT @@ROWCOUNT",
            ("@company", CompanyA), ("@label", label)));
    }
    private static Task<int> S3NegativeAttemptsAsync(Run run, Guid movement) => run.ScalarAsync<int>(
        "SELECT COUNT(*) FROM inventory.NegativeStockAttempts WHERE CompanyId=@company AND MovementId=@movement", ("@company", CompanyA), ("@movement", movement));
    private static Task<int> S3TerminalAttemptsAsync(Run run, string key) => run.ScalarAsync<int>(
        "SELECT COUNT(*) FROM logistics.LoadingAttempts WHERE CompanyId=@company AND CommandName=@command AND IdempotencyKeyHash=@key",
        ("@company", CompanyA), ("@command", LogisticsSubcontractDeliveryV1.ShipCommand), ("@key", LogisticsStore.IdempotencyKeyHash(key)));
    private static async Task AssertS3PhysicalWriteAbsentAsync(Run run, S3PreparedShipment prepared, Guid label)
    {
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE Id=@movement", ("@movement", prepared.MovementId)));
        Assert.Equal(80m, await run.ScalarAsync<decimal>("SELECT RemainingKg FROM inventory.Allocations WHERE Id=@allocation", ("@allocation", prepared.AllocationId)));
        Assert.Equal(20m, await run.ScalarAsync<decimal>("SELECT SUM(QuantityKg) FROM inventory.Balances WHERE CompanyId=@company AND LabelId=@label", ("@company", CompanyA), ("@label", label)));
    }
    private sealed class S3RefusalRecorderProbe : IInventoryNegativeStockAttemptRecorderV1
    {
        private IInventoryNegativeStockAttemptRecorderV1? actual;
        public int Calls { get; private set; } public InventoryNegativeStockAttemptEvidenceV1? Proof { get; private set; }
        public bool Fail { get; init; }
        public IInventoryNegativeStockAttemptRecorderV1 Bind(IInventoryNegativeStockAttemptRecorderV1 owner) { actual = owner; return this; }
        public Task RecordAfterRollbackAsync(Exception refusal, CancellationToken token = default)
        {
            Calls++; Proof = InventoryNegativeStockAttemptEvidenceException.Find(refusal);
            Assert.NotNull(Proof); Assert.False(token.IsCancellationRequested);
            if (Fail) throw new InvalidOperationException("SENTETIK recorder outage after confirmed rollback");
            return actual!.RecordAfterRollbackAsync(refusal, token);
        }
    }
    private sealed class S3RefusalRollbackInterceptor : DbTransactionInterceptor
    {
        public int RollbackAttempts { get; private set; } public bool RefuseRollback { get; init; } public Action? AfterRollback { get; set; }
        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken token = default)
        {
            RollbackAttempts++;
            if (RefuseRollback) throw new InvalidOperationException("SENTETIK unconfirmed rollback");
            return ValueTask.FromResult(result);
        }
        public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken token = default)
        { AfterRollback?.Invoke(); return Task.CompletedTask; }
    }
    private sealed class S3RefusalCurrentUser(Guid actor) : ICurrentUser
    {
        public string ActorId { get; set; } = actor.ToString("D"); public bool IsAuthenticated => true; public string? UserId => ActorId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { CompanyA };
        public bool CanAccessCompany(Guid company) => AllowedCompanyIds.Contains(company);
    }
    private sealed class S3RefusalAuthority : IErpAuthorityRevalidationService
    {
        public string? DeniedPermission { get; set; }
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default) =>
            Task.FromResult(permission == DeniedPermission ? PermissionScope.Denied : GrantedManufacturingScope());
    }
}

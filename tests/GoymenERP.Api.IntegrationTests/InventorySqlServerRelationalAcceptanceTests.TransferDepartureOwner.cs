using GoymenERP.Api.LogisticsRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Transfers;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Transfers;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Data.SqlClient;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

// Genuine Inventory + Logistics + Audit SQL transactions. Authority-failure callbacks are explicitly synthetic;
// no GİB transport success is simulated. Runtime evidence remains NOT_RUN until the final acceptance phase.
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Transfer_departure_Down_guard_passes_empty_and_refuses_with_51991_after_a_genuine_transfer_association()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddTransferDepartures().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);
        var plan = await SeedDepartureTransferAsync(run, "SYN-EXIT-GUARD");
        Assert.Equal(51991, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard))).Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.TransferDepartureStates WHERE TransferId=@id", ("@id", plan.TransferId)));
    }
    [SqlServerFact]
    public async Task Transfer_actual_owner_explicit_no_link_and_weighing_preserve_original_replay_and_company_boundaries()
    {
        await using var run = await Run.StartAsync();
        var plan = await SeedDepartureTransferAsync(run, "SYN-EXIT-FACT");
        await using var scope = DepartureScope(run, CompanyA);
        var query = DepartureQuery(plan);
        var before = await scope.Owner.ReadAsync(query);
        Assert.Equal((LogisticsTransferEDispatchAssociationV1.NotLinked, 1L, false), (before.Association, before.Version, before.NoFurtherExit));
        var command = new RecordTransferDepartureV1("1", LogisticsTransferDepartureEventKindV1.ExitWeighing,
            At.AddHours(2), "SENTETIK gerçek kantar", new(1, "1000", "1005"));
        var envelope = DepartureEnvelope(run, "weigh", LogisticsTransferDepartureV1.RecordPermission);
        var first = await scope.Owner.RecordAsync(plan.TransferId, command, envelope, Authorized);
        var replay = await scope.Owner.RecordAsync(plan.TransferId, command, envelope with { CorrelationId = "SENTETIK retry" }, Authorized);
        Assert.False(first.IsReplay); Assert.True(replay.IsReplay);
        Assert.Equal(first.Facts, replay.Facts); Assert.Equal(run.Correlation, replay.OriginalCorrelationId);
        Assert.Equal(5m, await run.ScalarAsync<decimal>("SELECT NetWeighingKg FROM logistics.TransferDepartureEvents WHERE TransferId=@id AND Kind='ExitWeighing'", ("@id", plan.TransferId)));
        var mismatch = await Assert.ThrowsAsync<LogisticsTransferDepartureException>(() => scope.Owner.RecordAsync(plan.TransferId,
            command with { Weighing = new(1, "1000", "1006") }, envelope, Authorized));
        Assert.Equal((409, "logistics_transfer_departure_idempotency_conflict"), (mismatch.Status, mismatch.Code));
        await using var foreign = DepartureScope(run, CompanyB);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => foreign.Owner.ReadAsync(query));
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM logistics.TransferDepartureStates WHERE TransferId=@id", ("@id", plan.TransferId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.TransferDepartureAttempts WHERE TransferId=@id", ("@id", plan.TransferId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE SourceId=@id AND SourceKind=N'inventory.transfer'", ("@id", plan.TransferId)));
    }

    [SqlServerFact]
    public async Task Transfer_exit_and_no_further_exit_owner_race_have_one_winner_and_no_inventory_effect()
    {
        await using var run = await Run.StartAsync();
        var plan = await SeedDepartureTransferAsync(run, "SYN-EXIT-RACE");
        using var barrier = new Barrier(2);
        async Task<string> Compete(bool exit)
        {
            await using var scope = DepartureScope(run, CompanyA);
            var calls = 0;
            async Task Fence(CancellationToken token)
            {
                if (Interlocked.Increment(ref calls) == 2)
                    Assert.True(await Task.Run(() => barrier.SignalAndWait(TimeSpan.FromSeconds(30)), token));
            }
            try
            {
                if (exit) await scope.Owner.RecordAsync(plan.TransferId, new("1", LogisticsTransferDepartureEventKindV1.VehicleExit,
                    At.AddHours(2), "SENTETIK araç çıkışı"), DepartureEnvelope(run, "race-exit", LogisticsTransferDepartureV1.RecordPermission), Fence);
                else await scope.Owner.ConfirmAsync(DepartureIntent(plan),
                    DepartureEnvelope(run, "race-cancel", ErpPermissions.InventoryStockTransferCancel), Fence);
                return "winner";
            }
            catch (LogisticsTransferDepartureException exception) when (exception.Status == 409) { return "loser"; }
        }
        var results = await Task.WhenAll(Compete(true), Compete(false));
        Assert.Single(results, x => x == "winner"); Assert.Single(results, x => x == "loser");
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.TransferDepartureStates WHERE TransferId=@id AND Version=2 AND ((VehicleExitAtUtc IS NOT NULL AND NoFurtherExitVersion IS NULL) OR (VehicleExitAtUtc IS NULL AND NoFurtherExitVersion=2))", ("@id", plan.TransferId)));
        Assert.Equal((int)InventoryTransferStatus.InTransit, await run.ScalarAsync<int>("SELECT Status FROM inventory.Transfers WHERE Id=@id", ("@id", plan.TransferId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferDispatchCancellations WHERE TransferId=@id", ("@id", plan.TransferId)));
    }

    [SqlServerFact]
    public async Task Transfer_logistics_fence_survives_later_inventory_rollback_and_same_intent_retries_once()
    {
        await using var run = await Run.StartAsync();
        var plan = await SeedDepartureTransferAsync(run, "SYN-EXIT-ROLLBACK");
        var intent = DepartureIntent(plan);
        var envelope = DepartureEnvelope(run, "confirm-before-rollback", ErpPermissions.InventoryStockTransferCancel);
        long fenceVersion;
        await using (var logistics = DepartureScope(run, CompanyA))
            fenceVersion = (await logistics.Owner.ConfirmAsync(intent, envelope, Authorized)).NoExitConfirmationVersion!.Value;
        var metadata = Metadata(run) with { PermissionCode = ErpPermissions.InventoryStockTransferCancel,
            IdempotencyKey = envelope.IdempotencyKey };
        await using (var stock = run.Store(CompanyA))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => stock.Store.CancelDispatchAsync(plan.TransferId,
                new("2", intent.CancelledAtUtc, intent.Reason), fenceVersion, null, metadata,
                _ => throw new UnauthorizedAccessException("SENTETIK final authority fault")));
        }
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferDispatchCancellations WHERE TransferId=@id", ("@id", plan.TransferId)));
        await using (var logistics = DepartureScope(run, CompanyA))
        {
            var replay = await logistics.Owner.ConfirmAsync(intent, envelope with { CorrelationId = "SENTETIK retry" }, Authorized);
            Assert.Equal(fenceVersion, replay.NoExitConfirmationVersion);
            var refusal = await Assert.ThrowsAsync<LogisticsTransferDepartureException>(() => logistics.Owner.RecordAsync(plan.TransferId,
                new("2", LogisticsTransferDepartureEventKindV1.VehicleExit, At.AddHours(3), "SENTETIK geç çıkış"),
                DepartureEnvelope(run, "late-exit", LogisticsTransferDepartureV1.RecordPermission), Authorized));
            Assert.Equal((409, "logistics_transfer_departure_closed"), (refusal.Status, refusal.Code));
        }
        await using (var stock = run.Store(CompanyA))
        {
            var first = await stock.Store.CancelDispatchAsync(plan.TransferId, new("2", intent.CancelledAtUtc, intent.Reason),
                fenceVersion, null, metadata, Authorized);
            var replay = await stock.Store.CancelDispatchAsync(plan.TransferId, new("2", intent.CancelledAtUtc, intent.Reason),
                fenceVersion, null, metadata, Authorized);
            Assert.Equal("DispatchCancelled", first.Transfer.Status); Assert.True(replay.IsReplay);
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.TransferDepartureEvents WHERE TransferId=@id AND Kind='NoFurtherExit'", ("@id", plan.TransferId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferDispatchCancellations WHERE TransferId=@id", ("@id", plan.TransferId)));
    }

    [SqlServerFact]
    public async Task Transfer_owner_fresh_authority_failure_rolls_back_state_event_real_audit_and_attempt_together()
    {
        await using var run = await Run.StartAsync();
        var plan = await SeedDepartureTransferAsync(run, "SYN-EXIT-AUTH");
        await using var scope = DepartureScope(run, CompanyA);
        var calls = 0;
        Task FailAfterRealAudit(CancellationToken token) => ++calls == 3
            ? Task.FromException(new UnauthorizedAccessException("SENTETIK post-audit fault")) : Task.CompletedTask;
        var request = new RecordTransferDepartureV1("1", LogisticsTransferDepartureEventKindV1.VehicleExit, At.AddHours(2), "SENTETIK çıkış");
        var metadata = DepartureEnvelope(run, "exit-authority", LogisticsTransferDepartureV1.RecordPermission);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => scope.Owner.RecordAsync(plan.TransferId, request, metadata, FailAfterRealAudit));
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM logistics.TransferDepartureStates WHERE TransferId=@id", ("@id", plan.TransferId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.TransferDepartureEvents WHERE TransferId=@id AND Kind='VehicleExit'", ("@id", plan.TransferId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.TransferDepartureAttempts WHERE TransferId=@id", ("@id", plan.TransferId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Logistics.TransferDeparture' AND EntityId=@id", ("@id", plan.TransferId.ToString("D"))));
        Assert.False((await scope.Owner.RecordAsync(plan.TransferId, request, metadata, Authorized)).IsReplay);
    }

    [SqlServerFact]
    public async Task Transfer_legacy_missing_association_is_unknown_and_linked_without_real_official_owner_cannot_be_created()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-EXIT-LEGACY", 5m);
        var plan = TransferPlan(source, await run.LocationOfAsync(CompanyA), await TransferLocationAsync(run, CompanyA, 1), "5");
        await using (var stock = run.Store(CompanyA)) await stock.Store.PlanAsync(plan, Metadata(run), Authorized);
        var dispatch = TransferDispatch("2");
        await using (var logistics = DepartureScope(run, CompanyA))
        {
            var document = TransferOutgoingDocument.Create(CompanyA, dispatch.OutgoingDocumentId, plan.TransferId,
                plan.Lines.Select(x => x.LineId), null, run.Actor.ToString("D"), At);
            await Assert.ThrowsAsync<LogisticsStoreException>(() => logistics.Documents.StoreDraftWithAssociationAsync(
                DepartureEnvelope(run, "linked-missing", "SENTETIK document write"), HashText("linked-missing"), document,
                LogisticsTransferEDispatchAssociationV1.Linked, Authorized));
            await logistics.Documents.StoreDraftAsync(DepartureEnvelope(run, "legacy-doc", "SENTETIK document write"), HashText("legacy-doc"), document, Authorized);
            await logistics.Documents.MutateAsync(DepartureEnvelope(run, "legacy-final", "SENTETIK document write"), HashText("legacy-final"),
                LogisticsTransferOutgoingDocumentCommandsV1.Finalize, document.DocumentId, (item, now) => item.Finalize(1, run.Actor.ToString("D"), now), Authorized);
        }
        await using (var stock = run.Store(CompanyA)) await stock.Store.DispatchAsync(plan.TransferId, dispatch, EffectiveOutgoing(plan, dispatch),
            InventoryTransferSourceScaleSetting.On, Metadata(run), Authorized);
        await using (var logistics = DepartureScope(run, CompanyA))
        {
            var facts = await logistics.Owner.ReadAsync(DepartureQuery(plan));
            Assert.Equal(LogisticsTransferEDispatchAssociationV1.Unknown, facts.Association); Assert.Null(facts.Version);
            var mapped = LogisticsInventoryTransferCancelGate.Map(new(CompanyA, plan.TransferId, plan.SourceLocationId, dispatch.DispatchedAtUtc), facts);
            Assert.Equal(InventoryTransferEDispatchLinkV1.Unreadable, mapped.EDispatchLink);
            var failure = await Assert.ThrowsAsync<LogisticsTransferDepartureException>(() => logistics.Owner.ConfirmAsync(DepartureIntent(plan),
                DepartureEnvelope(run, "legacy-cancel", ErpPermissions.InventoryStockTransferCancel), Authorized));
            Assert.Equal(503, failure.Status);
        }
    }

    private static async Task<PlanInventoryTransferV1> SeedDepartureTransferAsync(Run run, string tag)
    {
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, tag, 5m);
        var plan = TransferPlan(source, await run.LocationOfAsync(CompanyA), await TransferLocationAsync(run, CompanyA, 1), "5");
        await using (var stock = run.Store(CompanyA)) await stock.Store.PlanAsync(plan, Metadata(run), Authorized);
        var dispatch = TransferDispatch("2");
        await using (var logistics = DepartureScope(run, CompanyA))
        {
            var document = TransferOutgoingDocument.Create(CompanyA, dispatch.OutgoingDocumentId, plan.TransferId,
                plan.Lines.Select(x => x.LineId), null, run.Actor.ToString("D"), At);
            await logistics.Documents.StoreDraftWithAssociationAsync(DepartureEnvelope(run, tag + "-document", "SENTETIK document write"),
                HashText(tag + "-document"), document, LogisticsTransferEDispatchAssociationV1.NotLinked, Authorized);
            await logistics.Documents.MutateAsync(DepartureEnvelope(run, tag + "-final", "SENTETIK document write"), HashText(tag + "-final"),
                LogisticsTransferOutgoingDocumentCommandsV1.Finalize, document.DocumentId,
                (item, now) => item.Finalize(1, run.Actor.ToString("D"), now), Authorized);
            var proof = await new InventoryTransferOutgoingDocumentProofAdapter(logistics.Documents).ReadAsync(
                new(CompanyA, plan.TransferId, dispatch.OutgoingDocumentId, 2, plan.Lines.Select(x => x.LineId)));
            Assert.NotNull(proof.EffectiveBinding);
            await using var stock = run.Store(CompanyA);
            await stock.Store.DispatchAsync(plan.TransferId, dispatch, proof.EffectiveBinding!, InventoryTransferSourceScaleSetting.On, Metadata(run), Authorized);
        }
        return plan;
    }

    [SqlServerFact]
    public async Task Transfer_cancel_time_before_dispatch_cannot_leave_a_permanent_logistics_fence_or_key()
    {
        await using var run = await Run.StartAsync();
        var plan = await SeedDepartureTransferAsync(run, "SYN-EXIT-BAD-TIME");
        await using var scope = DepartureScope(run, CompanyA);
        var error = await Assert.ThrowsAsync<LogisticsTransferDepartureException>(() => scope.Owner.ConfirmAsync(
            DepartureIntent(plan) with { CancelledAtUtc = At.AddMinutes(30) },
            DepartureEnvelope(run, "bad-cancel-time", ErpPermissions.InventoryStockTransferCancel), Authorized));
        Assert.Equal(422, error.Status);
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM logistics.TransferDepartureStates WHERE TransferId=@id", ("@id", plan.TransferId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.TransferDepartureEvents WHERE TransferId=@id AND Kind='NoFurtherExit'", ("@id", plan.TransferId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.TransferDepartureAttempts WHERE TransferId=@id", ("@id", plan.TransferId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Logistics.TransferDeparture' AND EntityId=@id", ("@id", plan.TransferId.ToString("D"))));
    }
    private static LogisticsTransferDepartureQueryV1 DepartureQuery(PlanInventoryTransferV1 plan) =>
        new(CompanyA, plan.TransferId, plan.SourceLocationId, At.AddHours(1));
    private static LogisticsTransferCancelIntentV1 DepartureIntent(PlanInventoryTransferV1 plan) =>
        new(DepartureQuery(plan), "2", At.AddHours(2), "SENTETIK hatalı sevk", true, false);
    private static LogisticsWriteEnvelope DepartureEnvelope(Run run, string key, string permission) =>
        new(run.Actor.ToString("D"), permission, ["SENTETIK"], run.Correlation, null, key, "Web");
    private static string HashText(string text) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
    private static TransferDepartureScope DepartureScope(Run run, Guid companyId)
    {
        var scope = run.Factory.Services.CreateAsyncScope();
        var company = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(companyId);
        var database = scope.ServiceProvider.GetRequiredService<LogisticsDbContext>();
        var actor = new FixedCurrentUser(run.Actor, companyId);
        var source = new InventoryLogisticsTransferReferenceSource(scope.ServiceProvider.GetRequiredService<IInventoryTransferReaderV1>(), company, actor);
        var audit = scope.ServiceProvider.GetRequiredService<IAuditEventCollector>();
        var writer = scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
        return new(scope, new(database, company, actor, audit, writer, TimeProvider.System, source),
            new(database, company, audit, TimeProvider.System, source, null, writer));
    }
    private sealed class TransferDepartureScope(AsyncServiceScope scope, LogisticsTransferDepartureOwner owner,
        LogisticsTransferOutgoingDocumentStore documents) : IAsyncDisposable
    {
        public LogisticsTransferDepartureOwner Owner => owner;
        public LogisticsTransferOutgoingDocumentStore Documents => documents;
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
}

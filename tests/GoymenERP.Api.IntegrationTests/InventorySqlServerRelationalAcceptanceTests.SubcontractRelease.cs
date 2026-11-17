using System.Data;
using System.Globalization;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Subcontract_explicit_release_keeps_eighty_shipped_twenty_physical_and_original_consumption_then_replays_without_a_second_effect()
    {
        await using var run = await Run.StartAsync(); var seed = await SeedSubcontractReleaseAsync(run, "SENTETIK-S3-RELEASE");
        var movementCount = await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE CompanyId=@c", ("@c", CompanyA));
        var outboxCount = await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OutboxMessages WHERE CompanyId=@c", ("@c", CompanyA));
        InventorySubcontractAllocationReleaseResultV1 first;
        await using (var scope = SubcontractReleaseScope.Create(run, CompanyA))
        {
            var choice = Assert.Single((await scope.Store.ListAsync(null, _ => Task.CompletedTask)).Items);
            Assert.Equal(seed.Id, choice.Allocation.AllocationId); Assert.Equal("20", choice.Allocation.RemainingKg);
            first = await scope.Store.ReleaseAsync(seed.Id, new("2", "SENTETIK explicit unused release"), ReleaseMetadata(run, "s3-release"), _ => Task.CompletedTask);
            Assert.Equal("20", first.Release.ReleasedKg); Assert.Equal("80", first.Release.PreviouslyConsumedKg);
            Assert.Equal("100", first.Release.Allocation.AllocatedKg); Assert.Equal("0", first.Release.Allocation.RemainingKg);
            var replay = await scope.Store.ReleaseAsync(seed.Id, new("2", "SENTETIK explicit unused release"), ReleaseMetadata(run, "s3-release") with { CorrelationId = "SENTETIK-later" }, _ => Task.CompletedTask);
            Assert.True(replay.IsReplay); Assert.Equal(first.Release, replay.Release); Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId);
        }
        Assert.Equal(20m, await run.ScalarAsync<decimal>("SELECT SUM(QuantityKg) FROM inventory.Balances WHERE LabelId=@id", ("@id", seed.LabelId)));
        Assert.Equal(movementCount, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE CompanyId=@c", ("@c", CompanyA)));
        Assert.Equal(outboxCount, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OutboxMessages WHERE CompanyId=@c", ("@c", CompanyA)));
        Assert.Equal(80m, await run.ScalarAsync<decimal>("SELECT SUM(l.QuantityKg) FROM inventory.MovementLines l JOIN inventory.Movements m ON m.CompanyId=l.CompanyId AND m.Id=l.MovementId WHERE m.Id=@id", ("@id", seed.ShipmentMovementId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=@event AND EntityId=@id", ("@event", InventorySubcontractAllocationReleaseV1.Event), ("@id", seed.Id.ToString("D"))));
        await using var foreign = SubcontractReleaseScope.Create(run, CompanyB);
        Assert.Empty((await foreign.Store.ListAsync(null, _ => Task.CompletedTask)).Items);
        Assert.Equal(404, (await Assert.ThrowsAsync<InventoryAllocationException>(() => foreign.Store.ReleaseAsync(seed.Id, new("2", "SENTETIK"), ReleaseMetadata(run, "foreign-release"), _ => Task.CompletedTask))).Status);
    }

    [SqlServerFact]
    public async Task Subcontract_release_actual_audit_append_failure_and_late_fence_loss_roll_back_label_allocation_and_attempt_then_same_key_succeeds()
    {
        await using var run = await Run.StartAsync(); var seed = await SeedSubcontractReleaseAsync(run, "SENTETIK-S3-ROLLBACK");
        var beforeVersion = await run.ScalarAsync<long>("SELECT Version FROM inventory.Labels WHERE Id=@id", ("@id", seed.LabelId));
        await using (var scope = SubcontractReleaseScope.Create(run, CompanyA, writer => new FailingSubcontractReleaseAudit(writer)))
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Store.ReleaseAsync(seed.Id, new("2", "SENTETIK"), ReleaseMetadata(run, "release-failure"), _ => Task.CompletedTask));
        await using (var scope = SubcontractReleaseScope.Create(run, CompanyA))
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => scope.Store.ReleaseAsync(seed.Id, new("2", "SENTETIK"), ReleaseMetadata(run, "release-failure"),
                _ => scope.Db.Commands.Local.Any() ? throw new UnauthorizedAccessException("SENTETIK late authority fault") : Task.CompletedTask));
        Assert.Equal(20m, await run.ScalarAsync<decimal>("SELECT RemainingKg FROM inventory.Allocations WHERE Id=@id", ("@id", seed.Id)));
        Assert.Equal(beforeVersion, await run.ScalarAsync<long>("SELECT Version FROM inventory.Labels WHERE Id=@id", ("@id", seed.LabelId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands WHERE CommandName=@command", ("@command", InventorySubcontractAllocationReleaseV1.Command)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=@event AND EntityId=@id", ("@event", InventorySubcontractAllocationReleaseV1.Event), ("@id", seed.Id.ToString("D"))));
        await using var retry = SubcontractReleaseScope.Create(run, CompanyA);
        Assert.False((await retry.Store.ReleaseAsync(seed.Id, new("2", "SENTETIK"), ReleaseMetadata(run, "release-failure"), _ => Task.CompletedTask)).IsReplay);
    }

    [SqlServerFact]
    public async Task Subcontract_release_race_has_one_release_terminal_version_replay_and_original_actor_and_payload_ownership()
    {
        await using var run = await Run.StartAsync(); var seed = await SeedSubcontractReleaseAsync(run, "SENTETIK-S3-RACE");
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(bool Success, string Key)> Send(string key)
        {
            await start.Task; await using var scope = SubcontractReleaseScope.Create(run, CompanyA);
            try { await scope.Store.ReleaseAsync(seed.Id, new("2", "SENTETIK"), ReleaseMetadata(run, key), _ => Task.CompletedTask); return (true, key); }
            catch (InventoryAllocationException error) when (error.Code == InventoryAllocationCodesV1.AllocationVersionConflict) { return (false, key); }
        }
        var a = Send("release-a"); var b = Send("release-b"); start.SetResult(); var results = await Task.WhenAll(a, b).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(results, x => x.Success); var loser = Assert.Single(results, x => !x.Success);
        await using var replay = SubcontractReleaseScope.Create(run, CompanyA);
        var refusal = await Assert.ThrowsAsync<InventoryAllocationException>(() => replay.Store.ReleaseAsync(seed.Id, new("2", "SENTETIK"), ReleaseMetadata(run, loser.Key), _ => Task.CompletedTask));
        Assert.True(refusal.IsReplay); Assert.Equal(InventoryAllocationCodesV1.AllocationVersionConflict, refusal.Code);
        var changed = await Assert.ThrowsAsync<InventoryAllocationException>(() => replay.Store.ReleaseAsync(seed.Id, new("2", "SENTETIK changed"), ReleaseMetadata(run, loser.Key), _ => Task.CompletedTask));
        Assert.Equal(InventoryAllocationCodesV1.IdempotencyConflict, changed.Code);
        var actor = await Assert.ThrowsAsync<InventoryAllocationException>(() => replay.Store.ReleaseAsync(seed.Id, new("2", "SENTETIK"), ReleaseMetadata(run, loser.Key) with { ActorId = Guid.NewGuid().ToString("D") }, _ => Task.CompletedTask));
        Assert.Equal(InventoryAllocationCodesV1.IdempotencyConflict, actor.Code);
        Assert.Equal(3L, await run.ScalarAsync<long>("SELECT Version FROM inventory.Allocations WHERE Id=@id", ("@id", seed.Id)));
    }

    private static InventoryCommandMetadata ReleaseMetadata(Run run, string key) => Metadata(run) with
    { IdempotencyKey = key, PermissionCode = ErpPermissions.InventoryPhysicalAllocationWrite, CorrelationId = "SENTETIK-" + key };
    private sealed record SubcontractReleaseSeed(Guid Id, Guid LabelId, Guid ShipmentMovementId);
    private static async Task<SubcontractReleaseSeed> SeedSubcontractReleaseAsync(Run run, string code)
    {
        var label = NewLabel(CompanyA, code); var customer = Guid.NewGuid(); var location = await run.LocationOfAsync(CompanyA);
        var position = new StockPosition(CompanyA, new(StockPartyKind.Counterparty, customer), new(StockPartyKind.Company, CompanyA), location, label, BlockState.Free);
        await using (var store = run.Store(CompanyA))
        { await store.Labels.SaveLabelAsync(label, Metadata(run)); await store.Movements.RecordMovementAsync(Movement(CompanyA, StockMovementKind.Receipt, In(position, 100m)), Metadata(run)); }
        // Explicit synthetic Sales/Manufacturing identity proof; all Inventory stores, physical movements, locks, Audit and SQL are real.
        var source = new InventorySubcontractDeliverySourceV1(CompanyA, customer, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, 0,
            label.ProductId, label.ProductVersion, Guid.NewGuid(), 1, Guid.NewGuid(), 1, Guid.NewGuid(), label.Id, "100",
            InventorySubcontractDeliveryContractV1.SourceVersion, 1, Guid.NewGuid());
        var allocationId = Guid.NewGuid(); var movementId = Guid.NewGuid();
        for (var step = 0; step < 2; step++)
        {
            await using var scope = run.Factory.Services.CreateAsyncScope(); var provider = scope.ServiceProvider;
            var company = provider.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
            var collector = provider.GetRequiredService<IAuditEventCollector>(); var writer = provider.GetRequiredService<IAuditTransactionWriterV1>();
            var settings = new ApprovedSettingsPrerequisiteProvider(company, new FixedCurrentUser(run.Actor, CompanyA));
            await using var db = run.Context(CompanyA); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await using var participant = await InventorySubcontractDeliveryParticipant.EnlistAsync(db.Database.GetDbConnection(), tx.GetDbTransaction(), CompanyA,
                settings, collector, writer, provider.GetRequiredService<ISalesOrderLineSerializationLockBinderV1>());
            var version = (await db.Labels.AsNoTracking().Where(x => x.Id == label.Id).Select(x => x.Version).SingleAsync()).ToString(CultureInfo.InvariantCulture);
            if (step == 0) await participant.ReserveAsync(new(CompanyA, Guid.NewGuid(), [new(allocationId, label.Id, version, "100", "SENTETIK", source)]), ReleaseMetadata(run, code + "-reserve"), _ => Task.CompletedTask);
            else await participant.ShipAsync(new(CompanyA, Guid.NewGuid(), customer, At.AddHours(1), [new(Guid.NewGuid(), Guid.NewGuid(), allocationId, "1", label.Id, version, movementId, "80", source)]), ReleaseMetadata(run, code + "-ship"), _ => Task.CompletedTask);
            await tx.CommitAsync();
        }
        return new(allocationId, label.Id, movementId);
    }
    private sealed class SubcontractReleaseScope(AsyncServiceScope scope, InventoryDbContext db, InventorySubcontractAllocationReleaseStoreV1 store) : IAsyncDisposable
    {
        public InventorySubcontractAllocationReleaseStoreV1 Store => store; public InventoryDbContext Db => db;
        public static SubcontractReleaseScope Create(Run run, Guid companyId, Func<IAuditTransactionWriterV1, IAuditTransactionWriterV1>? wrap = null)
        {
            var scope = run.Factory.Services.CreateAsyncScope(); var provider = scope.ServiceProvider; var company = provider.GetRequiredService<ICompanyContext>(); company.Set(companyId);
            var collector = provider.GetRequiredService<IAuditEventCollector>(); var writer = provider.GetRequiredService<IAuditTransactionWriterV1>();
            var db = run.Context(companyId); var physical = new InventoryStore(db, company, collector,
                new InventoryKilogramScale(new ApprovedSettingsPrerequisiteProvider(company, new FixedCurrentUser(run.Actor, companyId))), TimeProvider.System);
            return new(scope, db, new(db, physical, company, provider.GetRequiredService<ISalesOrderLineSerializationLockBinderV1>(), collector,
                wrap?.Invoke(writer) ?? writer, provider.GetRequiredService<IRefusedAttemptRecorderV1>(), TimeProvider.System));
        }
        public async ValueTask DisposeAsync() { await db.DisposeAsync(); await scope.DisposeAsync(); }
    }
    private sealed class FailingSubcontractReleaseAudit(IAuditTransactionWriterV1 actual) : IAuditTransactionWriterV1
    { public bool HasPendingDrafts => actual.HasPendingDrafts; public void DiscardPending() => actual.DiscardPending();
        public async Task WritePendingAsync(IDbTransaction transaction, CancellationToken cancellationToken = default)
        { await actual.WritePendingAsync(transaction, cancellationToken); throw new InvalidOperationException("SENTETIK after actual Audit append"); } }
}

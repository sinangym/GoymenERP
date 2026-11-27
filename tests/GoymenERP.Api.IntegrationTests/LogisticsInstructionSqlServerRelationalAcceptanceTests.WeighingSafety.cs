using System.Transactions;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.WeighingDifferences;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class LogisticsInstructionSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Weighing_owned_abort_before_or_after_save_discards_actual_audit_and_same_key_can_retry()
    {
        foreach (var approve in new[] { false, true })
        foreach (var afterSave in new[] { false, true })
        foreach (var interceptAudit in new[] { false, true })
        {
            await using var run = await Run.StartAsync();
            var note = await KilogramNoteAsync(run);
            await using var scope = KilogramStore(run, CompanyA, interceptAudit);
            var source = (await scope.Store.ReadSourceAsync(note))!;
            var record = approve ? WeighingSafetyPendingRecord(run, source) : KilogramRecord(run, source);
            if (approve) await scope.Store.StoreAsync(run.Envelope("weigh-seed"), Hash("seed"), record, source, _ => Task.CompletedTask);
            var envelope = run.Envelope("weigh-retry") with { ActorId = approve ? "SENTETIK-independent-checker" : run.Envelope("unused").ActorId };
            var hash = Hash("weigh-retry"); var observed = false;
            Task Fence(CancellationToken _)
            {
                var saved = scope.Context.ChangeTracker.Entries<WeighingDifferenceRecordRow>()
                    .Any(row => row.State == EntityState.Unchanged && row.Entity.Version == (approve ? 2 : 1));
                if (afterSave ? saved : scope.Writer.HasPendingDrafts)
                { observed = true; throw new InvalidOperationException("SENTETIK fresh authority/delegation lost"); }
                return Task.CompletedTask;
            }
            Task<WeighingDifferenceStoreResultV1> Invoke(Func<CancellationToken, Task> fence) => approve
                ? scope.Store.MutateAsync(envelope, hash, LogisticsWeighingDifferenceCommandsV1.Approve, record.RecordId,
                    (item, now) => item.Approve(1, envelope.ActorId, now), fence)
                : scope.Store.StoreAsync(envelope, hash, record, source, fence);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Invoke(Fence));
            Assert.True(observed); Assert.Empty(scope.Context.ChangeTracker.Entries()); Assert.False(scope.Writer.HasPendingDrafts);
            Assert.Equal(approve ? 1 : 0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.WeighingDifferenceAttempts WHERE RecordId=@id", ("@id", record.RecordId)));
            Assert.Equal(approve ? 1 : 0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Logistics.WeighingDifference' AND EntityId=@id", ("@id", record.RecordId.ToString("D"))));
            Assert.Equal(approve ? 1 : 0, await run.ScalarAsync<int>("SELECT COALESCE(MAX(Version),0) FROM logistics.WeighingDifferences WHERE RecordId=@id", ("@id", record.RecordId)));
            Assert.Equal(LogisticsWriteOutcome.Recorded, (await Invoke(_ => Task.CompletedTask)).Outcome);
            Assert.False(scope.Writer.HasPendingDrafts);
            Assert.Equal(approve ? 2 : 1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Logistics.WeighingDifference' AND EntityId=@id", ("@id", record.RecordId.ToString("D"))));
        }
    }

    [SqlServerFact]
    public async Task Weighing_entry_refusals_preserve_caller_transaction_changes_and_audit()
    {
        foreach (var approve in new[] { false, true })
        {
            await using var run = await Run.StartAsync(); var note = await KilogramNoteAsync(run);
            await using (var seed = KilogramStore(run, CompanyA))
            {
                var source = (await seed.Store.ReadSourceAsync(note))!;
                var record = approve ? WeighingSafetyPendingRecord(run, source) : KilogramRecord(run, source);
                await seed.Store.StoreAsync(run.Envelope("weigh-entry-seed"), Hash("seed"), record, source, _ => Task.CompletedTask);
            }
            foreach (var boundary in new[] { "missing-writer", "borrowed", "dirty", "audit", "ambient" })
            {
                await using var scope = KilogramStore(run, CompanyA);
                var source = (await scope.Store.ReadSourceAsync(note))!;
                var record = approve ? (await scope.Store.FindByDispatchNoteAsync(note))! : KilogramRecord(run, source);
                if (approve) Assert.Equal(WeighingDifferenceRecordStatus.PendingApproval, record.Status);
                var store = boundary == "missing-writer"
                    ? new LogisticsWeighingDifferenceStore(scope.Context, scope.Company, scope.Collector, TimeProvider.System) : scope.Store;
                await using var borrowed = boundary == "borrowed" ? await scope.Context.Database.BeginTransactionAsync() : null;
                var tracked = boundary == "dirty" ? await scope.Context.WeighingDifferences.SingleAsync(row => row.DispatchNoteId == note) : null;
                if (tracked is not null) tracked.Version++;
                if (boundary is "audit" or "missing-writer") scope.Collector.Enqueue(WeighingCallerDraft(run));
                using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
                var called = false;
                Task Fence(CancellationToken _) { called = true; return Task.CompletedTask; }
                var error = await Assert.ThrowsAsync<LogisticsStoreException>(async () =>
                {
                    if (approve) await store.MutateAsync(run.Envelope("weigh-entry"), Hash("entry"), LogisticsWeighingDifferenceCommandsV1.Approve,
                        record.RecordId, (_, _) => throw new InvalidOperationException("must not mutate"), Fence);
                    else await store.StoreAsync(run.Envelope("weigh-entry"), Hash("entry"), record, source, Fence);
                });
                Assert.Equal(LogisticsWeighingDifferenceCodesV1.Unavailable, error.Code); Assert.False(called);
                Assert.Same(borrowed, scope.Context.Database.CurrentTransaction);
                Assert.Equal(boundary == "dirty", scope.Context.ChangeTracker.HasChanges());
                Assert.Equal(boundary is "audit" or "missing-writer", scope.Writer.HasPendingDrafts);
                if (tracked is not null) Assert.Equal(2, tracked.Version);
                if (boundary is "audit" or "missing-writer") scope.Writer.DiscardPending();
            }
        }
    }

    [SqlServerFact]
    public async Task Weighing_captured_company_change_after_save_rolls_back_without_leaking_audit()
    {
        await using var run = await Run.StartAsync(); var note = await KilogramNoteAsync(run);
        await using var scope = KilogramStore(run, CompanyA);
        var source = (await scope.Store.ReadSourceAsync(note))!; var record = KilogramRecord(run, source);
        var changed = false;
        await Assert.ThrowsAsync<LogisticsStoreException>(() => scope.Store.StoreAsync(run.Envelope("weigh-company"), Hash("company"), record, source,
            _ =>
            {
                if (scope.Context.ChangeTracker.Entries<WeighingDifferenceRecordRow>().Any(row => row.State == EntityState.Unchanged))
                { changed = true; scope.Company.Set(CompanyB); }
                return Task.CompletedTask;
            }));
        Assert.True(changed); Assert.Empty(scope.Context.ChangeTracker.Entries()); Assert.False(scope.Writer.HasPendingDrafts);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.WeighingDifferenceAttempts WHERE RecordId=@id", ("@id", record.RecordId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Logistics.WeighingDifference' AND EntityId=@id", ("@id", record.RecordId.ToString("D"))));
    }

    [SqlServerFact]
    public async Task Weighing_immutable_approval_key_replays_after_later_dispatch_cancellation_without_running_business_mutation()
    {
        await using var run = await Run.StartAsync(); var note = await KilogramNoteAsync(run);
        await using var scope = KilogramStore(run, CompanyA);
        var source = (await scope.Store.ReadSourceAsync(note))!; var record = WeighingSafetyPendingRecord(run, source);
        await scope.Store.StoreAsync(run.Envelope("weigh-record"), Hash("record"), record, source, _ => Task.CompletedTask);
        var envelope = run.Envelope("weigh-approve") with { ActorId = "SENTETIK-independent-checker" };
        var original = await scope.Store.MutateAsync(envelope, Hash("approve"), LogisticsWeighingDifferenceCommandsV1.Approve,
            record.RecordId, (item, now) => item.Approve(1, envelope.ActorId, now), _ => Task.CompletedTask);
        await using (var instruction = run.Store(CompanyA))
            await instruction.Store.MutateInstructionAsync(run.Envelope("weigh-later-cancel"), Hash("cancel"), LogisticsCommands.CancelDispatchNoteV1,
                source.InstructionId, (item, now) => item.CancelDispatchNote(item.Version, note, run.Actor, "SENTETIK later cancellation", now),
                new LogisticsMutationAudit("logistics.loading-instruction.dispatch-note-cancelled.v1", "CancelDispatchNote", "SENTETIK later cancellation"));
        Assert.False((await scope.Store.ReadSourceAsync(note))!.IsDraft);
        var fences = 0;
        var replay = await scope.Store.MutateAsync(envelope, Hash("approve"), LogisticsWeighingDifferenceCommandsV1.Approve,
            record.RecordId, (_, _) => throw new InvalidOperationException("immutable replay must not mutate"),
            _ => { fences++; return Task.CompletedTask; });
        Assert.Equal(LogisticsWriteOutcome.Replayed, replay.Outcome); Assert.Equal(original.Record.Version, replay.Record.Version);
        Assert.Equal(original.OriginalCorrelationId, replay.OriginalCorrelationId); Assert.True(fences > 0);
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.WeighingDifferenceAttempts WHERE RecordId=@id", ("@id", record.RecordId)));
    }

    // Safety tests need an actual pending approval. The existing KilogramRecord intentionally covers
    // its 0.001 kg difference with 1 kg free stock and must retain its NoExcess/Recorded semantics.
    private static WeighingDifferenceRecord WeighingSafetyPendingRecord(Run run, WeighingDifferenceSourceV1 source)
    {
        var original = KilogramRecord(run, source);
        var evaluation = WeighingDifferenceCalculator.Evaluate(source.Lines.Select(line => new WeighingDifferenceNoteLine(
            line.LineNo, line.ShipmentId, line.ProductId, Kg(line.Quantity))).ToArray(),
            [new(source.Lines[0].ProductId, Kg(5.001m), Kg(0m))], WeighingDifferencePolicy.Unreadable);
        var record = WeighingDifferenceRecord.Create(CompanyA, original.RecordId, source.DispatchNoteId, source.InstructionId,
            evaluation, source.EnteredBy.Append(run.Actor), source.EnteredByComplete, run.Actor, original.RecordedAtUtc,
            original.KilogramAllocation);
        Assert.Equal(0.001m, record.Evaluation.TotalExcessKg.Value);
        Assert.Equal(WeighingDifferenceDecision.ApprovalRequired, record.Evaluation.Decision);
        Assert.Equal(WeighingDifferenceRecordStatus.PendingApproval, record.Status);
        Assert.True(record.EnteredByComplete);
        return record;
    }

    private static AuditEventDraft WeighingCallerDraft(Run run) => new(CompanyA, "logistics.weighing-difference.recorded.v1",
        "Logistics.WeighingDifference", Guid.NewGuid().ToString("D"), "Record", null, 1, AuditActorTypes.User,
        run.Envelope("caller").ActorId, "SENTETIK.test", [], "{}", "{}", "Caller-owned draft", null,
        AuditAiInvolvement.None, null, "Web", null, null, run.Envelope("caller").CorrelationId, null, "caller-owned");
}

using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

// Native dispatch APPROVE boundary only. Existing lifecycle fixture owns all shipments/numbering; no alternate calculator.
public sealed partial class LogisticsInstructionSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Dispatch_owned_fresh_authority_loss_before_or_after_actual_save_rolls_back_number_attempt_and_same_audit_then_same_key_retries()
    {
        foreach (var afterSave in new[] { false, true })
        foreach (var interceptAudit in new[] { false, true })
        {
            await using var run = await Run.StartAsync(); var note = await DraftedInstructionAsync(run, Owners.For(CompanyA));
            var probe = new DispatchApprovalSaveProbe();
            await using var scope = DispatchApprovalScope(run, interceptAudit, probe);
            var before = (await scope.Store.FindInstructionAsync(note.Id))!.Instruction.Version;
            var envelope = run.Envelope("dispatch-owned-retry") with { ActorId = "SENTETIK-independent-checker" };
            var observed = false;
            Task Fence(CancellationToken _)
            {
                if (afterSave ? probe.Saved : scope.Writer.HasPendingDrafts)
                { observed = true; throw new LogisticsInstructionServiceException(403, "forbidden"); }
                return Task.CompletedTask;
            }
            await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => scope.Store.ApproveDispatchNoteOwnedAsync(
                envelope, Hash("dispatch-owned-retry"), note.Id, note.Note, before, Approved, Fence));
            Assert.True(observed); Assert.Equal(afterSave ? 1 : 0, probe.Saves);
            Assert.Empty(scope.Context.ChangeTracker.Entries()); Assert.False(scope.Writer.HasPendingDrafts);
            Assert.Equal(before, await run.ScalarAsync<long>("SELECT Version FROM logistics.LoadingInstructions WHERE Id=@id", ("@id", note.Id)));
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT Status FROM logistics.DispatchNotes WHERE DispatchNoteId=@id", ("@id", note.Note)));
            await AssertNoDispatchApprovalEffects(run, note.Id);
            var saved = await scope.Store.ApproveDispatchNoteOwnedAsync(envelope, Hash("dispatch-owned-retry"), note.Id, note.Note, before, Approved, _ => Task.CompletedTask);
            Assert.NotNull(saved); Assert.True(saved.CommandReceiptProven); Assert.Equal(before + 1, saved.DispatchNoteApprovalReceipt!.ApprovedVersion);
            Assert.Equal("IRS-000001", saved.Instruction.Instruction.DispatchNotes.Single(n => n.DispatchNoteId == note.Note).Number!.Text);
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingAttempts WHERE CommandName=@command AND InstructionId=@id",
                ("@command", LogisticsCommands.ApproveDispatchNoteV1), ("@id", note.Id)));
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=N'logistics.loading-instruction.dispatch-note-approved.v1'", ("@id", note.Id.ToString("D"))));
        }
    }

    [SqlServerFact]
    public async Task Dispatch_owned_entry_preserves_borrowed_transaction_dirty_rows_and_caller_audit()
    {
        await using var run = await Run.StartAsync(); var note = await DraftedInstructionAsync(run, Owners.For(CompanyA));
        foreach (var boundary in new[] { "borrowed", "dirty", "audit", "missing-writer", "ambient" })
        foreach (var operation in new[] { "replay", "approve", "refusal" })
        {
            await using var scope = DispatchApprovalScope(run);
            var version = (await scope.Store.FindInstructionAsync(note.Id))!.Instruction.Version;
            var store = boundary == "missing-writer" ? new LogisticsStore(scope.Context, scope.Company, scope.Collector, TimeProvider.System) : scope.Store;
            await using var borrowed = boundary == "borrowed" ? await scope.Context.Database.BeginTransactionAsync() : null;
            var row = boundary == "dirty" ? await scope.Context.Instructions.SingleAsync(x => x.Id == note.Id) : null;
            if (row is not null) row.Version++;
            if (boundary is "audit" or "missing-writer") scope.Collector.Enqueue(WeighingCallerDraft(run));
            using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
            var called = false;
            Task Fence(CancellationToken _) { called = true; return Task.CompletedTask; }
            var envelope = run.Envelope("dispatch-entry") with { ActorId = "SENTETIK-independent-checker" };
            var error = await Assert.ThrowsAsync<LogisticsStoreException>(async () =>
            {
                if (operation == "approve") await store.ApproveDispatchNoteOwnedAsync(envelope, Hash("dispatch-entry"), note.Id, note.Note, version, Approved, Fence);
                else if (operation == "replay") await store.FindDispatchNoteApprovalReplayAsync(envelope, Hash("dispatch-entry"), note.Id, note.Note, version, Fence);
                else await store.RecordDispatchNoteApprovalRefusalAsync(envelope, Hash("dispatch-entry"), new(409, "SENTETIK", []), Fence);
            });
            Assert.Equal("logistics_dispatch_note_approval_unavailable", error.Code); Assert.False(called);
            Assert.Same(borrowed, scope.Context.Database.CurrentTransaction);
            Assert.Equal(boundary == "dirty", scope.Context.ChangeTracker.HasChanges());
            Assert.Equal(boundary is "audit" or "missing-writer", scope.Writer.HasPendingDrafts);
            if (row is not null) Assert.Equal(version + 1, row.Version);
            if (boundary is "audit" or "missing-writer") scope.Writer.DiscardPending();
        }
        await AssertNoDispatchApprovalEffects(run, note.Id);
    }

    [SqlServerFact]
    public async Task Dispatch_owned_captured_company_drift_after_save_uses_explicit_mutable_test_double_and_rolls_back()
    {
        await using var run = await Run.StartAsync(); var note = await DraftedInstructionAsync(run, Owners.For(CompanyA));
        var company = new DispatchApprovalMutableCompany { CompanyId = CompanyA };
        var probe = new DispatchApprovalSaveProbe();
        await using var scope = DispatchApprovalScope(run, true, probe, company);
        var version = (await scope.Store.FindInstructionAsync(note.Id))!.Instruction.Version;
        var changed = false;
        var error = await Assert.ThrowsAsync<LogisticsStoreException>(() => scope.Store.ApproveDispatchNoteOwnedAsync(
            run.Envelope("dispatch-company") with { ActorId = "SENTETIK-independent-checker" }, Hash("dispatch-company"), note.Id, note.Note, version, Approved,
            _ => { if (probe.Saved) { changed = true; company.CompanyId = CompanyB; } return Task.CompletedTask; }));
        Assert.Equal(LogisticsStoreCodes.CompanyContextMismatch, error.Code); Assert.True(changed);
        Assert.Empty(scope.Context.ChangeTracker.Entries()); Assert.False(scope.Writer.HasPendingDrafts);
        await AssertNoDispatchApprovalEffects(run, note.Id);
    }

    [SqlServerFact]
    public async Task Dispatch_original_receipt_replays_after_later_e_number_and_cancellation_without_business_revalidation_or_latest_version_substitution()
    {
        await using var run = await Run.StartAsync(); var note = await DraftedInstructionAsync(run, Owners.For(CompanyA));
        await using var scope = DispatchApprovalScope(run);
        var version = (await scope.Store.FindInstructionAsync(note.Id))!.Instruction.Version;
        var envelope = run.Envelope("dispatch-original") with { ActorId = "SENTETIK-independent-checker" };
        var first = await scope.Store.ApproveDispatchNoteOwnedAsync(envelope, Hash("dispatch-original"), note.Id, note.Note, version, Approved, _ => Task.CompletedTask);
        var receipt = Assert.IsType<LogisticsDispatchNoteApprovalReceiptV1>(first!.DispatchNoteApprovalReceipt);
        await scope.Store.MutateInstructionAsync(run.Envelope("dispatch-e-number"), Hash("e-number"), LogisticsCommands.SetEDispatchNumberV1, note.Id,
            (item, now) => item.SetEDispatchNumber(version + 1, note.Note, "GYM2026000000001", run.Actor, now), EDispatchRecorded);
        await scope.Store.MutateInstructionAsync(run.Envelope("dispatch-cancel"), Hash("cancel"), LogisticsCommands.CancelDispatchNoteV1, note.Id,
            (item, now) => item.CancelDispatchNote(version + 2, note.Note, run.Actor, "SENTETIK correction", now), Cancelled);
        var fences = 0;
        var replay = await scope.Store.FindDispatchNoteApprovalReplayAsync(envelope, Hash("dispatch-original"), note.Id, note.Note, version,
            _ => { fences++; return Task.CompletedTask; });
        Assert.NotNull(replay); Assert.True(replay.CommandReceiptProven); Assert.Equal(receipt, replay.DispatchNoteApprovalReceipt);
        Assert.Equal(version + 1, receipt.ApprovedVersion); Assert.Equal(version + 3, replay.Instruction!.Instruction.Version);
        Assert.Equal(DispatchNoteStatus.Cancelled, replay.Instruction.Instruction.DispatchNotes.Single(x => x.DispatchNoteId == note.Note).Status);
        Assert.True(fences >= 3);
        Assert.Null(await scope.Store.FindDispatchNoteApprovalReplayAsync(envelope with { IdempotencyKey = "dispatch-cancel" }, Hash("cancel"), note.Id, note.Note, version, _ => Task.CompletedTask));
        var denied = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => scope.Store.FindDispatchNoteApprovalReplayAsync(envelope, Hash("dispatch-original"), note.Id, note.Note, version,
            _ => throw new LogisticsInstructionServiceException(403, "forbidden")));
        Assert.Equal(403, denied.Status);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingAttempts WHERE InstructionId=@id AND CommandName=@command", ("@id", note.Id), ("@command", LogisticsCommands.ApproveDispatchNoteV1)));
    }

    [SqlServerFact]
    public async Task Dispatch_legacy_current_approved_view_without_original_receipt_is_not_promoted_to_Work_proof()
    {
        await using var run = await Run.StartAsync(); var note = await DraftedInstructionAsync(run, Owners.For(CompanyA));
        await using var scope = DispatchApprovalScope(run);
        var version = (await scope.Store.FindInstructionAsync(note.Id))!.Instruction.Version;
        var envelope = run.Envelope("dispatch-legacy") with { ActorId = "SENTETIK-independent-checker" };
        var legacy = await scope.Store.MutateInstructionAsync(envelope, Hash("legacy"), LogisticsCommands.ApproveDispatchNoteV1, note.Id,
            (item, now) => item.ApproveDispatchNote(version, note.Note, envelope.ActorId, now), Approved);
        Assert.False(legacy!.CommandReceiptProven);
        var replay = await scope.Store.FindDispatchNoteApprovalReplayAsync(envelope, Hash("legacy"), note.Id, note.Note, version, _ => Task.CompletedTask);
        Assert.NotNull(replay); Assert.False(replay.CommandReceiptProven); Assert.Null(replay.DispatchNoteApprovalReceipt);
        Assert.Equal(DispatchNoteStatus.Approved, replay.Instruction!.Instruction.DispatchNotes.Single(n => n.DispatchNoteId == note.Note).Status);
    }

    [SqlServerFact]
    public async Task Dispatch_actual_saving_concurrency_fault_aborts_audit_and_number_without_mutation_retry_and_a_later_same_key_can_execute()
    {
        await using var run = await Run.StartAsync(); var note = await DraftedInstructionAsync(run, Owners.For(CompanyA));
        var probe = new DispatchApprovalSaveProbe { ConcurrencyOnce = true };
        await using var scope = DispatchApprovalScope(run, true, probe);
        var version = (await scope.Store.FindInstructionAsync(note.Id))!.Instruction.Version;
        var envelope = run.Envelope("dispatch-concurrency") with { ActorId = "SENTETIK-independent-checker" };
        var error = await Assert.ThrowsAsync<LogisticsStoreException>(() => scope.Store.ApproveDispatchNoteOwnedAsync(envelope, Hash("concurrency"), note.Id, note.Note, version, Approved, _ => Task.CompletedTask));
        Assert.Equal(LogisticsStoreCodes.ConcurrentChange, error.Code); Assert.Equal(1, probe.Saves);
        Assert.Empty(scope.Context.ChangeTracker.Entries()); Assert.False(scope.Writer.HasPendingDrafts);
        Assert.Null(await scope.Store.FindDispatchNoteApprovalReplayAsync(envelope, Hash("concurrency"), note.Id, note.Note, version, _ => Task.CompletedTask));
        Assert.Equal(1, probe.Saves); await AssertNoDispatchApprovalEffects(run, note.Id);
        Assert.True((await scope.Store.ApproveDispatchNoteOwnedAsync(envelope, Hash("concurrency"), note.Id, note.Note, version, Approved, _ => Task.CompletedTask))!.CommandReceiptProven);
    }

    private static async Task AssertNoDispatchApprovalEffects(Run run, Guid instructionId)
    {
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingAttempts WHERE InstructionId=@id AND CommandName=@command", ("@id", instructionId), ("@command", LogisticsCommands.ApproveDispatchNoteV1)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=N'logistics.loading-instruction.dispatch-note-approved.v1'", ("@id", instructionId.ToString("D"))));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.DispatchNotes WHERE InstructionId=@id AND SeriesCode IS NOT NULL", ("@id", instructionId)));
    }

    private static DispatchApprovalStoreScope DispatchApprovalScope(Run run, bool interceptAudit = true,
        IInterceptor? probe = null, ICompanyContext? overriddenCompany = null)
    {
        var auditScope = run.Factory.Services.CreateScope();
        var realCompany = auditScope.ServiceProvider.GetRequiredService<ICompanyContext>(); realCompany.Set(CompanyA);
        var company = overriddenCompany ?? realCompany;
        var collector = auditScope.ServiceProvider.GetRequiredService<AuditEventCollector>();
        var writer = auditScope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
        var options = new DbContextOptionsBuilder<LogisticsDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString,
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LogisticsDbContext.Schema));
        if (interceptAudit) options.AddInterceptors(new AuditSaveChangesInterceptor(collector, company));
        if (probe is not null) options.AddInterceptors(probe);
        var context = new LogisticsDbContext(options.Options, company);
        return new(context, new LogisticsStore(context, company, collector, TimeProvider.System, writer), auditScope, company);
    }
    private sealed class DispatchApprovalStoreScope(LogisticsDbContext context, LogisticsStore store, IServiceScope auditScope,
        ICompanyContext company) : IAsyncDisposable
    {
        public LogisticsDbContext Context => context;
        public LogisticsStore Store => store;
        public ICompanyContext Company => company;
        public AuditEventCollector Collector => auditScope.ServiceProvider.GetRequiredService<AuditEventCollector>();
        public IAuditTransactionWriterV1 Writer => auditScope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
        public async ValueTask DisposeAsync() { await context.DisposeAsync(); auditScope.Dispose(); }
    }
    private sealed class DispatchApprovalMutableCompany : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; set; }
        public void Set(Guid id) => CompanyId = id; // Explicit drift-only test double; never re-Set real immutable CompanyContext.
    }
    private sealed class DispatchApprovalSaveProbe : SaveChangesInterceptor
    {
        public int Saves; public bool Saved; public bool ConcurrencyOnce;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (data.Context is LogisticsDbContext db && db.ChangeTracker.Entries<LoadingAttemptRow>()
                .Any(e => e.State == EntityState.Added && e.Entity.CommandName == LogisticsCommands.ApproveDispatchNoteV1))
            {
                Saves++;
                if (ConcurrencyOnce) { ConcurrencyOnce = false; throw new DbUpdateConcurrencyException("SENTETIK actual saving fault"); }
            }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { Saved = true; return ValueTask.FromResult(result); }
    }
}

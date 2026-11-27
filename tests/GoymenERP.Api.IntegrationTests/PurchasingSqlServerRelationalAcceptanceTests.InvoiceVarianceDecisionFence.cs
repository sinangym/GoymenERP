using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    private const string VarianceChecker = "SENTETIK-variance-checker";

    [SqlServerFact]
    public async Task Incoming_invoice_variance_missing_order_approval_proof_is_unavailable_without_a_decision()
    {
        await using var run = await Run.StartAsync();
        var id = await PrepareNativeVarianceAsync(run);
        var before = await VarianceCountsAsync(run, id);
        // Legacy/corruption negative, not an invented native transition: unknown actor is never an empty exclusion.
        await run.ExecuteAsync("UPDATE purchasing.PurchaseOrders SET ApprovedBy=NULL, ApprovedAtUtc=NULL WHERE Id IN "
            + "(SELECT OrderId FROM purchasing.InvoiceOrderBindings WHERE InvoiceId=@id)", ("@id", id));
        await using var writer = run.Store(CompanyA);
        var error = await Assert.ThrowsAsync<PurchasingServiceException>(() => ApproveNativeVarianceAsync(run,
            (IPurchasingIncomingInvoiceStoreV1)writer.Store, id, "variance-missing-proof", _ => Task.CompletedTask));
        Assert.Equal(503, error.Status);
        Assert.Empty(writer.Context.ChangeTracker.Entries());
        Assert.Equal(before, await VarianceCountsAsync(run, id));
    }

    [SqlServerFact]
    public async Task Incoming_invoice_terminal_refusal_rechecks_after_save_rolls_back_and_retries_with_fresh_authority()
    {
        await using var run = await Run.StartAsync();
        var id = await PrepareNativeVarianceAsync(run);
        var before = await VarianceCountsAsync(run, id);
        var allowed = true;
        var saved = new RequestDecisionSavedInterceptor(() => allowed = false);
        Task Fence(CancellationToken _) => allowed ? Task.CompletedTask : throw new PurchasingServiceException(403, "forbidden");
        var refusal = new PurchasingTerminalRefusal(409, "version_conflict", []);
        await using (var writer = run.Store(CompanyA, saved))
            await Assert.ThrowsAsync<PurchasingServiceException>(() => ((IPurchasingIncomingInvoiceStoreV1)writer.Store)
                .RecordIncomingInvoiceRefusalAsync(PurchaseIncomingInvoiceCommandsV1.ApproveVariance,
                    run.Envelope("variance-terminal", actor: VarianceChecker), Hash("variance-terminal"), refusal, Fence));
        Assert.Equal(1, saved.Calls);
        Assert.Equal(before, await VarianceCountsAsync(run, id));
        allowed = true;
        await using var retry = run.Store(CompanyA);
        var owner = (IPurchasingIncomingInvoiceStoreV1)retry.Store;
        var accepted = await owner.RecordIncomingInvoiceRefusalAsync(PurchaseIncomingInvoiceCommandsV1.ApproveVariance,
            run.Envelope("variance-terminal", actor: VarianceChecker), Hash("variance-terminal"), refusal, Fence);
        Assert.False(accepted.IsReplay); Assert.Equal("version_conflict", accepted.Refusal!.Code);
        var replay = await owner.RecordIncomingInvoiceRefusalAsync(PurchaseIncomingInvoiceCommandsV1.ApproveVariance,
            run.Envelope("variance-terminal", actor: VarianceChecker), Hash("variance-terminal"), refusal, Fence);
        Assert.True(replay.IsReplay);
        Assert.Equal((before.Revisions, before.Attempts + 1, before.Audits), await VarianceCountsAsync(run, id));
    }

    [SqlServerFact]
    public async Task Incoming_invoice_variance_delegation_loss_after_save_rolls_back_revision_attempt_and_audit_then_retries()
    {
        await using var run = await Run.StartAsync();
        var id = await PrepareNativeVarianceAsync(run);
        var before = await VarianceCountsAsync(run, id);
        var allowed = true;
        var execution = new WorkDecisionExecutionFenceV1();
        using var lease = execution.Enter(_ => Task.FromResult(allowed));
        var authority = new WorkDecisionAuthorityV1(new RequestDecisionAuthority(), execution);
        var saved = new RequestDecisionSavedInterceptor(() => allowed = false);
        async Task Fence(CancellationToken token)
        {
            foreach (var permission in new[] { PurchaseIncomingInvoicePermissionNamesV1.ApproveVariance, PurchasingPermissionManifestV1.OrdersPriceRead })
                if (!(await authority.RevalidatePermissionScopeAsync(permission, CompanyA, token)).IsGranted)
                    throw new PurchasingServiceException(403, "forbidden");
        }
        await using (var writer = run.Store(CompanyA, saved))
        {
            var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() => ApproveNativeVarianceAsync(run,
                (IPurchasingIncomingInvoiceStoreV1)writer.Store, id, "variance-retry", Fence));
            Assert.Equal(403, denied.Status); Assert.Equal(1, saved.Calls);
            Assert.Empty(writer.Context.ChangeTracker.Entries());
        }
        Assert.Equal(before, await VarianceCountsAsync(run, id));
        allowed = true;
        await using var retry = run.Store(CompanyA);
        var owner = (IPurchasingIncomingInvoiceStoreV1)retry.Store;
        var accepted = await ApproveNativeVarianceAsync(run, owner, id, "variance-retry", Fence);
        Assert.Equal(3L, accepted!.Invoice!.Version);
        Assert.NotNull(accepted.Invoice.VarianceDecision);
        Assert.Equal(PurchaseIncomingInvoiceStatus.Matched, accepted.Invoice.Status);
        var replay = await ApproveNativeVarianceAsync(run, owner, id, "variance-retry", Fence);
        Assert.True(replay!.IsReplay); Assert.Equal(run.Correlation, replay.OriginalCorrelationId);
        Assert.Equal((before.Revisions + 1, before.Attempts + 1, before.Audits + 1), await VarianceCountsAsync(run, id));
        allowed = false;
        await Assert.ThrowsAsync<PurchasingServiceException>(() => ApproveNativeVarianceAsync(run, owner, id, "variance-retry", Fence));
    }

    [SqlServerFact]
    public async Task Incoming_invoice_variance_native_command_excludes_the_actual_matched_order_approver()
    {
        await using var run = await Run.StartAsync();
        var id = await PrepareNativeVarianceAsync(run);
        var before = await VarianceCountsAsync(run, id);
        await using var writer = run.Store(CompanyA);
        var owner = (IPurchasingIncomingInvoiceStoreV1)writer.Store;
        var denied = await Assert.ThrowsAsync<PurchasingRuleException>(() => owner.MutateIncomingInvoiceAsync(id,
            PurchaseIncomingInvoiceCommandsV1.ApproveVariance, run.Envelope("variance-own-order", actor: OrderApprover), Hash("variance-own-order"), [],
            (invoice, _) => { invoice.ApproveVariance(2, OrderApprover, "SENTETIK gerekçe", DateTimeOffset.UtcNow); return Task.CompletedTask; }, _ => Task.CompletedTask));
        Assert.Equal(PurchaseIncomingInvoice.MakerChecker, denied.Code);
        Assert.Empty(writer.Context.ChangeTracker.Entries());
        Assert.Equal(before, await VarianceCountsAsync(run, id));
        // Different native checker, same source and frozen formula, succeeds without another match/receipt.
        Assert.NotNull((await ApproveNativeVarianceAsync(run, owner, id, "variance-independent", _ => Task.CompletedTask))!.Invoice!.VarianceDecision);
    }

    [SqlServerFact]
    public async Task Incoming_invoice_guard_preserves_ambient_caller_state_before_new_write_replay_and_refusal_callbacks()
    {
        await using var run = await Run.StartAsync();
        var id = await RecordedNativeVarianceAsync(run);
        var before = await VarianceCountsAsync(run, id);
        await using var writer = run.Store(CompanyA);
        var tracked = await writer.Context.IncomingInvoices.SingleAsync(x => x.InvoiceId == id);
        using (var ambient = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
        {
            await RefuseBorrowedVarianceCallsAsync(run, (IPurchasingIncomingInvoiceStoreV1)writer.Store, id);
            Assert.Equal(System.Transactions.TransactionStatus.Active, System.Transactions.Transaction.Current!.TransactionInformation.Status);
        }
        Assert.Same(tracked, writer.Context.ChangeTracker.Entries().Single().Entity);
        Assert.Equal(EntityState.Unchanged, writer.Context.Entry(tracked).State);
        Assert.Equal(before, await VarianceCountsAsync(run, id));
    }

    [SqlServerFact]
    public async Task Incoming_invoice_guard_preserves_borrowed_transaction_before_new_write_replay_and_refusal_callbacks()
    {
        await using var run = await Run.StartAsync();
        var id = await RecordedNativeVarianceAsync(run);
        await using var writer = run.Store(CompanyA);
        var tracked = await writer.Context.IncomingInvoices.SingleAsync(x => x.InvoiceId == id);
        await using var borrowed = await writer.Context.Database.BeginTransactionAsync();
        await RefuseBorrowedVarianceCallsAsync(run, (IPurchasingIncomingInvoiceStoreV1)writer.Store, id);
        Assert.Same(borrowed, writer.Context.Database.CurrentTransaction);
        Assert.Same(tracked, writer.Context.ChangeTracker.Entries().Single().Entity);
        Assert.Equal(EntityState.Unchanged, writer.Context.Entry(tracked).State);
        Assert.Equal(id, await writer.Context.IncomingInvoices.AsNoTracking().Where(x => x.InvoiceId == id).Select(x => x.InvoiceId).SingleAsync());
        await borrowed.RollbackAsync();
    }

    [SqlServerFact]
    public async Task Incoming_invoice_guard_preserves_dirty_caller_values_before_new_write_replay_and_refusal_callbacks()
    {
        await using var run = await Run.StartAsync();
        var id = await RecordedNativeVarianceAsync(run);
        await using var writer = run.Store(CompanyA);
        var tracked = await writer.Context.IncomingInvoices.SingleAsync(x => x.InvoiceId == id);
        var original = tracked.OfficialNumber;
        tracked.OfficialNumber += "-caller";
        Assert.True(writer.Context.ChangeTracker.HasChanges());
        await RefuseBorrowedVarianceCallsAsync(run, (IPurchasingIncomingInvoiceStoreV1)writer.Store, id);
        Assert.Same(tracked, writer.Context.ChangeTracker.Entries().Single().Entity);
        Assert.Equal(EntityState.Modified, writer.Context.Entry(tracked).State);
        Assert.Equal(original + "-caller", tracked.OfficialNumber);
        Assert.Equal(original, writer.Context.Entry(tracked).Property(x => x.OfficialNumber).OriginalValue);
        Assert.Equal(original, await run.ScalarAsync<string>("SELECT OfficialNumber FROM purchasing.IncomingInvoices WHERE InvoiceId=@id", ("@id", id)));
    }

    [SqlServerFact]
    public async Task Incoming_invoice_guard_preserves_pending_caller_audit_before_new_write_replay_and_refusal_callbacks()
    {
        await using var run = await Run.StartAsync();
        var id = await RecordedNativeVarianceAsync(run);
        await using var scope = run.Factory.Services.CreateAsyncScope();
        var company = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
        var database = scope.ServiceProvider.GetRequiredService<PurchasingDbContext>();
        var collector = scope.ServiceProvider.GetRequiredService<IAuditEventCollector>();
        var auditWriter = scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
        collector.Enqueue(VariancePendingAudit(run, id));
        var owner = new PurchasingStore(database, company, collector, TimeProvider.System, decisionAuditWriter: auditWriter);
        await RefuseBorrowedVarianceCallsAsync(run, owner, id);
        Assert.True(auditWriter.HasPendingDrafts);
        Assert.Empty(database.ChangeTracker.Entries());
        auditWriter.DiscardPending();
    }

    [SqlServerFact]
    public async Task Incoming_invoice_owned_abort_discards_post_save_pending_audit_and_clears_only_its_attempt()
    {
        await using var run = await Run.StartAsync();
        var id = await PrepareNativeVarianceAsync(run);
        var before = await VarianceCountsAsync(run, id);
        await using var scope = run.Factory.Services.CreateAsyncScope();
        var company = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
        var collector = scope.ServiceProvider.GetRequiredService<AuditEventCollector>();
        var auditWriter = scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
        var saved = new RequestDecisionSavedInterceptor(() => collector.Enqueue(VariancePendingAudit(run, id)));
        var options = new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema))
            .AddInterceptors(new AuditSaveChangesInterceptor(collector, company), saved).Options;
        await using var database = new PurchasingDbContext(options, company);
        var owner = new PurchasingStore(database, company, collector, TimeProvider.System, decisionAuditWriter: auditWriter);
        await Assert.ThrowsAsync<PurchasingServiceException>(() => ApproveNativeVarianceAsync(run, owner, id, "variance-own-abort", _ =>
        {
            if (saved.Calls > 0) { Assert.True(auditWriter.HasPendingDrafts); throw new PurchasingServiceException(403, "forbidden"); }
            return Task.CompletedTask;
        }));
        Assert.Equal(1, saved.Calls); Assert.False(auditWriter.HasPendingDrafts);
        Assert.Empty(database.ChangeTracker.Entries());
        Assert.Equal(before, await VarianceCountsAsync(run, id));
    }

    private static async Task<Guid> PrepareNativeVarianceAsync(Run run)
    {
        var invoice = IncomingInvoice(run);
        var receipt = await IncomingInvoiceReceiptAsync(run, invoice);
        await using var writer = run.Store(CompanyA);
        var owner = (IPurchasingIncomingInvoiceStoreV1)writer.Store;
        await owner.RecordIncomingInvoiceAsync(invoice, run.Envelope("variance-record"), Hash("variance-record"), _ => Task.CompletedTask);
        // Real recorded receipt membership/bindings; the frozen commercial comparison input is synthetic.
        // This does not claim real matching master-owner or a complete Work/database acceptance.
        await owner.MutateIncomingInvoiceAsync(invoice.Id, PurchaseIncomingInvoiceCommandsV1.Match,
            run.Envelope("variance-match", actor: "SENTETIK-variance-matcher"), Hash("variance-match"), [receipt.OrderId],
            (value, _) => { value.Match(1, [receipt with { ExpectedNet = 30m }], new(Guid.NewGuid(), 1, 0m, 0m), "SENTETIK-variance-matcher"); return Task.CompletedTask; },
            _ => Task.CompletedTask);
        return invoice.Id;
    }

    private static Task<PurchasingIncomingInvoiceWriteResult?> ApproveNativeVarianceAsync(Run run, IPurchasingIncomingInvoiceStoreV1 owner,
        Guid id, string key, Func<CancellationToken, Task> fence) => owner.MutateIncomingInvoiceAsync(id,
            PurchaseIncomingInvoiceCommandsV1.ApproveVariance, run.Envelope(key, actor: VarianceChecker), Hash(key), [],
            (invoice, _) => { invoice.ApproveVariance(2, VarianceChecker, "SENTETIK insan gerekçesi", DateTimeOffset.UtcNow); return Task.CompletedTask; }, fence);

    private static async Task<Guid> RecordedNativeVarianceAsync(Run run)
    {
        var id = await PrepareNativeVarianceAsync(run);
        await using var writer = run.Store(CompanyA);
        await ApproveNativeVarianceAsync(run, (IPurchasingIncomingInvoiceStoreV1)writer.Store, id, "variance-recorded", _ => Task.CompletedTask);
        return id;
    }

    private static async Task RefuseBorrowedVarianceCallsAsync(Run run, IPurchasingIncomingInvoiceStoreV1 owner, Guid id)
    {
        var calls = 0;
        Task Fence(CancellationToken _) { calls++; return Task.CompletedTask; }
        foreach (var key in new[] { "variance-new", "variance-recorded" })
            await Assert.ThrowsAsync<InvalidOperationException>(() => ApproveNativeVarianceAsync(run, owner, id, key, Fence));
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.FindIncomingInvoiceAttemptAsync(PurchaseIncomingInvoiceCommandsV1.ApproveVariance,
            run.Envelope("variance-recorded", actor: VarianceChecker), Hash("variance-recorded")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.RecordIncomingInvoiceRefusalAsync(PurchaseIncomingInvoiceCommandsV1.ApproveVariance,
            run.Envelope("variance-refused", actor: VarianceChecker), Hash("variance-refused"), new(409, "version_conflict", []), Fence));
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.RecordIncomingInvoiceAsync(IncomingInvoice(run),
            run.Envelope("variance-new-record"), Hash("variance-new-record"), Fence));
        Assert.Equal(0, calls);
    }

    private static async Task<(int Revisions, int Attempts, int Audits)> VarianceCountsAsync(Run run, Guid id) =>
        (await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.IncomingInvoiceRevisions WHERE InvoiceId=@id", ("@id", id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.IncomingInvoiceAttempts"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Purchasing.IncomingInvoice' AND EntityId=@id", ("@id", id.ToString("D"))));

    private static AuditEventDraft VariancePendingAudit(Run run, Guid id) => new(CompanyA, "purchasing.synthetic-variance-caller.v1",
        "Purchasing.SyntheticVarianceCaller", id.ToString("D"), "prepared", null, 1, AuditActorTypes.User, run.Actor,
        PurchaseIncomingInvoicePermissionNamesV1.ApproveVariance, [], "{}", "{}", "SENTETIK pending caller audit", null,
        AuditAiInvolvement.None, null, "Web", null, null, run.Correlation, null, "variance-caller-audit");
}

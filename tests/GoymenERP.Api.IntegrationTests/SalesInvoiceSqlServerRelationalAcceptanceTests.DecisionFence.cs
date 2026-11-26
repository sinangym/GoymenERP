using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesInvoiceSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Native_invoice_late_delegation_loss_rolls_back_decision_attempt_and_audit_before_commit()
    {
        await using var run = await Run.StartAsync();
        var invoiceId = Guid.NewGuid();
        await using (var preparer = run.Store(CompanyA))
        {
            await preparer.Invoices.StoreInvoiceAsync(run.Envelope("guarded-create"), Hash('a'),
                number => Invoice(invoiceId, number, run.Actor, Guid.NewGuid(), Guid.NewGuid()));
            await preparer.Invoices.DecideInvoiceAsync(run.Envelope("guarded-submit"), Hash('b'), SalesInvoiceCommands.SubmitV1,
                invoiceId, (invoice, now) => invoice.Submit(1, run.Actor, now),
                new("sales.invoice.submitted.v1", "Submit", "SENTETIK"));
        }
        const string checker = "SENTETIK-independent-checker";
        var authorityAllowed = true;
        var execution = new WorkDecisionExecutionFenceV1();
        using var lease = execution.Enter(_ => Task.FromResult(authorityAllowed));
        var authority = new WorkDecisionAuthorityV1(new InvoiceFenceAuthority(), execution);
        async Task Fence(CancellationToken token)
        {
            var decision = await authority.RevalidatePermissionScopeAsync(SalesPermissionManifestV1.InvoicesApprove, CompanyA, token);
            if (!decision.IsGranted) throw new SalesDraftServiceException(403, "forbidden");
        }
        var saved = new InvoiceDecisionSavedInterceptor(() => authorityAllowed = false);
        await using (var writer = run.InvoiceDecisionStore(saved))
        {
            var denied = await Assert.ThrowsAsync<SalesDraftServiceException>(() => writer.Invoices.DecideInvoiceGuardedAsync(
                run.Envelope("guarded-approve", actor: checker, permission: SalesPermissionManifestV1.InvoicesApprove), Hash('c'),
                SalesInvoiceCommands.ApproveV1, invoiceId, (invoice, now) => invoice.Approve(2, checker, now),
                new("sales.invoice.approved.v1", "Approve", "SENTETIK"), Fence));
            Assert.Equal(403, denied.Status);
            Assert.Equal(1, saved.Calls);
            Assert.Empty(writer.Context.ChangeTracker.Entries());
        }
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM sales.Invoices WHERE Id = @id", ("@id", invoiceId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.InvoiceAttempts WHERE InvoiceId = @id AND CommandName = @command",
            ("@id", invoiceId), ("@command", SalesInvoiceCommands.ApproveV1)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id AND EventType = N'sales.invoice.approved.v1'",
            ("@id", invoiceId.ToString("D"))));

        authorityAllowed = true;
        await using var retry = run.Store(CompanyA);
        var approved = await retry.Invoices.DecideInvoiceGuardedAsync(
            run.Envelope("guarded-approve", actor: checker, permission: SalesPermissionManifestV1.InvoicesApprove), Hash('c'),
            SalesInvoiceCommands.ApproveV1, invoiceId, (invoice, now) => invoice.Approve(2, checker, now),
            new("sales.invoice.approved.v1", "Approve", "SENTETIK"), Fence);
        Assert.Equal((SalesInvoiceState.Approved, 3L), (approved!.Invoice.State, approved.Invoice.Version));
        var before = await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.InvoiceAttempts WHERE InvoiceId = @id", ("@id", invoiceId));
        var replay = await retry.Invoices.DecideInvoiceGuardedAsync(
            run.Envelope("guarded-approve", correlation: "SENTETIK-replay", actor: checker, permission: SalesPermissionManifestV1.InvoicesApprove),
            Hash('c'), SalesInvoiceCommands.ApproveV1, invoiceId, (_, _) => throw new InvalidOperationException("Replay cannot mutate"),
            new("sales.invoice.approved.v1", "Approve", "SENTETIK"), Fence);
        Assert.Equal((SalesDraftWriteOutcome.Replayed, run.Correlation), (replay!.Outcome, replay.OriginalCorrelationId));
        authorityAllowed = false;
        await Assert.ThrowsAsync<SalesDraftServiceException>(() => retry.Invoices.DecideInvoiceGuardedAsync(
            run.Envelope("guarded-approve", actor: checker, permission: SalesPermissionManifestV1.InvoicesApprove), Hash('c'),
            SalesInvoiceCommands.ApproveV1, invoiceId, (_, _) => throw new InvalidOperationException("Replay cannot mutate"),
            new("sales.invoice.approved.v1", "Approve", "SENTETIK"), Fence));
        Assert.Equal(before, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.InvoiceAttempts WHERE InvoiceId = @id", ("@id", invoiceId)));
    }

    private sealed partial class Run
    {
        public StoreScope InvoiceDecisionStore(IInterceptor interceptor)
        {
            var company = new CompanyContext(); company.Set(CompanyA);
            var audit = new AuditEventCollector();
            var db = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(audit, company), interceptor).Options, company);
            return new(db, new SalesStore(db, company, audit, TimeProvider.System));
        }
    }
    private sealed class InvoiceDecisionSavedInterceptor(Action afterSave) : SaveChangesInterceptor
    {
        public int Calls { get; private set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        { Calls++; afterSave(); return ValueTask.FromResult(result); }
    }
    private sealed class InvoiceFenceAuthority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
    }
}

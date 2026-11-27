using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesSubcontractServiceInvoiceSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Guarded_subcontract_update_and_refusal_reject_an_ambient_transaction_before_any_fence_or_receipt()
    {
        await using var run = await Run.StartAsync();
        var invoice = await CreateLifecycleAsync(run, Weighing, "ambient-create");
        await using var scope = run.SalesStore(CompanyA);
        var fenceCalls = 0;
        Task FreshFence(CancellationToken ct) { fenceCalls++; return Task.CompletedTask; }
        using (var ambient = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Store.UpdateLifecycleWithFenceAsync(
                run.Envelope("ambient-update", "SENTETIK-checker", SalesPermissionManifestV1.InvoicesApprove), Hash('a'),
                SalesSubcontractInvoiceLifecycleCommandsV1.Approve, invoice.Id, (_, _) => throw new InvalidOperationException("Must not mutate"),
                new(SalesSubcontractInvoiceLifecycleCommandsV1.Approve, "Approve", "SENTETIK"), FreshFence));
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Store.RecordLifecycleRefusalWithFenceAsync(
                run.Envelope("ambient-refusal", "SENTETIK-checker", SalesPermissionManifestV1.InvoicesApprove), Hash('a'),
                SalesSubcontractInvoiceLifecycleCommandsV1.Approve, new(409, SalesDraftProblemCodesV1.VersionConflict, []), FreshFence));
        }
        Assert.Equal(0, fenceCalls);
        Assert.False(scope.Context.ChangeTracker.HasChanges());
        Assert.Null(scope.Context.Database.CurrentTransaction);
        Assert.Equal(1, await run.ScalarAsync<long>("SELECT Version FROM sales.SubcontractServiceInvoices WHERE Id=@id", ("@id", invoice.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.SubcontractInvoiceCommandAttempts WHERE CommandName=@command", ("@command", SalesSubcontractInvoiceLifecycleCommandsV1.Approve)));
    }

    [SqlServerFact]
    public async Task Guarded_subcontract_update_and_refusal_preserve_a_borrowed_owner_transaction_without_writing()
    {
        await using var run = await Run.StartAsync();
        var invoice = await CreateLifecycleAsync(run, Weighing, "borrowed-create");
        await using var scope = run.SalesStore(CompanyA);
        await using var borrowed = await scope.Context.Database.BeginTransactionAsync();
        Task FreshFence(CancellationToken ct) => Task.CompletedTask;
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Store.UpdateLifecycleWithFenceAsync(
            run.Envelope("borrowed-update", "SENTETIK-checker", SalesPermissionManifestV1.InvoicesApprove), Hash('a'),
            SalesSubcontractInvoiceLifecycleCommandsV1.Approve, invoice.Id, (_, _) => throw new InvalidOperationException("Must not mutate"),
            new(SalesSubcontractInvoiceLifecycleCommandsV1.Approve, "Approve", "SENTETIK"), FreshFence));
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Store.RecordLifecycleRefusalWithFenceAsync(
            run.Envelope("borrowed-refusal", "SENTETIK-checker", SalesPermissionManifestV1.InvoicesApprove), Hash('a'),
            SalesSubcontractInvoiceLifecycleCommandsV1.Approve, new(409, SalesDraftProblemCodesV1.VersionConflict, []), FreshFence));
        Assert.Same(borrowed, scope.Context.Database.CurrentTransaction);
        Assert.False(scope.Context.ChangeTracker.HasChanges());
        Assert.False(await scope.Context.SubcontractInvoiceCommandAttempts.AsNoTracking()
            .AnyAsync(row => row.InvoiceId == invoice.Id && row.CommandName == SalesSubcontractInvoiceLifecycleCommandsV1.Approve));
        await borrowed.RollbackAsync();
    }

    [SqlServerFact]
    public async Task A_lost_fresh_fence_after_the_real_save_rolls_back_lifecycle_receipt_and_audit_before_commit()
    {
        await using var run = await Run.StartAsync();
        var invoice = await CreateLifecycleAsync(run, Weighing, "work-fence-create");
        await MoveLifecycleAsync(run, invoice.Id, "work-fence-submit", SalesSubcontractInvoiceLifecycleCommandsV1.Submit,
            (target, now) => target.Submit(1, "SENTETIK-submitter", now), "SENTETIK-submitter");
        await using var scope = run.SalesStore(CompanyA);
        var observedSavedRows = false;
        async Task FreshFence(CancellationToken ct)
        {
            if (!await scope.Context.SubcontractInvoiceCommandAttempts.AsNoTracking().AnyAsync(row => row.InvoiceId == invoice.Id
                && row.CommandName == SalesSubcontractInvoiceLifecycleCommandsV1.Approve, ct)) return;
            observedSavedRows = true;
            Assert.NotNull(scope.Context.Database.CurrentTransaction);
            Assert.False(scope.Context.ChangeTracker.HasChanges());
            Assert.Equal(3, await scope.Context.SubcontractServiceInvoices.AsNoTracking().Where(row => row.Id == invoice.Id).Select(row => row.Version).SingleAsync(ct));
            throw new SalesDraftServiceException(403, "forbidden");
        }
        var error = await Assert.ThrowsAsync<SalesDraftServiceException>(() => scope.Store.UpdateLifecycleWithFenceAsync(
            run.Envelope("work-fence-approve", "SENTETIK-checker", SalesPermissionManifestV1.InvoicesApprove), Hash('a'),
            SalesSubcontractInvoiceLifecycleCommandsV1.Approve, invoice.Id, (target, now) => target.Approve(2, "SENTETIK-checker", now),
            new(SalesSubcontractInvoiceLifecycleCommandsV1.Approve, "Approve", "SENTETIK"), FreshFence));
        Assert.Equal(403, error.Status); Assert.True(observedSavedRows);
        Assert.False(scope.Context.ChangeTracker.HasChanges());
        Assert.Equal(2, await run.ScalarAsync<long>("SELECT Version FROM sales.SubcontractServiceInvoices WHERE Id=@id", ("@id", invoice.Id)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.SubcontractInvoiceLifecycles WHERE InvoiceId=@id", ("@id", invoice.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.SubcontractInvoiceCommandAttempts WHERE InvoiceId=@id AND CommandName=@command", ("@id", invoice.Id), ("@command", SalesSubcontractInvoiceLifecycleCommandsV1.Approve)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND Operation=N'Approve'", ("@id", invoice.Id.ToString("D"))));
        Assert.Null(await scope.Store.FindLifecycleReplayAsync(run.Envelope("work-fence-approve", "SENTETIK-checker", SalesPermissionManifestV1.InvoicesApprove), Hash('a'), SalesSubcontractInvoiceLifecycleCommandsV1.Approve));
    }
}

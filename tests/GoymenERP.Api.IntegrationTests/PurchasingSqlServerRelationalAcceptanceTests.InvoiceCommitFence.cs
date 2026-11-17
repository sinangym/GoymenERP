using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Incoming_invoice_record_revoked_after_SQL_save_rolls_back_source_history_audit_and_attempt_before_same_key_retry()
    {
        await using var run = await Run.StartAsync();
        var invoice = IncomingInvoice(run);
        var envelope = run.Envelope("invoice-record-commit-fence");
        var hash = Hash("invoice-record-commit-fence");
        var observedSavedRows = false;
        await using (var scope = run.Store(CompanyA))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => ((IPurchasingIncomingInvoiceStoreV1)scope.Store)
                .RecordIncomingInvoiceAsync(invoice, envelope, hash, async token =>
                {
                    // Read through the real SQL transaction, not the EF tracker or a call counter.
                    if (await scope.Context.IncomingInvoices.AsNoTracking().AnyAsync(row => row.InvoiceId == invoice.Id, token))
                    {
                        observedSavedRows = true;
                        throw new InvalidOperationException("SENTETIK authority revoked after SQL save");
                    }
                }));
            Assert.True(observedSavedRows);
            Assert.Empty(scope.Context.ChangeTracker.Entries());
            Assert.Null(scope.Context.Database.CurrentTransaction);
        }
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.IncomingInvoices WHERE InvoiceId=@id", ("@id", invoice.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.IncomingInvoiceRevisions WHERE InvoiceId=@id", ("@id", invoice.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.IncomingInvoiceAttempts WHERE RequestHash=@hash", ("@hash", hash)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType='Purchasing.IncomingInvoice' AND EntityId=@id", ("@id", invoice.Id.ToString("D"))));
        await using var retry = run.Store(CompanyA);
        var result = await ((IPurchasingIncomingInvoiceStoreV1)retry.Store).RecordIncomingInvoiceAsync(invoice,
            envelope, hash, _ => Task.CompletedTask);
        Assert.False(result.IsReplay);
        Assert.Equal(invoice.Source, result.Invoice!.Source);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.IncomingInvoices WHERE InvoiceId=@id", ("@id", invoice.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.IncomingInvoiceAttempts WHERE RequestHash=@hash", ("@hash", hash)));
    }

    [SqlServerFact]
    public async Task Incoming_invoice_terminal_refusal_revoked_after_SQL_save_is_not_committed_or_replayed()
    {
        await using var run = await Run.StartAsync();
        var envelope = run.Envelope("invoice-refusal-commit-fence");
        var hash = Hash("invoice-refusal-commit-fence");
        var refusal = new PurchasingTerminalRefusal(409, PurchaseIncomingInvoice.SourceMismatch, []);
        var observedSavedRows = false;
        await using (var scope = run.Store(CompanyA))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => ((IPurchasingIncomingInvoiceStoreV1)scope.Store)
                .RecordIncomingInvoiceRefusalAsync(PurchaseIncomingInvoiceCommandsV1.Match, envelope, hash, refusal, async token =>
                {
                    if (await scope.Context.IncomingInvoiceAttempts.AsNoTracking().AnyAsync(row => row.RequestHash == hash, token))
                    {
                        observedSavedRows = true;
                        throw new InvalidOperationException("SENTETIK authority revoked after refusal SQL save");
                    }
                }));
            Assert.True(observedSavedRows);
            Assert.Empty(scope.Context.ChangeTracker.Entries());
            Assert.Null(scope.Context.Database.CurrentTransaction);
        }
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.IncomingInvoiceAttempts WHERE RequestHash=@hash", ("@hash", hash)));
        await using var retry = run.Store(CompanyA);
        var result = await ((IPurchasingIncomingInvoiceStoreV1)retry.Store).RecordIncomingInvoiceRefusalAsync(
            PurchaseIncomingInvoiceCommandsV1.Match, envelope, hash, refusal, _ => Task.CompletedTask);
        Assert.False(result.IsReplay);
        Assert.Equal(refusal.Code, result.Refusal!.Code);
        var replay = await ((IPurchasingIncomingInvoiceStoreV1)retry.Store).FindIncomingInvoiceAttemptAsync(
            PurchaseIncomingInvoiceCommandsV1.Match, envelope, hash);
        Assert.True(replay!.IsReplay);
        Assert.Equal(refusal.Code, replay.Refusal!.Code);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.IncomingInvoiceAttempts WHERE RequestHash=@hash", ("@hash", hash)));
    }
}

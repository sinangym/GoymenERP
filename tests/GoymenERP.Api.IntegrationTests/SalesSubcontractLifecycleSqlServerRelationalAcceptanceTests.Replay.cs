using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesSubcontractServiceInvoiceSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Subcontract_command_and_creation_replays_return_their_original_history_after_later_commands()
    {
        await using var run = await Run.StartAsync();
        var invoice = await CreateLifecycleAsync(run, Weighing, "immutable-create");
        await MoveLifecycleAsync(run, invoice.Id, "immutable-submit", SalesSubcontractInvoiceLifecycleCommandsV1.Submit,
            (value, now) => value.Submit(1, "SENTETIK-submitter", now), "SENTETIK-submitter");
        await MoveLifecycleAsync(run, invoice.Id, "immutable-approve", SalesSubcontractInvoiceLifecycleCommandsV1.Approve,
            (value, now) => value.Approve(2, "SENTETIK-checker", now), "SENTETIK-checker");
        var submitReplay = await MoveLifecycleAsync(run, invoice.Id, "immutable-submit", SalesSubcontractInvoiceLifecycleCommandsV1.Submit,
            (_, _) => throw new InvalidOperationException("Stored replay cannot run another mutation"), "SENTETIK-submitter");
        Assert.Equal(SalesDraftWriteOutcome.Replayed, submitReplay.Outcome);
        Assert.Equal(2, submitReplay.Invoice.Version);
        Assert.Equal(SalesSubcontractServiceInvoiceState.PendingApproval, submitReplay.Invoice.State);
        await using var fresh = run.SalesStore(CompanyA);
        var createReplay = await fresh.Store.FindSubcontractServiceInvoiceReplayAsync(
            run.Envelope("immutable-create", Preparer, SalesPermissionManifestV1.InvoicesWrite), Hash('c'));
        Assert.Equal(1, createReplay!.Invoice.Version);
        Assert.Equal(SalesSubcontractServiceInvoiceState.Draft, createReplay.Invoice.State);
        Assert.Equal(3, (await fresh.Store.FindSubcontractServiceInvoiceAsync(invoice.Id))!.Version);
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.SubcontractInvoiceLifecycles WHERE InvoiceId=@id", ("@id", invoice.Id)));
    }

    [SqlServerFact]
    public async Task Subcontract_finance_attempt_replays_use_sequence_when_multiple_results_share_one_invoice_version()
    {
        await using var run = await Run.StartAsync();
        var invoice = await CreateLifecycleAsync(run, Weighing, "finance-sequence-create");
        await MoveLifecycleAsync(run, invoice.Id, "finance-sequence-submit", SalesSubcontractInvoiceLifecycleCommandsV1.Submit,
            (value, now) => value.Submit(1, "SENTETIK-submitter", now), "SENTETIK-submitter");
        await MoveLifecycleAsync(run, invoice.Id, "finance-sequence-approve", SalesSubcontractInvoiceLifecycleCommandsV1.Approve,
            (value, now) => value.Approve(2, "SENTETIK-checker", now), "SENTETIK-checker");
        var official = LifecycleOfficial(invoice, Guid.NewGuid().ToString("D"));
        await MoveLifecycleAsync(run, invoice.Id, "finance-sequence-match", SalesSubcontractInvoiceLifecycleCommandsV1.Match,
            (value, now) => value.RecordMatch(3, "SENTETIK-matcher", official, Currency, null, 0.05m, false, null, now), "SENTETIK-matcher");
        await MoveLifecycleAsync(run, invoice.Id, "finance-sequence-finalize", SalesSubcontractInvoiceLifecycleCommandsV1.Finalize,
            (value, now) => value.Finalize(4, "SENTETIK-finalizer", Guid.NewGuid, now), "SENTETIK-finalizer");
        await MoveLifecycleAsync(run, invoice.Id, "finance-sequence-first", SalesSubcontractInvoiceLifecycleCommandsV1.ResendFinance,
            (value, now) => value.RecordFinanceAttempt(false, "synthetic-first", now), "SENTETIK-finalizer");
        await MoveLifecycleAsync(run, invoice.Id, "finance-sequence-second", SalesSubcontractInvoiceLifecycleCommandsV1.ResendFinance,
            (value, now) => value.RecordFinanceAttempt(false, "synthetic-second", now), "SENTETIK-finalizer");
        var replay = await MoveLifecycleAsync(run, invoice.Id, "finance-sequence-first", SalesSubcontractInvoiceLifecycleCommandsV1.ResendFinance,
            (_, _) => throw new InvalidOperationException("Replay must not call Finance again"), "SENTETIK-finalizer");
        Assert.Equal(5, replay.Invoice.Version);
        Assert.Equal(1, replay.Invoice.Finalization!.AttemptCount);
        Assert.Equal("synthetic-first", replay.Invoice.Finalization.LastOutcome);
        await using var fresh = run.SalesStore(CompanyA);
        var latest = await fresh.Store.FindSubcontractServiceInvoiceAsync(invoice.Id);
        Assert.Equal(5, latest!.Version); Assert.Equal(2, latest.Finalization!.AttemptCount);
    }

    [SqlServerFact]
    public async Task Subcontract_replay_sequence_Down_passes_before_command_outcomes_and_refuses_lost_response_history_with_51937()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations.AddSubcontractInvoiceReplaySequence().DownOperations[0]).Sql;
        await run.ScalarAsync<int>(guard + "; SELECT 1");
        var invoice = await CreateLifecycleAsync(run, Weighing, "sequence-down-create");
        await run.ScalarAsync<int>(guard + "; SELECT 1");
        await MoveLifecycleAsync(run, invoice.Id, "sequence-down-submit", SalesSubcontractInvoiceLifecycleCommandsV1.Submit,
            (value, now) => value.Submit(1, "SENTETIK-submitter", now), "SENTETIK-submitter");
        Assert.Equal(51937, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + "; SELECT 1"))).Number);
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.SubcontractInvoiceLifecycles WHERE InvoiceId=@id", ("@id", invoice.Id)));
    }
}

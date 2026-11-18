using System.Text.Json;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesSubcontractServiceInvoiceSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Subcontract_invoice_selection_pages_real_order_headers_and_rejects_foreign_company_or_order_cursor()
    {
        await using var run = await Run.StartAsync();
        await run.StoreAsync("list-first", Weighing);
        await run.StoreAsync("list-second", Guid.NewGuid());
        await using var own = run.SalesStore(CompanyA);
        var first = await own.Store.ListSubcontractInvoicesAsync(Order, 1, null);
        Assert.Single(first.Items);
        Assert.NotNull(first.NextCursor);
        var second = await own.Store.ListSubcontractInvoicesAsync(Order, 1, first.NextCursor);
        Assert.Single(second.Items);
        Assert.NotEqual(first.Items[0].Id, second.Items[0].Id);
        Assert.Null(second.NextCursor);
        var wrongOrder = await Assert.ThrowsAsync<SalesDraftStoreException>(() => own.Store.ListSubcontractInvoicesAsync(Guid.NewGuid(), 1, first.NextCursor));
        Assert.Equal(SalesDraftStoreCodes.InvalidCursor, wrongOrder.Code);
        await using var other = run.SalesStore(CompanyB);
        Assert.Empty((await other.Store.ListSubcontractInvoicesAsync(Order, 1, null)).Items);
        var wrongCompany = await Assert.ThrowsAsync<SalesDraftStoreException>(() => other.Store.ListSubcontractInvoicesAsync(Order, 1, first.NextCursor));
        Assert.Equal(SalesDraftStoreCodes.InvalidCursor, wrongCompany.Code);
    }

    [SqlServerFact]
    public async Task Subcontract_lifecycle_Down_reverses_empty_storage_but_preserves_evidence_with_51994()
    {
        await using var run = await Run.StartAsync();
        await using (var context = run.SalesContext(CompanyA))
        {
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            var index = Array.FindIndex(applied, id => id.EndsWith("_AddSubcontractInvoiceLifecycle", StringComparison.Ordinal));
            Assert.True(index > 0);
            await context.GetService<IMigrator>().MigrateAsync(applied[index - 1]);
            Assert.DoesNotContain(applied[index], await context.Database.GetAppliedMigrationsAsync());
            await context.Database.MigrateAsync();
            Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
        }
        var invoice = await CreateLifecycleAsync(run, Weighing, "guard-create");
        await using (var context = run.SalesContext(CompanyA))
        {
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            var index = Array.FindIndex(applied, id => id.EndsWith("_AddSubcontractInvoiceLifecycle", StringComparison.Ordinal));
            var refusal = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(applied[index - 1]));
            Assert.Equal(51994, refusal.Number);
            // Later empty migrations have already stepped down before this guarded migration refuses.
            Assert.Equal(applied[..(index + 1)], await context.Database.GetAppliedMigrationsAsync());
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.SubcontractInvoiceLifecycles WHERE InvoiceId=@id", ("@id", invoice.Id)));
            await context.Database.MigrateAsync();
            Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
        }
    }

    [SqlServerFact]
    public async Task Subcontract_lifecycle_official_claim_finalization_and_outbox_are_atomic_replayable_and_company_scoped()
    {
        await using var run = await Run.StartAsync();
        var invoice = await CreateLifecycleAsync(run, Weighing, "lifecycle-create");
        await MoveLifecycleAsync(run, invoice.Id, "submit", SalesSubcontractInvoiceLifecycleCommandsV1.Submit,
            (target, now) => target.Submit(1, "SENTETIK-submitter", now), "SENTETIK-submitter");
        await MoveLifecycleAsync(run, invoice.Id, "approve", SalesSubcontractInvoiceLifecycleCommandsV1.Approve,
            (target, now) => target.Approve(2, "SENTETIK-checker", now), "SENTETIK-checker");
        var ettn = Guid.NewGuid().ToString("D");
        var official = LifecycleOfficial(invoice, ettn);
        await MoveLifecycleAsync(run, invoice.Id, "match", SalesSubcontractInvoiceLifecycleCommandsV1.Match,
            (target, now) => target.RecordMatch(3, "SENTETIK-matcher", official, Currency, null, 0.05m, false, null, now), "SENTETIK-matcher");
        var finalized = await MoveLifecycleAsync(run, invoice.Id, "finalize", SalesSubcontractInvoiceLifecycleCommandsV1.Finalize,
            (target, now) => target.Finalize(4, "SENTETIK-finalizer", Guid.NewGuid, now), "SENTETIK-finalizer");
        Assert.Equal(SalesInvoiceFinanceStatus.Waiting, finalized.Invoice.Finalization!.FinanceStatus);
        Assert.Equal((5, 4, 1, 1), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.SubcontractInvoiceLifecycles WHERE InvoiceId=@id", ("@id", invoice.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.SubcontractInvoiceCommandAttempts WHERE InvoiceId=@id", ("@id", invoice.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.OfficialInvoiceClaims WHERE InvoiceId=@id", ("@id", invoice.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.OutboxMessages WHERE AggregateId=@id", ("@id", invoice.Id))));
        var payload = await run.ScalarAsync<string>("SELECT PayloadJson FROM sales.OutboxMessages WHERE AggregateId=@id", ("@id", invoice.Id));
        using var json = JsonDocument.Parse(payload);
        Assert.Equal("Service", json.RootElement.GetProperty("lines")[0].GetProperty("itemType").GetString());
        Assert.Equal(invoice.Totals.Payable.Amount, json.RootElement.GetProperty("payable").GetDecimal());
        Assert.Equal(Day.AddDays(2).ToString("yyyy-MM-dd"), json.RootElement.GetProperty("invoiceDate").GetString());
        var replay = await MoveLifecycleAsync(run, invoice.Id, "finalize", SalesSubcontractInvoiceLifecycleCommandsV1.Finalize,
            (_, _) => throw new InvalidOperationException("Replay must not execute the mutation"), "SENTETIK-finalizer");
        Assert.Equal(SalesDraftWriteOutcome.Replayed, replay.Outcome);
        Assert.Equal(finalized.Invoice.Finalization.AdmissionId, replay.Invoice.Finalization!.AdmissionId);
        await using (var other = run.SalesStore(CompanyB))
        {
            Assert.Null(await other.Store.FindSubcontractServiceInvoiceAsync(invoice.Id));
            Assert.Null(await other.Store.FindOfficialClaimAsync(official.OfficialNo, ettn));
        }
        // The same common claim is consulted by both the ordinary and subcontract invoice stores.
        await using (var own = run.SalesStore(CompanyA))
            Assert.Equal(invoice.Id, await own.Store.FindMatchedInvoiceIdAsync(official.OfficialNo, ettn));
        Assert.Equal(51997, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE sales.SubcontractInvoiceLifecycles SET FactsJson=N'{}' WHERE InvoiceId=@id; SELECT 1", ("@id", invoice.Id)))).Number);
        Assert.Equal(51997, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "DELETE FROM sales.OfficialInvoiceClaims WHERE InvoiceId=@id; SELECT 1", ("@id", invoice.Id)))).Number);
    }

    [SqlServerFact]
    public async Task Subcontract_finalization_failure_rolls_back_state_audit_attempt_lifecycle_and_outbox_together()
    {
        await using var run = await Run.StartAsync();
        var invoice = await CreateLifecycleAsync(run, Weighing, "rollback-create");
        await MoveLifecycleAsync(run, invoice.Id, "submit", SalesSubcontractInvoiceLifecycleCommandsV1.Submit,
            (target, now) => target.Submit(1, "SENTETIK-submitter", now), "SENTETIK-submitter");
        await MoveLifecycleAsync(run, invoice.Id, "approve", SalesSubcontractInvoiceLifecycleCommandsV1.Approve,
            (target, now) => target.Approve(2, "SENTETIK-checker", now), "SENTETIK-checker");
        var official = LifecycleOfficial(invoice, Guid.NewGuid().ToString("D"));
        await MoveLifecycleAsync(run, invoice.Id, "match", SalesSubcontractInvoiceLifecycleCommandsV1.Match,
            (target, now) => target.RecordMatch(3, "SENTETIK-matcher", official, Currency, null, 0.05m, false, null, now), "SENTETIK-matcher");
        await run.ScalarAsync<int>("ALTER TABLE sales.OutboxMessages ADD CONSTRAINT CK_SyntheticRejectSubcontract CHECK (AggregateType <> N'Sales.SubcontractServiceInvoice'); SELECT 1");
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => MoveLifecycleAsync(run, invoice.Id, "finalize", SalesSubcontractInvoiceLifecycleCommandsV1.Finalize,
                (target, now) => target.Finalize(4, "SENTETIK-finalizer", Guid.NewGuid, now), "SENTETIK-finalizer"));
            Assert.Equal((3, 4L, 4, 0), (
                await run.ScalarAsync<int>("SELECT State FROM sales.SubcontractServiceInvoices WHERE Id=@id", ("@id", invoice.Id)),
                await run.ScalarAsync<long>("SELECT Version FROM sales.SubcontractServiceInvoices WHERE Id=@id", ("@id", invoice.Id)),
                await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.SubcontractInvoiceLifecycles WHERE InvoiceId=@id", ("@id", invoice.Id)),
                await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.OutboxMessages WHERE AggregateId=@id", ("@id", invoice.Id))));
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.SubcontractInvoiceCommandAttempts WHERE InvoiceId=@id AND CommandName=@command",
                ("@id", invoice.Id), ("@command", SalesSubcontractInvoiceLifecycleCommandsV1.Finalize)));
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND Operation=N'Finalize'",
                ("@id", invoice.Id.ToString("D"))));
        }
        finally { await run.ScalarAsync<int>("ALTER TABLE sales.OutboxMessages DROP CONSTRAINT CK_SyntheticRejectSubcontract; SELECT 1"); }
        var repaired = await MoveLifecycleAsync(run, invoice.Id, "finalize", SalesSubcontractInvoiceLifecycleCommandsV1.Finalize,
            (target, now) => target.Finalize(4, "SENTETIK-finalizer", Guid.NewGuid, now), "SENTETIK-finalizer");
        Assert.Equal(SalesSubcontractServiceInvoiceState.Finalized, repaired.Invoice.State);
    }

    private static async Task<SalesSubcontractServiceInvoice> CreateLifecycleAsync(Run run, Guid weighing, string key)
    {
        await using var scope = run.SalesStore(CompanyA);
        var result = await scope.Store.StoreSubcontractServiceInvoiceAsync(run.Envelope(key, Preparer, SalesPermissionManifestV1.InvoicesWrite), Hash('c'), number =>
        {
            var invoice = Invoice(number, weighing, TimeProvider.System.GetUtcNow());
            var terms = SalesDueTerms.Create([new(1, SalesDueBasis.OrderDate, 30, "100")]);
            invoice.SetDuePlan(terms, SalesDueDateCalculator.Compute(terms, Day, null).DueDates);
            return invoice;
        });
        return result.Invoice;
    }

    private static SalesOfficialInvoiceFacts LifecycleOfficial(SalesSubcontractServiceInvoice invoice, string ettn) =>
        new("SENTETIK-SFH-" + invoice.Id.ToString("N"), ettn, invoice.Header.PartyId, Currency,
            invoice.Totals.Payable.Amount, invoice.Totals.Payable.Amount, invoice.Totals.Vat.Amount, invoice.Totals.Withholding.Amount, Day.AddDays(2),
            new(CompanyA, Guid.NewGuid(), "approved-import", "1", TimeProvider.System.GetUtcNow(), null, null, null, Guid.NewGuid(), new string('a', 64), false));

    private static async Task<SalesSubcontractServiceInvoiceStoreResult> MoveLifecycleAsync(Run run, Guid id, string key, string command,
        Action<SalesSubcontractServiceInvoice, DateTimeOffset> mutate, string actor)
    {
        await using var scope = run.SalesStore(CompanyA);
        return (await scope.Store.UpdateLifecycleAsync(run.Envelope(key, actor, SalesPermissionManifestV1.InvoicesWrite), Hash('a'), command, id,
            mutate, new(command, command == SalesSubcontractInvoiceLifecycleCommandsV1.Finalize ? "Finalize" : "Lifecycle", "SENTETIK lifecycle")))!;
    }
}

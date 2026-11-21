using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application.Inbox;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Incoming_invoice_refuses_a_borrowed_transaction_before_any_write_and_retries_after_the_caller_commits()
    {
        await using var run = await Run.StartAsync();
        var invoice = IncomingInvoice(run);
        await using (var scope = run.Store(CompanyA))
        {
            var store = (IPurchasingIncomingInvoiceStoreV1)scope.Store;
            await using var transaction = await scope.Context.Database.BeginTransactionAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.RecordIncomingInvoiceAsync(invoice,
                run.Envelope("borrowed"), Hash("borrowed"), _ => Task.CompletedTask));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.RecordIncomingInvoiceRefusalAsync(
                PurchaseIncomingInvoiceCommandsV1.Match, run.Envelope("borrowed-refusal"), Hash("borrowed-refusal"),
                new(409, PurchaseIncomingInvoice.SourceMismatch, []), _ => Task.CompletedTask));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.MutateIncomingInvoiceAsync(invoice.Id,
                PurchaseIncomingInvoiceCommandsV1.Match, run.Envelope("borrowed-mutate"), Hash("borrowed-mutate"), [],
                (_, _) => throw new InvalidOperationException("Must refuse before mutation"), _ => Task.CompletedTask));
            Assert.Empty(scope.Context.ChangeTracker.Entries());
            await transaction.CommitAsync();
        }
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.IncomingInvoiceAttempts"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.IncomingInvoices"));
        await using var retry = run.Store(CompanyA);
        Assert.False((await ((IPurchasingIncomingInvoiceStoreV1)retry.Store).RecordIncomingInvoiceAsync(invoice,
            run.Envelope("borrowed"), Hash("borrowed"), _ => Task.CompletedTask)).IsReplay);
    }

    [SqlServerFact]
    public async Task Incoming_invoice_match_does_not_round_receipt_quantity_into_a_larger_stored_share()
    {
        await using var run = await Run.StartAsync();
        var invoice = IncomingInvoice(run); var receipt = await IncomingInvoiceReceiptAsync(run, invoice);
        await using var scope = run.Store(CompanyA);
        var store = (IPurchasingIncomingInvoiceStoreV1)scope.Store;
        await store.RecordIncomingInvoiceAsync(invoice, run.Envelope("exact-quantity"), Hash("exact-quantity"), _ => Task.CompletedTask);
        await Assert.ThrowsAsync<ArgumentException>(() => store.MutateIncomingInvoiceAsync(invoice.Id,
            PurchaseIncomingInvoiceCommandsV1.Match, run.Envelope("excess-scale"), Hash("excess-scale"), [receipt.OrderId],
            (value, _) => { value.Match(1, [receipt with { Quantity = 3.9999999m }], new(Guid.NewGuid(), 1, 0m, 0m), run.Actor); return Task.CompletedTask; },
            _ => Task.CompletedTask));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.InvoiceReceiptBindings"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.IncomingInvoiceRevisions"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.IncomingInvoiceAttempts WHERE CommandName='purchasing.incoming-invoice.match.v1'"));
    }

    [SqlServerFact]
    public async Task Incoming_invoice_identity_and_attempt_are_company_scoped_and_replay_the_original_snapshot()
    {
        await using var run = await Run.StartAsync();
        var invoice = IncomingInvoice(run); var envelope = run.Envelope("invoice-import");
        await using (var scope = run.Store(CompanyA))
        {
            var store = (IPurchasingIncomingInvoiceStoreV1)scope.Store;
            var result = await store.RecordIncomingInvoiceAsync(invoice, envelope, Hash("invoice-import"), _ => Task.CompletedTask);
            Assert.False(result.IsReplay);
            var replay = await store.RecordIncomingInvoiceAsync(invoice, envelope with { CorrelationId = "SENTETIK-retry" },
                Hash("invoice-import"), _ => Task.CompletedTask);
            Assert.True(replay.IsReplay);
            Assert.Equal(envelope.CorrelationId, replay.OriginalCorrelationId);
            Assert.Equal(invoice.Source, replay.Invoice!.Source);
            await Assert.ThrowsAsync<PurchasingStoreException>(() => store.FindIncomingInvoiceAttemptAsync(
                PurchaseIncomingInvoiceCommandsV1.Record, envelope with { ActorId = "SENTETIK-other" }, Hash("invoice-import")));
        }
        await using (var scope = run.Store(CompanyB))
            Assert.Null(await ((IPurchasingIncomingInvoiceStoreV1)scope.Store).FindIncomingInvoiceAsync(invoice.Id));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.IncomingInvoices WHERE InvoiceId=@id", ("@id", invoice.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.IncomingInvoiceRevisions WHERE InvoiceId=@id", ("@id", invoice.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType='Purchasing.IncomingInvoice' AND EntityId=@id", ("@id", invoice.Id.ToString("D"))));
    }

    [SqlServerFact]
    public async Task Incoming_invoice_matches_real_receipts_and_emits_one_accounting_event_before_a_separate_finance_acknowledgement()
    {
        await using var run = await Run.StartAsync();
        var invoice = IncomingInvoice(run); var receipt = await IncomingInvoiceReceiptAsync(run, invoice);
        await using (var scope = run.Store(CompanyA))
        {
            var store = (IPurchasingIncomingInvoiceStoreV1)scope.Store;
            await store.RecordIncomingInvoiceAsync(invoice, run.Envelope("import"), Hash("import"), _ => Task.CompletedTask);
            await store.MutateIncomingInvoiceAsync(invoice.Id, PurchaseIncomingInvoiceCommandsV1.Match, run.Envelope("match"), Hash("match"),
                [receipt.OrderId], (value, _) => { value.Match(1, [receipt], new(Guid.NewGuid(), 1, 0m, 0m), run.Actor); return Task.CompletedTask; },
                _ => Task.CompletedTask);
            var result = await store.MutateIncomingInvoiceAsync(invoice.Id, PurchaseIncomingInvoiceCommandsV1.Finalize,
                run.Envelope("finalize"), Hash("finalize"), [],
                (value, _) => { value.Finalize(2, Guid.NewGuid(), run.Actor, AcceptanceTime); return Task.CompletedTask; }, _ => Task.CompletedTask);
            Assert.Equal(PurchaseIncomingInvoiceStatus.FinancePending, result!.Invoice!.Status);
            var replay = await store.MutateIncomingInvoiceAsync(invoice.Id, PurchaseIncomingInvoiceCommandsV1.Finalize,
                run.Envelope("finalize"), Hash("finalize"), [], (_, _) => throw new InvalidOperationException("Replay must not mutate"), _ => Task.CompletedTask);
            Assert.True(replay!.IsReplay);
            var admission = result.Invoice.AdmissionId!.Value;
            await store.MutateIncomingInvoiceAsync(invoice.Id, PurchaseIncomingInvoiceCommandsV1.AcknowledgeFinance,
                run.Envelope("finance-ack"), Hash("finance-ack"), [],
                (value, _) => { value.AcknowledgeFinance(3, admission, AcceptanceTime.AddMinutes(1)); return Task.CompletedTask; }, _ => Task.CompletedTask);
            var loaded = (await store.FindIncomingInvoiceAsync(invoice.Id))!.Restore();
            Assert.Equal(4, loaded.Version);
            Assert.Equal(3, loaded.FinalizedVersion);
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseInvoiceOrderBindings WHERE InvoiceId=@id", ("@id", invoice.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.InvoiceReceiptBindings WHERE InvoiceId=@id", ("@id", invoice.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.OutboxMessages WHERE AggregateId=@id AND EventType='purchasing.invoice.finalized.v1'", ("@id", invoice.Id)));
        var json = await run.ScalarAsync<string>("SELECT PayloadJson FROM purchasing.OutboxMessages WHERE AggregateId=@id AND EventType='purchasing.invoice.finalized.v1'", ("@id", invoice.Id));
        // The real producer serializes the Accounting public contract, not a second ad-hoc payable.
        var consumed = JsonSerializer.Deserialize<AccountingPurchaseInvoiceFinalizedEventV1>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(invoice.Source.OfficialNumber, consumed.OfficialInvoiceNo);
        Assert.Equal((invoice.Id, CompanyA, (decimal?)48m, (DateOnly?)invoice.Source.InvoiceDate, run.Actor),
            (consumed.InvoiceId, consumed.CompanyId, consumed.Payable, consumed.InvoiceDate, consumed.FinalizedBy));
        Assert.Equal("resale", Assert.IsType<AccountingPurchaseInvoiceFinalizedLineV1>(Assert.Single(consumed.Lines!)).LinePurpose);
    }

    [SqlServerFact]
    public async Task Incoming_invoice_last_fence_failure_leaves_no_match_binding_revision_audit_or_attempt_and_same_key_can_retry()
    {
        await using var run = await Run.StartAsync();
        var invoice = IncomingInvoice(run); var receipt = await IncomingInvoiceReceiptAsync(run, invoice);
        await using (var scope = run.Store(CompanyA))
            await ((IPurchasingIncomingInvoiceStoreV1)scope.Store).RecordIncomingInvoiceAsync(invoice, run.Envelope("import"), Hash("import"), _ => Task.CompletedTask);
        await using (var scope = run.Store(CompanyA))
        {
            var calls = 0;
            await Assert.ThrowsAsync<InvalidOperationException>(() => ((IPurchasingIncomingInvoiceStoreV1)scope.Store)
                .MutateIncomingInvoiceAsync(invoice.Id, PurchaseIncomingInvoiceCommandsV1.Match, run.Envelope("match"), Hash("match"),
                    [receipt.OrderId], (value, _) => { value.Match(1, [receipt], new(Guid.NewGuid(), 1, 0m, 0m), run.Actor); return Task.CompletedTask; },
                    _ => ++calls == 3 ? throw new InvalidOperationException("SENTETIK late authority loss") : Task.CompletedTask));
        }
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.InvoiceReceiptBindings WHERE InvoiceId=@id", ("@id", invoice.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.IncomingInvoiceRevisions WHERE InvoiceId=@id", ("@id", invoice.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.IncomingInvoiceAttempts WHERE CommandName='purchasing.incoming-invoice.match.v1'"));
        await using (var scope = run.Store(CompanyA))
        {
            var retry = await ((IPurchasingIncomingInvoiceStoreV1)scope.Store).MutateIncomingInvoiceAsync(invoice.Id,
                PurchaseIncomingInvoiceCommandsV1.Match, run.Envelope("match"), Hash("match"), [receipt.OrderId],
                (value, _) => { value.Match(1, [receipt], new(Guid.NewGuid(), 1, 0m, 0m), run.Actor); return Task.CompletedTask; }, _ => Task.CompletedTask);
            Assert.False(retry!.IsReplay);
            Assert.Equal(2, retry.Invoice!.Version);
        }
    }

    [SqlServerFact]
    public async Task Incoming_invoice_migration_Down_refuses_while_official_source_or_terminal_history_exists()
    {
        var guard = Assert.IsType<SqlOperation>(new AddPurchaseIncomingInvoices().DownOperations[0]).Sql;
        await using var run = await Run.StartAsync();
        await run.ExecuteAsync(guard);
        await using (var scope = run.Store(CompanyA))
            await ((IPurchasingIncomingInvoiceStoreV1)scope.Store).RecordIncomingInvoiceRefusalAsync(PurchaseIncomingInvoiceCommandsV1.Match,
                run.Envelope("refused-match"), Hash("refused-match"), new(409, PurchaseIncomingInvoice.SourceMismatch, []), _ => Task.CompletedTask);
        var exception = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51980, exception.Number);
    }

    [SqlServerFact]
    public async Task Incoming_invoice_outbox_Down_passes_with_only_old_event_types_and_refuses_while_a_finalized_event_exists()
    {
        await using var run = await Run.StartAsync();
        // Prove the complete generated Down/Up on the empty latest module before protecting real event history.
        await using (var scope = run.Factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            var context = scope.ServiceProvider.GetRequiredService<PurchasingDbContext>();
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            // Later migrations may follow this one; the empty module rolls back through them to the one before it.
            var target = Array.FindIndex(applied, migration =>
                migration.EndsWith("_AllowPurchaseIncomingInvoiceFinalizedOutboxEvents", StringComparison.Ordinal));
            Assert.True(target > 0);
            await context.GetService<IMigrator>().MigrateAsync(applied[target - 1]);
            Assert.Equal(applied[..target], (await context.Database.GetAppliedMigrationsAsync()).ToArray());
            await context.Database.MigrateAsync();
            Assert.Equal(applied, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
        }

        var guard = Assert.IsType<SqlOperation>(new AllowPurchaseIncomingInvoiceFinalizedOutboxEvents().DownOperations[0]).Sql;
        const string historySql = "SELECT STRING_AGG(CAST(MigrationId AS nvarchar(max)), N'|') WITHIN GROUP (ORDER BY MigrationId) FROM purchasing.__EFMigrationsHistory";
        var history = await run.ScalarAsync<string>(historySql);
        await run.ExecuteAsync(guard);

        // These two event types were already legal before this forward correction.
        await InsertAsync(PurchasingOrderLineClosedEventV1.EventType);
        await InsertAsync(PurchasingOrderLineRevivedEventV1.EventType);
        await run.ExecuteAsync(guard);
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.OutboxMessages"));

        // The check remains closed: widening for the already contracted invoice event permits no arbitrary name.
        var unknown = await Assert.ThrowsAsync<SqlException>(() => InsertAsync("purchasing.unknown.v1"));
        Assert.Equal(547, unknown.Number);
        await InsertAsync(PurchaseInvoiceFinalizedEventV1.EventType);
        var protectedRows = await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.OutboxMessages");
        Assert.Equal(3, protectedRows);
        var refusal = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51815, refusal.Number);
        Assert.Equal(protectedRows, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.OutboxMessages"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.OutboxMessages WHERE EventType = @type",
            ("@type", PurchaseInvoiceFinalizedEventV1.EventType)));
        Assert.Equal(history, await run.ScalarAsync<string>(historySql));

        // With protected rows, execute only the first guard; the empty full Down/Up was proven above.
        Task InsertAsync(string eventType) => run.ExecuteAsync(
            "INSERT INTO purchasing.OutboxMessages "
            + "(Id, CompanyId, EventType, SchemaVersion, SourceOperation, AggregateType, AggregateId, AggregateVersion, Sequence, "
            + "PayloadJson, OccurredAtUtc, CorrelationId, RecordedAtUtc) "
            + "VALUES (@id, @company, @type, 1, 'SENTETIK-outbox-guard', 'SENTETIK.Outbox', @aggregate, 1, 1, '{}', @now, 'SENTETIK-outbox-guard', @now)",
            ("@id", Guid.NewGuid()), ("@company", CompanyA), ("@type", eventType), ("@aggregate", Guid.NewGuid()),
            ("@now", DateTimeOffset.UtcNow));
    }

    private static PurchaseIncomingInvoice IncomingInvoice(Run run) => PurchaseIncomingInvoice.Record(Guid.NewGuid(), CompanyA,
        AcceptanceSupplier, AcceptanceAccount, InvoiceCostRateCurrency, InvoiceCostRateCurrency, 2, 2, null,
        new(Guid.NewGuid(), 1, new string('a', 64), "SENTETIK-" + Guid.NewGuid().ToString("N"), Guid.NewGuid(), Day), new(40m, 8m, 0m), null,
        [new(Guid.NewGuid(), 1, AcceptanceProduct, AcceptanceUnit, 4m, "material", "resale", 20m, new(40m, 8m, 0m), null)],
        [new(Guid.NewGuid(), Guid.NewGuid(), 1, Day.AddDays(30), 48m, null)], run.Actor, AcceptanceTime);

    private static async Task<PurchaseInvoiceReceiptMatch> IncomingInvoiceReceiptAsync(Run run, PurchaseIncomingInvoice invoice)
    {
        var (order, line, version) = await InvoiceCostRateOrderAsync(run,
            PurchaseOrderMoney.Of(InvoiceCostRateCurrency, "TRY", InvoiceCostRateProfile, 1, 2), Day);
        var receipt = Guid.NewGuid();
        await StageAndCommitAsync(run, CompanyA, AcceptanceRequest(new AcceptanceSeed(order, line, AcceptanceUnit, version), receipt, "4"),
            Envelope("SENTETIK-receipt-" + receipt.ToString("N"), "SENTETIK-receipt"));
        var current = await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", order));
        return new(CompanyA, invoice.Lines[0].Id, receipt, order, line, current, AcceptanceSupplier, AcceptanceAccount,
            InvoiceCostRateCurrency, AcceptanceUnit, 4m, 40m, null, DateOnly.FromDateTime(AcceptanceTime.UtcDateTime));
    }
}

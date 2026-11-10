using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// S5 API-32 (Fatura çekirdeği 1) against a REAL SQL Server: the invoice draft's one save (header, whole-share lines with their
/// frozen invoice-currency and TL amounts, attempt, number counter, audit event), the ADR-011 replay and same-key mismatch, the
/// filtered unique index that keeps a shipment on one active invoice line (AT-055, SAT-D94) under a second key, company isolation
/// and the guarded rollback (51221). The invoice is built by the domain from SYNTHETIC shares: it proves the store and the schema,
/// not any owner's rule. Every identity is SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesInvoiceSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 11, 7);
    private static readonly Guid Usd = Guid.Parse("6b1d2c3b-4a59-4687-9a0b-1c2d3e4f5c01");
    private static readonly Guid Try = Guid.Parse("6b1d2c3b-4a59-4687-9a0b-1c2d3e4f5c02");
    private static readonly Guid Kg = Guid.Parse("6b1d2c3b-4a59-4687-9a0b-1c2d3e4f5c03");
    private static readonly Guid Product = Guid.Parse("6b1d2c3b-4a59-4687-9a0b-1c2d3e4f5c04");

    [SqlServerFact]
    public async Task An_invoice_draft_is_stored_in_one_save_replayed_by_its_key_and_its_shipment_stays_on_one_active_line_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var shipment = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();

        SalesInvoiceStoreResult stored;
        await using (var scope = run.Store(CompanyA))
        {
            stored = await scope.Invoices.StoreInvoiceAsync(run.Envelope("i-create"), Hash('a'),
                number => Invoice(invoiceId, number, run.Actor, shipment, shipment2: Guid.NewGuid()));
        }

        Assert.Equal(SalesDraftWriteOutcome.Recorded, stored.Outcome);
        Assert.Matches("^SF-[0-9]{4}-000001$", stored.Invoice.DocumentNo);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.Invoices WHERE Id = @id AND State = 1 AND Version = 1 AND ExchangeRate = 34.5678 AND FunctionalScale = 2",
            ("@id", invoiceId)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.InvoiceLines WHERE InvoiceId = @id AND ReleasedAtUtc IS NULL AND FunctionalNetAmount IS NOT NULL",
            ("@id", invoiceId)));
        // EK42: the group difference is on the last line of the 20 % group (346.03, not 346.02).
        Assert.Equal(346.03m, await run.ScalarAsync<decimal>(
            "SELECT FunctionalNetAmount FROM sales.InvoiceLines WHERE InvoiceId = @id AND [LineNo] = 2", ("@id", invoiceId)));
        Assert.Equal("Net,Vat", await run.ScalarAsync<string>(
            "SELECT FunctionalAdjusted FROM sales.InvoiceLines WHERE InvoiceId = @id AND [LineNo] = 2", ("@id", invoiceId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.InvoiceAttempts WHERE CommandName = N'sales.invoice.create.v1' AND InvoiceId = @id AND Outcome = 1",
            ("@id", invoiceId)));
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT [Next] FROM sales.DocumentCounters WHERE CompanyId = @c AND Series = 'SF'",
            ("@c", CompanyA)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Sales.Invoice' AND EventType = N'sales.invoice.created.v1' "
            + "AND EntityId = @id AND EntityVersionAfter = 1 AND PermissionCode = N'sales.invoices.write'", ("@id", invoiceId.ToString("D"))));

        await using (var scope = run.Store(CompanyA))
        {
            // ADR-011 §4: the same key, content and actor answers the stored invoice, rebuilt from its rows with the frozen amounts.
            var replay = await scope.Invoices.FindInvoiceReplayAsync(run.Envelope("i-create", "SENTETIK-again"), Hash('a'));
            Assert.Equal((SalesDraftWriteOutcome.Replayed, invoiceId, run.Correlation), (replay!.Outcome, replay.Invoice.Id, replay.OriginalCorrelationId));
            Assert.Equal(stored.Invoice.Totals, replay.Invoice.Totals);
            Assert.Equal(stored.Invoice.FunctionalTotals, replay.Invoice.FunctionalTotals);
            Assert.Equal(stored.Invoice.Lines.Select(line => (line.Functional!.Amounts, string.Join(',', line.Functional.Adjusted))),
                replay.Invoice.Lines.Select(line => (line.Functional!.Amounts, string.Join(',', line.Functional.Adjusted))));
            var mismatch = await Assert.ThrowsAsync<SalesDraftStoreException>(() =>
                scope.Invoices.FindInvoiceReplayAsync(run.Envelope("i-create"), Hash('b')));
            Assert.Equal(SalesDraftStoreCodes.SameKeyPayloadMismatch, mismatch.Code);
            Assert.Equal([shipment], await scope.Invoices.FindInvoicedShipmentsAsync([shipment, Guid.NewGuid()]));
            Assert.Single((await scope.Invoices.ListInvoicesAsync(10, null)).Items);
            // API-09: the order's "Faturalar" tab reads by the order id of a line; an unknown order has none.
            var orderId = stored.Invoice.Lines[0].Share.SalesOrderId;
            var ofOrder = await scope.Invoices.ListInvoicesOfOrderAsync(orderId, 100);
            Assert.Equal((invoiceId, false), (Assert.Single(ofOrder.Items).Id, ofOrder.HasMore));
            Assert.Empty((await scope.Invoices.ListInvoicesOfOrderAsync(Guid.NewGuid(), 100)).Items);
        }

        // AT-055 / SAT-D94: another key taking the same shipment loses on the active-share index; nothing of it is stored.
        await using (var scope = run.Store(CompanyA))
        {
            var race = await Assert.ThrowsAsync<SalesDraftStoreException>(() => scope.Invoices.StoreInvoiceAsync(run.Envelope("i-second"),
                Hash('c'), number => Invoice(Guid.NewGuid(), number, run.Actor, shipment, Guid.NewGuid())));
            Assert.Equal(SalesDraftStoreCodes.ConcurrentChange, race.Code);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Invoices"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.InvoiceAttempts"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Sales.Invoice'"));

        // Company isolation: another company sees neither the invoice nor its shipment claim nor its key.
        await using (var scope = run.Store(CompanyB))
        {
            Assert.Null(await scope.Invoices.FindInvoiceAsync(invoiceId));
            Assert.Empty((await scope.Invoices.ListInvoicesOfOrderAsync(stored.Invoice.Lines[0].Share.SalesOrderId, 100)).Items);
            Assert.Empty(await scope.Invoices.FindInvoicedShipmentsAsync([shipment]));
            Assert.Null(await scope.Invoices.FindInvoiceReplayAsync(run.Envelope("i-create"), Hash('a')));
        }

        // The schema refuses half a rate and a line that holds only part of its TL amounts.
        var halfRate = await Assert.ThrowsAsync<SqlException>(() =>
            run.ExecuteAsync("UPDATE sales.Invoices SET RateDay = NULL WHERE Id = @id", ("@id", invoiceId)));
        Assert.Contains("CK_Invoices_Rate", halfRate.Message, StringComparison.Ordinal);
        var halfLine = await Assert.ThrowsAsync<SqlException>(() =>
            run.ExecuteAsync("UPDATE sales.InvoiceLines SET FunctionalVatAmount = NULL WHERE InvoiceId = @id AND [LineNo] = 1", ("@id", invoiceId)));
        Assert.Contains("CK_InvoiceLines_Functional", halfLine.Message, StringComparison.Ordinal);
    }

    [SqlServerFact]
    public async Task The_invoice_migrations_Down_passes_with_no_invoice_and_refuses_while_an_invoice_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddSalesInvoices().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);

        var invoiceId = Guid.NewGuid();
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Invoices.StoreInvoiceAsync(run.Envelope("i-down"), Hash('d'),
                number => Invoice(invoiceId, number, run.Actor, Guid.NewGuid(), Guid.NewGuid()));
        }

        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51221, refused.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Invoices WHERE Id = @id", ("@id", invoiceId)));
    }

    [SqlServerFact]
    public async Task An_invoice_is_submitted_approved_by_another_actor_and_then_frozen_in_sql_with_its_audit_and_attempts()
    {
        await using var run = await Run.StartAsync();
        var approver = "SENTETIK-approver-" + Guid.NewGuid().ToString("N");
        var invoiceId = Guid.NewGuid();
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Invoices.StoreInvoiceAsync(run.Envelope("a-create"), Hash('a'),
                number => Invoice(invoiceId, number, run.Actor, Guid.NewGuid(), Guid.NewGuid()));
        }

        await using (var scope = run.Store(CompanyA))
        {
            var submitted = await scope.Invoices.DecideInvoiceAsync(run.Envelope("a-submit"), Hash('b'), SalesInvoiceCommands.SubmitV1, invoiceId,
                (invoice, now) => invoice.Submit(1, run.Actor, now), new SalesDraftStateAudit("sales.invoice.submitted.v1", "Submit", "SENTETIK"));
            Assert.Equal(SalesInvoiceState.PendingApproval, submitted!.Invoice.State);
        }

        // SAT-D88/D204 in SQL: the decider may not be the preparer, even by a raw write that skips the domain.
        var self = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE sales.Invoices SET State = 3, DecidedBy = CreatedBy, DecidedAtUtc = SYSUTCDATETIME() WHERE Id = @id", ("@id", invoiceId)));
        Assert.Contains("CK_Invoices_MakerChecker", self.Message, StringComparison.Ordinal);

        await using (var scope = run.Store(CompanyA))
        {
            var approved = await scope.Invoices.DecideInvoiceAsync(run.Envelope("a-approve", actor: approver,
                permission: SalesPermissionManifestV1.InvoicesApprove), Hash('c'), SalesInvoiceCommands.ApproveV1, invoiceId,
                (invoice, now) => invoice.Approve(2, approver, now), new SalesDraftStateAudit("sales.invoice.approved.v1", "Approve", "SENTETIK"));
            Assert.Equal((SalesInvoiceState.Approved, 3L), (approved!.Invoice.State, approved.Invoice.Version));
            var replay = await scope.Invoices.FindInvoiceStateReplayAsync(run.Envelope("a-approve", actor: approver,
                permission: SalesPermissionManifestV1.InvoicesApprove), Hash('c'), SalesInvoiceCommands.ApproveV1);
            Assert.Equal(invoiceId, replay!.Invoice!.Id);
            var other = await Assert.ThrowsAsync<SalesDraftStoreException>(() => scope.Invoices.FindInvoiceStateReplayAsync(
                run.Envelope("a-approve", actor: approver, permission: SalesPermissionManifestV1.InvoicesApprove), Hash('d'), SalesInvoiceCommands.ApproveV1));
            Assert.Equal(SalesDraftStoreCodes.SameKeyPayloadMismatch, other.Code);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.Invoices WHERE Id = @id AND State = 3 AND Version = 3 AND SubmittedBy = CreatedBy AND DecidedBy = @a",
            ("@id", invoiceId), ("@a", approver)));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.InvoiceAttempts WHERE InvoiceId = @id AND Outcome = 1", ("@id", invoiceId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Sales.Invoice' AND EventType = N'sales.invoice.approved.v1' AND EntityId = @id "
            + "AND EntityVersionAfter = 3 AND PermissionCode = N'sales.invoices.approve'", ("@id", invoiceId.ToString("D"))));

        // The approved invoice and its lines are immutable in SQL: delete, edit, state change and release all refuse.
        // A header delete meets the attempts' foreign key first (547); the trigger (51223) is the second wall behind it.
        var headerDelete = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync("DELETE FROM sales.Invoices WHERE Id = @id", ("@id", invoiceId)));
        Assert.Contains(headerDelete.Number, new[] { 547, 51223 });
        foreach (var (sql, number) in new[]
        {
            ("UPDATE sales.Invoices SET DocumentNo = N'SF-HACK' WHERE Id = @id", 51223),
            ("UPDATE sales.Invoices SET SubmittedAtUtc = DATEADD(DAY, 1, SubmittedAtUtc) WHERE Id = @id", 51223),
            ("UPDATE sales.InvoiceLines SET Quantity = Quantity + 1 WHERE InvoiceId = @id", 51224),
            ("DELETE FROM sales.InvoiceLines WHERE InvoiceId = @id", 51224),
            ("UPDATE sales.InvoiceLines SET ReleasedAtUtc = SYSUTCDATETIME() WHERE InvoiceId = @id", 51224),
        })
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(sql, ("@id", invoiceId)));
            Assert.Equal(number, refused.Number);
        }

        // The rollback guard refuses once an invoice left the draft state.
        var guard = Assert.IsType<SqlOperation>(new AddSalesInvoiceApproval().DownOperations[0]).Sql;
        var down = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51222, down.Number);
    }

    [SqlServerFact]
    public async Task A_cancelled_invoice_releases_its_shares_in_the_same_save_and_the_shipment_can_be_invoiced_again_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var shipment = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Invoices.StoreInvoiceAsync(run.Envelope("c-create"), Hash('a'),
                number => Invoice(invoiceId, number, run.Actor, shipment, Guid.NewGuid()));
        }

        // The Down guard passes while only drafts and create attempts exist (nothing to lose).
        var guard = Assert.IsType<SqlOperation>(new AddSalesInvoiceApproval().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard.Split("DROP TRIGGER", 2)[0]);

        await using (var scope = run.Store(CompanyA))
        {
            var cancelled = await scope.Invoices.DecideInvoiceAsync(run.Envelope("c-cancel"), Hash('b'), SalesInvoiceCommands.CancelV1, invoiceId,
                (invoice, now) => invoice.Cancel(1, run.Actor, "SENTETIK vazgecildi", now),
                new SalesDraftStateAudit("sales.invoice.cancelled.v1", "Cancel", "SENTETIK"));
            Assert.Equal(SalesInvoiceState.Cancelled, cancelled!.Invoice.State);
            Assert.Empty(await scope.Invoices.FindInvoicedShipmentsAsync([shipment]));
        }

        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.InvoiceLines WHERE InvoiceId = @id AND ReleasedAtUtc IS NOT NULL", ("@id", invoiceId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.Invoices WHERE Id = @id AND State = 5 AND CancelledBy = @a AND CancellationReason IS NOT NULL", ("@id", invoiceId), ("@a", run.Actor)));

        // AT-055: the released share is free; a new invoice takes it, and the cancelled invoice stays frozen.
        var second = Guid.NewGuid();
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Invoices.StoreInvoiceAsync(run.Envelope("c-second"), Hash('c'),
                number => Invoice(second, number, run.Actor, shipment, Guid.NewGuid()));
        }

        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.InvoiceLines WHERE InvoiceId = @id AND ReleasedAtUtc IS NULL", ("@id", second)));
        var unrelease = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE sales.InvoiceLines SET ReleasedAtUtc = NULL WHERE InvoiceId = @id AND [LineNo] = 2", ("@id", invoiceId)));
        Assert.Equal(51224, unrelease.Number);
        // A release of a line of a draft invoice (no cancel) is refused too: the stamp belongs to a decided invoice only.
        var early = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE sales.InvoiceLines SET ReleasedAtUtc = SYSUTCDATETIME() WHERE InvoiceId = @id", ("@id", second)));
        Assert.Equal(51224, early.Number);
    }

    [SqlServerFact]
    public async Task A_terminal_refusal_of_an_invoice_command_is_recorded_under_its_key_and_replayed_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var invoiceId = Guid.NewGuid();
        await using var scope = run.Store(CompanyA);
        await scope.Invoices.StoreInvoiceAsync(run.Envelope("r-create"), Hash('a'),
            number => Invoice(invoiceId, number, run.Actor, Guid.NewGuid(), Guid.NewGuid()));

        var refusal = new SalesDraftTerminalRefusal(409, SalesInvoiceProblemCodesV1.SelfApproval, [new("actor", "invoice_self_approval")]);
        await scope.Invoices.RecordInvoiceRefusalAsync(run.Envelope("r-self", permission: SalesPermissionManifestV1.InvoicesApprove), Hash('b'),
            SalesInvoiceCommands.ApproveV1, invoiceId, refusal);

        var replay = await scope.Invoices.FindInvoiceStateReplayAsync(run.Envelope("r-self", permission: SalesPermissionManifestV1.InvoicesApprove),
            Hash('b'), SalesInvoiceCommands.ApproveV1);
        Assert.Null(replay!.Invoice);
        Assert.Equal((409, SalesInvoiceProblemCodesV1.SelfApproval), (replay.Refusal!.Status, replay.Refusal.Code));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.InvoiceAttempts WHERE InvoiceId = @id AND CommandName = N'sales.invoice.approve.v1' AND Outcome = 2", ("@id", invoiceId)));
    }

    private const string OfficialEttn = "0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d";

    /// <summary>Stores an invoice and takes it to Approved by another actor (version 3), through the real store.</summary>
    private static async Task<SalesInvoice> ApprovedAsync(Run run, string prefix, string approver,
        Func<Guid, string, SalesInvoice>? build = null)
    {
        var invoiceId = Guid.NewGuid();
        await using var scope = run.Store(CompanyA);
        await scope.Invoices.StoreInvoiceAsync(run.Envelope(prefix + "-create"), Hash('a'),
            number => build is null ? Invoice(invoiceId, number, run.Actor, Guid.NewGuid(), Guid.NewGuid()) : build(invoiceId, number));
        await scope.Invoices.DecideInvoiceAsync(run.Envelope(prefix + "-submit"), Hash('b'), SalesInvoiceCommands.SubmitV1, invoiceId,
            (invoice, now) => invoice.Submit(1, run.Actor, now), new SalesDraftStateAudit("sales.invoice.submitted.v1", "Submit", "SENTETIK"));
        var approved = await scope.Invoices.DecideInvoiceAsync(run.Envelope(prefix + "-approve", actor: approver,
            permission: SalesPermissionManifestV1.InvoicesApprove), Hash('c'), SalesInvoiceCommands.ApproveV1, invoiceId,
            (invoice, now) => invoice.Approve(2, approver, now), new SalesDraftStateAudit("sales.invoice.approved.v1", "Approve", "SENTETIK"));
        return approved!.Invoice;
    }

    [SqlServerFact]
    public async Task An_official_match_is_one_save_with_attempt_and_audit_leaves_the_approved_invoice_untouched_and_replays_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var approver = "SENTETIK-approver-" + Guid.NewGuid().ToString("N");
        var matcher = "SENTETIK-matcher-" + Guid.NewGuid().ToString("N");
        var approved = await ApprovedAsync(run, "m1", approver);
        var official = new SalesOfficialInvoiceFacts("GIB2026000000001", OfficialEttn, approved.Header.PartyId, approved.Header.CurrencyId,
            approved.PayableFunctionalAmount + 0.05m, approved.PayableFunctionalAmount + 0.05m, approved.VatFunctionalAmount,
            approved.WithholdingFunctionalAmount);
        var envelope = run.Envelope("m1-match", actor: matcher, permission: SalesPermissionManifestV1.InvoicesMatch);

        await using (var scope = run.Store(CompanyA))
        {
            var matched = await scope.Invoices.MatchInvoiceAsync(envelope, Hash('e'), approved.Id,
                (invoice, now) => invoice.RecordMatch(3, matcher, official, 0.05m, false, null, now),
                new SalesDraftStateAudit("sales.invoice.matched.v1", "Match", "SENTETIK"));
            Assert.Equal((SalesDraftWriteOutcome.Recorded, SalesInvoiceState.Approved, 3L), (matched!.Outcome, matched.Invoice.State, matched.Invoice.Version));
            Assert.Equal(0.05m, matched.Invoice.Match!.Difference);
            Assert.Equal(approved.Id, await scope.Invoices.FindMatchedInvoiceIdAsync("GIB2026000000001", OfficialEttn));
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.Invoices WHERE Id = @id AND State = 3 AND Version = 3", ("@id", approved.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.InvoiceMatches WHERE InvoiceId = @id AND Difference = 0.05 AND Tolerance = 0.05 AND DifferenceApprovedBy IS NULL AND MatchedBy = @m",
            ("@id", approved.Id), ("@m", matcher)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.InvoiceAttempts WHERE InvoiceId = @id AND CommandName = N'sales.invoice.match.v1' AND Outcome = 1", ("@id", approved.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Sales.Invoice' AND EventType = N'sales.invoice.matched.v1' AND EntityId = @id "
            + "AND EntityVersionBefore = 3 AND EntityVersionAfter = 3 AND PermissionCode = N'sales.invoices.match'", ("@id", approved.Id.ToString("D"))));

        await using (var scope = run.Store(CompanyA))
        {
            var replay = await scope.Invoices.MatchInvoiceAsync(envelope with { CorrelationId = "SENTETIK-again" }, Hash('e'), approved.Id,
                (_, _) => throw new InvalidOperationException("A replay must not decide again."),
                new SalesDraftStateAudit("sales.invoice.matched.v1", "Match", "SENTETIK"));
            Assert.Equal(SalesDraftWriteOutcome.Replayed, replay!.Outcome);
            var found = await scope.Invoices.FindInvoiceAsync(approved.Id);
            Assert.Equal("GIB2026000000001", found!.Match!.OfficialNo);
        }

        // A second invoice cannot take the same official number or ETTN (SAT-D89): the database refuses a raw duplicate too.
        var second = await ApprovedAsync(run, "m2", approver);
        var duplicate = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "INSERT INTO sales.InvoiceMatches (CompanyId, InvoiceId, OfficialInvoiceNo, Ettn, InvoiceAmount, OfficialAmount, Difference, Tolerance, "
            + "MatchedBy, MatchedAtUtc, CorrelationId) SELECT CompanyId, @second, OfficialInvoiceNo, Ettn, InvoiceAmount, OfficialAmount, Difference, "
            + "Tolerance, MatchedBy, MatchedAtUtc, CorrelationId FROM sales.InvoiceMatches WHERE InvoiceId = @first",
            ("@second", second.Id), ("@first", approved.Id)));
        Assert.Contains(duplicate.Number, new[] { 2601, 2627 });
    }

    [SqlServerFact]
    public async Task A_foreign_currency_tl_difference_of_six_kurus_writes_nothing_without_approval_and_is_stored_approved_on_real_sql()
    {
        // SAT-D232: the USD invoice is measured on its TL equivalent; 0,06 TL is above the 0,05 TL tolerance, so an unapproved match
        // writes no row, attempt or audit event, and the approved one stores the approver and the reason (CK_InvoiceMatches_Amounts).
        await using var run = await Run.StartAsync();
        var approver = "SENTETIK-approver-" + Guid.NewGuid().ToString("N");
        var matcher = "SENTETIK-matcher-" + Guid.NewGuid().ToString("N");
        var approved = await ApprovedAsync(run, "m6", approver);
        Assert.Equal(Usd, approved.Header.CurrencyId);
        var tolerance = new SalesInvoiceMatchTolerancePolicy().ToleranceAmount;
        var official = new SalesOfficialInvoiceFacts("GIB2026000000006", OfficialEttn, approved.Header.PartyId, approved.Header.CurrencyId,
            approved.Totals.Payable.Amount, approved.PayableFunctionalAmount + 0.06m, approved.VatFunctionalAmount,
            approved.WithholdingFunctionalAmount);
        var envelope = run.Envelope("m6-match", actor: matcher, permission: SalesPermissionManifestV1.InvoicesMatch);

        await using (var scope = run.Store(CompanyA))
        {
            var refused = await Assert.ThrowsAsync<SalesRuleException>(() => scope.Invoices.MatchInvoiceAsync(envelope, Hash('a'), approved.Id,
                (invoice, now) => invoice.RecordMatch(3, matcher, official, tolerance, false, null, now),
                new SalesDraftStateAudit("sales.invoice.matched.v1", "Match", "SENTETIK")));
            Assert.Equal(SalesRuleCodes.InvoiceMatchDifferenceNotApproved, refused.Code);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.InvoiceMatches WHERE CompanyId = @company AND InvoiceId = @id",
            ("@company", CompanyA), ("@id", approved.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.InvoiceAttempts WHERE CompanyId = @company AND InvoiceId = @id AND CommandName = N'sales.invoice.match.v1'",
            ("@company", CompanyA), ("@id", approved.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId = @company AND EntityType = N'Sales.Invoice' AND EntityId = @id "
            + "AND EventType = N'sales.invoice.matched.v1'", ("@company", CompanyA), ("@id", approved.Id.ToString("D"))));

        await using (var scope = run.Store(CompanyA))
        {
            var matched = await scope.Invoices.MatchInvoiceAsync(envelope, Hash('b'), approved.Id,
                (invoice, now) => invoice.RecordMatch(3, matcher, official, tolerance, true, "SENTETIK kur yuvarlama farkı", now),
                new SalesDraftStateAudit("sales.invoice.matched.v1", "Match", "SENTETIK"));
            Assert.Equal((SalesDraftWriteOutcome.Recorded, 0.06m), (matched!.Outcome, matched.Invoice.Match!.Difference));
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.InvoiceMatches WHERE CompanyId = @company AND InvoiceId = @id AND Difference = 0.06 AND Tolerance = 0.05 "
            + "AND DifferenceApprovedBy = @m AND DifferenceReason IS NOT NULL AND OfficialAmount = @official",
            ("@company", CompanyA), ("@id", approved.Id), ("@m", matcher), ("@official", approved.PayableFunctionalAmount + 0.06m)));
        // The same audit filter that counted 0 above now counts the approved match once, so the earlier 0 is a meaningful proof.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId = @company AND EntityType = N'Sales.Invoice' AND EntityId = @id "
            + "AND EventType = N'sales.invoice.matched.v1'", ("@company", CompanyA), ("@id", approved.Id.ToString("D"))));
    }

    [SqlServerFact]
    public async Task A_vat_or_withholding_tl_difference_above_the_tolerance_writes_nothing_without_approval_and_is_stored_approved_on_real_sql()
    {
        // SAT-D241 (EK48): the payable TL agrees exactly; the VAT TL differs by 0,06 on one invoice and the withholding TL on another.
        // Each unapproved match writes no row, attempt or audit event; the approved one stores the six comparison columns.
        await using var run = await Run.StartAsync();
        var approver = "SENTETIK-approver-" + Guid.NewGuid().ToString("N");
        var matcher = "SENTETIK-matcher-" + Guid.NewGuid().ToString("N");
        var tolerance = new SalesInvoiceMatchTolerancePolicy().ToleranceAmount;
        foreach (var (prefix, no, vatDelta, withholdingDelta) in new[]
        {
            ("t1", "GIB2026000000241", 0.06m, 0m),
            ("t2", "GIB2026000000242", 0m, 0.06m),
        })
        {
            var approved = await ApprovedAsync(run, prefix, approver);
            var ettn = Guid.NewGuid().ToString("D");
            var official = new SalesOfficialInvoiceFacts(no, ettn, approved.Header.PartyId, approved.Header.CurrencyId,
                approved.Totals.Payable.Amount, approved.PayableFunctionalAmount, approved.VatFunctionalAmount + vatDelta,
                approved.WithholdingFunctionalAmount + withholdingDelta);
            var envelope = run.Envelope(prefix + "-match", actor: matcher, permission: SalesPermissionManifestV1.InvoicesMatch);

            await using (var scope = run.Store(CompanyA))
            {
                var refused = await Assert.ThrowsAsync<SalesRuleException>(() => scope.Invoices.MatchInvoiceAsync(envelope, Hash('a'),
                    approved.Id, (invoice, now) => invoice.RecordMatch(3, matcher, official, tolerance, false, null, now),
                    new SalesDraftStateAudit("sales.invoice.matched.v1", "Match", "SENTETIK")));
                Assert.Equal(SalesRuleCodes.InvoiceMatchDifferenceNotApproved, refused.Code);
            }

            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.InvoiceMatches WHERE InvoiceId = @id", ("@id", approved.Id)));
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sales.InvoiceAttempts WHERE InvoiceId = @id AND CommandName = N'sales.invoice.match.v1'", ("@id", approved.Id)));
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId = @company AND EntityType = N'Sales.Invoice' AND EntityId = @id "
                + "AND EventType = N'sales.invoice.matched.v1'", ("@company", CompanyA), ("@id", approved.Id.ToString("D"))));

            await using (var scope = run.Store(CompanyA))
            {
                var matched = await scope.Invoices.MatchInvoiceAsync(envelope, Hash('b'), approved.Id,
                    (invoice, now) => invoice.RecordMatch(3, matcher, official, tolerance, true, "SENTETIK vergi farkı", now),
                    new SalesDraftStateAudit("sales.invoice.matched.v1", "Match", "SENTETIK"));
                Assert.Equal((SalesDraftWriteOutcome.Recorded, 0m), (matched!.Outcome, matched.Invoice.Match!.Difference));
                var found = await scope.Invoices.FindInvoiceAsync(approved.Id);
                Assert.Equal((vatDelta, withholdingDelta), (found!.Match!.Taxes!.VatDifference, found.Match.Taxes.WithholdingDifference));
            }

            Assert.Equal(1, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sales.InvoiceMatches WHERE InvoiceId = @id AND Difference = 0 AND Tolerance = 0.05 "
                + "AND DifferenceApprovedBy = @m AND DifferenceReason IS NOT NULL AND InvoiceVatAmount = @vat AND OfficialVatAmount = @officialVat "
                + "AND VatDifference = @vatDiff AND InvoiceWithholdingAmount = @wh AND OfficialWithholdingAmount = @officialWh "
                + "AND WithholdingDifference = @whDiff",
                ("@id", approved.Id), ("@m", matcher), ("@vat", approved.VatFunctionalAmount), ("@officialVat", official.VatFunctionalAmount),
                ("@vatDiff", vatDelta), ("@wh", approved.WithholdingFunctionalAmount), ("@officialWh", official.WithholdingFunctionalAmount),
                ("@whDiff", withholdingDelta)));
        }
    }

    [SqlServerFact]
    public async Task The_match_tax_comparison_migrations_Down_passes_with_a_legacy_match_and_refuses_while_a_compared_match_exists_on_real_sql()
    {
        // SAT-D241 (EK48): the six columns are all-or-nothing (CK_InvoiceMatches_TaxAmounts), a VAT difference above the tolerance needs
        // the approver (CK_InvoiceMatches_Amounts), and the forward-fix Down refuses (51242) once a compared match exists.
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddInvoiceMatchTaxComparison().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);

        var approver = "SENTETIK-approver-" + Guid.NewGuid().ToString("N");
        var matcher = "SENTETIK-matcher-" + Guid.NewGuid().ToString("N");
        var legacy = await ApprovedAsync(run, "d1", approver);
        var other = await ApprovedAsync(run, "d2", approver);
        var compared = await ApprovedAsync(run, "d3", approver);
        const string insert = "INSERT INTO sales.InvoiceMatches (CompanyId, InvoiceId, OfficialInvoiceNo, Ettn, InvoiceAmount, OfficialAmount, "
            + "Difference, Tolerance, MatchedBy, MatchedAtUtc, CorrelationId";

        // A match stored before SAT-D241 carries no comparison: storable, and the Down still passes.
        await run.ExecuteAsync(insert + ") VALUES (@c, @id, N'GIB2026000000243', @ettn, 100, 100, 0, 0.05, N'SENTETIK-m', SYSUTCDATETIME(), "
            + "N'SENTETIK-c')", ("@c", CompanyA), ("@id", legacy.Id), ("@ettn", Guid.NewGuid().ToString("D")));
        await run.ExecuteAsync(guard);

        var partial = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(insert + ", InvoiceVatAmount) VALUES (@c, @id, "
            + "N'GIB2026000000244', @ettn, 100, 100, 0, 0.05, N'SENTETIK-m', SYSUTCDATETIME(), N'SENTETIK-c', 20)",
            ("@c", CompanyA), ("@id", other.Id), ("@ettn", Guid.NewGuid().ToString("D"))));
        Assert.Contains("CK_InvoiceMatches_TaxAmounts", partial.Message, StringComparison.Ordinal);

        var unapproved = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(insert + ", InvoiceVatAmount, OfficialVatAmount, "
            + "VatDifference, InvoiceWithholdingAmount, OfficialWithholdingAmount, WithholdingDifference) VALUES (@c, @id, N'GIB2026000000244', "
            + "@ettn, 100, 100, 0, 0.05, N'SENTETIK-m', SYSUTCDATETIME(), N'SENTETIK-c', 20, 20.06, 0.06, 0, 0, 0)",
            ("@c", CompanyA), ("@id", other.Id), ("@ettn", Guid.NewGuid().ToString("D"))));
        Assert.Contains("CK_InvoiceMatches_Amounts", unapproved.Message, StringComparison.Ordinal);

        var official = new SalesOfficialInvoiceFacts("GIB2026000000245", OfficialEttn, compared.Header.PartyId, compared.Header.CurrencyId,
            compared.Totals.Payable.Amount, compared.PayableFunctionalAmount, compared.VatFunctionalAmount, compared.WithholdingFunctionalAmount);
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Invoices.MatchInvoiceAsync(run.Envelope("d3-match", actor: matcher, permission: SalesPermissionManifestV1.InvoicesMatch),
                Hash('c'), compared.Id, (invoice, now) => invoice.RecordMatch(3, matcher, official, 0.05m, false, null, now),
                new SalesDraftStateAudit("sales.invoice.matched.v1", "Match", "SENTETIK"));
        }

        var down = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51242, down.Number);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.InvoiceMatches WHERE InvoiceId = @id AND VatDifference = 0 AND WithholdingDifference = 0",
            ("@id", compared.Id)));
    }

    [SqlServerFact]
    public async Task The_official_match_table_is_append_only_checks_its_amounts_and_its_Down_refuses_once_a_match_exists_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddSalesInvoiceOfficialMatch().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);

        var approver = "SENTETIK-approver-" + Guid.NewGuid().ToString("N");
        var matcher = "SENTETIK-matcher-" + Guid.NewGuid().ToString("N");
        var approved = await ApprovedAsync(run, "m3", approver);
        var other = await ApprovedAsync(run, "m4", approver);
        var official = new SalesOfficialInvoiceFacts("GIB2026000000003", OfficialEttn, approved.Header.PartyId, approved.Header.CurrencyId,
            approved.PayableFunctionalAmount, approved.PayableFunctionalAmount, approved.VatFunctionalAmount,
            approved.WithholdingFunctionalAmount);
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Invoices.MatchInvoiceAsync(run.Envelope("m3-match", actor: matcher, permission: SalesPermissionManifestV1.InvoicesMatch),
                Hash('f'), approved.Id, (invoice, now) => invoice.RecordMatch(3, matcher, official, 0.05m, false, null, now),
                new SalesDraftStateAudit("sales.invoice.matched.v1", "Match", "SENTETIK"));
        }

        foreach (var sql in new[]
        {
            "UPDATE sales.InvoiceMatches SET MatchedBy = N'SENTETIK-hack' WHERE InvoiceId = @id",
            "DELETE FROM sales.InvoiceMatches WHERE InvoiceId = @id",
        })
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(sql, ("@id", approved.Id)));
            Assert.Equal(51226, refused.Number);
        }

        // CK_InvoiceMatches_Amounts: a difference above the tolerance without an approver and a reason is not storable.
        var unapproved = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "INSERT INTO sales.InvoiceMatches (CompanyId, InvoiceId, OfficialInvoiceNo, Ettn, InvoiceAmount, OfficialAmount, Difference, Tolerance, "
            + "MatchedBy, MatchedAtUtc, CorrelationId) VALUES (@c, @id, N'GIB2026000000004', @ettn, 100, 101, 1, 0.05, N'SENTETIK-m', SYSUTCDATETIME(), N'SENTETIK-c')",
            ("@c", CompanyA), ("@id", other.Id), ("@ettn", "1a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d")));
        Assert.Contains("CK_InvoiceMatches_Amounts", unapproved.Message, StringComparison.Ordinal);

        var down = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51225, down.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.InvoiceMatches WHERE InvoiceId = @id", ("@id", approved.Id)));
    }

    [SqlServerFact]
    public async Task A_finalization_is_one_save_with_ledger_outbox_attempt_and_audit_and_the_triggers_and_Down_guard_hold_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddSalesInvoiceFinalization().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);

        var approver = "SENTETIK-approver-" + Guid.NewGuid().ToString("N");
        var matcher = "SENTETIK-matcher-" + Guid.NewGuid().ToString("N");
        var finalizer = "SENTETIK-finalizer-" + Guid.NewGuid().ToString("N");
        var approved = await ApprovedAsync(run, "f1", approver);
        var unmatched = await ApprovedAsync(run, "f2", approver);
        var official = new SalesOfficialInvoiceFacts("GIB2026000000011", OfficialEttn, approved.Header.PartyId, approved.Header.CurrencyId,
            approved.PayableFunctionalAmount, approved.PayableFunctionalAmount, approved.VatFunctionalAmount,
            approved.WithholdingFunctionalAmount);
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Invoices.MatchInvoiceAsync(run.Envelope("f1-match", actor: matcher, permission: SalesPermissionManifestV1.InvoicesMatch),
                Hash('1'), approved.Id, (invoice, now) => invoice.RecordMatch(3, matcher, official, 0.05m, false, null, now),
                new SalesDraftStateAudit("sales.invoice.matched.v1", "Match", "SENTETIK"));
        }

        // The header guard: no finalisation without a match, even by a raw write that skips the domain.
        var unmatchedRaw = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE sales.Invoices SET State = 6, Version = Version + 1 WHERE Id = @id", ("@id", unmatched.Id)));
        Assert.Equal(51223, unmatchedRaw.Number);

        SalesInvoiceStoreResult? finalized;
        await using (var scope = run.Store(CompanyA))
        {
            finalized = await scope.Invoices.FinalizeInvoiceAsync(
                run.Envelope("f1-final", actor: finalizer, permission: SalesPermissionManifestV1.InvoicesFinalize), Hash('2'), approved.Id,
                (invoice, now) => invoice.Finalize(3, finalizer, Guid.NewGuid, now),
                new SalesDraftStateAudit("sales.invoice.finalized.v1", "Finalize", "SENTETIK"));
        }

        Assert.Equal(SalesInvoiceState.Finalized, finalized!.Invoice.State);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.InvoiceFinalizations WHERE InvoiceId = @id AND FinanceStatus = 1 AND AttemptCount = 0",
            ("@id", approved.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.OutboxMessages WHERE AggregateId = @id AND EventType = N'sales.invoice.finalized.v1'",
            ("@id", approved.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id AND EventType = N'sales.invoice.finalized.v1' "
            + "AND EntityVersionBefore = 3 AND EntityVersionAfter = 4", ("@id", approved.Id.ToString("D"))));

        // The hand-off answer is a second, separate save of the ledger only.
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Invoices.RecordFinanceAttemptAsync(
                run.Envelope("f1-offer", actor: finalizer, permission: SalesPermissionManifestV1.InvoicesFinalize), null, null, approved.Id,
                (invoice, now) => invoice.RecordFinanceAttempt(true, "admitted", now),
                new SalesDraftStateAudit("sales.invoice.finance-offered.v1", "FinanceOffer", "SENTETIK"));
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.InvoiceFinalizations WHERE InvoiceId = @id AND FinanceStatus = 2 AND AttemptCount = 1",
            ("@id", approved.Id)));
        Assert.Equal(4L, await run.ScalarAsync<long>("SELECT Version FROM sales.Invoices WHERE Id = @id", ("@id", approved.Id)));

        foreach (var (sql, number) in new[]
        {
            ("UPDATE sales.Invoices SET Version = Version + 1 WHERE Id = @id", 51223),
            ("UPDATE sales.InvoiceFinalizations SET FinanceStatus = 1, AdmittedAtUtc = NULL WHERE InvoiceId = @id", 51232),
            ("UPDATE sales.InvoiceFinalizations SET AdmissionId = NEWID() WHERE InvoiceId = @id", 51232),
            ("DELETE FROM sales.InvoiceFinalizations WHERE InvoiceId = @id", 51232),
            ("UPDATE sales.OutboxMessages SET SourceOperation = N'hack' WHERE AggregateId = @id", 51233),
            ("DELETE FROM sales.OutboxMessages WHERE AggregateId = @id", 51233),
        })
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(sql, ("@id", approved.Id)));
            Assert.Equal(number, refused.Number);
        }

        var down = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51231, down.Number);
    }

    [SqlServerFact]
    public async Task A_single_instalment_usd_invoice_whose_tl_payable_is_zero_is_refused_at_finalization_and_writes_nothing_on_real_sql()
    {
        // SAT-D245 (EK48): 1 kg at 0.01 USD at rate 0.4 is stored with a TL payable of exactly 0.00. Finalisation refuses the single
        // instalment with the SAT-D233/SAT-D237 code; no ledger, outbox row, finalize attempt or audit event is written and the invoice
        // stays approved at its version (the service answers 422 sales_invoice_instalment_functional_invalid without a stored refusal).
        await using var run = await Run.StartAsync();
        var approver = "SENTETIK-approver-" + Guid.NewGuid().ToString("N");
        var matcher = "SENTETIK-matcher-" + Guid.NewGuid().ToString("N");
        var finalizer = "SENTETIK-finalizer-" + Guid.NewGuid().ToString("N");
        var approved = await ApprovedAsync(run, "z1", approver, (id, number) => ZeroTlInvoice(id, number, run.Actor));
        Assert.Equal((0.01m, 0m), (approved.Totals.Payable.Amount, approved.PayableFunctionalAmount));
        Assert.Single(approved.Instalments);
        Assert.Equal(0m, await run.ScalarAsync<decimal>(
            "SELECT SUM(FunctionalNetAmount + FunctionalVatAmount - FunctionalWithholdingAmount) FROM sales.InvoiceLines WHERE InvoiceId = @id",
            ("@id", approved.Id)));
        var official = new SalesOfficialInvoiceFacts("GIB2026000000245", OfficialEttn, approved.Header.PartyId, approved.Header.CurrencyId,
            approved.Totals.Payable.Amount, approved.PayableFunctionalAmount, approved.VatFunctionalAmount,
            approved.WithholdingFunctionalAmount);
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Invoices.MatchInvoiceAsync(run.Envelope("z1-match", actor: matcher, permission: SalesPermissionManifestV1.InvoicesMatch),
                Hash('d'), approved.Id, (invoice, now) => invoice.RecordMatch(3, matcher, official, 0.05m, false, null, now),
                new SalesDraftStateAudit("sales.invoice.matched.v1", "Match", "SENTETIK"));
        }

        await using (var scope = run.Store(CompanyA))
        {
            var refused = await Assert.ThrowsAsync<SalesRuleException>(() => scope.Invoices.FinalizeInvoiceAsync(
                run.Envelope("z1-final", actor: finalizer, permission: SalesPermissionManifestV1.InvoicesFinalize), Hash('e'), approved.Id,
                (invoice, now) => invoice.Finalize(3, finalizer, Guid.NewGuid, now),
                new SalesDraftStateAudit("sales.invoice.finalized.v1", "Finalize", "SENTETIK")));
            Assert.Equal(SalesRuleCodes.InvoiceInstalmentFunctionalInvalid, refused.Code);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.Invoices WHERE CompanyId = @company AND Id = @id AND State = 3 AND Version = 3",
            ("@company", CompanyA), ("@id", approved.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.InvoiceFinalizations WHERE CompanyId = @company AND InvoiceId = @id",
            ("@company", CompanyA), ("@id", approved.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.OutboxMessages WHERE CompanyId = @company AND AggregateId = @id",
            ("@company", CompanyA), ("@id", approved.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.InvoiceAttempts WHERE CompanyId = @company AND InvoiceId = @id AND CommandName = N'"
            + SalesInvoiceCommands.FinalizeV1 + "'", ("@company", CompanyA), ("@id", approved.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId = @company AND EntityType = N'Sales.Invoice' AND EntityId = @id "
            + "AND EventType = N'sales.invoice.finalized.v1'", ("@company", CompanyA), ("@id", approved.Id.ToString("D"))));
    }

    /// <summary>SAT-D245: one 20 % line of 1 kg at 0.01 USD at rate 0.4 (0.004 TL, frozen as 0.00 TL), due in one instalment.</summary>
    private static SalesInvoice ZeroTlInvoice(Guid id, string number, string actor)
    {
        var tax = new SalesLineTaxSnapshot(Product, Day, "Normal", "Steel", "Resolved", null, "Record", Guid.NewGuid(), 1, "Resolved", 20m,
            "NotRequired", null, null, "NotApplied", null, "NotRequired", null, null, null, 0, "Normal", false);
        var share = new SalesInvoiceShareInput(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "IRS-2026-000245", 1, Day, Guid.NewGuid(),
            Guid.NewGuid(), 0, Product, Kg, 1m, "1", new CommercialDraftFields("0.01", Kg, null), tax);
        var terms = SalesDueTerms.Create([new SalesDueInstalment(1, SalesDueBasis.OrderDate, 30, "100")]);
        return SalesInvoice.Create(id, CompanyA, number,
            new SalesInvoiceHeader(Guid.NewGuid(), Guid.NewGuid(), Usd, Guid.NewGuid(), "1", 2, Day,
                new SalesInvoiceRateSnapshot(0.4m, Day, Day.AddDays(-1), Try, 2)),
            [share], SalesDueDateCalculator.Compute(terms, Day, Day).DueDates, terms, actor, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// API-34 (SAT-D244, SAT-D246, SAT-D248): the export reads only finalised invoices by the finalisation instant (from included, to
    /// excluded), oldest first, says when more exist than the limit, and resolves every order number of a multi-order invoice; another
    /// company sees neither the invoices nor the order numbers.
    /// </summary>
    [SqlServerFact]
    public async Task The_export_reads_only_finalized_invoices_of_the_range_with_every_order_number_and_company_isolation_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var firstOrder = await OrderDraftAsync(run, CompanyA, "x-order-1");
        var secondOrder = await OrderDraftAsync(run, CompanyA, "x-order-2");
        var otherCompanyOrder = await OrderDraftAsync(run, CompanyB, "x-order-b");
        var approver = "SENTETIK-approver-" + Guid.NewGuid().ToString("N");
        var finalizer = "SENTETIK-finalizer-" + Guid.NewGuid().ToString("N");
        var multiOrder = await FinalizedAsync(run, "x1", "GIB2026000000021", OfficialEttn, approver, finalizer,
            (id, number) => Invoice(id, number, run.Actor, Guid.NewGuid(), Guid.NewGuid(), firstOrder.Id, secondOrder.Id));
        var later = await FinalizedAsync(run, "x2", "GIB2026000000022", "1b2c3d4e-5f6a-4b7c-8d9e-0f1a2b3c4d5e", approver, finalizer);
        var onlyApproved = await ApprovedAsync(run, "x3", approver);

        SalesInvoiceExportBatch all;
        await using (var scope = run.Store(CompanyA))
        {
            all = await scope.Invoices.ListFinalizedInvoicesAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1),
                SalesInvoiceApiV1.MaximumExportInvoices);
            Assert.False(all.HasMore);
            Assert.Equal([multiOrder.Id, later.Id], all.Items.Select(invoice => invoice.Id));
            Assert.DoesNotContain(onlyApproved.Id, all.Items.Select(invoice => invoice.Id));
            Assert.All(all.Items, invoice => Assert.Equal(SalesInvoiceState.Finalized, invoice.State));
            var exported = all.Items[0];
            Assert.Equal([1, 2], exported.Lines.Select(line => line.LineNo).Order());
            Assert.Equal(new HashSet<Guid> { firstOrder.Id, secondOrder.Id }, exported.Lines.Select(line => line.Share.SalesOrderId).ToHashSet());
            Assert.NotNull(exported.Finalization);
            Assert.NotEmpty(exported.Finalization!.Plan);
            Assert.Equal("GIB2026000000021", exported.Match?.OfficialNo);
            // SAT-D279 (EK55): the export batch restores the SAT-D241 official VAT and withholding.
            Assert.Equal((exported.VatFunctionalAmount, exported.WithholdingFunctionalAmount),
                (exported.Match!.Taxes!.OfficialVatAmount, exported.Match.Taxes.OfficialWithholdingAmount));

            var limited = await scope.Invoices.ListFinalizedInvoicesAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), 1);
            Assert.True(limited.HasMore);
            Assert.Equal([multiOrder.Id], limited.Items.Select(invoice => invoice.Id));

            // The bounds: the first finalisation instant is included as "from" and excluded as "to".
            var first = exported.Finalization.FinalizedAtUtc;
            Assert.Contains(multiOrder.Id, (await scope.Invoices.ListFinalizedInvoicesAsync(first, first.AddDays(1), 10)).Items.Select(i => i.Id));
            Assert.DoesNotContain(multiOrder.Id, (await scope.Invoices.ListFinalizedInvoicesAsync(first.AddDays(-1), first, 10)).Items
                .Select(i => i.Id));
            Assert.Empty((await scope.Invoices.ListFinalizedInvoicesAsync(DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(2),
                10)).Items);

            var numbers = await scope.Invoices.FindOrderDocumentNumbersAsync([firstOrder.Id, secondOrder.Id, otherCompanyOrder.Id]);
            Assert.Equal(new Dictionary<Guid, string> { [firstOrder.Id] = firstOrder.DocumentNo, [secondOrder.Id] = secondOrder.DocumentNo },
                numbers);
        }

        await using (var scope = run.Store(CompanyB))
        {
            Assert.Empty((await scope.Invoices.ListFinalizedInvoicesAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1),
                SalesInvoiceApiV1.MaximumExportInvoices)).Items);
            var numbers = await scope.Invoices.FindOrderDocumentNumbersAsync([firstOrder.Id, otherCompanyOrder.Id]);
            Assert.Equal(new Dictionary<Guid, string> { [otherCompanyOrder.Id] = otherCompanyOrder.DocumentNo }, numbers);
        }
    }

    /// <summary>
    /// An approved, matched and finalised invoice of company A. Each call needs its own official number and ETTN: both are unique per
    /// company (UX_InvoiceMatches_OfficialNo, UX_InvoiceMatches_Ettn).
    /// </summary>
    private static async Task<SalesInvoice> FinalizedAsync(Run run, string prefix, string officialNo, string ettn, string approver,
        string finalizer, Func<Guid, string, SalesInvoice>? build = null)
    {
        var approved = await ApprovedAsync(run, prefix, approver, build);
        var matcher = "SENTETIK-matcher-" + Guid.NewGuid().ToString("N");
        var official = new SalesOfficialInvoiceFacts(officialNo, ettn, approved.Header.PartyId, approved.Header.CurrencyId,
            approved.PayableFunctionalAmount, approved.PayableFunctionalAmount, approved.VatFunctionalAmount,
            approved.WithholdingFunctionalAmount);
        await using var scope = run.Store(CompanyA);
        await scope.Invoices.MatchInvoiceAsync(run.Envelope(prefix + "-match", actor: matcher, permission: SalesPermissionManifestV1.InvoicesMatch),
            Hash('1'), approved.Id, (invoice, now) => invoice.RecordMatch(3, matcher, official, 0.05m, false, null, now),
            new SalesDraftStateAudit("sales.invoice.matched.v1", "Match", "SENTETIK"));
        var finalized = await scope.Invoices.FinalizeInvoiceAsync(
            run.Envelope(prefix + "-final", actor: finalizer, permission: SalesPermissionManifestV1.InvoicesFinalize), Hash('2'), approved.Id,
            (invoice, now) => invoice.Finalize(3, finalizer, Guid.NewGuid, now),
            new SalesDraftStateAudit("sales.invoice.finalized.v1", "Finalize", "SENTETIK"));
        return finalized!.Invoice;
    }

    /// <summary>
    /// A stored one-line order draft of the company (a draft is stored only with a line, #257): the export only needs its number.
    /// </summary>
    private static async Task<SalesOrderDraft> OrderDraftAsync(Run run, Guid company, string key)
    {
        var money = new MoneyProfileSnapshot(company, Usd, SalesOrderDraft.MoneyOperation, Guid.NewGuid(), "3", Day, true, Day.AddDays(-30),
            null, 2);
        var header = new SalesDraftHeader(Guid.NewGuid(), Guid.NewGuid(), Usd, Day, Day.AddDays(10), DueTerms: TestDueTerms.PointDue30);
        var unitProfile = Guid.NewGuid();
        var unit = new UnitProfileSnapshot(company, Kg, "kg", "weight", unitProfile, "4", true, 3, 3, true, Day.AddDays(-30), null);
        var binding = new UnitBindingSnapshot(company, Kg, unitProfile, "4");
        var product = new ProductSnapshot(Product, "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        await using var scope = run.Store(company);
        return (await scope.Drafts.StoreDraftAsync(run.Envelope(key, permission: SalesPermissionManifestV1.DraftsCreate), Hash('d'),
            documentNo =>
            {
                var draft = SalesOrderDraft.Create(Guid.NewGuid(), company, documentNo, header, money, DateTimeOffset.UtcNow);
                draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit, "10", 10m, Day.AddDays(3)),
                    DateTimeOffset.UtcNow);
                return draft;
            })).Draft;
    }

    private static string Hash(char fill) => new(fill, 64);

    [SqlServerFact]
    public async Task The_control_center_lists_every_order_of_a_multi_order_invoice_in_line_order_and_another_company_sees_none_on_real_sql()
    {
        // SAT-D246 (EK49): an invoice over two orders is read back from its stored lines and its API-37 item names both orders, in the
        // order of the lines, once each; another company's control center reads none of it.
        await using var run = await Run.StartAsync();
        var approver = "SENTETIK-approver-" + Guid.NewGuid().ToString("N");
        var invoiceId = Guid.NewGuid();
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Invoices.StoreInvoiceAsync(run.Envelope("cc-create"), Hash('a'),
                number => Invoice(invoiceId, number, run.Actor, Guid.NewGuid(), Guid.NewGuid()));
        }

        await using (var scope = run.Store(CompanyA))
        {
            await scope.Invoices.DecideInvoiceAsync(run.Envelope("cc-submit"), Hash('b'), SalesInvoiceCommands.SubmitV1, invoiceId,
                (invoice, now) => invoice.Submit(1, run.Actor, now), new SalesDraftStateAudit("sales.invoice.submitted.v1", "Submit", "SENTETIK"));
        }

        await using (var scope = run.Store(CompanyA))
        {
            var approved = await scope.Invoices.DecideInvoiceAsync(run.Envelope("cc-approve", actor: approver,
                permission: SalesPermissionManifestV1.InvoicesApprove), Hash('c'), SalesInvoiceCommands.ApproveV1, invoiceId,
                (invoice, now) => invoice.Approve(2, approver, now), new SalesDraftStateAudit("sales.invoice.approved.v1", "Approve", "SENTETIK"));
            Assert.Equal(SalesInvoiceState.Approved, approved!.Invoice.State);
        }

        var firstOrder = await run.ScalarAsync<Guid>("SELECT SalesOrderId FROM sales.InvoiceLines WHERE InvoiceId = @id AND [LineNo] = 1", ("@id", invoiceId));
        var secondOrder = await run.ScalarAsync<Guid>("SELECT SalesOrderId FROM sales.InvoiceLines WHERE InvoiceId = @id AND [LineNo] = 2", ("@id", invoiceId));
        Assert.NotEqual(firstOrder, secondOrder);

        await using (var scope = run.Store(CompanyA))
        {
            var answer = await ControlCenter(scope, CompanyA).GetControlCenterAsync();
            var waiting = Assert.Single(answer.Items, item => item.Kind == SalesControlCenterCheckKindView.InvoiceAwaitingOfficialMatch);
            Assert.Equal((invoiceId, 2), (waiting.InvoiceId, waiting.Count));
            Assert.Equal([firstOrder, secondOrder], waiting.SalesOrderIds);
        }

        await using (var scope = run.Store(CompanyB))
        {
            var other = await ControlCenter(scope, CompanyB).GetControlCenterAsync();
            Assert.DoesNotContain(other.Items, item => item.InvoiceId == invoiceId);
            Assert.DoesNotContain(other.Items, item => item.SalesOrderIds.Contains(firstOrder) || item.SalesOrderIds.Contains(secondOrder));
            Assert.Empty(other.Items);
        }
    }

    private static SalesControlCenterService ControlCenter(StoreScope scope, Guid company)
    {
        var companyContext = new CompanyContext();
        companyContext.Set(company);
        return new SalesControlCenterService(companyContext, new ReadingUser(company), new ReadAuthority(), scope.Invoices,
            new SalesControlCenterPolicy(), TimeProvider.System);
    }

    private sealed class ReadingUser(Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public string? UserId => "SENTETIK-control-center-reader";

        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };

        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class ReadAuthority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(permission == SalesPermissionManifestV1.InvoicesRead
                ? new PermissionScope(true, true, new HashSet<string>(StringComparer.Ordinal) { "SENTETIK-SALES" },
                    new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
    }

    /// <summary>A USD invoice of two 20 % lines (99.99 and 10.01) at 34.5678 — the calculator golden's first group.</summary>
    private static SalesInvoice Invoice(Guid id, string number, string actor, Guid shipment1, Guid shipment2,
        Guid? order1 = null, Guid? order2 = null)
    {
        var tax = new SalesLineTaxSnapshot(Product, Day, "Normal", "Steel", "Resolved", null, "Record", Guid.NewGuid(), 1, "Resolved", 20m,
            "NotRequired", null, null, "NotApplied", null, "NotRequired", null, null, null, 0, "Normal", false);
        SalesInvoiceShareInput Share(Guid shipment, int lineNo, string quantity, string price, Guid? order) => new(Guid.NewGuid(), shipment,
            Guid.NewGuid(), "IRS-2026-000001", lineNo, Day, order ?? Guid.NewGuid(), Guid.NewGuid(), 0, Product, Kg, decimal.Parse(quantity,
                System.Globalization.CultureInfo.InvariantCulture), quantity, new CommercialDraftFields(price, Kg, null), tax);
        var terms = SalesDueTerms.Create([new SalesDueInstalment(1, SalesDueBasis.OrderDate, 30, "100")]);
        return SalesInvoice.Create(id, CompanyA, number,
            new SalesInvoiceHeader(Guid.NewGuid(), Guid.NewGuid(), Usd, Guid.NewGuid(), "1", 2, Day,
                new SalesInvoiceRateSnapshot(34.5678m, Day, Day.AddDays(-1), Try, 2)),
            [Share(shipment1, 1, "3", "33.33", order1), Share(shipment2, 2, "1", "10.01", order2)],
            SalesDueDateCalculator.Compute(terms, Day, Day).DueDates, terms, actor, DateTimeOffset.UtcNow);
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }
        public string Actor { get; } = "SENTETIK-invoicer-" + Guid.NewGuid().ToString("N");
        public string Correlation { get; } = $"sales-api32-{Guid.NewGuid():N}";

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            await using var scope = run.Store(CompanyA);
            await scope.Context.Database.MigrateAsync();
            return run;
        }

        public SalesDraftWriteEnvelope Envelope(string key, string? correlation = null, string? actor = null,
            string permission = SalesPermissionManifestV1.InvoicesWrite) =>
            new(actor ?? Actor, permission, ["SENTETIK-SALES"], correlation ?? Correlation, null, key, "Web",
                "127.0.0.1", "SENTETIK-agent");

        public StoreScope Store(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var dbContext = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext)).Options, companyContext);
            return new StoreScope(dbContext, new SalesStore(dbContext, companyContext, collector, TimeProvider.System));
        }

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{Factory.DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{Factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{Factory.DatabaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class StoreScope(SalesDbContext dbContext, SalesStore store) : IAsyncDisposable
    {
        public ISalesInvoiceStore Invoices => store;

        public ISalesOrderDraftStore Drafts => store;

        public SalesDbContext Context => dbContext;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }
}

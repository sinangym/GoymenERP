using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Real SQL Server evidence for the F-005 D1 manual official sales invoice record (FNS-D17…FNS-D21) and its
/// reservation (#66 6860586175: reserve → upload → record; one PDF content per company). Every row, number, ETTN, PDF
/// pin and permission in this class is SENTETIK test data; this is not a real invoice, grant or UAT. FNS-D39: a rejected
/// or withdrawn record keeps its row and frees its official number, ETTN and PDF content (unique among live records).
/// </summary>
public sealed class FinanceOfficialSalesInvoiceSqlServerRelationalAcceptanceTests
{
    private static readonly Guid Company = TestCompanyIds.CompanyA;
    private static readonly Guid Unreserved = Guid.Parse("91000000-0000-4000-8000-000000000001");
    private static readonly Guid Customer = Guid.Parse("92000000-0000-4000-8000-000000000002");
    private static readonly Guid Currency = Guid.Parse("93000000-0000-4000-8000-000000000003");
    private static readonly Guid Pdf = Guid.Parse("94000000-0000-4000-8000-000000000004");
    private const string Ettn = "95000000-0000-4000-8000-000000000005";
    private const string OfficialNo = "SNT2026000000001";
    private const string ReservationFk = "FK_OfficialSalesInvoices_OfficialSalesInvoiceReservations_CompanyId_Id";
    private const string PreviousMigration = "20261107154419_AddPaymentAllocations";
    private const string BeforeDecisionsMigration = "20261111233047_AddOfficialSalesInvoices";

    [SqlServerFact]
    public async Task A_different_actor_approves_the_record_and_rows_commands_and_audits_persist_together()
    {
        await using var run = await Run.StartAsync();
        var invoice = await ReserveAsync(run, "SENTETIK-maker");
        await using (var maker = run.Service("SENTETIK-maker"))
        {
            var recorded = await maker.Service.RecordAsync(maker.Context, new CommandMetadata("record-key"),
                Request(invoice), default);
            Assert.Equal(CommandOutcome.Succeeded, recorded.Outcome);
            Assert.Equal(FinanceOfficialSalesInvoiceStatusV1.PendingApproval, recorded.Resource!.Status);
            Assert.Null(recorded.ExistingOfficialNo);

            var selfApproval = await maker.Service.ApproveAsync(maker.Context, new CommandMetadata("self-approve-key"),
                new FinanceOfficialSalesInvoiceApproveRequestV1(invoice, "1"), default);
            Assert.Equal(CommandOutcome.ValidationFailed, selfApproval.Outcome);
            Assert.Equal([new ValidationIssue("approver", "approver_must_differ_from_recorder")], selfApproval.Issues);
            Assert.Null(selfApproval.Resource);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OfficialSalesInvoices WHERE Status = 1 AND ApprovedBy IS NULL"));
        // The reservation's command and the record's command.
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialSalesInvoiceCommands"));

        await using (var checker = run.Service("SENTETIK-checker"))
        {
            var approved = await checker.Service.ApproveAsync(checker.Context, new CommandMetadata("approve-key"),
                new FinanceOfficialSalesInvoiceApproveRequestV1(invoice, "1"), default);
            Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
            Assert.Equal(FinanceOfficialSalesInvoiceStatusV1.Approved, approved.Resource!.Status);
            Assert.Equal("SENTETIK-checker", approved.Resource.ApprovedBy);
            Assert.Equal("1042.08", approved.Resource.TaxBaseFunctionalAmount);
        }

        Assert.Equal("SENTETIK-maker|SENTETIK-checker|2|1042.08", await run.ScalarAsync<string>(
            "SELECT CONCAT(RecordedBy COLLATE DATABASE_DEFAULT, '|', ApprovedBy COLLATE DATABASE_DEFAULT, '|', "
            + "Status, '|', TaxBaseFunctionalAmountText COLLATE DATABASE_DEFAULT) "
            + "FROM finance.OfficialSalesInvoices WHERE Id = @id", ("@id", invoice)));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialSalesInvoiceCommands"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.OfficialSalesInvoice'"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.official-sales-invoice.reserved.v1'"));
    }

    [SqlServerFact]
    public async Task The_database_refuses_an_approval_by_the_recorder_and_a_missing_tax_base()
    {
        await using var run = await Run.StartAsync();
        var invoice = await RecordAsync(run);

        var selfApproval = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE finance.OfficialSalesInvoices SET Status = 2, Version = 2, ApprovedBy = RecordedBy, "
            + "ApprovedAtUtc = SYSUTCDATETIME() WHERE Id = @id", ("@id", invoice)));
        Assert.Equal(547, selfApproval.Number);
        Assert.Contains("CK_OfficialSalesInvoices_Approval", selfApproval.Message, StringComparison.Ordinal);

        var emptyTaxBase = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE finance.OfficialSalesInvoices SET TaxBaseFunctionalAmountText = '' WHERE Id = @id", ("@id", invoice)));
        Assert.Equal(547, emptyTaxBase.Number);
        Assert.Contains("CK_OfficialSalesInvoices_Text", emptyTaxBase.Message, StringComparison.Ordinal);

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OfficialSalesInvoices WHERE Status = 1 AND ApprovedBy IS NULL "
            + "AND TaxBaseFunctionalAmountText = '1042.08'"));
    }

    [SqlServerFact]
    public async Task Official_sales_invoice_migration_refuses_a_stored_reservation_and_rolls_back_when_empty()
    {
        await using var run = await Run.StartAsync();
        await using (var context = run.Context())
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'finance') AND name LIKE N'OfficialSalesInvoice%'"));
            await context.Database.MigrateAsync();
            Assert.Equal(3, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'finance') AND name LIKE N'OfficialSalesInvoice%'"));
        }

        // A reservation alone (no record yet) may already own an uploaded PDF: the Down guard refuses it as well.
        await ReserveAsync(run, "SENTETIK-maker", key: "rollback-guard");

        await using (var context = run.Context())
        {
            var error = await Assert.ThrowsAsync<SqlException>(() =>
                context.GetService<IMigrator>().MigrateAsync(PreviousMigration));
            Assert.Equal(51051, error.Number);
            Assert.Contains("forward fix", error.Message, StringComparison.Ordinal);
            Assert.EndsWith("_AddOfficialSalesInvoices",
                (await context.Database.GetAppliedMigrationsAsync()).Last(), StringComparison.Ordinal);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialSalesInvoiceReservations"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialSalesInvoices"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialSalesInvoiceCommands"));
    }

    [SqlServerFact]
    public async Task Two_checkers_racing_to_approve_store_one_approval_and_the_loser_answers_a_concurrent_change()
    {
        await using var run = await Run.StartAsync();
        var invoice = await RecordAsync(run);

        // Both checkers read the same pending record, then wait for each other at the save.
        using var barrier = new Barrier(2);
        async Task<FinanceOfficialSalesInvoiceResultV1> ApproveAsync(string checker)
        {
            await using var scope = run.Service(checker, barrier: barrier);
            return await scope.Service.ApproveAsync(scope.Context, new CommandMetadata("approve-" + checker),
                new FinanceOfficialSalesInvoiceApproveRequestV1(invoice, "1"), default);
        }

        var results = await Task.WhenAll(Task.Run(() => ApproveAsync("SENTETIK-checker-1")),
            Task.Run(() => ApproveAsync("SENTETIK-checker-2")));
        var winner = Assert.Single(results, result => result.Outcome == CommandOutcome.Succeeded);
        var loser = Assert.Single(results, result => result.Outcome == CommandOutcome.VersionConflict);
        Assert.Equal([new ValidationIssue("request", "concurrent_change")], loser.Issues);
        Assert.False(loser.IsReplay);
        Assert.Equal(winner.Resource!.ApprovedBy + "|2|2", await run.ScalarAsync<string>(
            "SELECT CONCAT(ApprovedBy COLLATE DATABASE_DEFAULT, '|', Status, '|', Version) "
            + "FROM finance.OfficialSalesInvoices WHERE Id = @id", ("@id", invoice)));
        // The loser's attempt is not a stored answer: only the record and the winner's approval are.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OfficialSalesInvoiceCommands WHERE CommandName = @name",
            ("@name", FinanceOfficialSalesInvoiceServiceV1.ApproveCommand)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.official-sales-invoice.approved.v1'"));
    }

    [SqlServerFact]
    public async Task Two_requests_with_one_key_racing_to_record_store_one_invoice_and_the_loser_replays_it()
    {
        await using var run = await Run.StartAsync();
        var invoice = await ReserveAsync(run, "SENTETIK-maker");

        // The same maker sends the same body with the same key twice; both pass the replay lookup before either saves.
        using var barrier = new Barrier(2);
        async Task<FinanceOfficialSalesInvoiceResultV1> RecordWithTheSameKeyAsync()
        {
            await using var scope = run.Service("SENTETIK-maker", barrier: barrier);
            return await scope.Service.RecordAsync(scope.Context, new CommandMetadata("record-key"), Request(invoice),
                default);
        }

        var results = await Task.WhenAll(Task.Run(() => RecordWithTheSameKeyAsync()),
            Task.Run(() => RecordWithTheSameKeyAsync()));
        Assert.All(results, result => Assert.Equal(CommandOutcome.Succeeded, result.Outcome));
        var first = Assert.Single(results, result => !result.IsReplay);
        var replay = Assert.Single(results, result => result.IsReplay);
        Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(first.Resource!.InvoiceId, replay.Resource!.InvoiceId);
        Assert.Equal(first.Resource.RecordedAtUtc, replay.Resource.RecordedAtUtc);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialSalesInvoices"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OfficialSalesInvoiceCommands WHERE CommandName = @name",
            ("@name", FinanceOfficialSalesInvoiceServiceV1.RecordCommand)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.official-sales-invoice.recorded.v1'"));
    }

    [SqlServerFact]
    public async Task Another_company_neither_reads_nor_approves_the_record_and_its_refusal_is_stored_under_its_own_company()
    {
        await using var run = await Run.StartAsync();
        var invoice = await RecordAsync(run);

        await using (var foreign = run.Service("SENTETIK-checker", TestCompanyIds.CompanyB))
        {
            Assert.Equal(ReadOutcome.NotFound, (await foreign.Service.GetAsync(foreign.Context, invoice, default)).Outcome);
            var page = await foreign.Service.ListAsync(foreign.Context, pendingOnly: false, cursor: null, default);
            Assert.Equal(ReadOutcome.Found, page.Outcome);
            Assert.Empty(page.Resource!.Items);

            var approve = await foreign.Service.ApproveAsync(foreign.Context, new CommandMetadata("foreign-approve"),
                new FinanceOfficialSalesInvoiceApproveRequestV1(invoice, "1"), default);
            Assert.Equal(CommandOutcome.ValidationFailed, approve.Outcome);
            Assert.Equal([new ValidationIssue("invoiceId", "invoice_not_found")], approve.Issues);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OfficialSalesInvoices WHERE CompanyId = @a AND Status = 1 AND ApprovedBy IS NULL",
            ("@a", Company)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OfficialSalesInvoiceCommands WHERE CompanyId = @b AND CommandName = @name",
            ("@b", TestCompanyIds.CompanyB), ("@name", FinanceOfficialSalesInvoiceServiceV1.ApproveCommand)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OfficialSalesInvoiceCommands WHERE CompanyId = @a AND CommandName = @name",
            ("@a", Company), ("@name", FinanceOfficialSalesInvoiceServiceV1.ApproveCommand)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.official-sales-invoice.approved.v1'"));
    }

    [SqlServerFact]
    public async Task A_reservation_replays_under_its_key_is_consumed_once_and_the_database_keeps_the_record_on_it()
    {
        await using var run = await Run.StartAsync();
        FinanceOfficialSalesInvoiceReservationResultV1 first;
        await using (var maker = run.Service("SENTETIK-maker"))
        {
            first = await maker.Service.ReserveAsync(maker.Context, new CommandMetadata("reserve-key"),
                new FinanceOfficialSalesInvoiceReservationRequestV1(), default);
            Assert.Equal(CommandOutcome.Succeeded, first.Outcome);
            Assert.False(first.IsReplay);
        }

        var invoice = first.Resource!.InvoiceId;
        await using (var maker = run.Service("SENTETIK-maker"))
        {
            var replay = await maker.Service.ReserveAsync(maker.Context, new CommandMetadata("reserve-key"),
                new FinanceOfficialSalesInvoiceReservationRequestV1(), default);
            Assert.Equal((CommandOutcome.Succeeded, true), (replay.Outcome, replay.IsReplay));
            Assert.Equal(first.Resource, replay.Resource);
            Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId);
        }

        Assert.Equal("SENTETIK-maker", await run.ScalarAsync<string>(
            "SELECT ReservedBy FROM finance.OfficialSalesInvoiceReservations WHERE Id = @id AND CompanyId = @a",
            ("@id", invoice), ("@a", Company)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialSalesInvoiceReservations"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OfficialSalesInvoiceCommands WHERE CommandName = @name",
            ("@name", FinanceOfficialSalesInvoiceServiceV1.ReserveCommand)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.official-sales-invoice.reserved.v1'"));

        // Only the reserving actor consumes the id, and an id nobody reserved is refused; neither writes anything.
        await using (var other = run.Service("SENTETIK-other"))
        {
            foreach (var (key, id) in new[] { ("other-record", invoice), ("unreserved-record", Unreserved) })
            {
                var refused = await other.Service.RecordAsync(other.Context, new CommandMetadata(key), Request(id),
                    default);
                Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
                Assert.Equal([new ValidationIssue("invoiceId", "reservation_not_found")], refused.Issues);
            }
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialSalesInvoices"));

        await using (var maker = run.Service("SENTETIK-maker"))
        {
            Assert.Equal(CommandOutcome.Succeeded, (await maker.Service.RecordAsync(maker.Context,
                new CommandMetadata("record-key"), Request(invoice), default)).Outcome);
            var again = await maker.Service.RecordAsync(maker.Context, new CommandMetadata("record-again-key"),
                Second(invoice), default);
            Assert.Equal(CommandOutcome.IdempotencyConflict, again.Outcome);
            Assert.Equal([new ValidationIssue("invoiceId", "invoice_already_exists")], again.Issues);
        }

        // The database keeps every record on its own company's reservation: it is neither deleted nor moved away.
        var deleted = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "DELETE FROM finance.OfficialSalesInvoiceReservations WHERE Id = @id", ("@id", invoice)));
        Assert.Equal(547, deleted.Number);
        Assert.Contains(ReservationFk, deleted.Message, StringComparison.Ordinal);
        var moved = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE finance.OfficialSalesInvoices SET CompanyId = @b WHERE Id = @id",
            ("@b", TestCompanyIds.CompanyB), ("@id", invoice)));
        Assert.Equal(547, moved.Number);
        Assert.Contains(ReservationFk, moved.Message, StringComparison.Ordinal);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OfficialSalesInvoices WHERE Id = @id AND CompanyId = @a",
            ("@id", invoice), ("@a", Company)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialSalesInvoiceReservations"));
    }

    [SqlServerFact]
    public async Task The_same_pdf_content_in_a_second_record_is_refused_with_the_first_official_number_per_company()
    {
        await using var run = await Run.StartAsync();
        var sameContent = Sha("SENTETIK-same-content");
        var first = await ReserveAsync(run, "SENTETIK-maker");
        var second = await ReserveAsync(run, "SENTETIK-maker");
        await using (var maker = run.Service("SENTETIK-maker", pdfSha: sameContent))
        {
            Assert.Equal(CommandOutcome.Succeeded, (await maker.Service.RecordAsync(maker.Context,
                new CommandMetadata("first-record"), Request(first), default)).Outcome);
            var duplicate = await maker.Service.RecordAsync(maker.Context, new CommandMetadata("second-record"),
                Second(second), default);
            Assert.Equal(CommandOutcome.ValidationFailed, duplicate.Outcome);
            Assert.Null(duplicate.Resource);
            Assert.Equal([new ValidationIssue("pdfDocumentId", "pdf_already_recorded")], duplicate.Issues);
            Assert.Equal(OfficialNo, duplicate.ExistingOfficialNo);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialSalesInvoices"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OfficialSalesInvoiceCommands WHERE InvoiceId = @id AND CommandName = @name",
            ("@id", second), ("@name", FinanceOfficialSalesInvoiceServiceV1.RecordCommand)));

        // Other content records on the same reservation; the unique index refuses a later copy of the first hash.
        await using (var maker = run.Service("SENTETIK-maker", pdfSha: Sha("SENTETIK-other-content")))
        {
            Assert.Equal(CommandOutcome.Succeeded, (await maker.Service.RecordAsync(maker.Context,
                new CommandMetadata("second-record-other-pdf"), Second(second), default)).Outcome);
        }

        var copied = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE finance.OfficialSalesInvoices SET PdfSha256 = @sha WHERE Id = @id",
            ("@sha", sameContent), ("@id", second)));
        Assert.Equal(2601, copied.Number);
        Assert.Contains("UX_OfficialSalesInvoices_PdfSha256", copied.Message, StringComparison.Ordinal);

        // The key is (CompanyId, PdfSha256): the same content records in another company.
        var foreign = await ReserveAsync(run, "SENTETIK-maker", TestCompanyIds.CompanyB);
        await using (var other = run.Service("SENTETIK-maker", TestCompanyIds.CompanyB, pdfSha: sameContent))
        {
            Assert.Equal(CommandOutcome.Succeeded, (await other.Service.RecordAsync(other.Context,
                new CommandMetadata("foreign-record"), Request(foreign), default)).Outcome);
        }

        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(DISTINCT CompanyId) FROM finance.OfficialSalesInvoices WHERE PdfSha256 = @sha",
            ("@sha", sameContent)));
    }

    [SqlServerFact]
    public async Task Two_records_racing_with_the_same_pdf_content_store_one_and_the_loser_answers_the_winners_number()
    {
        await using var run = await Run.StartAsync();
        var sameContent = Sha("SENTETIK-raced-content");
        var first = await ReserveAsync(run, "SENTETIK-maker");
        var second = await ReserveAsync(run, "SENTETIK-maker");

        // Two records with different ids, numbers and keys but one PDF content pass every check, then meet at the save.
        using var barrier = new Barrier(2);
        async Task<FinanceOfficialSalesInvoiceResultV1> RecordTheSameContentAsync(
            FinanceOfficialSalesInvoiceRecordRequestV1 request)
        {
            await using var scope = run.Service("SENTETIK-maker", barrier: barrier, pdfSha: sameContent);
            return await scope.Service.RecordAsync(scope.Context, new CommandMetadata("record-" + request.OfficialNo),
                request, default);
        }

        var results = await Task.WhenAll(Task.Run(() => RecordTheSameContentAsync(Request(first))),
            Task.Run(() => RecordTheSameContentAsync(Second(second))));
        var winner = Assert.Single(results, result => result.Outcome == CommandOutcome.Succeeded);
        var loser = Assert.Single(results, result => result.Outcome == CommandOutcome.ValidationFailed);
        Assert.Equal([new ValidationIssue("pdfDocumentId", "pdf_already_recorded")], loser.Issues);
        Assert.Equal(winner.Resource!.OfficialNo, loser.ExistingOfficialNo);
        Assert.False(loser.IsReplay);
        Assert.Null(loser.Resource);
        Assert.Equal(winner.Resource.OfficialNo, await run.ScalarAsync<string>(
            "SELECT OfficialNo FROM finance.OfficialSalesInvoices WHERE PdfSha256 = @sha", ("@sha", sameContent)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialSalesInvoices"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OfficialSalesInvoiceCommands WHERE CommandName = @name",
            ("@name", FinanceOfficialSalesInvoiceServiceV1.RecordCommand)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.official-sales-invoice.recorded.v1'"));
    }

    [SqlServerFact]
    public async Task Another_company_neither_finds_nor_consumes_a_reservation_through_the_owner_read_or_the_record()
    {
        await using var run = await Run.StartAsync();
        var invoice = await ReserveAsync(run, "SENTETIK-maker");

        Assert.Equal(ReadOutcome.Found, await run.OwnerReadAsync("SENTETIK-checker", Company, invoice));
        Assert.Equal(ReadOutcome.NotFound, await run.OwnerReadAsync("SENTETIK-checker", Company, Unreserved));
        Assert.Equal(ReadOutcome.NotFound,
            await run.OwnerReadAsync("SENTETIK-checker", TestCompanyIds.CompanyB, invoice));

        // The same actor name in another company does not see the reservation, so it cannot record on it.
        await using (var foreign = run.Service("SENTETIK-maker", TestCompanyIds.CompanyB))
        {
            var record = await foreign.Service.RecordAsync(foreign.Context, new CommandMetadata("foreign-record"),
                Request(invoice), default);
            Assert.Equal(CommandOutcome.ValidationFailed, record.Outcome);
            Assert.Equal([new ValidationIssue("invoiceId", "reservation_not_found")], record.Issues);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialSalesInvoices"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OfficialSalesInvoiceCommands WHERE CompanyId = @b",
            ("@b", TestCompanyIds.CompanyB)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OfficialSalesInvoiceReservations WHERE CompanyId = @a", ("@a", Company)));
    }

    [SqlServerFact]
    public async Task A_rejected_or_withdrawn_record_frees_its_number_ettn_and_pdf_while_two_live_records_still_collide()
    {
        await using var run = await Run.StartAsync();
        var rejected = await RecordAsync(run);
        await using (var checker = run.Service("SENTETIK-checker"))
        {
            var reject = await checker.Service.RejectAsync(checker.Context, new CommandMetadata("reject-key"),
                new FinanceOfficialSalesInvoiceRejectRequestV1(rejected, "1", "SENTETIK tutar uyuşmuyor"), default);
            Assert.Equal(CommandOutcome.Succeeded, reject.Outcome);
            Assert.Equal(FinanceOfficialSalesInvoiceStatusV1.Rejected, reject.Resource!.Status);
        }

        // The same number, ETTN and PDF content (one hash for every record here) record again on a new reservation.
        var withdrawn = await ReserveAsync(run, "SENTETIK-maker");
        await using (var maker = run.Service("SENTETIK-maker"))
        {
            Assert.Equal(CommandOutcome.Succeeded, (await maker.Service.RecordAsync(maker.Context,
                new CommandMetadata("record-after-reject"), Request(withdrawn), default)).Outcome);
            var withdraw = await maker.Service.WithdrawAsync(maker.Context, new CommandMetadata("withdraw-key"),
                new FinanceOfficialSalesInvoiceWithdrawRequestV1(withdrawn, "1"), default);
            Assert.Equal(CommandOutcome.Succeeded, withdraw.Outcome);
            Assert.Equal(FinanceOfficialSalesInvoiceStatusV1.Withdrawn, withdraw.Resource!.Status);
        }

        var live = await ReserveAsync(run, "SENTETIK-maker");
        var refusedLive = await ReserveAsync(run, "SENTETIK-maker");
        await using (var maker = run.Service("SENTETIK-maker"))
        {
            Assert.Equal(CommandOutcome.Succeeded, (await maker.Service.RecordAsync(maker.Context,
                new CommandMetadata("record-after-withdraw"), Request(live), default)).Outcome);
            // A second live record with the same content is still refused while the first one is pending.
            var duplicate = await maker.Service.RecordAsync(maker.Context, new CommandMetadata("record-live-copy"),
                Request(refusedLive), default);
            Assert.Equal(CommandOutcome.ValidationFailed, duplicate.Outcome);
            Assert.Null(duplicate.Resource);
        }

        // Every row stays (nothing is deleted); one number is held by a rejected, a withdrawn and a live record.
        Assert.Equal("3|4|1", await run.ScalarAsync<string>(
            "SELECT STRING_AGG(CAST(Status AS nvarchar(1)), N'|') WITHIN GROUP (ORDER BY RecordedAtUtc, Status) "
            + "FROM finance.OfficialSalesInvoices WHERE CompanyId = @a AND OfficialNo = @no AND Ettn = @ettn",
            ("@a", Company), ("@no", OfficialNo), ("@ettn", Ettn)));
        Assert.Equal("SENTETIK-checker|SENTETIK tutar uyuşmuyor|SENTETIK-maker", await run.ScalarAsync<string>(
            "SELECT CONCAT((SELECT RejectedBy COLLATE DATABASE_DEFAULT FROM finance.OfficialSalesInvoices WHERE Id = @r), "
            + "N'|', (SELECT RejectionReason FROM finance.OfficialSalesInvoices WHERE Id = @r), N'|', "
            + "(SELECT WithdrawnBy COLLATE DATABASE_DEFAULT FROM finance.OfficialSalesInvoices WHERE Id = @w))",
            ("@r", rejected), ("@w", withdrawn)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OfficialSalesInvoices WHERE Id = @id", ("@id", refusedLive)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.official-sales-invoice.rejected.v1'"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.official-sales-invoice.withdrawn.v1'"));

        // The unique indexes are filtered to live records (pending, approved) and still hold among them: the withdrawn
        // row put back to pending collides, and so does a live copy that shares any one of the three keys.
        foreach (var index in new[] { "UX_OfficialSalesInvoices_Ettn", "UX_OfficialSalesInvoices_OfficialNo",
                     "UX_OfficialSalesInvoices_PdfSha256" })
        {
            Assert.Equal("[Status] IN (1, 2)|1", await run.ScalarAsync<string>(
                "SELECT CONCAT(i.filter_definition, N'|', CAST(i.is_unique AS int)) FROM sys.indexes i "
                + "WHERE i.object_id = OBJECT_ID(N'finance.OfficialSalesInvoices') AND i.name = @name",
                ("@name", index)));
        }

        var revived = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE finance.OfficialSalesInvoices SET Status = 1, Version = 1, WithdrawnBy = NULL, "
            + "WithdrawnAtUtc = NULL WHERE Id = @id", ("@id", withdrawn)));
        Assert.Equal(2601, revived.Number);
        Assert.Contains("UX_OfficialSalesInvoices_", revived.Message, StringComparison.Ordinal);
        var otherKeys = new Dictionary<string, string>
        {
            ["OfficialNo"] = "SNT2026000000009",
            ["Ettn"] = "98000000-0000-4000-8000-000000000008",
            ["PdfSha256"] = Sha("SENTETIK-other-content"),
        };
        foreach (var column in otherKeys.Keys)
        {
            // A live copy that differs in the other two keys still collides on this one.
            var changed = otherKeys.Where(pair => pair.Key != column).ToArray();
            var copied = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
                "UPDATE finance.OfficialSalesInvoices SET Status = 1, Version = 1, WithdrawnBy = NULL, "
                + "WithdrawnAtUtc = NULL, " + string.Join(", ", changed.Select(pair => $"{pair.Key} = @{pair.Key}"))
                + " WHERE Id = @id",
                [("@id", withdrawn), .. changed.Select(pair => ("@" + pair.Key, (object)pair.Value))]));
            Assert.Equal(2601, copied.Number);
            Assert.Contains("UX_OfficialSalesInvoices_" + column, copied.Message, StringComparison.Ordinal);
        }

        Assert.Equal(4, await run.ScalarAsync<int>(
            "SELECT Status FROM finance.OfficialSalesInvoices WHERE Id = @id", ("@id", withdrawn)));
    }

    [SqlServerFact]
    public async Task The_decision_migrations_Down_passes_with_live_records_and_refuses_while_a_rejected_or_withdrawn_record_exists()
    {
        var guard = Assert.IsType<SqlOperation>(new GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
            .AddOfficialSalesInvoiceRejectionAndWithdrawal().DownOperations[0]).Sql;
        foreach (var decision in new[] { "reject", "withdraw" })
        {
            await using var run = await Run.StartAsync();
            var pending = await RecordAsync(run);
            var approved = await ReserveAsync(run, "SENTETIK-maker");
            await using (var maker = run.Service("SENTETIK-maker", pdfSha: Sha("SENTETIK-approved-content")))
            {
                Assert.Equal(CommandOutcome.Succeeded, (await maker.Service.RecordAsync(maker.Context,
                    new CommandMetadata("record-approved"), Second(approved), default)).Outcome);
            }
            await using (var checker = run.Service("SENTETIK-checker"))
            {
                Assert.Equal(CommandOutcome.Succeeded, (await checker.Service.ApproveAsync(checker.Context,
                    new CommandMetadata("approve-key"), new FinanceOfficialSalesInvoiceApproveRequestV1(approved, "1"),
                    default)).Outcome);
            }

            // Only pending and approved records: the guard passes (it changes nothing).
            await run.ExecuteAsync(guard);

            await using (var actor = run.Service(decision == "reject" ? "SENTETIK-checker" : "SENTETIK-maker"))
            {
                var decided = decision == "reject"
                    ? await actor.Service.RejectAsync(actor.Context, new CommandMetadata("decide-key"),
                        new FinanceOfficialSalesInvoiceRejectRequestV1(pending, "1", "SENTETIK gerekçe"), default)
                    : await actor.Service.WithdrawAsync(actor.Context, new CommandMetadata("decide-key"),
                        new FinanceOfficialSalesInvoiceWithdrawRequestV1(pending, "1"), default);
                Assert.Equal(CommandOutcome.Succeeded, decided.Outcome);
            }

            var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
            Assert.Equal(51273, refused.Number);
            Assert.Contains("forward fix", refused.Message, StringComparison.Ordinal);

            await using (var context = run.Context())
            {
                var rollback = await Assert.ThrowsAsync<SqlException>(() =>
                    context.GetService<IMigrator>().MigrateAsync(BeforeDecisionsMigration));
                Assert.Equal(51273, rollback.Number);
                Assert.EndsWith("_AddOfficialSalesInvoiceRejectionAndWithdrawal",
                    (await context.Database.GetAppliedMigrationsAsync()).Last(), StringComparison.Ordinal);
            }

            Assert.Equal(decision == "reject" ? "2|3" : "2|4", await run.ScalarAsync<string>(
                "SELECT STRING_AGG(CAST(Status AS nvarchar(1)), N'|') WITHIN GROUP (ORDER BY Status) "
                + "FROM finance.OfficialSalesInvoices"));
        }
    }

    private static async Task<Guid> ReserveAsync(Run run, string actor, Guid? company = null, string? key = null)
    {
        await using var scope = run.Service(actor, company);
        var reserved = await scope.Service.ReserveAsync(scope.Context,
            new CommandMetadata(key ?? "reserve-" + Guid.NewGuid().ToString("N")),
            new FinanceOfficialSalesInvoiceReservationRequestV1(), default);
        Assert.Equal(CommandOutcome.Succeeded, reserved.Outcome);
        return reserved.Resource!.InvoiceId;
    }

    private static async Task<Guid> RecordAsync(Run run)
    {
        var invoice = await ReserveAsync(run, "SENTETIK-maker");
        await using var maker = run.Service("SENTETIK-maker");
        Assert.Equal(CommandOutcome.Succeeded, (await maker.Service.RecordAsync(maker.Context,
            new CommandMetadata("record-key"), Request(invoice), default)).Outcome);
        return invoice;
    }

    private static FinanceOfficialSalesInvoiceRecordRequestV1 Request(Guid invoice) => new(invoice, OfficialNo, Ettn,
        Customer, Currency, "1250.50", "1250.50", "1042.08", "208.42", "0", Pdf);

    /// <summary>A second invoice: its own number, ETTN and document.</summary>
    private static FinanceOfficialSalesInvoiceRecordRequestV1 Second(Guid invoice) => Request(invoice) with
    {
        OfficialNo = "SNT2026000000002",
        Ettn = "96000000-0000-4000-8000-000000000006",
        PdfDocumentId = Guid.Parse("97000000-0000-4000-8000-000000000007"),
    };

    private static string Sha(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            await using var context = run.Context();
            await context.Database.MigrateAsync();
            return run;
        }

        public FinanceDbContext Context()
        {
            var companyContext = new CompanyContext();
            companyContext.Set(Company);
            return new FinanceDbContext(Options(Factory), companyContext);
        }

        public Scope Service(string actor, Guid? company = null, Barrier? barrier = null, string? pdfSha = null)
        {
            var companyId = company ?? Company;
            var companyContext = new CompanyContext();
            companyContext.Set(companyId);
            var collector = new AuditEventCollector();
            var builder = new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext));
            if (barrier is not null) builder.AddInterceptors(new BarrierInterceptor(barrier));
            var db = new FinanceDbContext(builder.Options, companyContext);
            var service = new FinanceOfficialSalesInvoiceServiceV1(db, companyContext, new User(actor, companyId),
                new AllowAuthority(), new AcceptedDocuments(pdfSha ?? new string('d', 64)), new Candidates(), collector,
                new FinanceOfficialSalesInvoiceAuditEnvelopesV1(
                    new FinanceAuditEnvelope(FinanceOfficialSalesInvoicePermissionNamesV1.Record,
                        ["SENTETIK-FINANCE"], "Integration"),
                    new FinanceAuditEnvelope(FinanceOfficialSalesInvoicePermissionNamesV1.Approve,
                        ["SENTETIK-FINANCE"], "Integration"),
                    new FinanceAuditEnvelope(FinanceOfficialSalesInvoicePermissionNamesV1.Approve,
                        ["SENTETIK-FINANCE"], "Integration"),
                    new FinanceAuditEnvelope(FinanceOfficialSalesInvoicePermissionNamesV1.Withdraw,
                        ["SENTETIK-FINANCE"], "Integration")),
                TimeProvider.System);
            return new Scope(db, service,
                new FinanceContext(companyId, actor, $"fin-official-sales-{Guid.NewGuid():N}", null));
        }

        /// <summary>The Documents owner read (finance.official-sales-invoice) as the actor in that company.</summary>
        public async Task<ReadOutcome> OwnerReadAsync(string actor, Guid company, Guid invoiceId)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            await using var db = new FinanceDbContext(Options(Factory), companyContext);
            var read = new FinanceOfficialSalesInvoiceOwnerReadV1(db, companyContext, new User(actor, company),
                new AllowAuthority());
            return await read.CheckReadableAsync(
                new FinanceContext(company, actor, $"fin-owner-read-{Guid.NewGuid():N}", null), invoiceId, default);
        }

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
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

        private static DbContextOptions<FinanceDbContext> Options(SqlServerRelationalApiFactory factory) =>
            new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema)).Options;
    }

    private sealed class Scope(FinanceDbContext db, FinanceOfficialSalesInvoiceServiceV1 service, FinanceContext context)
        : IAsyncDisposable
    {
        public FinanceOfficialSalesInvoiceServiceV1 Service => service;
        public FinanceContext Context => context;
        public ValueTask DisposeAsync() => db.DisposeAsync();
    }

    private sealed class User(string actor, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class AllowAuthority : IFinanceOfficialSalesInvoiceAuthorityV1
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context,
            FinanceOfficialSalesInvoiceAuthorityActionV1 action, Guid invoiceId, CancellationToken cancellationToken) =>
            Task.FromResult(PermissionDecision.Allowed);
    }

    private sealed class Candidates : ISettingsMoneyCandidateReader
    {
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MoneyCurrencyCandidateV1>>(
                [new MoneyCurrencyCandidateV1(Currency, "TRY", "SENTETIK Lira")]);
    }

    /// <summary>Holds each save until both writers have reached it, so both read the same state first.</summary>
    private sealed class BarrierInterceptor(Barrier barrier) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(30), cancellationToken);
            return ValueTask.FromResult(result);
        }
    }

    /// <summary>The vault's answer: a clean PDF owned by the record id, with the given content hash.</summary>
    private sealed class AcceptedDocuments(string sha) : IFinanceOfficialSalesInvoiceDocumentPortV1
    {
        public Task<FinanceOfficialSalesInvoicePdfCheckV1> CheckAsync(FinanceContext context, Guid documentId,
            Guid invoiceId, int? versionNo, CancellationToken cancellationToken) =>
            Task.FromResult(new FinanceOfficialSalesInvoicePdfCheckV1(
                FinanceOfficialSalesInvoicePdfOutcomeV1.Accepted, versionNo ?? 1, sha));
    }
}

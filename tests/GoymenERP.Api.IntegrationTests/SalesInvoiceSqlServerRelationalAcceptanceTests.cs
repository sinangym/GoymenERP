using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Sales.Application.Contracts;
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

    private static string Hash(char fill) => new(fill, 64);

    /// <summary>A USD invoice of two 20 % lines (99.99 and 10.01) at 34.5678 — the calculator golden's first group.</summary>
    private static SalesInvoice Invoice(Guid id, string number, string actor, Guid shipment1, Guid shipment2)
    {
        var tax = new SalesLineTaxSnapshot(Product, Day, "Normal", "Steel", "Resolved", null, "Record", Guid.NewGuid(), 1, "Resolved", 20m,
            "NotRequired", null, null, "NotApplied", null, "NotRequired", null, null, null, 0, "Normal", false);
        SalesInvoiceShareInput Share(Guid shipment, int lineNo, string quantity, string price) => new(Guid.NewGuid(), shipment, Guid.NewGuid(),
            "IRS-2026-000001", lineNo, Day, Guid.NewGuid(), Guid.NewGuid(), 0, Product, Kg, decimal.Parse(quantity,
                System.Globalization.CultureInfo.InvariantCulture), quantity, new CommercialDraftFields(price, Kg, null), tax);
        var terms = SalesDueTerms.Create([new SalesDueInstalment(1, SalesDueBasis.OrderDate, 30, "100")]);
        return SalesInvoice.Create(id, CompanyA, number,
            new SalesInvoiceHeader(Guid.NewGuid(), Guid.NewGuid(), Usd, Guid.NewGuid(), "1", 2, Day,
                new SalesInvoiceRateSnapshot(34.5678m, Day, Day.AddDays(-1), Try, 2)),
            [Share(shipment1, 1, "3", "33.33"), Share(shipment2, 2, "1", "10.01")],
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

        public SalesDbContext Context => dbContext;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }
}

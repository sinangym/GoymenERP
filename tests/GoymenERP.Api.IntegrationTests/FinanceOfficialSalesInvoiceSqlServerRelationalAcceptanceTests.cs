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

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Real SQL Server evidence for the F-005 D1 manual official sales invoice record (FNS-D17…FNS-D21). Every row,
/// number, ETTN, PDF pin and permission in this class is SENTETIK test data; this is not a real invoice, grant or UAT.
/// </summary>
public sealed class FinanceOfficialSalesInvoiceSqlServerRelationalAcceptanceTests
{
    private static readonly Guid Company = TestCompanyIds.CompanyA;
    private static readonly Guid Invoice = Guid.Parse("91000000-0000-4000-8000-000000000001");
    private static readonly Guid Customer = Guid.Parse("92000000-0000-4000-8000-000000000002");
    private static readonly Guid Currency = Guid.Parse("93000000-0000-4000-8000-000000000003");
    private static readonly Guid Pdf = Guid.Parse("94000000-0000-4000-8000-000000000004");
    private const string Ettn = "95000000-0000-4000-8000-000000000005";
    private const string PreviousMigration = "20261107154419_AddPaymentAllocations";

    [SqlServerFact]
    public async Task A_different_actor_approves_the_record_and_rows_commands_and_audits_persist_together()
    {
        await using var run = await Run.StartAsync();
        await using (var maker = run.Service("SENTETIK-maker"))
        {
            var recorded = await maker.Service.RecordAsync(maker.Context, new CommandMetadata("record-key"), Request(),
                default);
            Assert.Equal(CommandOutcome.Succeeded, recorded.Outcome);
            Assert.Equal(FinanceOfficialSalesInvoiceStatusV1.PendingApproval, recorded.Resource!.Status);

            var selfApproval = await maker.Service.ApproveAsync(maker.Context, new CommandMetadata("self-approve-key"),
                new FinanceOfficialSalesInvoiceApproveRequestV1(Invoice, "1"), default);
            Assert.Equal(CommandOutcome.ValidationFailed, selfApproval.Outcome);
            Assert.Equal([new ValidationIssue("approver", "approver_must_differ_from_recorder")], selfApproval.Issues);
            Assert.Null(selfApproval.Resource);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OfficialSalesInvoices WHERE Status = 1 AND ApprovedBy IS NULL"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialSalesInvoiceCommands"));

        await using (var checker = run.Service("SENTETIK-checker"))
        {
            var approved = await checker.Service.ApproveAsync(checker.Context, new CommandMetadata("approve-key"),
                new FinanceOfficialSalesInvoiceApproveRequestV1(Invoice, "1"), default);
            Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
            Assert.Equal(FinanceOfficialSalesInvoiceStatusV1.Approved, approved.Resource!.Status);
            Assert.Equal("SENTETIK-checker", approved.Resource.ApprovedBy);
            Assert.Equal("1042.08", approved.Resource.TaxBaseFunctionalAmount);
        }

        Assert.Equal("SENTETIK-maker|SENTETIK-checker|2|1042.08", await run.ScalarAsync<string>(
            "SELECT CONCAT(RecordedBy COLLATE DATABASE_DEFAULT, '|', ApprovedBy COLLATE DATABASE_DEFAULT, '|', "
            + "Status, '|', TaxBaseFunctionalAmountText COLLATE DATABASE_DEFAULT) "
            + "FROM finance.OfficialSalesInvoices WHERE Id = @id", ("@id", Invoice)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialSalesInvoiceCommands"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.OfficialSalesInvoice'"));
    }

    [SqlServerFact]
    public async Task The_database_refuses_an_approval_by_the_recorder_and_a_missing_tax_base()
    {
        await using var run = await Run.StartAsync();
        await using (var maker = run.Service("SENTETIK-maker"))
        {
            Assert.Equal(CommandOutcome.Succeeded, (await maker.Service.RecordAsync(maker.Context,
                new CommandMetadata("record-key"), Request(), default)).Outcome);
        }

        var selfApproval = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE finance.OfficialSalesInvoices SET Status = 2, Version = 2, ApprovedBy = RecordedBy, "
            + "ApprovedAtUtc = SYSUTCDATETIME() WHERE Id = @id", ("@id", Invoice)));
        Assert.Equal(547, selfApproval.Number);
        Assert.Contains("CK_OfficialSalesInvoices_Approval", selfApproval.Message, StringComparison.Ordinal);

        var emptyTaxBase = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE finance.OfficialSalesInvoices SET TaxBaseFunctionalAmountText = '' WHERE Id = @id", ("@id", Invoice)));
        Assert.Equal(547, emptyTaxBase.Number);
        Assert.Contains("CK_OfficialSalesInvoices_Text", emptyTaxBase.Message, StringComparison.Ordinal);

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OfficialSalesInvoices WHERE Status = 1 AND ApprovedBy IS NULL "
            + "AND TaxBaseFunctionalAmountText = '1042.08'"));
    }

    [SqlServerFact]
    public async Task Official_sales_invoice_migration_refuses_a_stored_record_and_rolls_back_when_empty()
    {
        await using var run = await Run.StartAsync();
        await using (var context = run.Context())
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'finance') AND name LIKE N'OfficialSalesInvoice%'"));
            await context.Database.MigrateAsync();
            Assert.Equal(2, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'finance') AND name LIKE N'OfficialSalesInvoice%'"));
        }

        await using (var maker = run.Service("SENTETIK-maker"))
        {
            Assert.Equal(CommandOutcome.Succeeded, (await maker.Service.RecordAsync(maker.Context,
                new CommandMetadata("rollback-guard"), Request(), default)).Outcome);
        }

        await using (var context = run.Context())
        {
            var error = await Assert.ThrowsAsync<SqlException>(() =>
                context.GetService<IMigrator>().MigrateAsync(PreviousMigration));
            Assert.Equal(51051, error.Number);
            Assert.Contains("forward fix", error.Message, StringComparison.Ordinal);
            Assert.EndsWith("_AddOfficialSalesInvoices",
                (await context.Database.GetAppliedMigrationsAsync()).Last(), StringComparison.Ordinal);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialSalesInvoices"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialSalesInvoiceCommands"));
    }

    [SqlServerFact]
    public async Task Two_checkers_racing_to_approve_store_one_approval_and_the_loser_answers_a_concurrent_change()
    {
        await using var run = await Run.StartAsync();
        await RecordAsync(run);

        // Both checkers read the same pending record, then wait for each other at the save.
        using var barrier = new Barrier(2);
        async Task<FinanceOfficialSalesInvoiceResultV1> ApproveAsync(string checker)
        {
            await using var scope = run.Service(checker, barrier: barrier);
            return await scope.Service.ApproveAsync(scope.Context, new CommandMetadata("approve-" + checker),
                new FinanceOfficialSalesInvoiceApproveRequestV1(Invoice, "1"), default);
        }

        var results = await Task.WhenAll(Task.Run(() => ApproveAsync("SENTETIK-checker-1")),
            Task.Run(() => ApproveAsync("SENTETIK-checker-2")));
        var winner = Assert.Single(results, result => result.Outcome == CommandOutcome.Succeeded);
        var loser = Assert.Single(results, result => result.Outcome == CommandOutcome.VersionConflict);
        Assert.Equal([new ValidationIssue("request", "concurrent_change")], loser.Issues);
        Assert.False(loser.IsReplay);
        Assert.Equal(winner.Resource!.ApprovedBy + "|2|2", await run.ScalarAsync<string>(
            "SELECT CONCAT(ApprovedBy COLLATE DATABASE_DEFAULT, '|', Status, '|', Version) "
            + "FROM finance.OfficialSalesInvoices WHERE Id = @id", ("@id", Invoice)));
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

        // The same maker sends the same body with the same key twice; both pass the replay lookup before either saves.
        using var barrier = new Barrier(2);
        async Task<FinanceOfficialSalesInvoiceResultV1> RecordWithTheSameKeyAsync()
        {
            await using var scope = run.Service("SENTETIK-maker", barrier: barrier);
            return await scope.Service.RecordAsync(scope.Context, new CommandMetadata("record-key"), Request(),
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
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialSalesInvoiceCommands"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.official-sales-invoice.recorded.v1'"));
    }

    [SqlServerFact]
    public async Task Another_company_neither_reads_nor_approves_the_record_and_its_refusal_is_stored_under_its_own_company()
    {
        await using var run = await Run.StartAsync();
        await RecordAsync(run);

        await using (var foreign = run.Service("SENTETIK-checker", TestCompanyIds.CompanyB))
        {
            Assert.Equal(ReadOutcome.NotFound, (await foreign.Service.GetAsync(foreign.Context, Invoice, default)).Outcome);
            var page = await foreign.Service.ListAsync(foreign.Context, pendingOnly: false, cursor: null, default);
            Assert.Equal(ReadOutcome.Found, page.Outcome);
            Assert.Empty(page.Resource!.Items);

            var approve = await foreign.Service.ApproveAsync(foreign.Context, new CommandMetadata("foreign-approve"),
                new FinanceOfficialSalesInvoiceApproveRequestV1(Invoice, "1"), default);
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

    private static async Task RecordAsync(Run run)
    {
        await using var maker = run.Service("SENTETIK-maker");
        Assert.Equal(CommandOutcome.Succeeded, (await maker.Service.RecordAsync(maker.Context,
            new CommandMetadata("record-key"), Request(), default)).Outcome);
    }

    private static FinanceOfficialSalesInvoiceRecordRequestV1 Request() => new(Invoice, "SNT2026000000001", Ettn,
        Customer, Currency, "1250.50", "1250.50", "1042.08", "208.42", "0", Pdf);

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

        public Scope Service(string actor, Guid? company = null, Barrier? barrier = null)
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
                new AllowAuthority(), new AcceptedDocuments(), new Candidates(), collector,
                new FinanceOfficialSalesInvoiceAuditEnvelopesV1(
                    new FinanceAuditEnvelope(FinanceOfficialSalesInvoicePermissionNamesV1.Record,
                        ["SENTETIK-FINANCE"], "Integration"),
                    new FinanceAuditEnvelope(FinanceOfficialSalesInvoicePermissionNamesV1.Approve,
                        ["SENTETIK-FINANCE"], "Integration")),
                TimeProvider.System);
            return new Scope(db, service,
                new FinanceContext(companyId, actor, $"fin-official-sales-{Guid.NewGuid():N}", null));
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

    private sealed class AcceptedDocuments : IFinanceOfficialSalesInvoiceDocumentPortV1
    {
        public Task<FinanceOfficialSalesInvoicePdfCheckV1> CheckAsync(FinanceContext context, Guid documentId,
            Guid customerPartyId, int? versionNo, CancellationToken cancellationToken) =>
            Task.FromResult(new FinanceOfficialSalesInvoicePdfCheckV1(
                FinanceOfficialSalesInvoicePdfOutcomeV1.Accepted, versionNo ?? 1, new string('d', 64)));
    }
}

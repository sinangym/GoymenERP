using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F2a / MOD-04: a finalised invoice admitted as Finance open items through the real
/// <see cref="FinanceInvoiceOpenItemAdmissionService"/> against a REAL SQL Server. A sales invoice becomes receivables, a
/// purchase invoice payables (MOD-15); a foreign-currency invoice stores its rate and each installment's functional
/// amount exactly; the same admission replays, the same invoice with the same rows is a reuse, with other rows a refusal;
/// the exchange columns are all-or-none in SQL and the migration's Down refuses while exchange data is stored. The
/// party port is a SENTETIK test input.
/// </summary>
public sealed class FinanceInvoiceOpenItemAdmissionSqlServerRelationalAcceptanceTests
{
    private const string Actor = "SENTETIK-invoice-finaliser";
    private const string BeforeExchange = "20261102134844_AddOpenItemDirection";
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Lira = Guid.Parse("5a2f0e61-7c3b-4d8e-9f10-2b3c4d5e6f01");
    private static readonly Guid Dollar = Guid.Parse("5a2f0e61-7c3b-4d8e-9f10-2b3c4d5e6f02");
    private static readonly Guid Party = Guid.Parse("5a2f0e61-7c3b-4d8e-9f10-2b3c4d5e6f03");
    private static readonly Guid Account = Guid.Parse("5a2f0e61-7c3b-4d8e-9f10-2b3c4d5e6f04");

    [SqlServerFact]
    public async Task A_sales_invoice_becomes_receivables_and_the_same_admission_replays_once()
    {
        await using var run = await Run.StartAsync();
        var invoice = Invoice(FinanceInvoiceProducerV1.Sales, Lira, null,
            Installment(1, "600.00"), Installment(2, "400.00"));

        var first = await run.AdmitAsync(CompanyA, "sales-invoice-1", invoice);
        Assert.Equal(CommandOutcome.Succeeded, first.Outcome);
        Assert.False(first.IsReplay);
        Assert.Equal(invoice.Installments.Select(installment => installment.OpenItemId), first.OpenItemIds);
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE SourceId = @id AND Direction = 1 AND SourceKind = @kind "
            + "AND ExchangeRateText IS NULL AND FunctionalAmountText IS NULL AND SourceBatchId = @batch",
            ("@id", invoice.InvoiceId), ("@kind", FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment),
            ("@batch", invoice.AdmissionId)));
        Assert.Equal("600.00", await run.ScalarAsync<string>(
            "SELECT OutstandingAmountText FROM finance.OpenItems WHERE Id = @id", ("@id", invoice.Installments[0].OpenItemId)));

        var replay = await run.AdmitAsync(CompanyA, "sales-invoice-1", invoice);
        Assert.Equal(CommandOutcome.Succeeded, replay.Outcome);
        Assert.True(replay.IsReplay);
        Assert.Equal(first.OpenItemIds, replay.OpenItemIds);

        var conflict = await run.AdmitAsync(CompanyA, "sales-invoice-1",
            invoice with { Installments = [Installment(1, "1000.00")] });
        Assert.Equal(CommandOutcome.IdempotencyConflict, conflict.Outcome);

        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE SourceId = @id", ("@id", invoice.InvoiceId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItemSourceBatches WHERE Id = @id", ("@id", invoice.AdmissionId)));
    }

    [SqlServerFact]
    public async Task A_foreign_currency_purchase_invoice_becomes_payables_with_its_rate_and_is_reused_never_replaced()
    {
        await using var run = await Run.StartAsync();
        var invoice = Invoice(FinanceInvoiceProducerV1.Purchasing, Dollar, "38.4512",
            Installment(1, "100.00", "3845.12"), Installment(2, "50.50", "1941.79"));

        var first = await run.AdmitAsync(CompanyA, "purchase-invoice-1", invoice);
        Assert.Equal(CommandOutcome.Succeeded, first.Outcome);
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE SourceId = @id AND Direction = 2 AND SourceKind = @kind "
            + "AND ExchangeRateText = '38.4512'",
            ("@id", invoice.InvoiceId), ("@kind", FinanceOpenItemSourceKinds.ApprovedOfficialIncomingInvoiceInstallment)));
        Assert.Equal("1941.79", await run.ScalarAsync<string>(
            "SELECT FunctionalAmountText FROM finance.OpenItems WHERE Id = @id", ("@id", invoice.Installments[1].OpenItemId)));

        // The producer retries with a new admission (new key and id) and the same rows: no second open item.
        var again = invoice with
        {
            AdmissionId = Guid.NewGuid(),
            Installments = [.. invoice.Installments.Select(installment => installment with { OpenItemId = Guid.NewGuid() })],
        };
        var reused = await run.AdmitAsync(CompanyA, "purchase-invoice-2", again);
        Assert.Equal(CommandOutcome.Succeeded, reused.Outcome);
        // Like F-03's source admission, a reuse of stored identities answers as a replay.
        Assert.True(reused.IsReplay);
        Assert.Equal(first.OpenItemIds, reused.OpenItemIds);

        // A changed invoice is never silently replaced (SAT-D96: cancel and re-issue, F2b).
        var changed = again with
        {
            AdmissionId = Guid.NewGuid(),
            Installments = [again.Installments[0] with { Amount = "150.50", FunctionalAmount = "5786.91" }],
        };
        var refused = await run.AdmitAsync(CompanyA, "purchase-invoice-3", changed);
        Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
        Assert.Contains(refused.Issues, issue => issue.Code == FinanceInvoiceOpenItemAdmissionService.AlreadyAdmittedCode);
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE SourceId = @id", ("@id", invoice.InvoiceId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItemSourceBatches WHERE Id = @id", ("@id", changed.AdmissionId)));
    }

    [SqlServerFact]
    public async Task Another_company_context_and_an_ineligible_party_admit_nothing()
    {
        await using var run = await Run.StartAsync();
        var invoice = Invoice(FinanceInvoiceProducerV1.Sales, Lira, null, Installment(1, "10.00"));

        // The caller names company B while the server context and the user belong to company A.
        var denied = await run.AdmitAsync(CompanyA, "cross-company", invoice, contextCompany: CompanyB);
        Assert.Equal(CommandOutcome.Denied, denied.Outcome);

        var unknownParty = await run.AdmitAsync(CompanyA, "unknown-party", invoice with { PartyId = Guid.NewGuid() });
        Assert.Equal(CommandOutcome.ValidationFailed, unknownParty.Outcome);
        Assert.Contains(unknownParty.Issues, issue => issue.Code == "party_account_not_found");

        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE SourceId = @id", ("@id", invoice.InvoiceId)));
    }

    [SqlServerFact]
    public async Task The_exchange_columns_are_all_or_none_and_the_rollback_refuses_while_exchange_data_is_stored()
    {
        await using var run = await Run.StartAsync();
        var lira = Invoice(FinanceInvoiceProducerV1.Sales, Lira, null, Installment(1, "10.00"));
        Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync(CompanyA, "rollback-lira", lira)).Outcome);

        var half = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE finance.OpenItems SET ExchangeRateText = '1.5' WHERE Id = @id; SELECT @@ROWCOUNT",
            ("@id", lira.Installments[0].OpenItemId)));
        Assert.Contains("CK_OpenItems_Exchange", half.Message, StringComparison.Ordinal);

        // With only TL rows stored, the Down runs and the reapply restores the columns.
        const string ExchangeColumns = "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'finance.OpenItems') "
            + "AND name IN (N'ExchangeRateText', N'FunctionalAmountText')";
        await run.MigrateFinanceAsync(BeforeExchange);
        Assert.Equal(0, await run.ScalarAsync<int>(ExchangeColumns));
        await run.MigrateFinanceAsync(null);
        Assert.Equal(2, await run.ScalarAsync<int>(ExchangeColumns));

        var dollar = Invoice(FinanceInvoiceProducerV1.Purchasing, Dollar, "38.45", Installment(1, "10.00", "384.50"));
        Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync(CompanyA, "rollback-dollar", dollar)).Outcome);
        var refused = await Assert.ThrowsAsync<SqlException>(() => run.MigrateFinanceAsync(BeforeExchange));
        Assert.Equal(51051, refused.Number);
        Assert.Equal(2, await run.ScalarAsync<int>(ExchangeColumns));
        Assert.Equal("384.50", await run.ScalarAsync<string>(
            "SELECT FunctionalAmountText FROM finance.OpenItems WHERE Id = @id", ("@id", dollar.Installments[0].OpenItemId)));
    }

    [SqlServerFact]
    public async Task Enlisted_in_the_producers_transaction_the_open_items_commit_or_roll_back_with_it()
    {
        await using var run = await Run.StartAsync();
        var invoice = Invoice(FinanceInvoiceProducerV1.Purchasing, Lira, null, Installment(1, "250.00"));

        // ALS-D62: the producer rolls back its finalisation → Finance staged nothing that survives.
        var rolledBack = await run.EnlistedAdmitAsync(CompanyA, "enlisted-1", invoice, commit: false);
        Assert.Equal(CommandOutcome.Succeeded, rolledBack.Outcome);
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE SourceId = @id", ("@id", invoice.InvoiceId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItemSourceBatches WHERE Id = @id", ("@id", invoice.AdmissionId)));

        // The retry with the same key commits with the producer's transaction: one payable, one receipt.
        var committed = await run.EnlistedAdmitAsync(CompanyA, "enlisted-1", invoice, commit: true);
        Assert.Equal(CommandOutcome.Succeeded, committed.Outcome);
        Assert.False(committed.IsReplay);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE SourceId = @id AND Direction = 2", ("@id", invoice.InvoiceId)));

        // A later retry of the same admission replays without a second row.
        var replay = await run.EnlistedAdmitAsync(CompanyA, "enlisted-1", invoice, commit: true);
        Assert.True(replay.IsReplay);
        Assert.Equal(committed.OpenItemIds, replay.OpenItemIds);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE SourceId = @id", ("@id", invoice.InvoiceId)));
    }

    private static FinanceInvoiceOpenItemAdmissionV1 Invoice(FinanceInvoiceProducerV1 producer, Guid currency,
        string? rate, params FinanceInvoiceInstallmentV1[] installments) =>
        new(Guid.NewGuid(), producer, Guid.NewGuid(), "invoice-v1", Party, Account, currency, rate, installments);

    private static FinanceInvoiceInstallmentV1 Installment(int sequence, string amount, string? functional = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), sequence, new DateOnly(2026, 12, 1).AddMonths(sequence - 1), amount, functional);

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
            await using var context = run.Context(CompanyA, null);
            await context.Database.MigrateAsync();
            return run;
        }

        public async Task<FinanceOpenItemAdmissionResult> AdmitAsync(Guid company, string key,
            FinanceInvoiceOpenItemAdmissionV1 invoice, Guid? contextCompany = null)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            await using var dbContext = new FinanceDbContext(Options(companyContext, collector), companyContext);
            var service = new FinanceInvoiceOpenItemAdmissionService(dbContext, companyContext,
                new FixedCurrentUser(Actor, company), new SyntheticParty(company), collector,
                new FinanceAuditEnvelope("synthetic.finance.invoice-open-items.admit", ["SENTETIK-FINANCE"], "Web"),
                TimeProvider.System);
            return await service.AdmitAsync(
                new FinanceContext(contextCompany ?? company, Actor, $"f2a-{Guid.NewGuid():N}", null),
                new CommandMetadata($"f2a-{key}"), invoice, CancellationToken.None);
        }

        /// <summary>A producer host: opens its own transaction, enlists Finance, stages, then commits or rolls back.</summary>
        public async Task<FinanceOpenItemAdmissionResult> EnlistedAdmitAsync(Guid company, string key,
            FinanceInvoiceOpenItemAdmissionV1 invoice, bool commit)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            FinanceOpenItemAdmissionResult result;
            await using (var finance = await FinanceInvoiceOpenItemAdmissionParticipant.EnlistAsync(connection,
                transaction, company, new FixedCurrentUser(Actor, company), new SyntheticParty(company),
                new FinanceAuditEnvelope("synthetic.finance.invoice-open-items.admit", ["SENTETIK-FINANCE"], "Web")))
            {
                result = await finance.StageAsync(new FinanceContext(company, Actor, $"f2a-{Guid.NewGuid():N}", null),
                    new CommandMetadata($"f2a-{key}"), invoice, CancellationToken.None);
                await Assert.ThrowsAsync<InvalidOperationException>(() => finance.StageAsync(
                    new FinanceContext(company, Actor, "second", null), new CommandMetadata($"f2a-{key}"), invoice,
                    CancellationToken.None));
            }

            if (commit)
            {
                await transaction.CommitAsync();
            }
            else
            {
                await transaction.RollbackAsync();
            }

            return result;
        }

        /// <summary>Moves Finance's schema to <paramref name="target"/> (null: the latest migration).</summary>
        public async Task MigrateFinanceAsync(string? target)
        {
            await using var context = Context(CompanyA, null);
            await context.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync(target);
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

        private FinanceDbContext Context(Guid company, AuditEventCollector? collector)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new FinanceDbContext(Options(companyContext, collector), companyContext);
        }

        private DbContextOptions<FinanceDbContext> Options(CompanyContext companyContext, AuditEventCollector? collector)
        {
            var builder = new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema));
            if (collector is not null)
            {
                builder.AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext));
            }

            return builder.Options;
        }
    }

    /// <summary>SENTETIK: one active party account (Party, Account) in every company.</summary>
    private sealed class SyntheticParty(Guid company) : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId,
            Guid companyAccountId, CancellationToken cancellationToken) =>
            Task.FromResult(partyId == Party && companyAccountId == Account && context.CompanyId == company
                ? new ReadResult<PartyAccountEligibility>(ReadOutcome.Found,
                    new PartyAccountEligibility(Party, Account, company, true, IsPartyActive: true))
                : new ReadResult<PartyAccountEligibility>(ReadOutcome.NotFound, null));
    }

    private sealed class FixedCurrentUser(string userId, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}

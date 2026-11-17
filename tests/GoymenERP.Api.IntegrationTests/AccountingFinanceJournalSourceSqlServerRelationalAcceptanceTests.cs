using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Real SQL received-source dedup, tenant filter, immutable row and audit. Binding catalog is explicitly synthetic.</summary>
public sealed class AccountingFinanceJournalSourceSqlServerRelationalAcceptanceTests
{
    private static readonly Guid Currency = Guid.Parse("b2891000-0000-4000-8000-000000000001");
    private static readonly DateTimeOffset At = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
    [SqlServerFact]
    public async Task One_recorded_finance_source_is_received_once_with_exact_money_audit_and_no_journal()
    {
        await using var run = await Run.Start(); var source = Source(); var envelope = Envelope(source);
        await using (var scope = run.Open())
        {
            await scope.Store.AcceptAsync(envelope, default); await scope.Store.AcceptAsync(envelope, default);
            var row = Assert.Single(await scope.Store.ListAsync(default));
            Assert.Equal(("100.01", Currency, "Received", "BindingUnavailable"), (row.Amount, row.CurrencyId, row.Status, row.MappingStatus));
            Assert.Null(row.PreparationVersion);
        }
        Assert.Equal(1, await run.Count("accounting.FinanceJournalSources"));
        Assert.Equal(0, await run.Count("accounting.Journals")); Assert.Equal(0, await run.Count("accounting.NormalJournalDrafts"));
        Assert.Equal(1, await run.Scalar<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Accounting.FinanceJournalSource'"));
        await using var mapped = run.Open(binding: source);
        var available = Assert.Single(await mapped.Store.ListAsync(default));
        Assert.Equal("BindingAvailable", available.MappingStatus); Assert.Equal(64, available.PreparationVersion!.Length);
        Assert.Equal("Received", available.Status); // Even a usable mapping is not a posted or prepared journal.
    }
    [SqlServerFact]
    public async Task Changed_event_or_duplicate_source_and_foreign_company_cannot_rewrite_or_repeat_the_financial_fact()
    {
        await using var run = await Run.Start(); var source = Source(); var envelope = Envelope(source);
        await using (var scope = run.Open())
        {
            await scope.Store.AcceptAsync(envelope, default);
            var changed = source with { Preparation = source.Preparation with { Money = new("200.02", Currency) } };
            await Assert.ThrowsAsync<OutboxPermanentFailureException>(() => scope.Store.AcceptAsync(Envelope(changed, envelope.EventId), default));
            await Assert.ThrowsAsync<OutboxPermanentFailureException>(() => scope.Store.AcceptAsync(Envelope(source), default));
        }
        await using (var foreign = run.Open(TestCompanyIds.CompanyB))
        {
            Assert.Empty(await foreign.Store.ListAsync(default));
            await Assert.ThrowsAsync<OutboxPermanentFailureException>(() => foreign.Store.AcceptAsync(envelope, default));
        }
        await using (var immutable = run.Open())
        {
            var row = await immutable.Db.FinanceJournalSources.SingleAsync(); row.AmountText = "200.02";
            await Assert.ThrowsAsync<InvalidOperationException>(() => immutable.Db.SaveChangesAsync());
        }
        Assert.Equal("100.01", await run.Scalar<string>("SELECT AmountText FROM accounting.FinanceJournalSources"));
        Assert.Equal(1, await run.Count("accounting.FinanceJournalSources"));
    }
    [SqlServerFact]
    public async Task Source_and_audit_rollback_together_when_durable_source_insert_fails()
    {
        await using var run = await Run.Start();
        // A fixed source-owned test constraint forces the failure after staging, without changing source policy.
        await run.Scalar<int>("ALTER TABLE accounting.FinanceJournalSources ADD CONSTRAINT CK_TestFinanceSourceFailure CHECK (AmountText <> '100.01'); SELECT 1;");
        await using (var scope = run.Open())
            await Assert.ThrowsAsync<DbUpdateException>(() => scope.Store.AcceptAsync(Envelope(Source()), default));
        Assert.Equal(0, await run.Count("accounting.FinanceJournalSources"));
        Assert.Equal(0, await run.Scalar<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Accounting.FinanceJournalSource'"));
    }
    [SqlServerFact]
    public async Task Received_source_Down_passes_when_empty_and_refuses_loss_while_a_financial_source_exists()
    {
        await using var run = await Run.Start();
        var guard = Assert.IsType<SqlOperation>(new GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations.AddFinanceJournalSources().DownOperations[0]);
        Assert.Equal(1, await run.Scalar<int>(guard.Sql + "\nSELECT 1;"));
        await using (var scope = run.Open()) await scope.Store.AcceptAsync(Envelope(Source()), default);
        Assert.Equal(51989, (await Assert.ThrowsAsync<SqlException>(() => run.Scalar<int>(guard.Sql + "\nSELECT 1;"))).Number);
        Assert.Equal(1, await run.Count("accounting.FinanceJournalSources"));
    }
    private static ExpensePaymentView Source() => new(Guid.NewGuid(), TestCompanyIds.CompanyA, ExpensePaymentStatus.Recorded,
        new(ExpensePaymentSourceKind.Bank, Guid.NewGuid(), Guid.NewGuid(), new("100.01", Currency), new("SENTETIK-profile", "1"),
            new(2026, 11, 17), null, null, "SENTETIK expense"), "SENTETIK-maker", At.AddHours(-2), "SENTETIK-checker",
        At.AddHours(-1), "SENTETIK-recorder", At, true, "3");
    private static WorkInboxEventV1 Envelope(ExpensePaymentView source, Guid? eventId = null)
    {
        var id = eventId ?? Guid.NewGuid();
        return new(FinanceJournalSourceInboxV1.SourceName, id, source.CompanyId, NormalJournalSourceKindsV1.RecordedExpensePayment, 1,
            JsonSerializer.Serialize(new FinanceExpensePaymentRecordedV1(id, source), FinanceJson.CreateOptions()), At, "SENTETIK-finance-source", null);
    }
    private sealed class Run(string baseline, SqlServerRelationalApiFactory factory) : IAsyncDisposable
    {
        public static async Task<Run> Start()
        {
            var baseline = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var run = new Run(baseline, new(baseline));
            try
            {
                using var client = run.Factory.CreateClient(); using var health = await client.GetAsync("/health/live"); health.EnsureSuccessStatusCode();
                await using var scope = run.Open(); await scope.Db.Database.MigrateAsync(); return run;
            }
            catch { await run.DisposeAsync(); throw; }
        }
        private SqlServerRelationalApiFactory Factory => factory;
        public Scope Open(Guid? company = null, ExpensePaymentView? binding = null) => new(factory.DatabaseConnectionString, company ?? TestCompanyIds.CompanyA, binding);
        public Task<int> Count(string table) => Scalar<int>("SELECT COUNT(*) FROM " + table); // Fixed source-owned test table names only.
        public async Task<T> Scalar<T>(string sql)
        {
            await using var connection = new SqlConnection(factory.DatabaseConnectionString); await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection); return (T)(await command.ExecuteScalarAsync())!;
        }
        public async ValueTask DisposeAsync()
        {
            await factory.DisposeAsync(); SqlConnection.ClearAllPools(); await using var connection = new SqlConnection(baseline); await connection.OpenAsync();
            await using var command = connection.CreateCommand(); command.CommandText = $"IF DB_ID(N'{factory.DatabaseName}') IS NOT NULL BEGIN ALTER DATABASE [{factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{factory.DatabaseName}]; END;";
            await command.ExecuteNonQueryAsync();
        }
    }
    private sealed class Scope : IAsyncDisposable
    {
        public AccountingDbContext Db { get; }
        public FinanceJournalSourceStoreV1 Store { get; }
        public Scope(string connection, Guid id, ExpensePaymentView? binding)
        {
            var company = new CompanyContext(); company.Set(id); var audit = new AuditEventCollector();
            Db = new(new DbContextOptionsBuilder<AccountingDbContext>().UseSqlServer(connection,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AccountingDbContext.Schema)).AddInterceptors(new AuditSaveChangesInterceptor(audit, company)).Options, company);
            var mappings = new ApprovedNormalJournalSourceBindingCatalogV1(binding is null ? [] : [Binding(binding)]);
            Store = new(Db, company, new Kind(binding?.Id), mappings, audit, TimeProvider.System);
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class Kind(Guid? source) : IApprovedNormalJournalSourceKindCatalogV1
    {
        public Task<string?> ResolveKindAsync(Guid companyId, Guid sourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(sourceId == source ? NormalJournalSourceKindsV1.RecordedExpensePayment : null);
    }
    private static ApprovedNormalJournalSourceBindingV1 Binding(ExpensePaymentView source)
    {
        var debit = new AccountRef(source.CompanyId, Guid.NewGuid()); var credit = new AccountRef(source.CompanyId, Guid.NewGuid()); var actor = Guid.NewGuid();
        var mirror = new AccountPlanMirrorV1(source.CompanyId, "SENTETIK", new string('a', 64), actor, At,
            [new(debit, "SENTETIK-1", "SENTETIK debit"), new(credit, "SENTETIK-2", "SENTETIK credit")]);
        var rule = new NormalJournalPostingRuleV1(mirror, "SENTETIK-purpose", "1", actor, At,
            [new("SENTETIK-debit", PostingSide.Debit, debit), new("SENTETIK-credit", PostingSide.Credit, credit)]);
        return new(source.CompanyId, source.Id, NormalJournalSourceKindsV1.RecordedExpensePayment, source.Version,
            source.Preparation.Profile.ProfileId, source.Preparation.Profile.Version, Currency, rule);
    }
}

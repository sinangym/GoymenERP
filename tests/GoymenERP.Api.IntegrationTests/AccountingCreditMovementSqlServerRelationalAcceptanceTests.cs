using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Real SQL store, constraints and audit; Settings mapping response is explicitly synthetic and missing.</summary>
public sealed partial class AccountingCreditMovementSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Actual_Finance_event_creates_one_waiting_credit_proposal_with_null_TL_and_no_journal_and_Down_refuses_loss()
    {
        await using var run = await Run.Start();
        var source = Source(); var envelope = Envelope(source);
        await using (var scope = run.Open())
        {
            await scope.Store.AcceptAsync(envelope, default);
            await scope.Store.AcceptAsync(envelope, default);
            var row = Assert.Single(await scope.Store.ListAsync(default));
            Assert.Equal("MappingMissing", row.MappingStatus);
            Assert.Contains("credit_functional_source_amount_missing", row.WaitingReasons);
            Assert.Null(row.Source.Components[0].FunctionalAmount);
            Assert.Equal("100.00", row.Source.Components[0].Amount);
            Assert.Equal("98.50", row.Source.BankEffect!.Amount);
        }
        Assert.Equal(1, await run.Count("accounting.CreditMovementProposals"));
        Assert.Equal(0, await run.Count("accounting.Journals"));
        Assert.Equal(0, await run.Count("accounting.NormalJournalDrafts"));
        Assert.Equal(1, await run.Scalar<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Accounting.CreditMovementProposal'"));
        var guard = Assert.IsType<SqlOperation>(new GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations.AddCreditMovementProposals().DownOperations[0]);
        Assert.Equal(51971, (await Assert.ThrowsAsync<SqlException>(() => run.Scalar<int>(guard.Sql + "\nSELECT 1;"))).Number);
    }
    [SqlServerFact]
    public async Task Credit_event_identity_conflict_and_foreign_company_cannot_replace_or_duplicate_the_accepted_source()
    {
        await using var run = await Run.Start();
        var source = Source(); var envelope = Envelope(source);
        await using (var scope = run.Open())
        {
            await scope.Store.AcceptAsync(envelope, default);
            await Assert.ThrowsAsync<OutboxPermanentFailureException>(() => scope.Store.AcceptAsync(Envelope(source with
                { Components = [new("principal", "101.00", null)] }), default));
            await Assert.ThrowsAsync<OutboxPermanentFailureException>(() => scope.Store.AcceptAsync(Envelope(source with { EventId = Guid.NewGuid() }), default));
        }
        await using (var foreign = run.Open(TestCompanyIds.CompanyB))
        {
            Assert.Empty(await foreign.Store.ListAsync(default));
            await Assert.ThrowsAsync<OutboxPermanentFailureException>(() => foreign.Store.AcceptAsync(envelope, default));
        }
        Assert.Equal(1, await run.Count("accounting.CreditMovementProposals"));
    }
    private static FinanceCreditMovementRecordedV1 Source() => new(Guid.NewGuid(), TestCompanyIds.CompanyA, Guid.NewGuid(), Guid.NewGuid(),
        Guid.Parse("c0711000-0000-4000-8000-000000000001"), CreditMovementKindV1.Drawdown, new("SENTETIK bank source", new(2026, 11, 17), "1"),
        new(2026, 11, 17), new(2026, 11, 17), [new("principal", "100.00", null)], new(Guid.NewGuid(), "98.50", "SENTETIK actual bank"), null, "SENTETIK checker", "SENTETIK source document");
    private static WorkInboxEventV1 Envelope(FinanceCreditMovementRecordedV1 source) => new(FinanceCreditMovementEventsV1.SourceName, source.EventId,
        source.CompanyId, FinanceCreditMovementEventsV1.Recorded, 1, JsonSerializer.Serialize(source, FinanceJson.CreateOptions()), new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero), "SENTETIK-credit", null);
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
        public Scope Open(Guid? company = null) => new(factory.DatabaseConnectionString, company ?? TestCompanyIds.CompanyA);
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
        public CreditMovementProposalStoreV1 Store { get; }
        public CompanyContext Company { get; }
        public AuditEventCollector Audit { get; }
        public Scope(string connection, Guid id)
        {
            var company = new CompanyContext(); company.Set(id); var audit = new AuditEventCollector();
            Company = company; Audit = audit;
            Db = new(new DbContextOptionsBuilder<AccountingDbContext>().UseSqlServer(connection,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AccountingDbContext.Schema)).AddInterceptors(new AuditSaveChangesInterceptor(audit, company)).Options, company);
            Store = new(Db, company, new MissingMappings(), audit, TimeProvider.System);
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class MissingMappings : ISettingsFinanceCreditAccountMappingProviderV1
    {
        public Task<SettingsFinanceCreditAccountMappingV1> GetCreditAccountMappingAsync(Guid companyId, FinanceCreditAccountPurposeV1 purpose, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SettingsFinanceCreditAccountMappingV1(companyId, purpose, FinanceCreditAccountMappingStatusV1.MappingMissing, null, 0));
    }
}

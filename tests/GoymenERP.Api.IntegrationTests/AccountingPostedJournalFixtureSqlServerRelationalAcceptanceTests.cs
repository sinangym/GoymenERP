using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Fixtures;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Settings.Application;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// The SENTETIK acc01 posted-journal fixture (NOT UAT) over real SQL Server: on a fresh database it stores the fixed
/// fin01 Open period and exactly ONE journal through Accounting's own posting path (two balanced lines, one delivery,
/// its audit event); a second run is a replay that writes nothing; another bound company is refused with nothing
/// written. The fixture stays behind the browser gate — this test builds it directly, as the gated host would.
/// </summary>
public sealed class AccountingPostedJournalFixtureSqlServerRelationalAcceptanceTests
{
    private static readonly Guid Gdc = AccountingPostedJournalFixture.CompanyId;
    private static readonly Guid Gsp = TestCompanyIds.CompanyB;

    [SqlServerFact]
    public async Task One_fixed_journal_is_posted_once_through_the_posting_path_and_a_rerun_writes_nothing()
    {
        await using var run = await Run.StartAsync();

        await using (var first = run.Fixture(new CompanyContext()))
        {
            await first.Store.EnsureAsync(CancellationToken.None);
        }

        var journalId = AccountingPostedJournalFixture.JournalId;
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting.Periods WHERE Id = @p AND CompanyId = @c AND Status = 1",
            ("@p", AccountingPostedJournalFixture.PeriodId), ("@c", Gdc)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting.Journals "
            + "WHERE Id = @j AND CompanyId = @c AND Kind = 1 AND PeriodId = @p AND SourceId = @s AND LinkedJournalId IS NULL",
            ("@j", journalId), ("@c", Gdc), ("@p", AccountingPostedJournalFixture.PeriodId),
            ("@s", AccountingPostedJournalFixture.SourceId)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting.JournalLines WHERE JournalId = @j AND CompanyId = @c",
            ("@j", journalId), ("@c", Gdc)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting.Deliveries WHERE CompanyId = @c AND Consumer = @k AND MessageId = @m AND JournalId = @j",
            ("@c", Gdc), ("@k", AccountingPostedJournalFixture.Consumer), ("@m", AccountingPostedJournalFixture.MessageId),
            ("@j", journalId)));
        var journalAudit = await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId = @c AND EntityType = N'Accounting.Journal' AND EntityId = @j",
            ("@c", Gdc), ("@j", journalId.ToString("D")));
        Assert.True(journalAudit >= 1);
        var before = await run.CountsAsync();

        // A re-run (new scope, as a second seed invocation) is the stored delivery's replay: nothing new is written.
        await using (var second = run.Fixture(new CompanyContext()))
        {
            await second.Store.EnsureAsync(CancellationToken.None);
        }

        Assert.Equal(before, await run.CountsAsync());
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Journals WHERE CompanyId = @c", ("@c", Gdc)));
    }

    [SqlServerFact]
    public async Task Another_bound_company_is_refused_and_nothing_is_written()
    {
        await using var run = await Run.StartAsync();
        var before = await run.CountsAsync();
        var gsp = new CompanyContext();
        gsp.Set(Gsp);

        await using (var scope = run.Fixture(gsp))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Store.EnsureAsync(CancellationToken.None));
        }

        Assert.Equal(before, await run.CountsAsync());
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting.Journals WHERE Id = @j", ("@j", AccountingPostedJournalFixture.JournalId)));
    }

    private sealed record Counts(int Periods, int Journals, int Lines, int Deliveries, int Audit);

    private sealed class FixtureScope(AccountingDbContext dbContext, IAccountingPostedJournalFixtureStore store) : IAsyncDisposable
    {
        public IAccountingPostedJournalFixtureStore Store => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    /// <summary>The fixture's writer as Settings sees it: authenticated, with access to GDC only.</summary>
    private sealed class FixtureUser : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public string? UserId => AccountingPostedJournalFixture.FixtureActorId.ToString("D");

        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Gdc };

        public bool CanAccessCompany(Guid companyId) => companyId == Gdc;
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

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            var company = new CompanyContext();
            company.Set(Gdc);
            await using var context = new AccountingDbContext(Options(factory).Options, company);
            await context.Database.MigrateAsync();
            return run;
        }

        /// <summary>Composes the fixture exactly as the gated host does: one scoped context shared by the stores.</summary>
        public FixtureScope Fixture(CompanyContext companyContext)
        {
            var collector = new AuditEventCollector();
            var dbContext = new AccountingDbContext(Options(Factory)
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext))
                .Options, companyContext);
            var settings = new ApprovedSettingsPrerequisiteProvider(companyContext, new FixtureUser());
            var postings = new AccountingStore(dbContext, companyContext, collector, TimeProvider.System,
                new AccountingAmountScaleVerifier(settings));
            var store = new AccountingPostedJournalFixtureStore(dbContext, companyContext,
                new AccountingOpenPeriodFixtureStore(dbContext, companyContext), postings);
            return new FixtureScope(dbContext, store);
        }

        public async Task<Counts> CountsAsync() => new(
            await ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Periods"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Journals"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM accounting.JournalLines"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Deliveries"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType LIKE N'Accounting.%'"));

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

        private static DbContextOptionsBuilder<AccountingDbContext> Options(SqlServerRelationalApiFactory factory) =>
            new DbContextOptionsBuilder<AccountingDbContext>()
                .UseSqlServer(factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AccountingDbContext.Schema));
    }
}

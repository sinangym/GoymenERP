using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Fixtures;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Real SQL Server evidence for the gated Sales fixture store (browser suite s04, decision W1/C): the seed
/// writes exactly the rows the store writes, once, through the domain, with no attempt and no audit; the real
/// store restores it and moves it along the approval axis as a real command; a re-run is a no-op whatever
/// state the axis has reached; a different draft under the same id, an unlabelled draft or another company's
/// context is refused with nothing written.
/// </summary>
public sealed class SalesDraftFixtureStoreSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid DraftId = Guid.Parse("d1000000-0000-4000-8000-0000000000a1");
    private static readonly DateOnly Day = new(2026, 9, 18);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset T0 = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);
    private const string Label = "SENTETIK browser-suite:s04";
    private static readonly SalesDraftStateAudit Submitted = new("sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");

    [SqlServerFact]
    public async Task A_fixture_draft_is_written_once_through_the_domain_without_attempt_or_audit_and_moves_only_through_the_real_store()
    {
        await using var run = await Run.StartAsync();
        var request = Request(CompanyA, DraftId, $"{Label} SO-0001");

        // First run: the rows of a Preparing draft with two lines (version 1 + 2), no attempt, no audit; the
        // store's ambient company context is bound by the store to the request's company.
        SalesDraftFixtureOutcome first;
        await using (var scope = run.Open(CompanyA, resolved: false))
        {
            first = await scope.Fixture.EnsureAsync(request, CancellationToken.None);
            Assert.True(scope.CompanyContext.IsResolved && scope.CompanyContext.CompanyId == CompanyA);
        }

        Assert.Equal((DraftId, 3L, true), (first.DraftId, first.Version, first.Added));
        Assert.Equal((1, 2, 0, 0, 1, 3L), await run.CountsAsync(DraftId));
        Assert.Equal($"{Label} SO-0001", await run.ScalarAsync<string>("SELECT DocumentNo FROM sales.Drafts WHERE Id = @id", ("@id", DraftId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftNumbers"));

        // The real store rebuilds it through the domain and moves it as a real command (attempt + audit).
        await using (var scope = run.Open(CompanyA))
        {
            var restored = await scope.Store.FindDraftAsync(DraftId);
            Assert.NotNull(restored);
            Assert.Equal((InternalApprovalState.Preparing, 3L, 2, $"{Label} rulo"), (restored.ApprovalState, restored.Version, restored.Lines.Count, restored.Lines[0].Product.Name));
            var submitted = await scope.Store.UpdateDraftStateAsync(run.Envelope("k-submit"), Hash("submit-3"), SalesDraftCommands.SubmitV1, DraftId,
                (draft, now) => draft.Submit(3, "SENTETIK-preparer", now), Submitted);
            Assert.NotNull(submitted);
            Assert.Equal((InternalApprovalState.PendingApproval, 4L), (submitted.Draft.ApprovalState, submitted.Draft.Version));
        }

        Assert.Equal((1, 2, 1, 1, 2, 4L), await run.CountsAsync(DraftId));

        // A re-run after the axis moved is a no-op: nothing is rewritten, nothing is reset.
        await using (var scope = run.Open(CompanyA))
        {
            var again = await scope.Fixture.EnsureAsync(request, CancellationToken.None);
            Assert.Equal((DraftId, 4L, false), (again.DraftId, again.Version, again.Added));
        }

        Assert.Equal((1, 2, 1, 1, 2, 4L), await run.CountsAsync(DraftId));

        // Refusals write nothing: another draft under the id, a draft without the label, another company's context.
        await using (var scope = run.Open(CompanyA))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Fixture.EnsureAsync(
                Request(CompanyA, DraftId, $"{Label} SO-0002"), CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Fixture.EnsureAsync(
                Request(CompanyA, Guid.NewGuid(), "SO-2026-000009"), CancellationToken.None));
        }

        await using (var scope = run.Open(CompanyB))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Fixture.EnsureAsync(
                Request(CompanyA, Guid.NewGuid(), $"{Label} SO-0003"), CancellationToken.None));
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Drafts"));
        Assert.Equal((1, 2, 1, 1, 2, 4L), await run.CountsAsync(DraftId));
    }

    private static SalesDraftFixtureRequest Request(Guid company, Guid draftId, string documentNo) =>
        new(draftId, company, documentNo, Label, Day,
            new SalesDraftFixtureOwners(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), From),
            [
                new SalesDraftFixtureLine(Guid.NewGuid(), Guid.NewGuid(), "S04-COIL", $"{Label} rulo", Guid.NewGuid(), Guid.NewGuid(),
                    "kg", "weight", IsKilogram: true, Scale: 3, "1.250", 1.250m),
                new SalesDraftFixtureLine(Guid.NewGuid(), Guid.NewGuid(), "S04-SHEET", $"{Label} levha", Guid.NewGuid(), Guid.NewGuid(),
                    "adet", "count", IsKilogram: false, Scale: 0, "3", 3m),
            ],
            $"{Label} seed", "browser-suite:s04:draft", T0);

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

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
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            var companyContext = new CompanyContext();
            companyContext.Set(CompanyA);
            await using var context = new SalesDbContext(Options(factory), companyContext);
            await context.Database.MigrateAsync();
            return run;
        }

        public SalesDraftWriteEnvelope Envelope(string key) =>
            new("SENTETIK-preparer", "SENTETIK-sales-permission", ["SENTETIK-SALES"], $"sales-s04-{Guid.NewGuid():N}", null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public Scope Open(Guid company, bool resolved = true)
        {
            var companyContext = new CompanyContext();
            if (resolved)
            {
                companyContext.Set(company);
            }

            var collector = new AuditEventCollector();
            var options = new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext))
                .Options;
            var dbContext = new SalesDbContext(options, companyContext);
            return new Scope(dbContext, companyContext,
                new SalesDraftFixtureStore(dbContext, companyContext),
                new SalesStore(dbContext, companyContext, collector, TimeProvider.System));
        }

        /// <summary>The draft's rows as the real SQL holds them: header rows, line rows, attempt rows, audit events, the approval state and the version.</summary>
        public async Task<(int Drafts, int Lines, int Attempts, int AuditEvents, int ApprovalState, long Version)> CountsAsync(Guid draftId) =>
            (await ScalarAsync<int>("SELECT COUNT(*) FROM sales.Drafts WHERE Id = @id", ("@id", draftId)),
                await ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftLines WHERE DraftId = @id", ("@id", draftId)),
                await ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE DraftId = @id", ("@id", draftId)),
                await ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id", ("@id", draftId.ToString("D"))),
                await ScalarAsync<int>("SELECT ApprovalState FROM sales.Drafts WHERE Id = @id", ("@id", draftId)),
                await ScalarAsync<long>("SELECT Version FROM sales.Drafts WHERE Id = @id", ("@id", draftId)));

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

        private static DbContextOptions<SalesDbContext> Options(SqlServerRelationalApiFactory factory) =>
            new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .Options;
    }

    private sealed class Scope(SalesDbContext dbContext, CompanyContext companyContext, SalesDraftFixtureStore fixture, SalesStore store) : IAsyncDisposable
    {
        public CompanyContext CompanyContext => companyContext;

        public ISalesDraftFixtureStore Fixture => fixture;

        public ISalesOrderDraftStore Store => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }
}

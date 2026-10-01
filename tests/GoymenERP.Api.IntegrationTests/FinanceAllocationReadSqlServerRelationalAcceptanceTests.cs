using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F-03 read acceptance over disposable real SQL. The permission answers are synthetic test inputs;
/// this is not a production grant. The read path must remain company-scoped, freshly fenced and write-free.
/// </summary>
public sealed class FinanceAllocationReadSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;

    [SqlServerFact]
    public async Task Read_permission_lists_reopens_and_fences_real_company_rows_without_writes()
    {
        await using var run = await Run.StartAsync();
        var companyA = await run.SeedAsync(CompanyA, 51);
        var companyB = await run.SeedAsync(CompanyB, 1);
        var before = await run.PersistentCountsAsync();

        await using (var first = run.Reader(CompanyA, PermissionDecision.Allowed))
        {
            var pageResult = await first.Service.ListAuthorizedAsync(first.Context, CancellationToken.None);
            var page = Assert.IsType<FinanceAllocationPage>(pageResult.Resource);
            Assert.Equal(ReadOutcome.Found, pageResult.Outcome);
            Assert.True(page.WindowFull);
            Assert.Equal(50, page.Items.Count);
            Assert.Equal(companyA.LatestAllocationId, page.Items[0].Id);
            Assert.All(page.Items, item => Assert.Equal(CompanyA, item.CompanyId));
        }

        await using (var reopened = run.Reader(
            CompanyA,
            PermissionDecision.Allowed,
            PermissionDecision.Allowed,
            PermissionDecision.Allowed))
        {
            var detailResult = await reopened.Service.GetAuthorizedAsync(
                reopened.Context,
                companyA.LatestAllocationId,
                CancellationToken.None);
            var detail = Assert.IsType<FinanceAllocationDetail>(detailResult.Resource);
            Assert.Equal(ReadOutcome.Found, detailResult.Outcome);
            Assert.Equal(companyA.LatestAllocationId, detail.Allocation.Id);
            Assert.Equal("7", detail.OpenItemVersion);
        }

        await using (var foreign = run.Reader(CompanyB, PermissionDecision.Allowed, PermissionDecision.Allowed))
        {
            var hidden = await foreign.Service.GetAuthorizedAsync(
                foreign.Context,
                companyA.LatestAllocationId,
                CancellationToken.None);
            var own = await foreign.Service.ListAuthorizedAsync(foreign.Context, CancellationToken.None);
            Assert.Equal(ReadOutcome.NotFound, hidden.Outcome);
            Assert.Null(hidden.Resource);
            Assert.Equal(companyB.LatestAllocationId,
                Assert.Single(Assert.IsType<FinanceAllocationPage>(own.Resource).Items).Id);
        }

        await using (var revoked = run.Reader(
            CompanyA,
            PermissionDecision.Allowed,
            PermissionDecision.Denied))
        {
            var refused = await revoked.Service.ListAuthorizedAsync(revoked.Context, CancellationToken.None);
            Assert.Equal(ReadOutcome.Denied, refused.Outcome);
            Assert.Null(refused.Resource);
        }

        Assert.Equal(before, await run.PersistentCountsAsync());
    }

    private sealed class SequencePermission(params PermissionDecision[] decisions) : IFinancePermissionPort
    {
        private int index;

        public Task<PermissionDecision> CheckAsync(
            FinanceContext context,
            FinanceAction action,
            Guid? sourceId,
            CancellationToken cancellationToken)
        {
            Assert.Equal(FinanceAction.ReadAllocation, action);
            var answer = decisions[Math.Min(index, decisions.Length - 1)];
            index++;
            return Task.FromResult(answer);
        }
    }

    private sealed class User(string actor, Guid companyId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { companyId };
        public bool CanAccessCompany(Guid company) => AllowedCompanyIds.Contains(company);
    }

    private sealed record SeedResult(Guid LatestAllocationId);

    private sealed class ReadScope : IAsyncDisposable
    {
        public ReadScope(FinanceDbContext db, FinanceAllocationReadService service, FinanceContext context)
        {
            Db = db;
            Service = service;
            Context = context;
        }

        public FinanceDbContext Db { get; }
        public FinanceAllocationReadService Service { get; }
        public FinanceContext Context { get; }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        private SqlServerRelationalApiFactory Factory { get; }

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            await using var context = run.Context(CompanyA);
            await context.Database.MigrateAsync();
            return run;
        }

        public ReadScope Reader(Guid companyId, params PermissionDecision[] decisions)
        {
            var company = new CompanyContext();
            company.Set(companyId);
            var actor = $"synthetic-allocation-reader-{companyId:N}";
            var db = Context(company);
            return new ReadScope(
                db,
                new FinanceAllocationReadService(db, company, new User(actor, companyId), new SequencePermission(decisions)),
                new FinanceContext(companyId, actor, $"f03-read-{Guid.NewGuid():N}", null));
        }

        public async Task<SeedResult> SeedAsync(Guid companyId, int count)
        {
            await using var db = Context(companyId);
            var batchId = Guid.NewGuid();
            const string checksum = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            db.OpenItemSourceBatches.Add(new FinanceOpenItemSourceBatchRow
            {
                Id = batchId,
                CompanyId = companyId,
                SourceKind = "synthetic.invoice-installment.v1",
                SourceVersion = "1",
                SourceChecksum = checksum,
                IdempotencyKeyHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                PayloadHash = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
                ActorId = "synthetic-f03-fixture",
                ResultJson = "{}",
                CorrelationId = $"synthetic-f03-{companyId:N}",
                AdmittedAtUtc = DateTimeOffset.UtcNow,
            });

            var first = DateTimeOffset.UtcNow.AddMinutes(-count);
            Guid latest = Guid.Empty;
            for (var index = 0; index < count; index++)
            {
                var openItemId = Guid.NewGuid();
                var sourceId = Guid.NewGuid();
                var allocationId = Guid.NewGuid();
                var partyId = Guid.NewGuid();
                var companyAccountId = Guid.NewGuid();
                var currencyId = Guid.Parse("55555555-5555-4555-8555-555555555555");
                var createdAt = first.AddSeconds(index);
                latest = allocationId;
                db.ReceiptDrafts.Add(new ReceiptDraftRow
                {
                    Id = sourceId,
                    CompanyId = companyId,
                    Version = 2,
                    PartyId = partyId,
                    CompanyAccountId = companyAccountId,
                    TreasuryAccountId = Guid.NewGuid(),
                    CurrencyId = currencyId,
                    AmountText = "125.50",
                    ProfileId = Guid.NewGuid().ToString("D"),
                    ProfileVersion = "1",
                    OccurredOn = DateOnly.FromDateTime(createdAt.UtcDateTime),
                    ValueOn = DateOnly.FromDateTime(createdAt.UtcDateTime),
                    CreatedAtUtc = createdAt,
                    CreatedBy = "synthetic-f03-fixture",
                    UpdatedAtUtc = createdAt,
                    UpdatedBy = "synthetic-f03-fixture",
                    Stage = ReceiptStages.Recorded,
                    RecordedAtUtc = createdAt,
                    RecordedBy = "synthetic-f03-fixture",
                });
                db.OpenItems.Add(new FinanceOpenItemRow
                {
                    Id = openItemId,
                    CompanyId = companyId,
                    SourceKind = "synthetic.invoice-installment.v1",
                    SourceId = Guid.NewGuid(),
                    SourceLineId = Guid.NewGuid(),
                    SourceVersion = "1",
                    SourceBatchId = batchId,
                    SourceBatchVersion = "1",
                    SourceBatchChecksum = checksum,
                    PartyId = partyId,
                    CompanyAccountId = companyAccountId,
                    CurrencyId = currencyId,
                    OriginalAmountText = "125.50",
                    OutstandingAmountText = "0",
                    Status = FinanceOpenItemStatuses.Closed,
                    Version = 7,
                    CreatedBy = "synthetic-f03-fixture",
                    CreatedAtUtc = createdAt,
                    UpdatedBy = "synthetic-f03-fixture",
                    UpdatedAtUtc = createdAt,
                });
                db.Allocations.Add(new FinanceAllocationRow
                {
                    Id = allocationId,
                    CompanyId = companyId,
                    SourceId = sourceId,
                    OpenItemId = openItemId,
                    CurrencyId = currencyId,
                    AmountText = "125.50",
                    Reason = "SENTETIK real-SQL read fixture",
                    Status = FinanceAllocationStatuses.Active,
                    Version = 1,
                    CreatedBy = "synthetic-f03-fixture",
                    CreatedAtUtc = createdAt,
                });
            }

            await db.SaveChangesAsync();
            return new SeedResult(latest);
        }

        public async Task<(long Allocations, long OpenItems, long Commands, long Audit)> PersistentCountsAsync() => (
            await ScalarAsync("SELECT COUNT_BIG(*) FROM finance.Allocations"),
            await ScalarAsync("SELECT COUNT_BIG(*) FROM finance.OpenItems"),
            await ScalarAsync("SELECT COUNT_BIG(*) FROM finance.Commands"),
            await ScalarAsync("SELECT COUNT_BIG(*) FROM audit.AuditEvents"));

        private FinanceDbContext Context(Guid companyId)
        {
            var company = new CompanyContext();
            company.Set(companyId);
            return Context(company);
        }

        private FinanceDbContext Context(ICompanyContext company) => new(
            new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .Options,
            company);

        private async Task<long> ScalarAsync(string sql)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            return (long)(await command.ExecuteScalarAsync())!;
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
}

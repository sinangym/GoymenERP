using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Inbox;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Real owner stores, SQL constraints and Audit interceptor; plans, currency and actors are SENTETIK.</summary>
public sealed class ManufacturingCostDifferenceSqlServerRelationalAcceptanceTests
{
    private static readonly Guid Company = TestCompanyIds.CompanyA;
    private static readonly Guid Maker = Guid.Parse("c0621000-0000-4000-8000-000000000001");
    private static readonly Guid Checker = Guid.Parse("c0621000-0000-4000-8000-000000000002");
    private static readonly Guid Currency = Guid.Parse("c0621000-0000-4000-8000-000000000003");
    private static readonly Guid Debit = Guid.Parse("c0621000-0000-4000-8000-000000000004");
    private static readonly Guid Credit = Guid.Parse("c0621000-0000-4000-8000-000000000005");
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-11-17T09:00:00Z");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [SqlServerFact]
    public async Task Inventory_rounded_share_posts_exactly_and_Down_refuses_the_immutable_source()
    {
        await using var run = await Run.StartAsync();
        var period = await run.OpenPeriod();
        var source = Source();
        source = source with { Outputs = [source.Outputs[0] with
        {
            NetQuantityKg = 3m, RemainingQuantityKg = 2m, ConsumedQuantityKg = 1m,
            TlUnitCostPerKg = 33.336666666666666666666666667m,
            OriginalTlAmount = 100.01m, RemainingTlValue = 66.67m, ConsumedTlDifference = 33.34m,
        }] };
        var id = source.CostDifferenceEventId!.Value;
        await using (var scope = run.Scope())
        {
            await scope.Store.AcceptAsync(Envelope(source), source);
            await scope.Store.PrepareAsync(Preparation(id, period), Maker, _ => Task.CompletedTask, Metadata(Maker));
            await scope.Store.ApproveAsync(id, 2, Checker, _ => Task.CompletedTask, Metadata(Checker));
        }
        Assert.Equal(33.34m, await run.Scalar<decimal>("SELECT Amount FROM accounting.JournalLines WHERE Side = 1"));
        var migration = new GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations.AddManufacturingCostDifferenceProposals();
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        var refused = await Assert.ThrowsAsync<SqlException>(() => run.Scalar<int>(guard.Sql + "\nSELECT 1;"));
        Assert.Equal(51962, refused.Number);
        Assert.Equal(1, await run.Count("accounting.ManufacturingCostDifferences"));
        Assert.Equal(1, await run.Count("accounting.Journals"));
    }

    [SqlServerFact]
    public async Task Cost_difference_is_received_once_prepared_in_an_open_period_and_posted_once_by_another_actor()
    {
        await using var run = await Run.StartAsync();
        var period = await run.OpenPeriod();
        var source = Source();
        await using (var scope = run.Scope())
        {
            var consumer = new ManufacturingCostDifferenceInboxConsumerV1(scope.Store, scope.Company);
            await consumer.HandleAsync(Envelope(source), CancellationToken.None);
            await consumer.HandleAsync(Envelope(source), CancellationToken.None);
            var view = await scope.Store.FindAsync(source.CostDifferenceEventId!.Value);
            Assert.Equal("AwaitingPreparation", view!.Status);
            Assert.Equal("600", view.SignedTlDifference);
            Assert.Equal("2000", Assert.Single(view.Outputs).OriginalTlAmount);
        }
        Assert.Equal(1, await run.Count("accounting.ManufacturingCostDifferences"));
        Assert.Equal(0, await run.Count("accounting.Journals"));
        await using (var foreign = run.Scope(TestCompanyIds.CompanyB)) Assert.Empty(await foreign.Store.ListAsync());
        var proposalId = source.CostDifferenceEventId!.Value;
        await using (var scope = run.Scope())
        {
            var prepare = Preparation(proposalId, period);
            var first = await scope.Service(Maker).PrepareAsync(prepare, "synthetic-prepare");
            var replay = await scope.Service(Maker).PrepareAsync(prepare, "synthetic-prepare");
            Assert.Equal(AccountingApiStatus.Ok, first.Status);
            Assert.True(replay.IsReplay);
            Assert.Equal(2, first.Resource!.Version);
        }
        await using (var scope = run.Scope())
        {
            var refusal = await scope.Service(Maker).ApproveAsync(proposalId, 2, "synthetic-self");
            Assert.Equal(AccountingApiStatus.Denied, refusal.Status);
        }
        Assert.Equal(0, await run.Count("accounting.Journals"));
        await using (var scope = run.Scope())
        {
            var first = await scope.Service(Checker).ApproveAsync(proposalId, 2, "synthetic-approve");
            var replay = await scope.Service(Checker).ApproveAsync(proposalId, 2, "synthetic-approve");
            Assert.Equal(AccountingApiStatus.Ok, first.Status);
            Assert.Equal("Posted", first.Resource!.Status);
            Assert.Equal(Checker, first.Resource.ApprovedBy);
            Assert.True(replay.IsReplay);
            Assert.Equal(first.Resource.JournalId, replay.Resource!.JournalId);
        }
        Assert.Equal(1, await run.Count("accounting.Journals"));
        Assert.Equal(2, await run.Count("accounting.JournalLines"));
        Assert.Equal(1, await run.Count("accounting.Deliveries"));
        Assert.Equal(600m, await run.Scalar<decimal>("SELECT Amount FROM accounting.JournalLines WHERE Side = 1"));
        Assert.Equal(0m, await run.Scalar<decimal>("SELECT SUM(CASE WHEN Side = 1 THEN Amount ELSE -Amount END) FROM accounting.JournalLines"));
        Assert.Equal(3, await run.Scalar<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Accounting.ManufacturingCostDifference'"));
        var invalidState = await Assert.ThrowsAsync<SqlException>(() => run.Scalar<int>(
            "UPDATE accounting.ManufacturingCostDifferences SET ApprovedBy = PreparedBy; SELECT 1;"));
        Assert.Equal(547, invalidState.Number);
    }

    [SqlServerFact]
    public async Task Late_authority_loss_rolls_back_journal_delivery_audit_and_decision_and_closed_period_is_not_reopened()
    {
        await using var run = await Run.StartAsync();
        var period = await run.OpenPeriod();
        var source = Source();
        var id = source.CostDifferenceEventId!.Value;
        await using (var scope = run.Scope())
        {
            await scope.Store.AcceptAsync(Envelope(source), source);
            await scope.Store.PrepareAsync(Preparation(id, period), Maker, _ => Task.CompletedTask, Metadata(Maker));
        }
        var audits = await run.Count("audit.AuditEvents");
        await using (var scope = run.Scope())
        {
            var fences = 0;
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Store.ApproveAsync(id, 2, Checker,
                _ => ++fences == 3 ? throw new InvalidOperationException("SENTETIK late authority loss") : Task.CompletedTask,
                Metadata(Checker)));
            Assert.Equal(3, fences);
        }
        Assert.Equal(0, await run.Count("accounting.Journals"));
        Assert.Equal(0, await run.Count("accounting.Deliveries"));
        Assert.Equal(audits, await run.Count("audit.AuditEvents"));
        await using (var scope = run.Scope())
        {
            Assert.Equal("Prepared", (await scope.Store.FindAsync(id))!.Status);
            var current = await scope.Posting.FindPeriodAsync(period);
            await scope.Posting.ClosePeriodAsync(period, current!.Version, Checker, Metadata(Checker));
        }
        await using (var scope = run.Scope())
        {
            var refusal = await scope.Service(Checker).ApproveAsync(id, 2, "closed-period");
            Assert.Equal(AccountingApiStatus.Unprocessable, refusal.Status);
            Assert.Equal("cost_difference_open_period_required", refusal.Code);
        }
        Assert.Equal(0, await run.Count("accounting.Journals"));
        Assert.Equal(2, await run.Scalar<int>("SELECT Status FROM accounting.Periods"));
    }

    private static ManufacturingCostDifferenceEventV1 Source() => new(Company, Guid.NewGuid(), Guid.NewGuid(), "2", "4", 3,
        "SENTETIK-fingerprint", [new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100m, 70m, 30m, 20m, 1400m, 600m, 4m, [], 2000m)],
        Guid.NewGuid(), false, "SENTETIK-cost-difference");

    [SqlServerFact]
    public async Task Concurrent_approvals_leave_one_journal_and_one_proposal_decision()
    {
        await using var run = await Run.StartAsync();
        var period = await run.OpenPeriod();
        var source = Source();
        var id = source.CostDifferenceEventId!.Value;
        await using (var scope = run.Scope())
        {
            await scope.Store.AcceptAsync(Envelope(source), source);
            await scope.Store.PrepareAsync(Preparation(id, period), Maker, _ => Task.CompletedTask, Metadata(Maker));
        }
        async Task<AccountingApiResult<ManufacturingCostDifferenceViewV1>> Approve(string key)
        {
            await using var scope = run.Scope();
            return await scope.Service(Checker).ApproveAsync(id, 2, key);
        }
        var results = await Task.WhenAll(Approve("race-a"), Approve("race-b"));
        Assert.Single(results, result => result.Status == AccountingApiStatus.Ok);
        Assert.Single(results, result => result.Status == AccountingApiStatus.Conflict);
        Assert.Equal(1, await run.Count("accounting.Journals"));
        Assert.Equal(1, await run.Count("accounting.Deliveries"));
        Assert.Equal(3L, await run.Scalar<long>("SELECT Version FROM accounting.ManufacturingCostDifferences"));
        Assert.Equal(1, await run.Scalar<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'accounting.manufacturing-cost-difference.approved.v1'"));
    }
    private static WorkInboxEventV1 Envelope(ManufacturingCostDifferenceEventV1 source) => new(
        ManufacturingCostDifferenceInboxV1.SourceName, source.CostDifferenceEventId!.Value, Company,
        ManufacturingCostDifferenceInboxV1.EventType, 1, JsonSerializer.Serialize(source, Json), At, source.OriginalCorrelationId, null);
    private static PrepareManufacturingCostDifferenceV1 Preparation(Guid id, Guid period) => new(id, 1, period,
        new(2026, 11, 17), new(2026, 11, 16), Debit, Credit, "SENTETIK consumed cost accounting");
    private static AccountingCommandMetadata Metadata(Guid actor) => new(actor.ToString("D"),
        AccountingJournalReversalPermissionNamesV1.Propose, [], "SENTETIK-cost-difference", null, Guid.NewGuid().ToString("N"), "UnitTest");

    private sealed class Run(string baseline, SqlServerRelationalApiFactory factory) : IAsyncDisposable
    {
        public static async Task<Run> StartAsync()
        {
            var baseline = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var run = new Run(baseline, new SqlServerRelationalApiFactory(baseline));
            try
            {
                using var client = run.Factory.CreateClient();
                using var health = await client.GetAsync("/health/live");
                health.EnsureSuccessStatusCode();
                await using var scope = run.Scope();
                await scope.Db.Database.MigrateAsync();
                return run;
            }
            catch { await run.DisposeAsync(); throw; }
        }
        private SqlServerRelationalApiFactory Factory => factory;
        public Scope Scope(Guid? company = null) => new(factory.DatabaseConnectionString, company ?? Company);
        public async Task<Guid> OpenPeriod()
        {
            await using var scope = Scope();
            var id = Guid.NewGuid();
            var (proposed, _) = await scope.Posting.ProposePeriodAsync(id, new(2026, 11, 1), new(2026, 11, 30), Maker, Metadata(Maker));
            await scope.Posting.ApprovePeriodAsync(id, proposed.Version, Checker, Metadata(Checker));
            return id;
        }
        public Task<int> Count(string table) => Scalar<int>("SELECT COUNT(*) FROM " + table); // Only constants supplied in this test.
        public async Task<T> Scalar<T>(string sql)
        {
            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            return (T)(await command.ExecuteScalarAsync())!;
        }
        public async ValueTask DisposeAsync()
        {
            await factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseline);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"IF DB_ID(N'{factory.DatabaseName}') IS NOT NULL BEGIN ALTER DATABASE [{factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{factory.DatabaseName}]; END;";
            await command.ExecuteNonQueryAsync();
        }
    }
    private sealed class Scope : IAsyncDisposable
    {
        public CompanyContext Company { get; }
        public AccountingDbContext Db { get; }
        public AccountingStore Posting { get; }
        public ManufacturingCostDifferenceStoreV1 Store { get; }
        public Scope(string connection, Guid company)
        {
            Company = new CompanyContext(); Company.Set(company);
            var audit = new AuditEventCollector();
            Db = new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>()
                .UseSqlServer(connection, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AccountingDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(audit, Company)).Options, Company);
            Posting = new(Db, Company, audit, TimeProvider.System,
                new AccountingAmountScaleVerifier(new AccountingSqlServerRelationalAcceptanceTests.SyntheticMoneyProfiles(company)));
            Store = new(Db, Posting, Company, new Plan(), new Currencies(), audit, TimeProvider.System);
        }
        public ManufacturingCostDifferenceServiceV1 Service(Guid actor) => new(Store, Posting, new Authority(), Company, new User(actor), new Correlation());
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class Plan : IActiveAccountPlanReaderV1
    {
        public Task<ApprovedAccountingSourceCatalogRecordV1?> ReadActiveAsync(CancellationToken cancellationToken = default)
        {
            var hash = new string('A', 64);
            var mirror = new AccountPlanMirrorV1(Company, "SENTETIK-plan", hash, Checker, At,
                [new(new(Company, Debit), "620", "SENTETIK debit"), new(new(Company, Credit), "151", "SENTETIK credit")]);
            return Task.FromResult<ApprovedAccountingSourceCatalogRecordV1?>(new(Guid.Parse("c0621000-0000-4000-8000-000000000006"), Company,
                ApprovedAccountingSourceCatalogStatusV1.Active, 2, 1, "SENTETIK-template", "SENTETIK-plan", hash, hash,
                Maker, At.AddHours(-1), Checker, At, new(mirror, [])));
        }
    }
    private sealed class Currencies : ISettingsMoneyCandidateReader
    {
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MoneyCurrencyCandidateV1>>([new(Currency, "TRY", "SENTETIK TL")]);
    }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
    }
    private sealed class User(Guid actor) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string UserId => actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid value) => value == Company;
    }
    private sealed class Correlation : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => "SENTETIK-cost-difference";
        public string? CausationId => null;
        public string SourceChannel => "UnitTest";
        public string? ClientIpAddress => null;
        public string? UserAgent => null;
        public void Set(string a, string? b, string c, string? d, string? e) => throw new NotSupportedException();
    }
}

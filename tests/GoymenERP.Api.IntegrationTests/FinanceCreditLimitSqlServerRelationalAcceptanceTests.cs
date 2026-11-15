using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// API-85a (F018 plan §L1; FNS-D27/D27a) against a REAL SQL Server: the Finance party credit limit master on
/// Finance's own migrations over the host's migrated and seeded database, with Audit's real save interceptor.
/// <para>
/// Permission, the Settings currency candidates and Counterparty's party account answer are ISOLATED SYNTHETIC
/// ports: no Finance credit-limit grant exists, so these tests prove the store, its constraints and its rules,
/// not a user outcome. Every identity, amount and currency below is SENTETIK / NOT_ENV test data. A credit limit
/// moves no money.
/// </para>
/// </summary>
public sealed class FinanceCreditLimitSqlServerRelationalAcceptanceTests
{
    private const string Proposer = "SENTETIK-proposer";
    private const string Approver = "SENTETIK-approver";
    private const string SecondApprover = "SENTETIK-approver-2";
    private const string Reason = "SENTETIK gerekçe";
    private const int DownGuardNumber = 51274;
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Currency = Guid.Parse("8f2e3d4c-5b6a-4798-8a1b-2c3d4e5f6a01");
    private static readonly Guid Party = Guid.Parse("8f2e3d4c-5b6a-4798-8a1b-2c3d4e5f6a02");
    private static readonly Guid Account = Guid.Parse("8f2e3d4c-5b6a-4798-8a1b-2c3d4e5f6a03");

    [SqlServerFact]
    public async Task An_approver_sets_and_removes_a_limit_in_one_save_each_with_its_record_attempt_and_audit()
    {
        await using var run = await Run.StartAsync();

        PartyCreditLimitRecordView set;
        PartyCreditLimitRecordView removed;
        await using (var a = run.Open(CompanyA, Approver))
        {
            var stored = await a.Limits.SetAsync(a.Context, Key("s-1"),
                new PartyCreditLimitSetRequest(Party, Account, Currency, "150000.50", "0"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, stored.Outcome);
            set = stored.Resource!;
            Assert.Equal((PartyCreditLimitStatus.Approved, true, "1", "150000.50"), (set.Status, set.IsDirect, set.Revision, set.Amount));

            var gone = await a.Limits.RemoveAsync(a.Context, Key("r-1"),
                new PartyCreditLimitRemoveRequest(Party, Account, "1"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, gone.Outcome);
            removed = gone.Resource!;
            Assert.Equal((PartyCreditLimitKind.Remove, (Guid?)null, (string?)null, "2"),
                (removed.Kind, removed.CurrencyId, removed.Amount, removed.Revision));
        }

        // The amount is stored as given (never rounded); the set is superseded by the removal.
        Assert.Equal("150000.50", await run.ScalarAsync<string>(
            "SELECT AmountText FROM finance.PartyCreditLimits WHERE Id = @id", ("@id", set.Id)));
        Assert.Equal(FinancePartyCreditLimitService.Superseded, await run.ScalarAsync<int>(
            "SELECT Status FROM finance.PartyCreditLimits WHERE Id = @id", ("@id", set.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.PartyCreditLimits WHERE CompanyAccountId = @a AND Status = 2", ("@a", Account)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PartyCreditLimitCommands"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.credit-limit.set.v1' AND EntityId = @id",
            ("@id", set.Id.ToString())));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.credit-limit.removed.v1' AND EntityId = @id",
            ("@id", removed.Id.ToString())));
        // No money effect.
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
    }

    [SqlServerFact]
    public async Task A_proposal_is_decided_once_by_another_approver_and_the_proposer_is_refused_by_service_and_database()
    {
        await using var run = await Run.StartAsync();

        PartyCreditLimitRecordView proposal;
        await using (var p = run.Open(CompanyA, Proposer))
        {
            var proposed = await p.Limits.ProposeAsync(p.Context, Key("p-1"),
                new PartyCreditLimitProposeRequest(Party, Account, PartyCreditLimitKind.Set, Currency, "200000", "0"),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, proposed.Outcome);
            proposal = proposed.Resource!;

            // FNS-D27a maker≠checker: the proposer holds every action here and is still refused, storing nothing.
            var self = await p.Limits.ApproveAsync(p.Context, Key("a-self"),
                new PartyCreditLimitApproveRequest(proposal.Id, "1"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Denied, self.Outcome);
            Assert.Equal("finance_approver_is_proposer", Assert.Single(self.Issues).Code);
        }

        // The database refuses a self-decision too (CK_PartyCreditLimits_Decision, binary collation), whatever the caller.
        var check = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE finance.PartyCreditLimits SET Status = 2, DecidedBy = ProposedBy, DecidedAtUtc = SYSUTCDATETIME(), "
            + "AccountRevision = 1, Version = 2 WHERE Id = @id", ("@id", proposal.Id)));
        Assert.Equal(547, check.Number);
        Assert.Equal(FinancePartyCreditLimitService.Proposed, await run.ScalarAsync<int>(
            "SELECT Status FROM finance.PartyCreditLimits WHERE Id = @id", ("@id", proposal.Id)));

        await using (var b = run.Open(CompanyA, Approver))
        {
            var approved = await b.Limits.ApproveAsync(b.Context, Key("a-1"),
                new PartyCreditLimitApproveRequest(proposal.Id, "1"), CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, PartyCreditLimitStatus.Approved, false, "1", "2"),
                (approved.Outcome, approved.Resource!.Status, approved.Resource.IsDirect, approved.Resource.Revision,
                    approved.Resource.Version));
            Assert.Equal("credit_limit_not_proposed", Assert.Single((await b.Limits.ApproveAsync(b.Context, Key("a-2"),
                new PartyCreditLimitApproveRequest(proposal.Id, "2"), CancellationToken.None)).Issues).Code);
        }

        Assert.Equal((Proposer, Approver), (
            await run.ScalarAsync<string>("SELECT ProposedBy FROM finance.PartyCreditLimits WHERE Id = @id", ("@id", proposal.Id)),
            await run.ScalarAsync<string>("SELECT DecidedBy FROM finance.PartyCreditLimits WHERE Id = @id", ("@id", proposal.Id))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.credit-limit.approved.v1' AND ApprovalContextJson IS NOT NULL"));
    }

    [SqlServerFact]
    public async Task A_rejection_with_a_reason_keeps_the_effective_limit_and_stores_the_reason()
    {
        await using var run = await Run.StartAsync();

        PartyCreditLimitRecordView set;
        await using (var b = run.Open(CompanyA, Approver))
        {
            set = (await b.Limits.SetAsync(b.Context, Key("s-1"),
                new PartyCreditLimitSetRequest(Party, Account, Currency, "1000", "0"), CancellationToken.None)).Resource!;
        }

        PartyCreditLimitRecordView proposal;
        await using (var p = run.Open(CompanyA, Proposer))
        {
            proposal = (await p.Limits.ProposeAsync(p.Context, Key("p-1"),
                new PartyCreditLimitProposeRequest(Party, Account, PartyCreditLimitKind.Set, Currency, "2000", "1"),
                CancellationToken.None)).Resource!;
        }

        await using (var b = run.Open(CompanyA, SecondApprover))
        {
            var rejected = await b.Limits.RejectAsync(b.Context, Key("j-1"),
                new PartyCreditLimitRejectRequest(proposal.Id, "1", Reason), CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, PartyCreditLimitStatus.Rejected), (rejected.Outcome, rejected.Resource!.Status));

            var account = await b.Limits.GetAccountAsync(b.Context, Account, CancellationToken.None);
            Assert.Equal((set.Id, (PartyCreditLimitRecordView?)null, "1"),
                (account.Resource!.Effective!.Id, account.Resource.Pending, account.Resource.Revision));
        }

        Assert.Equal(Reason, await run.ScalarAsync<string>(
            "SELECT RejectionReason FROM finance.PartyCreditLimits WHERE Id = @id", ("@id", proposal.Id)));
        Assert.Equal(FinancePartyCreditLimitService.Approved, await run.ScalarAsync<int>(
            "SELECT Status FROM finance.PartyCreditLimits WHERE Id = @id", ("@id", set.Id)));
    }

    [SqlServerFact]
    public async Task Two_racing_direct_sets_from_one_revision_leave_one_effective_limit_and_the_loser_a_concurrent_change()
    {
        await using var run = await Run.StartAsync();

        // Two approvers change the never-limited account from revision 0 with different keys; both read the same
        // state, then meet at the save, where the live-state and revision unique indexes are the authority.
        var barrier = new Barrier(2);
        async Task<PartyCreditLimitCommandResult> SetAsync(string approver, string amount)
        {
            await using var scope = run.Open(CompanyA, approver, new BarrierInterceptor(barrier));
            return await scope.Limits.SetAsync(scope.Context, Key("s-" + approver),
                new PartyCreditLimitSetRequest(Party, Account, Currency, amount, "0"), CancellationToken.None);
        }

        var results = await Task.WhenAll(Task.Run(() => SetAsync(Approver, "100")), Task.Run(() => SetAsync(SecondApprover, "200")));
        var won = Assert.Single(results, result => result.Outcome == CommandOutcome.Succeeded);
        var lost = Assert.Single(results, result => result.Outcome == CommandOutcome.VersionConflict);
        Assert.Equal("concurrent_change", Assert.Single(lost.Issues).Code);
        Assert.Null(lost.Resource);

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PartyCreditLimits"));
        Assert.Equal(won.Resource!.Id, await run.ScalarAsync<Guid>(
            "SELECT Id FROM finance.PartyCreditLimits WHERE CompanyAccountId = @a AND Status = 2", ("@a", Account)));
        // The winner's attempt only; the loser's attempt and audit event rolled back with its save.
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PartyCreditLimitCommands"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.credit-limit.set.v1'"));
    }

    [SqlServerFact]
    public async Task Two_racing_proposals_for_one_account_store_one_pending_proposal()
    {
        await using var run = await Run.StartAsync();

        var barrier = new Barrier(2);
        async Task<PartyCreditLimitCommandResult> ProposeAsync(string key, string amount)
        {
            await using var scope = run.Open(CompanyA, Proposer, new BarrierInterceptor(barrier));
            return await scope.Limits.ProposeAsync(scope.Context, Key(key),
                new PartyCreditLimitProposeRequest(Party, Account, PartyCreditLimitKind.Set, Currency, amount, "0"),
                CancellationToken.None);
        }

        var results = await Task.WhenAll(Task.Run(() => ProposeAsync("p-1", "100")), Task.Run(() => ProposeAsync("p-2", "200")));
        Assert.Single(results, result => result.Outcome == CommandOutcome.Succeeded);
        Assert.Single(results, result => result.Outcome != CommandOutcome.Succeeded);

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.PartyCreditLimits WHERE CompanyAccountId = @a AND Status = 1", ("@a", Account)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.credit-limit.proposed.v1'"));
    }

    [SqlServerFact]
    public async Task The_same_key_replays_with_its_first_correlation_and_a_stale_revision_is_a_recorded_conflict()
    {
        await using var run = await Run.StartAsync();
        var request = new PartyCreditLimitSetRequest(Party, Account, Currency, "500", "0");

        await using var a = run.Open(CompanyA, Approver);
        var first = await a.Limits.SetAsync(a.Context, Key("k-1"), request, CancellationToken.None);
        var replay = await a.Limits.SetAsync(a.Context with { CorrelationId = run.Correlation + "-again" }, Key("k-1"), request,
            CancellationToken.None);
        Assert.Equal((CommandOutcome.Succeeded, true, run.Correlation, first.Resource!.Id),
            (replay.Outcome, replay.IsReplay, replay.OriginalCorrelationId, replay.Resource!.Id));

        // A second change still naming revision 0 is a terminal conflict: recorded without a target, no audit.
        var stale = await a.Limits.SetAsync(a.Context, Key("k-2"),
            new PartyCreditLimitSetRequest(Party, Account, Currency, "600", "0"), CancellationToken.None);
        Assert.Equal(CommandOutcome.VersionConflict, stale.Outcome);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.PartyCreditLimitCommands WHERE PartyCreditLimitId IS NULL"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PartyCreditLimits"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.credit-limit.set.v1'"));
    }

    [SqlServerFact]
    public async Task Another_companys_limit_is_never_seen_or_decided()
    {
        await using var run = await Run.StartAsync();
        PartyCreditLimitRecordView proposal;
        await using (var p = run.Open(CompanyA, Proposer))
        {
            proposal = (await p.Limits.ProposeAsync(p.Context, Key("p-1"),
                new PartyCreditLimitProposeRequest(Party, Account, PartyCreditLimitKind.Set, Currency, "100", "0"),
                CancellationToken.None)).Resource!;
        }

        await using (var b = run.Open(CompanyB, Approver))
        {
            Assert.Empty((await b.Limits.ListAsync(b.Context, CancellationToken.None)).Resource!.Items);
            Assert.Equal(ReadOutcome.NotFound, (await b.Limits.GetAccountAsync(b.Context, Account, CancellationToken.None)).Outcome);
            var approve = await b.Limits.ApproveAsync(b.Context, Key("a-1"),
                new PartyCreditLimitApproveRequest(proposal.Id, "1"), CancellationToken.None);
            Assert.Equal("credit_limit_proposal_not_found", Assert.Single(approve.Issues).Code);
        }

        Assert.Equal(FinancePartyCreditLimitService.Proposed, await run.ScalarAsync<int>(
            "SELECT Status FROM finance.PartyCreditLimits WHERE Id = @id", ("@id", proposal.Id)));
    }

    [SqlServerFact]
    public async Task The_party_credit_limit_migrations_Down_passes_on_an_empty_store_and_refuses_while_a_limit_record_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddPartyCreditLimits().DownOperations[0]).Sql;

        await run.ExecuteAsync(guard);

        PartyCreditLimitRecordView set;
        await using (var a = run.Open(CompanyA, Approver))
        {
            set = (await a.Limits.SetAsync(a.Context, Key("s-1"),
                new PartyCreditLimitSetRequest(Party, Account, Currency, "100", "0"), CancellationToken.None)).Resource!;
        }

        var error = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(DownGuardNumber, error.Number);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.PartyCreditLimits WHERE Id = @id", ("@id", set.Id)));
    }

    private static CommandMetadata Key(string key) => new CommandMetadata(key);

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }
        public string Correlation { get; } = $"api85a-{Guid.NewGuid():N}";

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            // The host migrates and seeds Organization, Audit and the rest; Finance's own migrations run after it.
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            var companyContext = new CompanyContext();
            companyContext.Set(CompanyA);
            await using var context = new FinanceDbContext(Options(factory), companyContext);
            await context.Database.MigrateAsync();
            return run;
        }

        public Scope Open(Guid company, string actor, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] extra)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var currentUser = new FixedCurrentUser(actor, company);
            var collector = new AuditEventCollector();
            var interceptors = new List<Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor>
            {
                new AuditSaveChangesInterceptor(collector, companyContext),
            };
            interceptors.AddRange(extra);
            var options = new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .AddInterceptors(interceptors)
                .Options;
            var dbContext = new FinanceDbContext(options, companyContext);
            var limits = new FinancePartyCreditLimitService(dbContext, companyContext, currentUser, new SyntheticPermissions(),
                new SyntheticCandidates(), new SyntheticParty(), collector,
                new FinancePartyCreditLimitEnvelopes(
                    new FinanceAuditEnvelope(ErpPermissions.FinanceCreditLimitsPropose, ["SENTETIK-FINANCE"], "Web"),
                    new FinanceAuditEnvelope(ErpPermissions.FinanceCreditLimitsApprove, ["SENTETIK-FINANCE"], "Web")),
                TimeProvider.System);
            return new Scope(dbContext, limits, new FinanceContext(company, actor, Correlation, null));
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

        private static DbContextOptions<FinanceDbContext> Options(SqlServerRelationalApiFactory factory) =>
            new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .Options;
    }

    private sealed class Scope(FinanceDbContext dbContext, FinancePartyCreditLimitService limits, FinanceContext context)
        : IAsyncDisposable
    {
        public FinancePartyCreditLimitService Limits => limits;
        public FinanceContext Context => context;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    /// <summary>SENTETIK: allows every Finance action. Not a grant; no credit-limit grant exists.</summary>
    private sealed class SyntheticPermissions : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }

    /// <summary>SENTETIK: one currency candidate. Not a Settings source.</summary>
    private sealed class SyntheticCandidates : ISettingsMoneyCandidateReader
    {
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MoneyCurrencyCandidateV1>>([new MoneyCurrencyCandidateV1(Currency, "SENTETIK", "SENTETIK para")]);
    }

    /// <summary>SENTETIK: Counterparty's answer — the one active party account of the calling company.</summary>
    private sealed class SyntheticParty : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId,
            Guid companyAccountId, CancellationToken cancellationToken) =>
            Task.FromResult(partyId == Party && companyAccountId == Account
                ? new ReadResult<PartyAccountEligibility>(ReadOutcome.Found,
                    new PartyAccountEligibility(Party, Account, context.CompanyId, true, IsPartyActive: true))
                : new ReadResult<PartyAccountEligibility>(ReadOutcome.NotFound, null));
    }

    private sealed class FixedCurrentUser(string userId, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    /// <summary>Holds each save until both writers have reached it, so both read the same state first.</summary>
    private sealed class BarrierInterceptor(Barrier barrier) : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return ValueTask.FromResult(result);
        }
    }
}

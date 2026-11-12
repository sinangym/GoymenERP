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
/// API-30 against a REAL SQL Server: the Finance cash account (kasa) master on Finance's own migrations
/// over the host's migrated and seeded database, with Audit's real save interceptor.
/// <para>
/// Permission and the Settings currency candidates are ISOLATED SYNTHETIC ports: no Finance grant
/// exists, so these tests prove the store and its rules, not a user outcome. Every identity and
/// currency below is SENTETIK / NOT_ENV test data. A cash account holds no balance and moves no money.
/// </para>
/// </summary>
public sealed class FinanceCashAccountSqlServerRelationalAcceptanceTests
{
    private const string Proposer = "SENTETIK-proposer";
    private const string Approver = "SENTETIK-approver";
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Currency = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5b01");

    [SqlServerFact]
    public async Task A_cash_account_is_active_only_after_another_user_approves_it_once_and_moves_no_money()
    {
        await using var run = await Run.StartAsync();

        CashAccountView proposed;
        await using (var a = run.Open(CompanyA, Proposer))
        {
            var result = await a.Accounts.ProposeAsync(a.Context, Key("p-1"),
                new CashAccountProposeRequest("SENTETIK kasa", Currency), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
            proposed = result.Resource!;
            Assert.Equal((CashAccountStatus.Proposed, false, "1"), (proposed.Status, proposed.IsActive, proposed.Version));

            var item = Assert.Single((await a.Accounts.ListAsync(a.Context, CancellationToken.None)).Resource!.Items);
            Assert.Equal((proposed.Id, true), (item.Account.Id, item.IsOwnProposal));

            // The proposer cannot approve their own proposal.
            var self = await a.Accounts.ApproveAsync(a.Context, Key("a-self"),
                new CashAccountApproveRequest(proposed.Id, "1"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Denied, self.Outcome);
            Assert.Equal("finance_approver_is_proposer", Assert.Single(self.Issues).Code);
        }

        await using (var b = run.Open(CompanyA, Approver))
        {
            var approved = await b.Accounts.ApproveAsync(b.Context, Key("a-1"),
                new CashAccountApproveRequest(proposed.Id, "1"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
            Assert.Equal((CashAccountStatus.Approved, true, "2"),
                (approved.Resource!.Status, approved.Resource.IsActive, approved.Resource.Version));

            // One decision only: a second is refused, whatever version it names.
            Assert.Equal(CommandOutcome.ValidationFailed, (await b.Accounts.ApproveAsync(b.Context, Key("a-2"),
                new CashAccountApproveRequest(proposed.Id, "2"), CancellationToken.None)).Outcome);
            Assert.Equal(CommandOutcome.VersionConflict, (await b.Accounts.ApproveAsync(b.Context, Key("a-3"),
                new CashAccountApproveRequest(proposed.Id, "1"), CancellationToken.None)).Outcome);

            var replay = await b.Accounts.ApproveAsync(b.Context, Key("a-1"),
                new CashAccountApproveRequest(proposed.Id, "1"), CancellationToken.None);
            Assert.True(replay.IsReplay);
            Assert.Equal("2", replay.Resource!.Version);
        }

        Assert.Equal(2, await run.ScalarAsync<int>("SELECT Status FROM finance.CashAccounts WHERE Id = @id", ("@id", proposed.Id)));
        Assert.Equal(Approver, await run.ScalarAsync<string>("SELECT DecidedBy FROM finance.CashAccounts WHERE Id = @id", ("@id", proposed.Id)));
        // Proposal, approval and the stale approval (a terminal refusal): three command records; the
        // self-approval and the not-proposed refusal store none.
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccountCommands"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.CashAccount' AND CorrelationId = @c", ("@c", run.Correlation)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.cash-account.approved.v1' AND ApprovalContextJson IS NOT NULL"));
        // A cash account is a definition: no money event, no balance.
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
    }

    [SqlServerFact]
    public async Task The_database_refuses_a_self_decision_and_a_second_live_account_of_the_same_name()
    {
        await using var run = await Run.StartAsync();

        // Without the service: the database refuses an approval by the proposer (547), binary-compared.
        var error = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "INSERT INTO finance.CashAccounts (Id, CompanyId, CurrencyId, DisplayName, Status, Version, ProposedBy, ProposedAtUtc, "
            + "DecidedBy, DecidedAtUtc, UpdatedAtUtc) VALUES (NEWID(), @c, @cur, N'SENTETIK', 2, 2, N'x', SYSDATETIMEOFFSET(), "
            + "N'x', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())", ("@c", CompanyA), ("@cur", Currency)));
        Assert.Equal(547, error.Number);
        Assert.Contains("CK_CashAccounts_Decision", error.Message, StringComparison.Ordinal);

        CashAccountView first;
        await using (var a = run.Open(CompanyA, Proposer))
        {
            first = (await a.Accounts.ProposeAsync(a.Context, Key("p-1"),
                new CashAccountProposeRequest("SENTETIK kasa", Currency), CancellationToken.None)).Resource!;
            var taken = await a.Accounts.ProposeAsync(a.Context, Key("p-2"),
                new CashAccountProposeRequest("SENTETIK kasa", Currency), CancellationToken.None);
            Assert.Equal("cash_account_name_taken", Assert.Single(taken.Issues).Code);
        }

        // Without the service: the filtered unique index refuses a second live row of the same name (2601).
        var duplicate = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "INSERT INTO finance.CashAccounts (Id, CompanyId, CurrencyId, DisplayName, Status, Version, ProposedBy, ProposedAtUtc, "
            + "UpdatedAtUtc) VALUES (NEWID(), @c, @cur, N'SENTETIK kasa', 1, 1, N'x', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())",
            ("@c", CompanyA), ("@cur", Currency)));
        Assert.Equal(2601, duplicate.Number);
        Assert.Contains("UX_CashAccounts_LiveName", duplicate.Message, StringComparison.Ordinal);

        // A rejection frees the name, and a read-only caller sees approved accounts only.
        await using (var b = run.Open(CompanyA, Approver))
        {
            var rejected = await b.Accounts.RejectAsync(b.Context, Key("r-1"),
                new CashAccountRejectRequest(first.Id, "1", "SENTETIK gerekçe"), CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, CashAccountStatus.Rejected), (rejected.Outcome, rejected.Resource!.Status));
        }

        CashAccountView second;
        await using (var a = run.Open(CompanyA, Proposer))
        {
            var again = await a.Accounts.ProposeAsync(a.Context, Key("p-3"),
                new CashAccountProposeRequest("SENTETIK kasa", Currency), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, again.Outcome);
            second = again.Resource!;
        }

        await using (var reader = run.Open(CompanyA, "SENTETIK-reader", new ReadOnlyPermissions()))
        {
            Assert.Empty((await reader.Accounts.ListAsync(reader.Context, CancellationToken.None)).Resource!.Items);
            Assert.Equal(ReadOutcome.NotFound, (await reader.Accounts.GetAsync(reader.Context, second.Id, CancellationToken.None)).Outcome);
        }

        await using (var b = run.Open(CompanyA, Approver))
        {
            Assert.Equal(CommandOutcome.Succeeded, (await b.Accounts.ApproveAsync(b.Context, Key("a-1"),
                new CashAccountApproveRequest(second.Id, "1"), CancellationToken.None)).Outcome);
        }

        await using (var reader = run.Open(CompanyA, "SENTETIK-reader", new ReadOnlyPermissions()))
        {
            var item = Assert.Single((await reader.Accounts.ListAsync(reader.Context, CancellationToken.None)).Resource!.Items);
            Assert.Equal((second.Id, CashAccountStatus.Approved), (item.Account.Id, item.Account.Status));
        }

        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccounts"));
        Assert.Equal("SENTETIK gerekçe", await run.ScalarAsync<string>(
            "SELECT RejectionReason FROM finance.CashAccounts WHERE Id = @id", ("@id", first.Id)));
    }

    [SqlServerFact]
    public async Task Company_B_can_neither_see_nor_approve_company_A_cash_account()
    {
        await using var run = await Run.StartAsync();
        CashAccountView accountA;
        await using (var a = run.Open(CompanyA, Proposer))
        {
            accountA = (await a.Accounts.ProposeAsync(a.Context, Key("p-1"),
                new CashAccountProposeRequest("SENTETIK A", Currency), CancellationToken.None)).Resource!;
        }

        await using (var b = run.Open(CompanyB, Approver))
        {
            Assert.Equal(ReadOutcome.NotFound, (await b.Accounts.GetAsync(b.Context, accountA.Id, CancellationToken.None)).Outcome);
            Assert.Empty((await b.Accounts.ListAsync(b.Context, CancellationToken.None)).Resource!.Items);
            var approve = await b.Accounts.ApproveAsync(b.Context, Key("a-1"),
                new CashAccountApproveRequest(accountA.Id, "1"), CancellationToken.None);
            Assert.Equal("cash_account_not_found", Assert.Single(approve.Issues).Code);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT Status FROM finance.CashAccounts WHERE Id = @id", ("@id", accountA.Id)));
        // A's proposal, and B's not-found as B's key's terminal answer: company B's, with no target.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.CashAccountCommands WHERE CompanyId = @b AND CashAccountId IS NULL", ("@b", CompanyB)));
    }

    [SqlServerFact]
    public async Task The_cash_account_migrations_Down_passes_on_an_empty_store_and_refuses_while_a_cash_account_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddCashAccounts().DownOperations[0]).Sql;

        await run.ExecuteAsync(guard);

        CashAccountView account;
        await using (var a = run.Open(CompanyA, Proposer))
        {
            account = (await a.Accounts.ProposeAsync(a.Context, Key("p-1"),
                new CashAccountProposeRequest("SENTETIK kasa", Currency), CancellationToken.None)).Resource!;
        }

        var error = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51051, error.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccounts WHERE Id = @id", ("@id", account.Id)));
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
        public string Correlation { get; } = $"api30-{Guid.NewGuid():N}";

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            // The host migrates and seeds Organization, Audit and the rest; Finance's own migrations
            // run after it, as their foreign keys need Organization's tables.
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            var companyContext = new CompanyContext();
            companyContext.Set(CompanyA);
            await using var context = new FinanceDbContext(Options(factory), companyContext);
            await context.Database.MigrateAsync();
            return run;
        }

        public Scope Open(Guid company, string actor) => Open(company, actor, new SyntheticPermissions());

        public Scope Open(Guid company, string actor, IFinancePermissionPort permissions)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var currentUser = new FixedCurrentUser(actor, company);
            var collector = new AuditEventCollector();
            var options = new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext))
                .Options;
            var dbContext = new FinanceDbContext(options, companyContext);
            var accounts = new FinanceCashAccountService(dbContext, companyContext, currentUser, permissions,
                new SyntheticCandidates(), collector,
                new FinanceCashAccountEnvelopes(
                    new FinanceAuditEnvelope(FinanceCashAccountService.ProposeCommand, ["SENTETIK-FINANCE"], "Web"),
                    new FinanceAuditEnvelope(FinanceCashAccountService.ApproveCommand, ["SENTETIK-FINANCE"], "Web")),
                TimeProvider.System);
            return new Scope(dbContext, accounts, new FinanceContext(company, actor, Correlation, null));
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

    private sealed class Scope(FinanceDbContext dbContext, FinanceCashAccountService accounts, FinanceContext context)
        : IAsyncDisposable
    {
        public FinanceCashAccountService Accounts => accounts;
        public FinanceContext Context => context;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    /// <summary>SENTETIK: allows every Finance action. Not a grant.</summary>
    private sealed class SyntheticPermissions : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }

    /// <summary>SENTETIK: holds only the cash-account read action, so non-approved accounts stay hidden.</summary>
    private sealed class ReadOnlyPermissions : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(
                action == FinanceAction.ReadCashAccounts ? PermissionDecision.Allowed : PermissionDecision.Denied);
    }

    /// <summary>SENTETIK: one currency candidate. Not a Settings source.</summary>
    private sealed class SyntheticCandidates : ISettingsMoneyCandidateReader
    {
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MoneyCurrencyCandidateV1>>([new MoneyCurrencyCandidateV1(Currency, "SENTETIK", "SENTETIK para")]);
    }

    private sealed class FixedCurrentUser(string userId, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}

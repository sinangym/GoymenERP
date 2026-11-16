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
/// FIN-S9 (FNS-D23) against a REAL SQL Server: the Finance opening balance store on Finance's own migrations over the
/// host's migrated and seeded database, with Audit's real save interceptor.
/// <para>
/// Permission, the Settings opening period, the Settings currency candidates and Counterparty's party account answer are
/// ISOLATED SYNTHETIC ports: no opening balance grant exists and Settings has no opening period adapter yet (the
/// registered default answers Unavailable), so these tests prove the store, its constraints and its rules, not a user
/// outcome. Every identity, amount and currency below is SENTETIK / NOT_ENV test data. An opening balance moves no money.
/// </para>
/// </summary>
public sealed class FinanceOpeningBalanceSqlServerRelationalAcceptanceTests
{
    private const string Specialist = "SENTETIK-specialist";
    private const string Manager = "SENTETIK-manager";
    private const string PeriodVersion = "SENTETIK-period-1";
    private const int DownGuardNumber = 51809;
    private const int CheckViolation = 547;
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Currency = Guid.Parse("9b3f4e5d-6c7b-4809-9b2c-3d4e5f6a7c01");
    private static readonly Guid Party = Guid.Parse("9b3f4e5d-6c7b-4809-9b2c-3d4e5f6a7c02");
    private static readonly Guid Account = Guid.Parse("9b3f4e5d-6c7b-4809-9b2c-3d4e5f6a7c03");
    private static readonly DateOnly AsOf = new(2026, 10, 31);

    [SqlServerFact]
    public async Task A_specialists_party_opening_is_approved_by_another_manager_in_one_save_each_with_its_attempt_and_audit()
    {
        await using var run = await Run.StartAsync();

        OpeningBalanceView proposed;
        await using (var specialist = run.Open(CompanyA, Specialist))
        {
            var result = await specialist.Openings.ProposeAsync(specialist.Context, Key("p-1"), Request("125000.40"),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
            proposed = result.Resource!;
            Assert.Equal((OpeningBalanceStatus.Proposed, "1", "125000.40", PeriodVersion),
                (proposed.Status, proposed.Version, proposed.Amount, proposed.OpeningPeriodVersion));

            var own = await specialist.Openings.ApproveAsync(specialist.Context, Key("a-own"),
                new OpeningBalanceApproveRequest(proposed.Id, "1"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Denied, own.Outcome);
        }

        await using (var manager = run.Open(CompanyA, Manager))
        {
            var approved = await manager.Openings.ApproveAsync(manager.Context, Key("a-1"),
                new OpeningBalanceApproveRequest(proposed.Id, "1"), CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, OpeningBalanceStatus.Approved, "2"),
                (approved.Outcome, approved.Resource!.Status, approved.Resource.Version));
        }

        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT [Status] FROM finance.OpeningBalances WHERE Id = @id", ("@id", proposed.Id)));
        Assert.Equal(Manager, await run.ScalarAsync<string>(
            "SELECT [DecidedBy] FROM finance.OpeningBalances WHERE Id = @id", ("@id", proposed.Id)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpeningBalanceCommands WHERE OpeningBalanceId = @id", ("@id", proposed.Id)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.OpeningBalance' AND EntityId = @id",
            ("@id", proposed.Id.ToString("D"))));
    }

    [SqlServerFact]
    public async Task The_database_refuses_a_proposers_own_decision_a_second_live_opening_and_a_zero_amount()
    {
        await using var run = await Run.StartAsync();
        OpeningBalanceView proposed;
        await using (var specialist = run.Open(CompanyA, Specialist))
        {
            proposed = (await specialist.Openings.ProposeAsync(specialist.Context, Key("p-1"), Request("10"),
                CancellationToken.None)).Resource!;
        }

        // The service's save guard is bypassed on purpose: the database checks are the authority.
        // CK_OpeningBalances_Decision: maker≠approver at person level.
        var own = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE finance.OpeningBalances SET [Status] = 2, DecidedBy = ProposedBy, DecidedAtUtc = SYSDATETIMEOFFSET(), "
            + "Version = Version + 1 WHERE Id = @id", ("@id", proposed.Id)));
        Assert.Equal(CheckViolation, own.Number);

        // UX_OpeningBalances_Live: one proposed or approved opening per target and currency.
        var live = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "INSERT INTO finance.OpeningBalances (Id, CompanyId, TargetKind, TargetId, PartyId, CurrencyId, Direction, "
            + "AmountText, AsOfDate, [Status], OpeningPeriodVersion, ProposedBy, ProposedAtUtc, DecidedBy, DecidedAtUtc, "
            + "RejectionReason, Version, UpdatedAtUtc) SELECT NEWID(), CompanyId, TargetKind, TargetId, PartyId, CurrencyId, "
            + "Direction, AmountText, AsOfDate, 1, OpeningPeriodVersion, ProposedBy, ProposedAtUtc, NULL, NULL, NULL, 1, "
            + "UpdatedAtUtc FROM finance.OpeningBalances WHERE Id = @id", ("@id", proposed.Id)));
        Assert.Contains(live.Number, new[] { 2601, 2627 });

        var amount = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE finance.OpeningBalances SET AmountText = '0.00' WHERE Id = @id", ("@id", proposed.Id)));
        Assert.Equal(CheckViolation, amount.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpeningBalances"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT [Status] FROM finance.OpeningBalances WHERE Id = @id", ("@id", proposed.Id)));
    }

    [SqlServerFact]
    public async Task Another_companys_opening_is_never_listed_or_decided()
    {
        await using var run = await Run.StartAsync();
        OpeningBalanceView proposed;
        await using (var specialist = run.Open(CompanyA, Specialist))
        {
            proposed = (await specialist.Openings.ProposeAsync(specialist.Context, Key("p-1"), Request("10"),
                CancellationToken.None)).Resource!;
        }

        await using (var other = run.Open(CompanyB, Manager))
        {
            var list = await other.Openings.ListAsync(other.Context, CancellationToken.None);
            Assert.Empty(list.Resource!.Items);
            var decided = await other.Openings.ApproveAsync(other.Context, Key("a-b"),
                new OpeningBalanceApproveRequest(proposed.Id, "1"), CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, decided.Outcome);
            Assert.Equal("opening_balance_not_found", Assert.Single(decided.Issues).Code);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT [Status] FROM finance.OpeningBalances WHERE Id = @id", ("@id", proposed.Id)));
    }

    [SqlServerFact]
    public async Task The_opening_balance_migrations_Down_passes_on_an_empty_store_and_refuses_while_an_opening_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddOpeningBalances().DownOperations[0]).Sql;

        await run.ExecuteAsync(guard);

        OpeningBalanceView proposed;
        await using (var specialist = run.Open(CompanyA, Specialist))
        {
            proposed = (await specialist.Openings.ProposeAsync(specialist.Context, Key("p-1"), Request("10"),
                CancellationToken.None)).Resource!;
        }

        // Forward fix: the rollback stops while an opening balance would be lost and deletes nothing.
        var error = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(DownGuardNumber, error.Number);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpeningBalances WHERE Id = @id", ("@id", proposed.Id)));
    }

    private static OpeningBalanceProposeRequest Request(string amount) =>
        new(OpeningBalanceTargetKind.PartyAccount, Account, Party, Currency, OpeningBalanceDirection.Debit, amount, AsOf);

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
        public string Correlation { get; } = $"fins9-{Guid.NewGuid():N}";

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

        public Scope Open(Guid company, string actor)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var options = new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext))
                .Options;
            var dbContext = new FinanceDbContext(options, companyContext);
            var openings = new FinanceOpeningBalanceService(dbContext, companyContext, new FixedCurrentUser(actor, company),
                new SyntheticPermissions(), new SyntheticCandidates(), new SyntheticParty(), new SyntheticOpenPeriod(),
                collector,
                new FinanceOpeningBalanceEnvelopes(
                    new FinanceAuditEnvelope(ErpPermissions.FinanceOpeningBalancesPropose, ["SENTETIK-FINANCE"], "Web"),
                    new FinanceAuditEnvelope(ErpPermissions.FinanceOpeningBalancesApprove, ["SENTETIK-FINANCE"], "Web")),
                TimeProvider.System);
            return new Scope(dbContext, openings, new FinanceContext(company, actor, Correlation, null));
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

    private sealed class Scope(FinanceDbContext dbContext, FinanceOpeningBalanceService openings, FinanceContext context)
        : IAsyncDisposable
    {
        public FinanceOpeningBalanceService Openings => openings;
        public FinanceContext Context => context;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    /// <summary>SENTETIK: allows every Finance action. Not a grant; no opening balance grant exists.</summary>
    private sealed class SyntheticPermissions : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }

    /// <summary>SENTETIK: an open period. Not a Settings source; the registered default answers Unavailable.</summary>
    private sealed class SyntheticOpenPeriod : IFinanceOpeningPeriodReader
    {
        public Task<FinanceOpeningPeriodAnswer> ReadAsync(Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult(new FinanceOpeningPeriodAnswer(OpeningPeriodState.Open, PeriodVersion));
    }

    /// <summary>SENTETIK: one currency candidate. Not a Settings source.</summary>
    private sealed class SyntheticCandidates : ISettingsMoneyCandidateReader
    {
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MoneyCurrencyCandidateV1>>([new MoneyCurrencyCandidateV1(Currency, "SENTETIK", "SENTETIK para")]);
    }

    /// <summary>SENTETIK: one active (party, company account) pair. Not a Counterparty source.</summary>
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
}

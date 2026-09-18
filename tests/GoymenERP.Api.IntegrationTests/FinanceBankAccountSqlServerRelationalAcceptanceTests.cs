using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// FS0 against a REAL SQL Server: the Finance bank account master and its authorized reader, on
/// Finance's own migrations over the host's migrated and seeded database, with Audit's real save
/// interceptor.
/// <para>
/// Permission and the Settings currency candidates are ISOLATED SYNTHETIC ports: neither a Finance
/// grant nor a Settings currency source exists on main, so these tests prove the store and its
/// rules, not a user outcome. The real unavailable Settings adapter refuses every proposal
/// (<see cref="A_currency_outside_the_settings_candidates_or_an_unavailable_source_stores_nothing"/>).
/// Every identity and currency below is SENTETIK / NOT_ENV test data.
/// </para>
/// </summary>
public sealed class FinanceBankAccountSqlServerRelationalAcceptanceTests
{
    private const string Operation = "SENTETIK-operation";
    private const string Proposer = "SENTETIK-proposer";
    private const string Approver = "SENTETIK-approver";
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Currency = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a91");
    private static readonly Guid ProfileId = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a92");

    [SqlServerFact]
    public async Task A_proposal_is_not_usable_until_another_user_approves_it_once_and_moves_no_money()
    {
        await using var run = await Run.StartAsync();

        BankAccountView proposed;
        await using (var a = run.Open(CompanyA, Proposer))
        {
            var result = await a.Accounts.ProposeAsync(a.Context, Key("p-1"), new BankAccountProposeRequest("SENTETIK hesap", Currency),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
            proposed = result.Resource!;
            Assert.Equal((false, false, "1"), (proposed.IsActive, proposed.IsUsableForReceipt, proposed.Version));

            // Listed for the approver to see, but a draft against a proposal is refused by the real reader.
            Assert.Equal(proposed.Id, Assert.Single((await a.Accounts.ListAuthorizedAsync(a.Context, CancellationToken.None)).Resource!.Items).Id);
            var draft = await a.Drafts.DraftSaveAsync(a.Context, Key("d-1"), Draft(proposed.Id), CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, draft.Outcome);
            Assert.Equal("bank_account_ineligible", Assert.Single(draft.Issues).Code);

            // Ö4: the proposer cannot approve their own proposal.
            var self = await a.Accounts.ApproveAsync(a.Context, Key("a-self"), new BankAccountApproveRequest(proposed.Id, "1"),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.Denied, self.Outcome);
            Assert.Equal("finance_approver_is_proposer", Assert.Single(self.Issues).Code);
        }

        await using (var b = run.Open(CompanyA, Approver))
        {
            var approved = await b.Accounts.ApproveAsync(b.Context, Key("a-1"), new BankAccountApproveRequest(proposed.Id, "1"),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
            Assert.Equal((true, true, "2"), (approved.Resource!.IsActive, approved.Resource.IsUsableForReceipt, approved.Resource.Version));

            // One approval only: a second is refused, whatever version it names.
            Assert.Equal(CommandOutcome.ValidationFailed, (await b.Accounts.ApproveAsync(b.Context, Key("a-2"),
                new BankAccountApproveRequest(proposed.Id, "2"), CancellationToken.None)).Outcome);
            Assert.Equal(CommandOutcome.VersionConflict, (await b.Accounts.ApproveAsync(b.Context, Key("a-3"),
                new BankAccountApproveRequest(proposed.Id, "1"), CancellationToken.None)).Outcome);
        }

        await using (var a = run.Open(CompanyA, Proposer))
        {
            // The same account is now usable: the real reader answers it and the draft saves against it.
            var read = await a.Accounts.GetAuthorizedAsync(a.Context, proposed.Id, CancellationToken.None);
            Assert.Equal((ReadOutcome.Found, true), (read.Outcome, read.Resource!.IsUsableForReceipt));
            Assert.Equal(CommandOutcome.Succeeded,
                (await a.Drafts.DraftSaveAsync(a.Context, Key("d-2"), Draft(proposed.Id), CancellationToken.None)).Outcome);
        }

        Assert.Equal(2, await run.ScalarAsync<int>("SELECT Status FROM finance.BankAccounts WHERE Id = @id", ("@id", proposed.Id)));
        Assert.Equal(Approver, await run.ScalarAsync<string>("SELECT ApprovedBy FROM finance.BankAccounts WHERE Id = @id", ("@id", proposed.Id)));
        // Proposal, approval and the second draft: three command records; refusals store none.
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands WHERE BankAccountId IS NOT NULL"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.BankAccount' AND CorrelationId = @c", ("@c", run.Correlation)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.bank-account.approved.v1' AND ApprovalContextJson IS NOT NULL"));
        // Money effect 0: the finance schema holds drafts, bank accounts and command records only.
        Assert.Equal("BankAccounts,Commands,ReceiptDrafts,__EFMigrationsHistory", await run.ScalarAsync<string>(
            "SELECT STRING_AGG(name, ',') WITHIN GROUP (ORDER BY name COLLATE Latin1_General_BIN2) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'finance')"));
    }

    [SqlServerFact]
    public async Task Company_B_can_neither_see_nor_approve_company_A_account()
    {
        await using var run = await Run.StartAsync();
        BankAccountView accountA;
        await using (var a = run.Open(CompanyA, Proposer))
        {
            accountA = (await a.Accounts.ProposeAsync(a.Context, Key("p-1"), new BankAccountProposeRequest("SENTETIK A", Currency),
                CancellationToken.None)).Resource!;
        }

        await using (var b = run.Open(CompanyB, Approver))
        {
            Assert.Equal(ReadOutcome.NotFound, (await b.Accounts.GetAuthorizedAsync(b.Context, accountA.Id, CancellationToken.None)).Outcome);
            Assert.Empty((await b.Accounts.ListAuthorizedAsync(b.Context, CancellationToken.None)).Resource!.Items);
            var approve = await b.Accounts.ApproveAsync(b.Context, Key("a-1"), new BankAccountApproveRequest(accountA.Id, "1"),
                CancellationToken.None);
            Assert.Equal("bank_account_not_found", Assert.Single(approve.Issues).Code);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT Status FROM finance.BankAccounts WHERE Id = @id", ("@id", accountA.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands"));
    }

    [SqlServerFact]
    public async Task The_same_key_replays_and_another_payload_or_actor_conflicts()
    {
        await using var run = await Run.StartAsync();
        BankAccountView first;
        await using (var a = run.Open(CompanyA, Proposer))
        {
            var request = new BankAccountProposeRequest("SENTETIK hesap", Currency);
            first = (await a.Accounts.ProposeAsync(a.Context, Key("k-1"), request, CancellationToken.None)).Resource!;
            var replay = await a.Accounts.ProposeAsync(a.Context, Key("k-1"), request, CancellationToken.None);
            Assert.True(replay.IsReplay);
            Assert.Equal(first.Id, replay.Resource!.Id);
            Assert.Equal(CommandOutcome.IdempotencyConflict, (await a.Accounts.ProposeAsync(a.Context, Key("k-1"),
                request with { DisplayName = "SENTETIK başka" }, CancellationToken.None)).Outcome);
            // ADR-011 §2: the key belongs to its command. The same key on another command is that
            // command's own attempt: here the draft is refused on its merits (a proposed account is not
            // usable), not as a replay or a conflict of the proposal.
            var draft = await a.Drafts.DraftSaveAsync(a.Context, Key("k-1"), Draft(first.Id), CancellationToken.None);
            Assert.Equal("bank_account_ineligible", Assert.Single(draft.Issues).Code);
        }

        await using (var other = run.Open(CompanyA, Approver))
        {
            // ADR-011 §3: the same key and payload by ANOTHER actor is a conflict that discloses nothing.
            var conflict = await other.Accounts.ProposeAsync(other.Context, Key("k-1"),
                new BankAccountProposeRequest("SENTETIK hesap", Currency), CancellationToken.None);
            Assert.Equal(CommandOutcome.IdempotencyConflict, conflict.Outcome);
            Assert.Null(conflict.Resource);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.BankAccounts"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ReceiptDrafts"));
    }

    [SqlServerFact]
    public async Task A_currency_outside_the_settings_candidates_or_an_unavailable_source_stores_nothing()
    {
        await using var run = await Run.StartAsync();
        await using (var a = run.Open(CompanyA, Proposer))
        {
            var outside = await a.Accounts.ProposeAsync(a.Context, Key("k-1"), new BankAccountProposeRequest("SENTETIK", Guid.NewGuid()),
                CancellationToken.None);
            Assert.Equal("currency_not_a_candidate", Assert.Single(outside.Issues).Code);
        }

        // The real adapter on main: no Settings currency source exists, so every proposal is refused.
        await using (var a = run.Open(CompanyA, Proposer, realSettings: true))
        {
            var unavailable = await a.Accounts.ProposeAsync(a.Context, Key("k-2"), new BankAccountProposeRequest("SENTETIK", Currency),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.Unavailable, unavailable.Outcome);
            Assert.Equal("settings_provider_unavailable", Assert.Single(unavailable.Issues).Code);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.BankAccounts"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.BankAccount'"));
    }

    [SqlServerFact]
    public async Task Two_racing_approvals_store_one_and_the_database_refuses_a_self_approval()
    {
        await using var run = await Run.StartAsync();
        BankAccountView proposed;
        await using (var a = run.Open(CompanyA, Proposer))
        {
            proposed = (await a.Accounts.ProposeAsync(a.Context, Key("p-1"), new BankAccountProposeRequest("SENTETIK", Currency),
                CancellationToken.None)).Resource!;
        }

        // Two approvers read the same proposal, then wait for each other at the save.
        var barrier = new Barrier(2);
        async Task<CommandOutcome> ApproveAsync(string approver)
        {
            await using var scope = run.Open(CompanyA, approver, new BarrierInterceptor(barrier));
            return (await scope.Accounts.ApproveAsync(scope.Context, Key("a-" + approver),
                new BankAccountApproveRequest(proposed.Id, "1"), CancellationToken.None)).Outcome;
        }

        var results = await Task.WhenAll(Task.Run(() => ApproveAsync(Approver)), Task.Run(() => ApproveAsync("SENTETIK-approver-2")));
        Assert.Single(results, outcome => outcome == CommandOutcome.Succeeded);
        Assert.Single(results, outcome => outcome == CommandOutcome.VersionConflict);
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM finance.BankAccounts WHERE Id = @id", ("@id", proposed.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.bank-account.approved.v1'"));

        // Without the service: the database itself refuses an approval by the proposer (547).
        var error = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "INSERT INTO finance.BankAccounts (Id, CompanyId, CurrencyId, DisplayName, Status, Version, ProposedBy, ProposedAtUtc, "
            + "ApprovedBy, ApprovedAtUtc, UpdatedAtUtc) VALUES (NEWID(), @c, @cur, N'SENTETIK', 2, 2, N'x', SYSDATETIMEOFFSET(), "
            + "N'x', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())", ("@c", CompanyA), ("@cur", Currency)));
        Assert.Equal(547, error.Number);
        Assert.Contains("CK_BankAccounts_Approval", error.Message, StringComparison.Ordinal);
    }

    [SqlServerFact]
    public async Task A_save_that_fails_in_the_database_leaves_nothing_behind()
    {
        await using var run = await Run.StartAsync();
        // A company Organization does not know: the foreign key refuses the save (547), and the
        // account, the command record and the audit event roll back together.
        var unknownCompany = Guid.NewGuid();
        await using (var scope = run.Open(unknownCompany, Proposer))
        {
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => scope.Accounts.ProposeAsync(scope.Context, Key("k-1"),
                new BankAccountProposeRequest("SENTETIK", Currency), CancellationToken.None));
            Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.BankAccounts"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.BankAccount'"));
    }

    private static CommandMetadata Key(string key) => new CommandMetadata(key);

    private static DraftSaveRequest Draft(Guid bankAccount) =>
        new DraftSaveRequest(null, null, new ReceiptPreparation(
            Parties.Party, Parties.Account, bankAccount, new MoneyText("1000", Currency),
            new ProfileReference(ProfileId.ToString("D"), "1"), new DateOnly(2026, 9, 13), null, null, null));

    private static class Parties
    {
        public static readonly Guid Party = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a93");
        public static readonly Guid Account = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a94");
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
        public string Correlation { get; } = $"fs0-{Guid.NewGuid():N}";

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

        public Scope Open(Guid company, string actor, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] extra) =>
            Open(company, actor, realSettings: false, extra);

        public Scope Open(Guid company, string actor, bool realSettings,
            params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] extra)
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
            var permissions = new SyntheticPermissions();
            ISettingsMoneyCandidateReader currencies = realSettings
                ? new UnavailableSettingsPrerequisiteProvider(companyContext, currentUser)
                : new SyntheticCandidates();
            var accounts = new FinanceBankAccountService(dbContext, companyContext, currentUser, permissions, currencies, collector,
                new FinanceAuditEnvelope(FinanceBankAccountService.ProposeCommand, ["SENTETIK-FINANCE"], "Web"), TimeProvider.System);
            // The draft service reads bank accounts through the REAL persisted reader above.
            var drafts = new ReceiptDraftService(dbContext, companyContext, currentUser, permissions, accounts,
                new SyntheticParty(company), new SyntheticSettings(company), collector, new FinanceReceiptDraftBinding(Operation),
                new FinanceAuditEnvelope(ReceiptDraftService.SaveDraftCommand, ["SENTETIK-FINANCE"], "Web"), TimeProvider.System);
            return new Scope(dbContext, accounts, drafts, new FinanceContext(company, actor, Correlation, null));
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

    private sealed class Scope(FinanceDbContext dbContext, FinanceBankAccountService accounts, ReceiptDraftService drafts,
        FinanceContext context) : IAsyncDisposable
    {
        public FinanceBankAccountService Accounts => accounts;
        public IManualReceiptService Drafts => drafts;
        public FinanceContext Context => context;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    /// <summary>SENTETIK: allows every Finance action. Not a grant; there is no Finance permission on main.</summary>
    private sealed class SyntheticPermissions : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }

    /// <summary>SENTETIK: one currency candidate. Not a Settings source; none exists on main.</summary>
    private sealed class SyntheticCandidates : ISettingsMoneyCandidateReader
    {
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MoneyCurrencyCandidateV1>>([new MoneyCurrencyCandidateV1(Currency, "SENTETIK", "SENTETIK para")]);
    }

    /// <summary>SENTETIK: one active party account of the company.</summary>
    private sealed class SyntheticParty(Guid company) : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId,
            Guid companyAccountId, CancellationToken cancellationToken) =>
            Task.FromResult(partyId == Parties.Party && companyAccountId == Parties.Account
                ? new ReadResult<PartyAccountEligibility>(ReadOutcome.Found,
                    new PartyAccountEligibility(Parties.Party, Parties.Account, company, true))
                : new ReadResult<PartyAccountEligibility>(ReadOutcome.NotFound, null));
    }

    /// <summary>SENTETIK: a money profile of scale 2 for the test currency — a test input, not a production scale.</summary>
    private sealed class SyntheticSettings(Guid company) : ISettingsPrerequisiteProvider
    {
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MoneyProfileV1(company, currencyId, "SENTETIK", operation,
                new ProfileVersionV1(ProfileId, 1), 2, true, new DateOnly(2026, 1, 1), null));

        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
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

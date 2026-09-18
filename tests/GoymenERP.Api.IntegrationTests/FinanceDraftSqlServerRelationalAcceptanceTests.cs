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
/// FIN-T04a against a REAL SQL Server: Finance's own migration on top of the host's migrated and
/// seeded database, and Audit's real save interceptor.
/// <para>
/// The positive paths use ISOLATED SYNTHETIC owner ports (bank account, party account, money
/// profile, permission): none of these owners exists on main, so these tests prove the draft store
/// only. They are not a finance delivery and claim no user outcome. The real adapters on main
/// refuse every save and store nothing (<see cref="The_real_unavailable_owners_refuse_every_save_and_store_nothing"/>).
/// Every identity, scale and currency below is SENTETIK / NOT_ENV test data.
/// </para>
/// </summary>
public sealed class FinanceDraftSqlServerRelationalAcceptanceTests
{
    private const string Operation = "SENTETIK-operation";
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Currency = Guid.Parse("6e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a81");
    private static readonly Guid ProfileId = Guid.Parse("6e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a82");

    [SqlServerFact]
    public async Task A_draft_is_saved_reopened_and_saved_again_with_a_fresh_version_and_moves_no_money()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);

        SourceDetail created;
        await using (var a = run.Service(CompanyA, owners))
        {
            var result = await a.Service.DraftSaveAsync(a.Context, Key("k-1"), Save(owners, "1000.5"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
            created = result.Resource!;
            Assert.Equal(SourceStage.Draft, created.Source.Stage);
            Assert.Equal("1", created.Source.Version);
            Assert.Equal("1000.5", created.Source.Amount.Money!.Amount);
            // A draft has no allocation fact: unknown, never 0.
            Assert.Equal(AllocationKnowledge.Unavailable, created.Unallocated.Knowledge);
            Assert.Null(created.RecordedAtUtc);
        }

        await using (var a = run.Service(CompanyA, owners))
        {
            var reopened = await a.Service.DetailAuthorizedAsync(a.Context, new SourceDetailRequest(created.Source.Id), CancellationToken.None);
            Assert.Equal(ReadOutcome.Found, reopened.Outcome);
            Assert.Equal(created.Source.Version, reopened.Resource!.Source.Version);
            Assert.Equal("1000.5", reopened.Resource.Source.Amount.Money!.Amount);

            var updated = await a.Service.DraftSaveAsync(a.Context, Key("k-2"),
                Save(owners, "1250.25", created.Source.Id, created.Source.Version, description: "SENTETIK açıklama"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, updated.Outcome);
            Assert.Equal("2", updated.Resource!.Source.Version);
            Assert.Equal("1250.25", updated.Resource.Source.Amount.Money!.Amount);
            Assert.Equal("SENTETIK açıklama", updated.Resource.Description);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ReceiptDrafts"));
        Assert.Equal("1250.25", await run.ScalarAsync<string>("SELECT AmountText FROM finance.ReceiptDrafts"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.ReceiptDraft' AND CorrelationId = @c", ("@c", run.Correlation)));
        // Money effect 0: the finance schema holds drafts, command records and the FS0 bank-account
        // master only (beside its own migration history); no source, balance or journal table.
        Assert.Equal("BankAccounts,Commands,ReceiptDrafts,__EFMigrationsHistory", await run.ScalarAsync<string>(
            "SELECT STRING_AGG(name, ',') WITHIN GROUP (ORDER BY name COLLATE Latin1_General_BIN2) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'finance')"));
    }

    [SqlServerFact]
    public async Task A_stale_version_changes_nothing_and_the_same_key_replays_or_conflicts()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        SourceDetail created;
        await using (var a = run.Service(CompanyA, owners))
        {
            created = (await a.Service.DraftSaveAsync(a.Context, Key("k-1"), Save(owners, "1000"), CancellationToken.None)).Resource!;
        }

        await using (var a = run.Service(CompanyA, owners))
        {
            // Same key, same payload: the stored result, as a replay, with no second draft.
            var replay = await a.Service.DraftSaveAsync(a.Context, Key("k-1"), Save(owners, "1000"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, replay.Outcome);
            Assert.True(replay.IsReplay);
            Assert.Equal(created.Source.Id, replay.Resource!.Source.Id);
            // Same key, another payload: refused.
            Assert.Equal(CommandOutcome.IdempotencyConflict,
                (await a.Service.DraftSaveAsync(a.Context, Key("k-1"), Save(owners, "999"), CancellationToken.None)).Outcome);
        }

        await using (var a = run.Service(CompanyA, owners))
        {
            Assert.Equal(CommandOutcome.Succeeded, (await a.Service.DraftSaveAsync(a.Context, Key("k-2"),
                Save(owners, "1100", created.Source.Id, created.Source.Version), CancellationToken.None)).Outcome);
            // The first version again is stale now: nothing is overwritten.
            Assert.Equal(CommandOutcome.VersionConflict, (await a.Service.DraftSaveAsync(a.Context, Key("k-3"),
                Save(owners, "1", created.Source.Id, created.Source.Version), CancellationToken.None)).Outcome);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ReceiptDrafts"));
        Assert.Equal("1100", await run.ScalarAsync<string>("SELECT AmountText FROM finance.ReceiptDrafts"));
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM finance.ReceiptDrafts"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands"));
    }

    [SqlServerFact]
    public async Task Company_B_can_neither_see_nor_overwrite_company_A_draft()
    {
        await using var run = await Run.StartAsync();
        var ownersA = Owners.For(CompanyA);
        SourceDetail draftA;
        await using (var a = run.Service(CompanyA, ownersA))
        {
            draftA = (await a.Service.DraftSaveAsync(a.Context, Key("shared-key"), Save(ownersA, "1000"), CancellationToken.None)).Resource!;
        }

        var ownersB = Owners.For(CompanyB);
        await using (var b = run.Service(CompanyB, ownersB))
        {
            Assert.Equal(ReadOutcome.NotFound,
                (await b.Service.DetailAuthorizedAsync(b.Context, new SourceDetailRequest(draftA.Source.Id), CancellationToken.None)).Outcome);
            var overwrite = await b.Service.DraftSaveAsync(b.Context, Key("k-b"),
                Save(ownersB, "1", draftA.Source.Id, draftA.Source.Version), CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, overwrite.Outcome);
            Assert.Contains(overwrite.Issues, issue => issue.Code == "draft_not_found");
            // The same idempotency key in another company is its own attempt.
            Assert.Equal(CommandOutcome.Succeeded,
                (await b.Service.DraftSaveAsync(b.Context, Key("shared-key"), Save(ownersB, "1000"), CancellationToken.None)).Outcome);
            // A context whose company is not the server's is refused before anything is read.
            var foreign = new FinanceContext(CompanyA, b.Context.ActorId, b.Context.CorrelationId, null);
            Assert.Equal(CommandOutcome.Denied,
                (await b.Service.DraftSaveAsync(foreign, Key("k-x"), Save(ownersA, "1"), CancellationToken.None)).Outcome);
        }

        Assert.Equal("1000", await run.ScalarAsync<string>("SELECT AmountText FROM finance.ReceiptDrafts WHERE Id = @id", ("@id", draftA.Source.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ReceiptDrafts WHERE CompanyId = @c", ("@c", CompanyA)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ReceiptDrafts WHERE CompanyId = @c", ("@c", CompanyB)));
    }

    [SqlServerFact]
    public async Task The_real_unavailable_owners_refuse_every_save_and_store_nothing()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);

        // The permission port on main: refused before any owner is read.
        await using (var a = run.Service(CompanyA, owners, permissions: new UnavailableFinancePermissionPort()))
        {
            var result = await a.Service.DraftSaveAsync(a.Context, Key("k-1"), Save(owners, "1000"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Unavailable, result.Outcome);
            Assert.Contains(result.Issues, issue => issue.Code == "finance_permission_mapping_unavailable");
        }

        // With a synthetic permission, the bank account reader on main: bank_account_unavailable.
        await using (var a = run.Service(CompanyA, owners, realBankReader: true))
        {
            var result = await a.Service.DraftSaveAsync(a.Context, Key("k-2"), Save(owners, "1000"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Unavailable, result.Outcome);
            Assert.Contains(result.Issues, issue => issue.Code == "bank_account_unavailable");
        }

        // With a synthetic bank account, the Settings provider on main: settings_provider_unavailable.
        await using (var a = run.Service(CompanyA, owners, realSettings: true))
        {
            var result = await a.Service.DraftSaveAsync(a.Context, Key("k-3"), Save(owners, "1000"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Unavailable, result.Outcome);
            Assert.Contains(result.Issues, issue => issue.Code == "settings_provider_unavailable");
        }

        // More decimals than the (synthetic) profile scale: refused, never rounded.
        await using (var a = run.Service(CompanyA, owners))
        {
            var result = await a.Service.DraftSaveAsync(a.Context, Key("k-4"), Save(owners, "1.001"), CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, result.Outcome);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ReceiptDrafts"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.ReceiptDraft'"));
    }

    [SqlServerFact]
    public async Task A_save_that_fails_in_the_database_leaves_nothing_behind()
    {
        await using var run = await Run.StartAsync();
        // A company Organization does not know: the foreign key to organization.Companies refuses the
        // save (547), and the draft, the command record and the audit event roll back together.
        var unknownCompany = Guid.NewGuid();
        var owners = Owners.For(unknownCompany);
        await using (var store = run.Service(unknownCompany, owners))
        {
            var error = await Assert.ThrowsAsync<DbUpdateException>(
                () => store.Service.DraftSaveAsync(store.Context, Key("k-1"), Save(owners, "1000"), CancellationToken.None));
            Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ReceiptDrafts"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.ReceiptDraft'"));
    }

    private static CommandMetadata Key(string key) => new CommandMetadata(key);

    private static DraftSaveRequest Save(Owners owners, string amount, Guid? draftId = null, string? expectedVersion = null,
        string? description = null) =>
        new DraftSaveRequest(draftId, expectedVersion, new ReceiptPreparation(
            owners.Party, owners.Account, owners.Bank, new MoneyText(amount, Currency),
            new ProfileReference(ProfileId.ToString("D"), "1"), new DateOnly(2026, 9, 13), null, "SENTETIK-REF", description));

    /// <summary>The synthetic owner identities of one company.</summary>
    private sealed record Owners(Guid Company, Guid Bank, Guid Party, Guid Account)
    {
        public static Owners For(Guid company) => new Owners(company, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
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
        public string Actor { get; } = Guid.NewGuid().ToString("D");
        public string Correlation { get; } = $"fin-t04a-{Guid.NewGuid():N}";

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            // The host migrates and seeds Organization, Audit and the rest; Finance's own migration
            // runs after it, as its foreign keys need Organization's tables.
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            var companyContext = new CompanyContext();
            companyContext.Set(CompanyA);
            await using var context = new FinanceDbContext(Options(factory), companyContext);
            await context.Database.MigrateAsync();
            return run;
        }

        public ServiceScope Service(Guid company, Owners owners, IFinancePermissionPort? permissions = null,
            bool realBankReader = false, bool realSettings = false)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var currentUser = new FixedCurrentUser(Actor, company);
            var collector = new AuditEventCollector();
            var options = new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext))
                .Options;
            var dbContext = new FinanceDbContext(options, companyContext);
            IFinanceBankAccountReader banks = realBankReader
                ? new UnavailableFinanceBankAccountReader(companyContext, currentUser)
                : new SyntheticBank(owners);
            ISettingsPrerequisiteProvider settings = realSettings
                ? new UnavailableSettingsPrerequisiteProvider(companyContext, currentUser)
                : new SyntheticSettings(company);
            var service = new ReceiptDraftService(dbContext, companyContext, currentUser,
                permissions ?? new SyntheticPermissions(), banks, new SyntheticParty(owners), settings, collector,
                new FinanceReceiptDraftBinding(Operation),
                new FinanceAuditEnvelope(ReceiptDraftService.SaveDraftCommand, ["SENTETIK-FINANCE"], "Web"),
                TimeProvider.System);
            return new ServiceScope(dbContext, service, new FinanceContext(company, Actor, Correlation, null));
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

    private sealed class ServiceScope(FinanceDbContext dbContext, ReceiptDraftService service, FinanceContext context)
        : IAsyncDisposable
    {
        public IManualReceiptService Service => service;
        public FinanceContext Context => context;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    /// <summary>SENTETIK: allows every Finance action. Not a grant; there is no Finance permission on main.</summary>
    private sealed class SyntheticPermissions : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }

    /// <summary>SENTETIK: one usable bank account of the company in the test currency.</summary>
    private sealed class SyntheticBank(Owners owners) : IFinanceBankAccountReader
    {
        public Task<ReadResult<BankAccountPage>> ListAuthorizedAsync(FinanceContext context, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ReadResult<BankAccountView>> GetAuthorizedAsync(FinanceContext context, Guid accountId,
            CancellationToken cancellationToken) =>
            Task.FromResult(accountId == owners.Bank
                ? new ReadResult<BankAccountView>(ReadOutcome.Found,
                    new BankAccountView(owners.Bank, owners.Company, Currency, "SENTETIK banka", true, true, "1"))
                : new ReadResult<BankAccountView>(ReadOutcome.NotFound, null));
    }

    /// <summary>SENTETIK: one active party account of the company.</summary>
    private sealed class SyntheticParty(Owners owners) : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId,
            Guid companyAccountId, CancellationToken cancellationToken) =>
            Task.FromResult(partyId == owners.Party && companyAccountId == owners.Account
                ? new ReadResult<PartyAccountEligibility>(ReadOutcome.Found,
                    new PartyAccountEligibility(owners.Party, owners.Account, owners.Company, true))
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
}

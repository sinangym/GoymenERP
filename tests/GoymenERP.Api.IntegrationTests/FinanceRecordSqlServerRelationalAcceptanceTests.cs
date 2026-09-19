using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// FIN-T05a against a REAL SQL Server: the Record of a realized manual bank receipt (card #66 5743678389).
/// <para>
/// Q1 — which date decides a recorded receipt's period (#66 5745134466) — is OPEN, so the product's only period rule
/// decides nothing and every Record is refused (<see cref="Undecided_period_rule_refuses_every_record_and_stores_nothing"/>).
/// The other cases inject <see cref="SyntheticOpenPeriodRule"/>, a TEST INPUT that answers "open" for any date: it
/// proves the one-event write, the ADR-011 answers and the race, NOT a period policy. Owner ports (bank account, party
/// account, money profile, permission) are isolated synthetic ones, as in the draft tests. Every identity, scale and
/// currency is SENTETIK / NOT_ENV test data. This is not a finance delivery and claims no user outcome.
/// </para>
/// </summary>
public sealed class FinanceRecordSqlServerRelationalAcceptanceTests
{
    private const string Operation = "SENTETIK-operation";
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Currency = Guid.Parse("6e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a91");
    private static readonly Guid ProfileId = Guid.Parse("6e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a92");
    private static readonly DateOnly OccurredOn = new DateOnly(2026, 9, 21);
    private static readonly DateOnly ValueOn = new DateOnly(2026, 9, 22);

    [SqlServerFact]
    public async Task Undecided_period_rule_refuses_every_record_and_stores_nothing()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var draft = await run.SaveDraftAsync(owners, "1250.50");

        await using (var a = run.Service(CompanyA, owners, productRule: true))
        {
            var refused = await a.Service.RecordAsync(a.Context, Key("r-1"), new RecordRequest(draft.Id, draft.Version), CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
            Assert.Equal(new ValidationIssue("period", RecordPeriodAnswer.RuleUnavailableCode), Assert.Single(refused.Issues));
            Assert.Null(refused.Resource);
        }

        // Nothing moved: no money event, the source is still a draft at its version, and the refusal is not the
        // key's answer (a corrected retry is measured again).
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.Equal((0, 1L), (await run.ScalarAsync<int>("SELECT Stage FROM finance.ReceiptDrafts"),
            await run.ScalarAsync<long>("SELECT Version FROM finance.ReceiptDrafts")));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands WHERE CommandName = @c",
            ("@c", ReceiptDraftService.RecordCommand)));
    }

    [SqlServerFact]
    public async Task A_record_makes_one_money_event_and_every_answer_of_its_key_holds()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var draft = await run.SaveDraftAsync(owners, "1250.50");

        CommandResult recorded;
        await using (var a = run.Service(CompanyA, owners))
        {
            recorded = await a.Service.RecordAsync(a.Context, Key("r-1"), new RecordRequest(draft.Id, draft.Version), CancellationToken.None);
        }

        Assert.Equal(CommandOutcome.Succeeded, recorded.Outcome);
        var source = recorded.Resource!;
        Assert.Equal((SourceStage.Recorded, "2"), (source.Source.Stage, source.Source.Version));
        Assert.NotNull(source.RecordedAtUtc);
        // No allocation relation exists yet: unallocated = the source amount − 0 active allocations, known and exact.
        Assert.Equal((AllocationKnowledge.Known, "1250.50"), (source.Unallocated.Knowledge, source.Unallocated.Amount.Money!.Amount));

        // ONE money event, its facts kept apart as Ö5 keeps them; the source records who and when.
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents WHERE SourceId = @s", ("@s", draft.Id)));
        Assert.Equal("1250.50", await run.ScalarAsync<string>("SELECT AmountText FROM finance.MoneyEvents"));
        Assert.Equal(OccurredOn.ToDateTime(TimeOnly.MinValue), await run.ScalarAsync<DateTime>("SELECT OccurredOn FROM finance.MoneyEvents"));
        Assert.Equal(ValueOn.ToDateTime(TimeOnly.MinValue), await run.ScalarAsync<DateTime>("SELECT ValueOn FROM finance.MoneyEvents"));
        Assert.Equal(run.Actor, await run.ScalarAsync<string>("SELECT RecordedBy FROM finance.MoneyEvents"));
        Assert.Equal((1, run.Actor), (await run.ScalarAsync<int>("SELECT Stage FROM finance.ReceiptDrafts"),
            await run.ScalarAsync<string>("SELECT RecordedBy FROM finance.ReceiptDrafts")));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.manual-receipt.recorded.v1' AND EntityId = @s",
            ("@s", draft.Id.ToString("D"))));

        await using (var a = run.Service(CompanyA, owners, correlation: "SENTETIK-retry"))
        {
            // Same key, same request: the stored answer as a replay with the FIRST correlation, and no second event.
            var replay = await a.Service.RecordAsync(a.Context, Key("r-1"), new RecordRequest(draft.Id, draft.Version), CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, true, run.Correlation), (replay.Outcome, replay.IsReplay, replay.OriginalCorrelationId));
            // Same key, another request: a conflict that discloses nothing.
            Assert.Equal(CommandOutcome.IdempotencyConflict,
                (await a.Service.RecordAsync(a.Context, Key("r-1"), new RecordRequest(draft.Id, "2"), CancellationToken.None)).Outcome);
            // A new key at the stale version: the key's terminal answer (§7).
            Assert.Equal(CommandOutcome.VersionConflict,
                (await a.Service.RecordAsync(a.Context, Key("r-2"), new RecordRequest(draft.Id, draft.Version), CancellationToken.None)).Outcome);
            // A new key at the current version: recorded already, not a draft — refused, and not the key's answer.
            var again = await a.Service.RecordAsync(a.Context, Key("r-3"), new RecordRequest(draft.Id, "2"), CancellationToken.None);
            Assert.Equal((CommandOutcome.ValidationFailed, "source_not_draft"), (again.Outcome, again.Issues.Single().Code));
            // A recorded source is never edited again.
            var edit = await a.Service.DraftSaveAsync(a.Context, Key("s-edit"), Save(owners, "1", draft.Id, "2"), CancellationToken.None);
            Assert.Equal((CommandOutcome.ValidationFailed, "source_not_draft"), (edit.Outcome, edit.Issues.Single().Code));
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM finance.ReceiptDrafts"));
        // Record commands kept: the success and the stale version's terminal answer.
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands WHERE CommandName = @c",
            ("@c", ReceiptDraftService.RecordCommand)));

        // Company B never records company A's source.
        await using (var b = run.Service(CompanyB, owners))
        {
            var foreign = await b.Service.RecordAsync(b.Context, Key("r-b"), new RecordRequest(draft.Id, "2"), CancellationToken.None);
            Assert.Equal((CommandOutcome.ValidationFailed, "draft_not_found"), (foreign.Outcome, foreign.Issues.Single().Code));
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
    }

    [SqlServerFact]
    public async Task Two_records_of_one_source_at_once_make_one_money_event()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var draft = await run.SaveDraftAsync(owners, "700");

        // Two operators' Records of the same source under two keys, at once: whatever the interleaving (a lost
        // version, a lost unique index, or the second reading a recorded source), exactly one money event.
        async Task<CommandResult> RecordAsync(string key, string actor)
        {
            await using var scope = run.Service(CompanyA, owners, actor: actor);
            return await scope.Service.RecordAsync(scope.Context, Key(key), new RecordRequest(draft.Id, draft.Version), CancellationToken.None);
        }

        var results = await Task.WhenAll(
            Task.Run(() => RecordAsync("race-a", "SENTETIK-operator-a")),
            Task.Run(() => RecordAsync("race-b", "SENTETIK-operator-b")));

        Assert.Single(results, result => result.Outcome == CommandOutcome.Succeeded && !result.IsReplay);
        Assert.Single(results, result => result.Outcome is CommandOutcome.VersionConflict or CommandOutcome.ValidationFailed);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents WHERE SourceId = @s", ("@s", draft.Id)));
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM finance.ReceiptDrafts"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.manual-receipt.recorded.v1'"));
    }

    [SqlServerFact]
    public async Task The_money_event_migration_rolls_back_only_while_no_money_fact_is_stored()
    {
        await using var run = await Run.StartAsync();
        const string beforeMoneyEvents = "20260919110000_RecordTerminalCommandResults";

        await using (var context = run.Context(CompanyA))
        {
            await context.GetService<IMigrator>().MigrateAsync(beforeMoneyEvents);
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.tables WHERE [name] = N'MoneyEvents' AND [schema_id] = SCHEMA_ID(N'finance')"));
            await context.Database.MigrateAsync();
        }

        var owners = Owners.For(CompanyA);
        var draft = await run.SaveDraftAsync(owners, "10");
        await using (var a = run.Service(CompanyA, owners))
        {
            Assert.Equal(CommandOutcome.Succeeded, (await a.Service.RecordAsync(a.Context, Key("r-1"),
                new RecordRequest(draft.Id, draft.Version), CancellationToken.None)).Outcome);
        }

        await using (var context = run.Context(CompanyA))
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(beforeMoneyEvents));
            Assert.Equal(51051, error.Number);
            Assert.EndsWith("_AddMoneyEvents", (await context.Database.GetAppliedMigrationsAsync()).Last(), StringComparison.Ordinal);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
    }

    private static CommandMetadata Key(string key) => new CommandMetadata(key);

    private static DraftSaveRequest Save(Owners owners, string amount, Guid? draftId = null, string? expectedVersion = null) =>
        new DraftSaveRequest(draftId, expectedVersion, new ReceiptPreparation(
            owners.Party, owners.Account, owners.Bank, new MoneyText(amount, Currency),
            new ProfileReference(ProfileId.ToString("D"), "1"), OccurredOn, ValueOn, "SENTETIK-REF", null));

    private sealed record Owners(Guid Company, Guid Bank, Guid Party, Guid Account)
    {
        public static Owners For(Guid company) => new Owners(company, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    }

    private sealed record SavedDraft(Guid Id, string Version);

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
        public string Correlation { get; } = $"fin-t05a-{Guid.NewGuid():N}";

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

        public FinanceDbContext Context(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .Options, companyContext);
        }

        public async Task<SavedDraft> SaveDraftAsync(Owners owners, string amount)
        {
            await using var a = Service(owners.Company, owners);
            var saved = await a.Service.DraftSaveAsync(a.Context, Key($"save-{Guid.NewGuid():N}"), Save(owners, amount), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, saved.Outcome);
            return new SavedDraft(saved.Resource!.Source.Id, saved.Resource.Source.Version);
        }

        /// <summary>
        /// A service scope. By default its period rule is the <see cref="SyntheticOpenPeriodRule"/> test input;
        /// <paramref name="productRule"/> uses the product's only rule, which decides nothing while Q1 is open.
        /// </summary>
        public ServiceScope Service(Guid company, Owners owners, string? actor = null, string? correlation = null,
            bool productRule = false)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var currentUser = new FixedCurrentUser(actor ?? Actor, company);
            var collector = new AuditEventCollector();
            var options = new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext))
                .Options;
            var dbContext = new FinanceDbContext(options, companyContext);
            var service = new ReceiptDraftService(dbContext, companyContext, currentUser,
                new SyntheticPermissions(), new SyntheticBank(owners), new SyntheticParty(owners), new SyntheticSettings(company),
                collector, new FinanceReceiptDraftBinding(Operation),
                new FinanceAuditEnvelope(ReceiptDraftService.SaveDraftCommand, ["SENTETIK-FINANCE"], "Web"),
                TimeProvider.System,
                new FinanceAuditEnvelope(ReceiptDraftService.RecordCommand, ["SENTETIK-FINANCE"], "Web"),
                productRule ? new UndecidedFinanceRecordPeriodRule() : new SyntheticOpenPeriodRule());
            return new ServiceScope(dbContext, service, new FinanceContext(company, actor ?? Actor, correlation ?? Correlation, null));
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
    }

    private sealed class ServiceScope(FinanceDbContext dbContext, ReceiptDraftService service, FinanceContext context)
        : IAsyncDisposable
    {
        public IManualReceiptService Service => service;
        public FinanceContext Context => context;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    /// <summary>TEST INPUT: answers "open" for any date. Not a period policy — Q1 (#66 5745134466) decides the date.</summary>
    private sealed class SyntheticOpenPeriodRule : IFinanceRecordPeriodRule
    {
        public Task<RecordPeriodAnswer> CheckAsync(FinanceContext context, DateOnly occurredOn, DateOnly? valueOn,
            DateTimeOffset recordedAtUtc, CancellationToken cancellationToken) =>
            Task.FromResult(new RecordPeriodAnswer(RecordPeriodOutcome.Open, null));
    }

    /// <summary>SENTETIK: allows every Finance action. Not a grant.</summary>
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
            Task.FromResult(accountId == owners.Bank && context.CompanyId == owners.Company
                ? new ReadResult<BankAccountView>(ReadOutcome.Found,
                    new BankAccountView(owners.Bank, owners.Company, Currency, "SENTETIK banka", true, true, "1"))
                : new ReadResult<BankAccountView>(ReadOutcome.NotFound, null));
    }

    /// <summary>SENTETIK: one active party account of the company.</summary>
    private sealed class SyntheticParty(Owners owners) : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId,
            Guid companyAccountId, CancellationToken cancellationToken) =>
            Task.FromResult(partyId == owners.Party && companyAccountId == owners.Account && context.CompanyId == owners.Company
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

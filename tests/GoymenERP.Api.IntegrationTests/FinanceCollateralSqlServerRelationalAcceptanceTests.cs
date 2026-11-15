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
/// API-85b (F018 plan §L2; FNS-D57, FNS-D31, FNS-D58) against a REAL SQL Server: the Finance customer collateral master
/// on Finance's own migrations over the host's migrated and seeded database, with Audit's real save interceptor.
/// <para>
/// Permission, the Settings currency candidates, the Settings collateral type catalog and Counterparty's party account
/// answer are ISOLATED SYNTHETIC ports: no collateral grant exists and Settings has no collateral type catalog adapter
/// yet, so these tests prove the store, its constraints and its rules, not a user outcome. Every identity, amount, rate
/// and currency below is SENTETIK / NOT_ENV test data. A collateral moves no money.
/// </para>
/// </summary>
public sealed class FinanceCollateralSqlServerRelationalAcceptanceTests
{
    private const string Recorder = "SENTETIK-recorder";
    private const string SecondRecorder = "SENTETIK-recorder-2";
    private const string Reason = "SENTETIK çözüm gerekçesi";
    private const int DownGuardNumber = 51585;
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Currency = Guid.Parse("9a2e3d4c-5b6a-4798-8a1b-2c3d4e5f6b01");
    private static readonly Guid Party = Guid.Parse("9a2e3d4c-5b6a-4798-8a1b-2c3d4e5f6b02");
    private static readonly Guid Account = Guid.Parse("9a2e3d4c-5b6a-4798-8a1b-2c3d4e5f6b03");
    private static readonly Guid Guarantee = Guid.Parse("9a2e3d4c-5b6a-4798-8a1b-2c3d4e5f6b04");
    private static readonly Guid Cheque = Guid.Parse("9a2e3d4c-5b6a-4798-8a1b-2c3d4e5f6b05");
    private static readonly DateOnly From = new(2026, 1, 1);

    [SqlServerFact]
    public async Task A_recorder_records_changes_and_releases_a_collateral_in_one_save_each_with_its_attempt_and_old_and_new_audit()
    {
        await using var run = await Run.StartAsync();

        PartyCollateralRecordView recorded;
        PartyCollateralRecordView changed;
        PartyCollateralRecordView released;
        await using (var a = run.Open(CompanyA, Recorder))
        {
            var stored = await a.Collaterals.RecordAsync(a.Context, Key("r-1"), Record("250000.50"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, stored.Outcome);
            recorded = stored.Resource!;
            Assert.Equal((PartyCollateralKind.Record, PartyCollateralStatus.Effective, "1", "250000.50"),
                (recorded.Kind, recorded.Status, recorded.Revision, recorded.Amount));

            var change = await a.Collaterals.ChangeAsync(a.Context, Key("c-1"),
                new PartyCollateralChangeRequest(recorded.CollateralId, "1", Guarantee, Currency, "300000", From, null,
                    "SENTETIK-BELGE-1"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, change.Outcome);
            changed = change.Resource!;
            Assert.Equal((PartyCollateralKind.Change, "2", "300000"), (changed.Kind, changed.Revision, changed.Amount));

            var release = await a.Collaterals.ReleaseAsync(a.Context, Key("x-1"),
                new PartyCollateralReleaseRequest(recorded.CollateralId, "2", Reason), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, release.Outcome);
            released = release.Resource!;
            Assert.Equal((PartyCollateralKind.Release, "3", Reason, "300000"),
                (released.Kind, released.Revision, released.ReleaseReason, released.Amount));

            // A released collateral is no longer held and cannot be changed again.
            var account = await a.Collaterals.GetAccountAsync(a.Context, Account, CancellationToken.None);
            Assert.Empty(account.Resource!.Held);
            Assert.Equal("collateral_released", Assert.Single((await a.Collaterals.ChangeAsync(a.Context, Key("c-2"),
                new PartyCollateralChangeRequest(recorded.CollateralId, "3", Guarantee, Currency, "1", From, null, null),
                CancellationToken.None)).Issues).Code);
        }

        // Append-only: three revisions, one effective (the release), the amount stored as given and never rounded.
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.PartyCollaterals WHERE CollateralId = @c", ("@c", recorded.CollateralId)));
        Assert.Equal(released.Id, await run.ScalarAsync<Guid>(
            "SELECT Id FROM finance.PartyCollaterals WHERE CollateralId = @c AND Status = 1", ("@c", recorded.CollateralId)));
        Assert.Equal("250000.50", await run.ScalarAsync<string>(
            "SELECT AmountText FROM finance.PartyCollaterals WHERE Id = @id", ("@id", recorded.Id)));
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.PartyCollateralCommands WHERE JSON_VALUE(ResultJson, '$.outcome') = N'Succeeded'"));

        // FNS-D57: every change audited with the old and the new values, one step and no approver.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.collateral.recorded.v1' AND EntityId = @id",
            ("@id", recorded.Id.ToString())));
        Assert.Equal(("250000.50", "300000"), (
            await run.ScalarAsync<string>("SELECT JSON_VALUE(BeforeJson, '$.amount') FROM audit.AuditEvents "
                + "WHERE EventType = N'finance.collateral.changed.v1' AND EntityId = @id", ("@id", changed.Id.ToString())),
            await run.ScalarAsync<string>("SELECT JSON_VALUE(AfterJson, '$.amount') FROM audit.AuditEvents "
                + "WHERE EventType = N'finance.collateral.changed.v1' AND EntityId = @id", ("@id", changed.Id.ToString()))));
        Assert.Equal(Reason, await run.ScalarAsync<string>(
            "SELECT JSON_VALUE(AfterJson, '$.releaseReason') FROM audit.AuditEvents "
            + "WHERE EventType = N'finance.collateral.released.v1' AND EntityId = @id", ("@id", released.Id.ToString())));
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType LIKE N'finance.collateral.%' "
            + "AND JSON_VALUE(ApprovalContextJson, '$.rule') = N'FNS-D57'"));
        // No money effect.
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
    }

    [SqlServerFact]
    public async Task The_account_answers_the_weighted_collateral_at_the_read_time_rate_and_the_documentary_risk_separately()
    {
        await using var run = await Run.StartAsync();

        await using var a = run.Open(CompanyA, Recorder);
        Assert.Equal(CommandOutcome.Succeeded,
            (await a.Collaterals.RecordAsync(a.Context, Key("r-1"), Record("1000.50"), CancellationToken.None)).Outcome);

        var account = (await a.Collaterals.GetAccountAsync(a.Context, Account, CancellationToken.None)).Resource!;
        Assert.Equal(PartyCollateralFigureState.Available, account.WeightedCollateral.State);
        // 1000.50 x 75.5 / 100, exact; the rate itself is never stored.
        Assert.Equal((Currency, "755.3775"), (Assert.Single(account.WeightedCollateral.Amounts).CurrencyId,
            account.WeightedCollateral.Amounts[0].Amount));
        // No customer instrument: a recorded zero, shown beside the collateral and never added to it.
        Assert.Equal((PartyCollateralFigureState.Available, 0),
            (account.DocumentaryRisk.State, account.DocumentaryRisk.Amounts.Count));

        // The catalog going unavailable makes the weighted figure unavailable; it never falls back to a stored rate.
        await using var b = run.Open(CompanyA, Recorder, catalogAvailable: false);
        var unavailable = (await b.Collaterals.GetAccountAsync(b.Context, Account, CancellationToken.None)).Resource!;
        Assert.Equal((PartyCollateralFigureState.Unavailable, "finance_collateral_type_rate_unavailable"),
            (unavailable.WeightedCollateral.State, unavailable.WeightedCollateral.ReasonCode));
        Assert.Single(unavailable.Held);
    }

    [SqlServerFact]
    public async Task A_cheque_or_note_type_and_an_unavailable_catalog_are_refused_and_store_no_collateral()
    {
        await using var run = await Run.StartAsync();

        await using (var a = run.Open(CompanyA, Recorder))
        {
            var cheque = await a.Collaterals.RecordAsync(a.Context, Key("r-cheque"), Record("100") with { CollateralTypeId = Cheque },
                CancellationToken.None);
            Assert.Equal((CommandOutcome.ValidationFailed, "collateral_type_is_instrument"),
                (cheque.Outcome, Assert.Single(cheque.Issues).Code));
        }

        await using (var b = run.Open(CompanyA, Recorder, catalogAvailable: false))
        {
            var refused = await b.Collaterals.RecordAsync(b.Context, Key("r-unavailable"), Record("100"), CancellationToken.None);
            Assert.NotEqual(CommandOutcome.Succeeded, refused.Outcome);
            Assert.Equal("collateral_type_catalog_unavailable", Assert.Single(refused.Issues).Code);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PartyCollaterals"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType LIKE N'finance.collateral.%'"));
    }

    [SqlServerFact]
    public async Task The_database_refuses_a_second_effective_revision_and_a_non_positive_amount()
    {
        await using var run = await Run.StartAsync();
        PartyCollateralRecordView recorded;
        await using (var a = run.Open(CompanyA, Recorder))
        {
            recorded = (await a.Collaterals.RecordAsync(a.Context, Key("r-1"), Record("100"), CancellationToken.None)).Resource!;
        }

        // UX_PartyCollaterals_Live: a copy of the effective revision under a new revision number cannot also be effective.
        // The service's save guard is bypassed on purpose: the index is the authority.
        var live = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "INSERT INTO finance.PartyCollaterals (Id, CompanyId, CollateralId, Revision, Direction, SourceKind, PartyId, "
            + "CompanyAccountId, Kind, CollateralTypeId, CollateralTypeVersion, CurrencyId, AmountText, ValidFrom, "
            + "ValidUntil, DocumentReference, ReleaseReason, Status, Version, RecordedBy, RecordedAtUtc, SupersededAtUtc, "
            + "UpdatedAtUtc) SELECT NEWID(), CompanyId, CollateralId, Revision + 1, Direction, SourceKind, PartyId, "
            + "CompanyAccountId, 2, CollateralTypeId, CollateralTypeVersion, CurrencyId, AmountText, ValidFrom, "
            + "ValidUntil, DocumentReference, NULL, 1, 1, RecordedBy, RecordedAtUtc, NULL, UpdatedAtUtc "
            + "FROM finance.PartyCollaterals WHERE Id = @id", ("@id", recorded.Id)));
        Assert.Contains(live.Number, new[] { 2601, 2627 });

        var amount = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE finance.PartyCollaterals SET AmountText = N'0' WHERE Id = @id", ("@id", recorded.Id)));
        Assert.Equal(547, amount.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PartyCollaterals"));
    }

    [SqlServerFact]
    public async Task Two_racing_changes_from_one_revision_leave_one_effective_revision_and_the_loser_a_concurrent_change()
    {
        await using var run = await Run.StartAsync();
        PartyCollateralRecordView recorded;
        await using (var a = run.Open(CompanyA, Recorder))
        {
            recorded = (await a.Collaterals.RecordAsync(a.Context, Key("r-1"), Record("100"), CancellationToken.None)).Resource!;
        }

        // Two recorders change the collateral from revision 1 with different keys; both read the same state, then meet
        // at the save, where the live-state and revision unique indexes and the row version are the authority.
        var barrier = new Barrier(2);
        async Task<PartyCollateralCommandResult> ChangeAsync(string recorder, string amount)
        {
            await using var scope = run.Open(CompanyA, recorder, true, new BarrierInterceptor(barrier));
            return await scope.Collaterals.ChangeAsync(scope.Context, Key("c-" + recorder),
                new PartyCollateralChangeRequest(recorded.CollateralId, "1", Guarantee, Currency, amount, From, null, null),
                CancellationToken.None);
        }

        var results = await Task.WhenAll(Task.Run(() => ChangeAsync(Recorder, "200")),
            Task.Run(() => ChangeAsync(SecondRecorder, "300")));
        var won = Assert.Single(results, result => result.Outcome == CommandOutcome.Succeeded);
        var lost = Assert.Single(results, result => result.Outcome == CommandOutcome.VersionConflict);
        Assert.Equal("concurrent_change", Assert.Single(lost.Issues).Code);
        Assert.Null(lost.Resource);

        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PartyCollaterals"));
        Assert.Equal(won.Resource!.Id, await run.ScalarAsync<Guid>(
            "SELECT Id FROM finance.PartyCollaterals WHERE CollateralId = @c AND Status = 1", ("@c", recorded.CollateralId)));
        // The record and the winner's change only; the loser's attempt and audit event rolled back with its save.
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PartyCollateralCommands"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.collateral.changed.v1'"));
    }

    [SqlServerFact]
    public async Task The_same_key_replays_with_its_first_correlation_and_a_stale_revision_is_a_recorded_conflict()
    {
        await using var run = await Run.StartAsync();
        var request = Record("500");

        await using var a = run.Open(CompanyA, Recorder);
        var first = await a.Collaterals.RecordAsync(a.Context, Key("k-1"), request, CancellationToken.None);
        var replay = await a.Collaterals.RecordAsync(a.Context with { CorrelationId = run.Correlation + "-again" }, Key("k-1"),
            request, CancellationToken.None);
        Assert.Equal((CommandOutcome.Succeeded, true, run.Correlation, first.Resource!.Id),
            (replay.Outcome, replay.IsReplay, replay.OriginalCorrelationId, replay.Resource!.Id));

        // The same key with another payload is a conflict that stores nothing new.
        var reused = await a.Collaterals.RecordAsync(a.Context, Key("k-1"), Record("600"), CancellationToken.None);
        Assert.Equal(CommandOutcome.IdempotencyConflict, reused.Outcome);

        // A change still naming a superseded revision is a terminal conflict: recorded, no new revision, no audit.
        Assert.Equal(CommandOutcome.Succeeded, (await a.Collaterals.ChangeAsync(a.Context, Key("c-1"),
            new PartyCollateralChangeRequest(first.Resource.CollateralId, "1", Guarantee, Currency, "700", From, null, null),
            CancellationToken.None)).Outcome);
        var stale = await a.Collaterals.ChangeAsync(a.Context, Key("c-2"),
            new PartyCollateralChangeRequest(first.Resource.CollateralId, "1", Guarantee, Currency, "800", From, null, null),
            CancellationToken.None);
        Assert.Equal(CommandOutcome.VersionConflict, stale.Outcome);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.PartyCollateralCommands WHERE JSON_VALUE(ResultJson, '$.outcome') = N'VersionConflict'"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PartyCollaterals"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.collateral.changed.v1'"));
    }

    [SqlServerFact]
    public async Task Another_companys_collateral_is_never_seen_changed_or_released()
    {
        await using var run = await Run.StartAsync();
        PartyCollateralRecordView recorded;
        await using (var a = run.Open(CompanyA, Recorder))
        {
            recorded = (await a.Collaterals.RecordAsync(a.Context, Key("r-1"), Record("100"), CancellationToken.None)).Resource!;
        }

        await using (var b = run.Open(CompanyB, Recorder))
        {
            Assert.Empty((await b.Collaterals.GetAccountAsync(b.Context, Account, CancellationToken.None)).Resource!.Held);
            Assert.Equal(ReadOutcome.NotFound, (await b.Collaterals.HistoryAsync(b.Context, Account, CancellationToken.None)).Outcome);
            var change = await b.Collaterals.ChangeAsync(b.Context, Key("c-1"),
                new PartyCollateralChangeRequest(recorded.CollateralId, "1", Guarantee, Currency, "1", From, null, null),
                CancellationToken.None);
            Assert.Equal("collateral_not_found", Assert.Single(change.Issues).Code);
            var release = await b.Collaterals.ReleaseAsync(b.Context, Key("x-1"),
                new PartyCollateralReleaseRequest(recorded.CollateralId, "1", Reason), CancellationToken.None);
            Assert.Equal("collateral_not_found", Assert.Single(release.Issues).Code);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PartyCollaterals"));
        Assert.Equal(FinancePartyCollateralService.Effective, await run.ScalarAsync<int>(
            "SELECT Status FROM finance.PartyCollaterals WHERE Id = @id", ("@id", recorded.Id)));
    }

    [SqlServerFact]
    public async Task The_party_collateral_migrations_Down_passes_on_an_empty_store_and_refuses_while_a_collateral_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddPartyCollaterals().DownOperations[0]).Sql;

        await run.ExecuteAsync(guard);

        PartyCollateralRecordView recorded;
        await using (var a = run.Open(CompanyA, Recorder))
        {
            recorded = (await a.Collaterals.RecordAsync(a.Context, Key("r-1"), Record("100"), CancellationToken.None)).Resource!;
        }

        // Forward fix: the rollback stops while a collateral would be lost and deletes nothing.
        var error = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(DownGuardNumber, error.Number);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.PartyCollaterals WHERE Id = @id", ("@id", recorded.Id)));
    }

    private static PartyCollateralRecordRequest Record(string amount) =>
        new(Party, Account, Guarantee, Currency, amount, From, null, "SENTETIK-BELGE-1");

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
        public string Correlation { get; } = $"api85b-{Guid.NewGuid():N}";

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

        public Scope Open(Guid company, string actor, bool catalogAvailable = true,
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
            var collaterals = new FinancePartyCollateralService(dbContext, companyContext, currentUser,
                new SyntheticPermissions(), new SyntheticCandidates(), new SyntheticParty(),
                new SyntheticCatalog(catalogAvailable), collector,
                new FinancePartyCollateralEnvelopes(
                    new FinanceAuditEnvelope(ErpPermissions.FinanceCollateralsRecord, ["SENTETIK-FINANCE"], "Web")),
                TimeProvider.System);
            return new Scope(dbContext, collaterals, new FinanceContext(company, actor, Correlation, null));
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

    private sealed class Scope(FinanceDbContext dbContext, FinancePartyCollateralService collaterals, FinanceContext context)
        : IAsyncDisposable
    {
        public FinancePartyCollateralService Collaterals => collaterals;
        public FinanceContext Context => context;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    /// <summary>SENTETIK: allows every Finance action. Not a grant; no collateral grant exists.</summary>
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

    /// <summary>
    /// SENTETIK: a guarantee type at 75.5 % and a cheque class Settings marks as a negotiable instrument. Not a Settings
    /// source and not a rate decision: Settings owns both (FNS-D31).
    /// </summary>
    private sealed class SyntheticCatalog(bool available) : IFinanceCollateralTypeCatalogReader
    {
        public Task<FinanceCollateralTypeCatalogAnswer> ListAsync(Guid companyId, DateOnly asOf,
            CancellationToken cancellationToken) =>
            Task.FromResult(available
                ? new FinanceCollateralTypeCatalogAnswer(FinanceCollateralTypeCatalogState.Available,
                [
                    new FinanceCollateralTypeCatalogItem(Guarantee, "SENTETIK-TUR", "SENTETIK teminat türü", IsActive: true,
                        "75.5", "1", IsNegotiableInstrument: false),
                    new FinanceCollateralTypeCatalogItem(Cheque, "SENTETIK-CEK", "SENTETIK çek", IsActive: true, "100", "1",
                        IsNegotiableInstrument: true),
                ])
                : FinanceCollateralTypeCatalogAnswer.NotAvailable);
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

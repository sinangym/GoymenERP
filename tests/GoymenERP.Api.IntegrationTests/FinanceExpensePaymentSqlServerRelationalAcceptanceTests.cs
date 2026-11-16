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
/// FNS-D25 / FNS-D47 (FIN-S15) against a REAL SQL Server: the expense payment (draft → approval → record) on
/// Finance's own migrations over the host's migrated and seeded database, with Audit's real save interceptor.
/// <para>
/// FNS-D47: the preparer is never the approver. The service refuses the self-approval before anything is written,
/// and <c>CK_ExpensePayments_Status</c> refuses the same row state when a writer goes around the service.
/// </para>
/// <para>
/// Permission, the Settings money profile, the record period rule and the treasury balance guard are ISOLATED
/// SYNTHETIC ports in the original store facts. The TreasuryLedger partial separately exercises the real internal
/// balance guard and transaction. These are bounded store/ledger proofs, not a granted user workflow.
/// Every identity, amount and account below is SENTETIK / NOT_ENV test data.
/// </para>
/// </summary>
public sealed partial class FinanceExpensePaymentSqlServerRelationalAcceptanceTests
{
    private const string Preparer = "SENTETIK-preparer";
    private const string Approver = "SENTETIK-approver";
    private const string SecondApprover = "SENTETIK-approver-2";
    private const string Recorder = "SENTETIK-recorder";
    private const string Amount = "1250.50";
    private const int DownGuardNumber = 51296;
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Currency = Guid.Parse("5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5ab1");
    private static readonly Guid Profile = Guid.Parse("5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5ab2");
    private static readonly DateOnly OccurredOn = new(2026, 11, 10);

    [SqlServerFact]
    public async Task Draft_approval_and_record_each_save_once_with_their_attempt_and_audit_and_write_no_movement_row()
    {
        await using var run = await Run.StartAsync();
        var bank = await run.ApprovedBankAccountAsync(CompanyA, "SENTETIK banka A");

        var draft = await run.DraftAsync(CompanyA, bank, "d-1");
        Assert.Equal((ExpensePaymentStatus.Draft, "1", Preparer), (draft.Status, draft.Version, draft.PreparedBy));

        ExpensePaymentView approved;
        await using (var b = run.Open(CompanyA, Approver))
        {
            var result = await b.Payments.ApproveAsync(b.Context, Key("a-1"),
                new ApproveExpensePaymentRequest(draft.Id, draft.Version), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
            approved = result.Resource!;
            Assert.Equal((ExpensePaymentStatus.Approved, "2", Approver), (approved.Status, approved.Version, approved.ApprovedBy));
        }

        await using (var r = run.Open(CompanyA, Recorder))
        {
            var result = await r.Payments.RecordAsync(r.Context, Key("r-1"),
                new RecordExpensePaymentRequest(approved.Id, approved.Version, true), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
            var recorded = result.Resource!;
            Assert.Equal((ExpensePaymentStatus.Recorded, "3", Recorder, true),
                (recorded.Status, recorded.Version, recorded.RecordedBy, recorded.PaymentConfirmed));
        }

        Assert.Equal(2, await run.ScalarAsync<int>("SELECT Status FROM finance.ExpensePayments WHERE Id = @id", ("@id", draft.Id)));
        Assert.True(await run.ScalarAsync<bool>(
            "SELECT PaymentConfirmed FROM finance.ExpensePayments WHERE Id = @id", ("@id", draft.Id)));
        Assert.Equal(3L, await run.ScalarAsync<long>("SELECT Version FROM finance.ExpensePayments WHERE Id = @id", ("@id", draft.Id)));
        Assert.Equal(Amount, await run.ScalarAsync<string>(
            "SELECT AmountText FROM finance.ExpensePayments WHERE Id = @id", ("@id", draft.Id)));
        Assert.Equal(Recorder, await run.ScalarAsync<string>(
            "SELECT RecordedBy FROM finance.ExpensePayments WHERE Id = @id", ("@id", draft.Id)));

        // One attempt per command, each naming the payment.
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.ExpensePaymentCommands WHERE PaymentId = @id", ("@id", draft.Id)));
        foreach (var command in new[]
                 {
                     ExpensePaymentService.DraftCommand, ExpensePaymentService.ApproveCommand, ExpensePaymentService.RecordCommand,
                 })
        {
            Assert.Equal(1, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM finance.ExpensePaymentCommands WHERE CommandName = @n", ("@n", command)));
        }

        foreach (var eventType in new[]
                 {
                     "finance.expense-payment.created.v1", "finance.expense-payment.approved.v1",
                     "finance.expense-payment.recorded.v1",
                 })
        {
            Assert.Equal(1, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = @t", ("@t", eventType)));
        }

        // The recorded row is the money fact itself: no second movement row is written for it.
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutgoingMoneyEvents"));
    }

    [SqlServerFact]
    public async Task The_preparer_cannot_approve_in_the_service_or_around_it_in_the_database()
    {
        await using var run = await Run.StartAsync();
        var bank = await run.ApprovedBankAccountAsync(CompanyA, "SENTETIK banka A");
        var draft = await run.DraftAsync(CompanyA, bank, "d-1");

        // FNS-D47 in the service: refused before anything is written; no attempt row, no audit event.
        await using (var p = run.Open(CompanyA, Preparer))
        {
            var self = await p.Payments.ApproveAsync(p.Context, Key("a-self"),
                new ApproveExpensePaymentRequest(draft.Id, draft.Version), CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, self.Outcome);
            Assert.Equal(("paymentId", "payment_approval_invalid"),
                (Assert.Single(self.Issues).Field, Assert.Single(self.Issues).Code));
            Assert.Null(self.Resource);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT Status FROM finance.ExpensePayments WHERE Id = @id", ("@id", draft.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.ExpensePaymentCommands WHERE CommandName = @n",
            ("@n", ExpensePaymentService.ApproveCommand)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.expense-payment.approved.v1'"));

        // FNS-D47 in the database: a writer that goes around the service cannot store a self-approval.
        var error = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE finance.ExpensePayments SET Status = 1, ApprovedBy = PreparedBy, ApprovedAtUtc = SYSDATETIMEOFFSET(), "
            + "Version = 2 WHERE Id = @id", ("@id", draft.Id)));
        Assert.Equal(547, error.Number);
        Assert.Contains("CK_ExpensePayments_Status", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT Status FROM finance.ExpensePayments WHERE Id = @id", ("@id", draft.Id)));

        // Another actor approves the untouched draft; the store keeps the two different actors.
        await using (var b = run.Open(CompanyA, Approver))
        {
            var approved = await b.Payments.ApproveAsync(b.Context, Key("a-1"),
                new ApproveExpensePaymentRequest(draft.Id, draft.Version), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
        }

        Assert.Equal(Preparer, await run.ScalarAsync<string>(
            "SELECT PreparedBy FROM finance.ExpensePayments WHERE Id = @id", ("@id", draft.Id)));
        Assert.Equal(Approver, await run.ScalarAsync<string>(
            "SELECT ApprovedBy FROM finance.ExpensePayments WHERE Id = @id", ("@id", draft.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT Status FROM finance.ExpensePayments WHERE Id = @id", ("@id", draft.Id)));
    }

    [SqlServerFact]
    public async Task Without_a_treasury_balance_source_the_record_is_unavailable_and_nothing_is_written()
    {
        await using var run = await Run.StartAsync();
        var bank = await run.ApprovedBankAccountAsync(CompanyA, "SENTETIK banka A");
        var draft = await run.DraftAsync(CompanyA, bank, "d-1");
        ExpensePaymentView approved;
        await using (var b = run.Open(CompanyA, Approver))
        {
            approved = (await b.Payments.ApproveAsync(b.Context, Key("a-1"),
                new ApproveExpensePaymentRequest(draft.Id, draft.Version), CancellationToken.None)).Resource!;
        }

        // The host's fail-closed default guard: no balance source, so no record can prove the balance covers it.
        await using (var r = run.Open(CompanyA, Recorder, new UnavailableTreasuryBalanceGuard()))
        {
            var refused = await r.Payments.RecordAsync(r.Context, Key("r-1"),
                new RecordExpensePaymentRequest(approved.Id, approved.Version, true), CancellationToken.None);
            Assert.Equal(CommandOutcome.Unavailable, refused.Outcome);
            Assert.Equal(("treasuryAccountId", TreasuryBalanceAnswer.UnavailableCode),
                (Assert.Single(refused.Issues).Field, Assert.Single(refused.Issues).Code));
            Assert.Null(refused.Resource);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT Status FROM finance.ExpensePayments WHERE Id = @id", ("@id", draft.Id)));
        Assert.False(await run.ScalarAsync<bool>(
            "SELECT PaymentConfirmed FROM finance.ExpensePayments WHERE Id = @id", ("@id", draft.Id)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ExpensePaymentCommands"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.ExpensePaymentCommands WHERE CommandName = @n",
            ("@n", ExpensePaymentService.RecordCommand)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.expense-payment.recorded.v1'"));
    }

    [SqlServerFact]
    public async Task The_same_key_replays_with_its_first_correlation_and_a_stale_version_is_a_recorded_conflict()
    {
        await using var run = await Run.StartAsync();
        var bank = await run.ApprovedBankAccountAsync(CompanyA, "SENTETIK banka A");

        await using (var p = run.Open(CompanyA, Preparer))
        {
            var request = Draft(bank);
            var first = await p.Payments.DraftSaveAsync(p.Context, Key("d-1"), request, CancellationToken.None);
            var replay = await p.Payments.DraftSaveAsync(p.Context with { CorrelationId = run.Correlation + "-again" },
                Key("d-1"), request, CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, true, run.Correlation, first.Resource!.Id),
                (replay.Outcome, replay.IsReplay, replay.OriginalCorrelationId, replay.Resource!.Id));

            // The same key with another payload is refused and writes nothing.
            var other = await p.Payments.DraftSaveAsync(p.Context, Key("d-1"), Draft(bank, "SENTETIK baska aciklama"),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.IdempotencyConflict, other.Outcome);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ExpensePayments"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ExpensePaymentCommands"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.expense-payment.created.v1'"));
        var paymentId = await run.ScalarAsync<Guid>("SELECT Id FROM finance.ExpensePayments");

        await using (var b = run.Open(CompanyA, Approver))
        {
            // A stale version is a terminal conflict: recorded against the payment, no state change, no audit.
            var stale = await b.Payments.ApproveAsync(b.Context, Key("a-stale"),
                new ApproveExpensePaymentRequest(paymentId, "7"), CancellationToken.None);
            Assert.Equal(CommandOutcome.VersionConflict, stale.Outcome);
            Assert.Equal("version_conflict", Assert.Single(stale.Issues).Code);

            var again = await b.Payments.ApproveAsync(b.Context with { CorrelationId = run.Correlation + "-again" },
                Key("a-stale"), new ApproveExpensePaymentRequest(paymentId, "7"), CancellationToken.None);
            Assert.Equal((CommandOutcome.VersionConflict, true, run.Correlation),
                (again.Outcome, again.IsReplay, again.OriginalCorrelationId));
            Assert.Equal("version_conflict", Assert.Single(again.Issues).Code);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.ExpensePaymentCommands WHERE CommandName = @n AND PaymentId = @id",
            ("@n", ExpensePaymentService.ApproveCommand), ("@id", paymentId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT Status FROM finance.ExpensePayments WHERE Id = @id", ("@id", paymentId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.expense-payment.approved.v1'"));
    }

    [SqlServerFact]
    public async Task Another_companys_payment_is_never_seen_or_approved()
    {
        await using var run = await Run.StartAsync();
        var bank = await run.ApprovedBankAccountAsync(CompanyA, "SENTETIK banka A");
        var draft = await run.DraftAsync(CompanyA, bank, "d-1");

        await using (var b = run.Open(CompanyB, Approver))
        {
            var list = await b.Payments.ListAuthorizedAsync(b.Context, null, CancellationToken.None);
            Assert.Equal(ReadOutcome.Found, list.Outcome);
            Assert.Empty(list.Resource!.Items);
            Assert.Equal(ReadOutcome.NotFound,
                (await b.Payments.DetailAuthorizedAsync(b.Context, draft.Id, CancellationToken.None)).Outcome);

            var approve = await b.Payments.ApproveAsync(b.Context, Key("a-1"),
                new ApproveExpensePaymentRequest(draft.Id, draft.Version), CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, approve.Outcome);
            Assert.Equal("payment_not_found", Assert.Single(approve.Issues).Code);
            Assert.Null(approve.Resource);
        }

        // Company B's terminal attempt is its own and names no payment; A's draft is untouched.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.ExpensePaymentCommands WHERE CompanyId = @c AND PaymentId IS NULL",
            ("@c", CompanyB)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT Status FROM finance.ExpensePayments WHERE Id = @id", ("@id", draft.Id)));
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM finance.ExpensePayments WHERE Id = @id", ("@id", draft.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.expense-payment.approved.v1'"));
    }

    [SqlServerFact]
    public async Task Two_racing_approvals_of_one_version_leave_one_approval_and_the_loser_a_concurrent_change()
    {
        await using var run = await Run.StartAsync();
        var bank = await run.ApprovedBankAccountAsync(CompanyA, "SENTETIK banka A");
        var draft = await run.DraftAsync(CompanyA, bank, "d-1");

        // Two approvers decide the same version with different keys; both read the same row, then meet at the save,
        // where the row version is the authority.
        var barrier = new Barrier(2);
        async Task<ExpensePaymentCommandResult> ApproveAsync(string approver)
        {
            await using var scope = run.Open(CompanyA, approver, null, new BarrierInterceptor(barrier));
            return await scope.Payments.ApproveAsync(scope.Context, Key("a-" + approver),
                new ApproveExpensePaymentRequest(draft.Id, draft.Version), CancellationToken.None);
        }

        var results = await Task.WhenAll(Task.Run(() => ApproveAsync(Approver)), Task.Run(() => ApproveAsync(SecondApprover)));
        var won = Assert.Single(results, result => result.Outcome == CommandOutcome.Succeeded);
        var lost = Assert.Single(results, result => result.Outcome == CommandOutcome.VersionConflict);
        Assert.Equal("concurrent_change", Assert.Single(lost.Issues).Code);

        Assert.Equal(won.Resource!.ApprovedBy, await run.ScalarAsync<string>(
            "SELECT ApprovedBy FROM finance.ExpensePayments WHERE Id = @id", ("@id", draft.Id)));
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM finance.ExpensePayments WHERE Id = @id", ("@id", draft.Id)));
        // The winner's attempt only; the loser's attempt and audit event rolled back with its save.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.ExpensePaymentCommands WHERE CommandName = @n",
            ("@n", ExpensePaymentService.ApproveCommand)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.expense-payment.approved.v1'"));
    }

    [SqlServerFact]
    public async Task The_expense_payment_down_refuses_while_a_payment_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddExpensePayments().DownOperations[0]).Sql;

        // An empty store passes the guard (nothing would be lost).
        await run.ExecuteAsync(guard);

        var bank = await run.ApprovedBankAccountAsync(CompanyA, "SENTETIK banka A");
        var draft = await run.DraftAsync(CompanyA, bank, "d-1");

        var error = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(DownGuardNumber, error.Number);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.ExpensePayments WHERE Id = @id", ("@id", draft.Id)));
    }

    private static CommandMetadata Key(string key) => new CommandMetadata(key);

    private static ExpensePaymentDraftSaveRequest Draft(Guid bank, string description = "SENTETIK kirtasiye") =>
        new ExpensePaymentDraftSaveRequest(null, null,
            new ExpensePaymentPreparation(ExpensePaymentSourceKind.Bank, bank, SingleExpenseItemCatalog.GeneralExpenseId,
                new MoneyText(Amount, Currency), new ProfileReference(Profile.ToString("D"), "1"), OccurredOn, null, null,
                description));

    private sealed partial class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }
        public string Correlation { get; } = $"fin-s15-{Guid.NewGuid():N}";

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

        /// <summary>A SENTETIK approved, money-movement-eligible bank account in the test currency.</summary>
        public async Task<Guid> ApprovedBankAccountAsync(Guid company, string name)
        {
            var id = Guid.NewGuid();
            await ExecuteAsync(
                "INSERT INTO finance.BankAccounts (Id, CompanyId, CurrencyId, DisplayName, MoneyMovementEligible, Iban, "
                + "Status, Version, ProposedBy, ProposedAtUtc, ApprovedBy, ApprovedAtUtc, UpdatedAtUtc) VALUES "
                + "(@id, @c, @cur, @n, 1, N'TR330006100519786457841326', 2, 2, N'SENTETIK-bank-proposer', "
                + "SYSDATETIMEOFFSET(), N'SENTETIK-bank-approver', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())",
                ("@id", id), ("@c", company), ("@cur", Currency), ("@n", name));
            return id;
        }

        /// <summary>The preparer saves a new draft and the test asserts it succeeded.</summary>
        public async Task<ExpensePaymentView> DraftAsync(Guid company, Guid bank, string key)
        {
            await using var p = Open(company, Preparer);
            var result = await p.Payments.DraftSaveAsync(p.Context, Key(key), Draft(bank), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
            return result.Resource!;
        }

        public Scope Open(Guid company, string actor, IFinanceTreasuryBalanceGuard? balance = null,
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
            var payments = new ExpensePaymentService(dbContext, companyContext, currentUser, new SyntheticPermissions(),
                new SingleExpenseItemCatalog(), balance ?? new SyntheticBalance(TreasuryBalanceOutcome.Sufficient),
                new SyntheticSettings(company), new SyntheticOpenPeriod(), collector,
                new ExpensePaymentBinding(FinanceMoneyOperations.ManualBankPaymentV1),
                new ExpensePaymentAuditEnvelopes(
                    Envelope(FinancePermissionNames.ExpensePaymentDraftSave),
                    Envelope(FinancePermissionNames.ExpensePaymentApprove),
                    Envelope(FinancePermissionNames.ExpensePaymentRecord)),
                TimeProvider.System);
            return new Scope(dbContext, payments, new FinanceContext(company, actor, Correlation, null));
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

        private static FinanceAuditEnvelope Envelope(string permission) =>
            new FinanceAuditEnvelope(permission, ["SENTETIK-FINANCE"], "Web");

        private static DbContextOptions<FinanceDbContext> Options(SqlServerRelationalApiFactory factory) =>
            new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .Options;
    }

    private sealed class Scope(FinanceDbContext dbContext, ExpensePaymentService payments, FinanceContext context)
        : IAsyncDisposable
    {
        public FinanceDbContext Db => dbContext;
        public ExpensePaymentService Payments => payments;
        public FinanceContext Context => context;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    /// <summary>SENTETIK: allows every Finance action. Not a grant; no expense-payment grant exists.</summary>
    private sealed class SyntheticPermissions : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }

    /// <summary>SENTETIK: the calling company's active money profile for the payment operation. Not a Settings source.</summary>
    private sealed class SyntheticSettings(Guid company) : ISettingsPrerequisiteProvider
    {
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => Task.FromResult(new MoneyProfileV1(
            company, currencyId, "SENTETIK", operation, new ProfileVersionV1(Profile, 1), 2, true,
            new DateOnly(2026, 1, 1), null));

        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn,
            DateOnly? valueOn, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>SENTETIK: every period is open. Not the accounting period source.</summary>
    private sealed class SyntheticOpenPeriod : IFinanceRecordPeriodRule
    {
        public Task<RecordPeriodAnswer> CheckAsync(FinanceContext context, DateOnly occurredOn, DateOnly? valueOn,
            DateTimeOffset recordedAtUtc, CancellationToken cancellationToken) =>
            Task.FromResult(new RecordPeriodAnswer(RecordPeriodOutcome.Open, null));
    }

    /// <summary>SENTETIK: a fixed treasury balance answer for the original isolated store facts.</summary>
    private sealed class SyntheticBalance(TreasuryBalanceOutcome outcome) : IFinanceTreasuryBalanceGuard
    {
        public Task<TreasuryBalanceAnswer> CheckOutgoingAsync(FinanceContext context,
            ExpensePaymentSourceKind sourceKind, Guid treasuryAccountId, MoneyText money,
            CancellationToken cancellationToken) => Task.FromResult(new TreasuryBalanceAnswer(outcome,
            outcome switch
            {
                TreasuryBalanceOutcome.Insufficient => TreasuryBalanceAnswer.InsufficientCode,
                TreasuryBalanceOutcome.Unavailable => TreasuryBalanceAnswer.UnavailableCode,
                _ => null,
            }));
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

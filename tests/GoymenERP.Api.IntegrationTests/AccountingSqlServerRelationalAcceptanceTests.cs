using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// ACC-3 against a REAL SQL Server: Accounting's own migration on top of the host's migrated and
/// seeded database, and Audit's real save interceptor. Every identity below is synthetic test data
/// in this test's own database; no chart of accounts, currency, rule or period is seeded anywhere.
/// </summary>
public sealed class AccountingSqlServerRelationalAcceptanceTests
{
    private const string Consumer = "synthetic-accounting-consumer";
    private const string Purpose = "synthetic-purpose-a";
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Currency = Guid.Parse("5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a71");
    private static readonly DateTimeOffset RecordedAt = new DateTimeOffset(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);
    private static readonly PostingDates InSeptember =
        new PostingDates(new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 15), null, RecordedAt);

    [SqlServerFact]
    public async Task Queue_posting_and_replay_are_durable_audited_and_answer_from_stored_facts()
    {
        await using var run = await Run.StartAsync();
        var period = await run.OpenSeptemberAsync(CompanyA);
        var source = Guid.NewGuid();
        var accounts = Accounts(CompanyA);

        await using (var a = run.Store(CompanyA))
        {
            var queued = await a.Postings.ReceiveAsync(Request(CompanyA, "m-1", source, accounts, mapCredit: false), period, Metadata(run));
            Assert.Equal(PostingOutcomeKind.ExceptionQueued, queued.Kind);
        }

        await using (var a = run.Store(CompanyA))
        {
            var waiting = await a.Postings.ReceiveAsync(Request(CompanyA, "m-2", source, accounts, amount: null), period, Metadata(run));
            Assert.Equal(PostingOutcomeKind.WaitingValuation, waiting.Kind);
            Assert.Empty(await a.Postings.ReadOpenExceptionsAsync());
            Assert.Equal(2, Assert.Single(await a.Postings.ReadPendingValuationsAsync()).LinesAwaitingAmount);
        }

        Journal posted;
        await using (var a = run.Store(CompanyA))
        {
            var outcome = await a.Postings.ReceiveAsync(Request(CompanyA, "m-3", source, accounts), period, Metadata(run));
            Assert.Equal(PostingOutcomeKind.Posted, outcome.Kind);
            posted = outcome.Journal!;
            Assert.Empty(await a.Postings.ReadPendingValuationsAsync());
        }

        await using (var a = run.Store(CompanyA))
        {
            // The same message again answers what it recorded, with no second effect.
            var replay = await a.Postings.ReceiveAsync(Request(CompanyA, "m-3", source, accounts), period, Metadata(run));
            Assert.Equal(PostingOutcomeKind.DuplicateMessage, replay.Kind);
            Assert.Equal(posted.Id, replay.Journal!.Id);
            // A queued message replayed returns its recorded queue entry, even though the fact is posted now.
            var queuedReplay = await a.Postings.ReceiveAsync(Request(CompanyA, "m-1", source, accounts), period, Metadata(run));
            Assert.Equal(PostingOutcomeKind.DuplicateMessage, queuedReplay.Kind);
            Assert.Equal(["synthetic-credit-key"], queuedReplay.Exception!.MissingMappingKeys);
            // A new message for the posted fact: already posted, the stored original.
            var again = await a.Postings.ReceiveAsync(Request(CompanyA, "m-4", source, accounts, ruleVersion: "synthetic-rule-2"),
                period, Metadata(run));
            Assert.Equal(PostingOutcomeKind.AlreadyPosted, again.Kind);
            Assert.Equal(posted.Id, again.Journal!.Id);
            var stored = (await a.Postings.FindJournalAsync(posted.Id))!;
            Assert.Equal((posted.TotalDebit, posted.TotalCredit, posted.Lines.Count), (stored.TotalDebit, stored.TotalCredit, stored.Lines.Count));
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Journals"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.JournalLines"));
        Assert.Equal(4, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Deliveries"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT Status FROM accounting.PostingExceptions WHERE SourceId = @s", ("@s", source)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT Status FROM accounting.PendingValuations WHERE SourceId = @s", ("@s", source)));
        // Audit: period opened; exception queued; exception resolved + pending queued; pending resolved + journal posted.
        // Replays and the already-posted delivery write no audit event.
        Assert.Equal(6, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = @c", ("@c", run.Correlation)));
    }

    [SqlServerFact]
    public async Task Each_company_sees_only_its_own_ledger_and_a_message_id_is_unique_per_company_only()
    {
        await using var run = await Run.StartAsync();
        var periodA = await run.OpenSeptemberAsync(CompanyA);
        var periodB = await run.OpenSeptemberAsync(CompanyB);
        var source = Guid.NewGuid();

        Journal journalA;
        await using (var a = run.Store(CompanyA))
        {
            journalA = (await a.Postings.ReceiveAsync(Request(CompanyA, "m-shared", source, Accounts(CompanyA)), periodA, Metadata(run))).Journal!;
        }

        await using (var b = run.Store(CompanyB))
        {
            // The same consumer + message id in another company is its own delivery (R4 key includes the company).
            var outcome = await b.Postings.ReceiveAsync(Request(CompanyB, "m-shared", source, Accounts(CompanyB)), periodB, Metadata(run));
            Assert.Equal(PostingOutcomeKind.Posted, outcome.Kind);
            // Company A's journal and period do not exist for B.
            Assert.Null(await b.Postings.FindJournalAsync(journalA.Id));
            Assert.Null(await b.Periods.FindPeriodAsync(periodA));
            // B cannot write a request of company A, nor post into A's period.
            Assert.Equal(AccountingStoreCodes.CompanyContextMismatch, (await Assert.ThrowsAsync<AccountingStoreException>(
                () => b.Postings.ReceiveAsync(Request(CompanyA, "m-from-b", Guid.NewGuid(), Accounts(CompanyA)), periodA, Metadata(run)))).Code);
            Assert.Equal(AccountingStoreCodes.PeriodNotFound, (await Assert.ThrowsAsync<AccountingStoreException>(
                () => b.Postings.ReceiveAsync(Request(CompanyB, "m-b-2", Guid.NewGuid(), Accounts(CompanyB)), periodA, Metadata(run)))).Code);
            // Reversing A's journal from B: not found.
            Assert.Equal(AccountingErrorCodes.JournalNotFound, (await Assert.ThrowsAsync<AccountingDomainException>(
                () => b.Postings.ReverseAsync(Consumer, "r-b", journalA.Id, Guid.NewGuid(), InSeptember, periodB, Metadata(run)))).Code);
        }

        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Deliveries WHERE MessageId = N'm-shared'"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Journals WHERE CompanyId = @c", ("@c", CompanyA)));
    }

    [SqlServerFact]
    public async Task Reversal_and_correction_identities_hold_in_the_store_and_in_the_database()
    {
        await using var run = await Run.StartAsync();
        var period = await run.OpenSeptemberAsync(CompanyA);
        var accounts = Accounts(CompanyA);
        var correctionId = Guid.NewGuid();
        Journal original;
        Journal reversal;
        Journal correction;
        await using (var a = run.Store(CompanyA))
        {
            original = (await a.Postings.ReceiveAsync(Request(CompanyA, "m-1", Guid.NewGuid(), accounts), period, Metadata(run))).Journal!;
        }

        await using (var a = run.Store(CompanyA))
        {
            reversal = (await a.Postings.ReverseAsync(Consumer, "r-1", original.Id, Guid.NewGuid(), InSeptember, period, Metadata(run))).Journal!;
        }

        await using (var a = run.Store(CompanyA))
        {
            var again = await a.Postings.ReverseAsync(Consumer, "r-2", original.Id, Guid.NewGuid(), InSeptember, period, Metadata(run));
            Assert.Equal(PostingOutcomeKind.AlreadyReversed, again.Kind);
            Assert.Equal(reversal.Id, again.Journal!.Id);
        }

        await using (var a = run.Store(CompanyA))
        {
            correction = (await a.Postings.CorrectAsync(Consumer, "c-1", correctionId, original.Id, Guid.NewGuid(), "synthetic-rule-2",
                InSeptember, Lines(accounts, 5m), period, Metadata(run))).Journal!;
        }

        await using (var a = run.Store(CompanyA))
        {
            var again = await a.Postings.CorrectAsync(Consumer, "c-2", correctionId, original.Id, Guid.NewGuid(), "synthetic-rule-2",
                InSeptember, Lines(accounts, 5m), period, Metadata(run));
            Assert.Equal(PostingOutcomeKind.AlreadyPosted, again.Kind);
            Assert.Equal(correction.Id, again.Journal!.Id);
        }

        // The unique indexes hold even without the store's own checks (a racing writer).
        await using (var raw = run.Context(CompanyA))
        {
            raw.Journals.Add(RawJournal(Guid.NewGuid(), kind: 2, original.Id, correctionId: null, original.Source.SourceId, period));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => raw.SaveChangesAsync());
            Assert.Contains("UX_Journals_Reversal", error.InnerException!.Message, StringComparison.Ordinal);
        }

        await using (var raw = run.Context(CompanyA))
        {
            raw.Journals.Add(RawJournal(Guid.NewGuid(), kind: 3, original.Id, correctionId, original.Source.SourceId, period));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => raw.SaveChangesAsync());
            Assert.Contains("UX_Journals_Correction", error.InnerException!.Message, StringComparison.Ordinal);
        }

        await using (var raw = run.Context(CompanyA))
        {
            raw.Journals.Add(RawJournal(Guid.NewGuid(), kind: 1, linkedJournalId: null, correctionId: null, original.Source.SourceId, period));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => raw.SaveChangesAsync());
            Assert.Contains("UX_Journals_Original", error.InnerException!.Message, StringComparison.Ordinal);
        }

        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Journals"));
    }

    [SqlServerFact]
    public async Task Two_racing_deliveries_of_one_fact_store_one_original_and_the_loser_retries_to_already_posted()
    {
        await using var run = await Run.StartAsync();
        var period = await run.OpenSeptemberAsync(CompanyA);
        var source = Guid.NewGuid();
        var accounts = Accounts(CompanyA);

        // Both writers read the same (empty) fact and the same period, then wait for each other at the save.
        var barrier = new Barrier(2);
        async Task<object> ReceiveAsync(string messageId)
        {
            await using var writer = run.Store(CompanyA, new BarrierInterceptor(barrier));
            try
            {
                return (await writer.Postings.ReceiveAsync(Request(CompanyA, messageId, source, accounts), period, Metadata(run))).Kind;
            }
            catch (AccountingStoreException exception)
            {
                return exception.Code;
            }
        }

        var results = await Task.WhenAll(Task.Run(() => ReceiveAsync("race-1")), Task.Run(() => ReceiveAsync("race-2")));

        Assert.Single(results, result => result is PostingOutcomeKind.Posted);
        Assert.Single(results, result => result is string code && code == AccountingStoreCodes.ConcurrentChange);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Journals"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Deliveries"));

        // The loser's retry answers from what the winner stored.
        var loser = results[0] is string ? "race-1" : "race-2";
        await using (var a = run.Store(CompanyA))
        {
            Assert.Equal(PostingOutcomeKind.AlreadyPosted,
                (await a.Postings.ReceiveAsync(Request(CompanyA, loser, source, accounts), period, Metadata(run))).Kind);
        }
    }

    [SqlServerFact]
    public async Task A_closed_period_takes_no_posting_and_a_close_racing_a_posting_cannot_both_commit()
    {
        await using var run = await Run.StartAsync();
        var period = await run.OpenSeptemberAsync(CompanyA);
        var accounts = Accounts(CompanyA);

        var barrier = new Barrier(2);
        async Task<object> PostAsync()
        {
            await using var writer = run.Store(CompanyA, new BarrierInterceptor(barrier));
            try
            {
                return (await writer.Postings.ReceiveAsync(Request(CompanyA, "race-post", Guid.NewGuid(), accounts), period, Metadata(run))).Kind;
            }
            catch (AccountingStoreException exception)
            {
                return exception.Code;
            }
        }

        async Task<object> CloseAsync()
        {
            await using var writer = run.Store(CompanyA, new BarrierInterceptor(barrier));
            try
            {
                await writer.Periods.ClosePeriodAsync(period, run.Actor, Metadata(run));
                return "closed";
            }
            catch (AccountingStoreException exception)
            {
                return exception.Code;
            }
        }

        var results = await Task.WhenAll(Task.Run(PostAsync), Task.Run(CloseAsync));
        Assert.Single(results, result => result is string code && code == AccountingStoreCodes.ConcurrentChange);
        Assert.Equal(1, results.Count(result => result is PostingOutcomeKind.Posted || (result is string text && text == "closed")));

        // Whatever won, a close then makes the period refuse the next posting, and nothing is recorded for it.
        await using (var a = run.Store(CompanyA))
        {
            if ((await a.Periods.FindPeriodAsync(period))!.ClosedAtUtc is null)
            {
                await a.Periods.ClosePeriodAsync(period, run.Actor, Metadata(run));
            }
        }

        var journals = await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Journals");
        await using (var a = run.Store(CompanyA))
        {
            Assert.Equal(AccountingErrorCodes.PeriodClosed, (await Assert.ThrowsAsync<AccountingDomainException>(
                () => a.Postings.ReceiveAsync(Request(CompanyA, "after-close", Guid.NewGuid(), accounts), period, Metadata(run)))).Code);
        }

        Assert.Equal(journals, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Journals"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Deliveries WHERE MessageId = N'after-close'"));
    }

    [SqlServerFact]
    public async Task A_save_that_fails_in_the_database_leaves_nothing_behind()
    {
        await using var run = await Run.StartAsync();
        // A company that Organization does not know: the foreign key to organization.Companies
        // refuses the save (547), and the calendar, the period and the audit event roll back together.
        var unknownCompany = Guid.NewGuid();
        await using (var store = run.Store(unknownCompany))
        {
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => store.Periods.OpenPeriodAsync(Guid.NewGuid(),
                new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), Metadata(run)));
            Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Periods"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.PeriodCalendars"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Accounting.Period'"));

        // The same opening for a known company then succeeds: nothing was half-written.
        await run.OpenSeptemberAsync(CompanyA);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Periods"));
    }

    private static (AccountRef Debit, AccountRef Credit) Accounts(Guid company) =>
        (new AccountRef(company, Guid.NewGuid()), new AccountRef(company, Guid.NewGuid()));

    private static JournalLine[] Lines((AccountRef Debit, AccountRef Credit) accounts, decimal amount) =>
        [new JournalLine(accounts.Debit, amount, 0m), new JournalLine(accounts.Credit, 0m, amount)];

    private static PostingRequest Request(Guid company, string messageId, Guid source, (AccountRef Debit, AccountRef Credit) accounts,
        decimal? amount = 100m, string ruleVersion = "synthetic-rule-1", bool mapCredit = true) =>
        new PostingRequest(
            Consumer,
            messageId,
            Guid.NewGuid(),
            new SourceReference(company, source, Purpose),
            ruleVersion,
            Currency,
            InSeptember,
            [
                new ProposedLine("synthetic-debit-key", accounts.Debit, PostingSide.Debit, amount),
                new ProposedLine("synthetic-credit-key", mapCredit ? accounts.Credit : null, PostingSide.Credit, amount),
            ]);

    private static JournalRow RawJournal(Guid id, int kind, Guid? linkedJournalId, Guid? correctionId, Guid source, Guid period) =>
        new JournalRow
        {
            Id = id,
            CompanyId = CompanyA,
            Kind = kind,
            SourceId = source,
            PostingPurpose = Purpose,
            PostingRuleVersion = "synthetic-rule-raw",
            CurrencyId = Currency,
            TransactionDate = InSeptember.TransactionDate,
            AccountingDate = InSeptember.AccountingDate,
            RecordedAtUtc = RecordedAt,
            LinkedJournalId = linkedJournalId,
            CorrectionId = correctionId,
            PeriodId = period,
            StoredAtUtc = RecordedAt,
            StoredBy = "synthetic-actor",
            CorrelationId = "synthetic-raw-writer",
        };

    private static AccountingCommandMetadata Metadata(Run run) => new AccountingCommandMetadata(
        run.Actor.ToString("D"), "synthetic.accounting.test-write", ["SYNTHETIC-ACCOUNTING-TEST"], run.Correlation,
        CausationId: null, IdempotencyKey: Guid.NewGuid().ToString("N"), SourceChannel: "Web");

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }
        public Guid Actor { get; } = Guid.NewGuid();
        public string Correlation { get; } = $"acc3-{Guid.NewGuid():N}";

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            // The host migrates and seeds Organization, Audit and the rest; Accounting's own
            // migration runs after it, as its foreign keys need Organization's tables.
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            await using var context = run.Context(CompanyA);
            await context.Database.MigrateAsync();
            return run;
        }

        public async Task<Guid> OpenSeptemberAsync(Guid company)
        {
            var id = Guid.NewGuid();
            await using var store = Store(company);
            Assert.Equal(AccountingWriteOutcome.Recorded, await store.Periods.OpenPeriodAsync(id, new DateOnly(2026, 9, 1),
                new DateOnly(2026, 9, 30), Metadata(this)));
            return id;
        }

        public AccountingDbContext Context(Guid company, params IInterceptor[] interceptors)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var options = new DbContextOptionsBuilder<AccountingDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AccountingDbContext.Schema))
                .AddInterceptors(interceptors)
                .Options;
            return new AccountingDbContext(options, companyContext);
        }

        public StoreScope Store(Guid company, params IInterceptor[] extra)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var interceptors = new List<IInterceptor> { new AuditSaveChangesInterceptor(collector, companyContext) };
            interceptors.AddRange(extra);
            var options = new DbContextOptionsBuilder<AccountingDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AccountingDbContext.Schema))
                .AddInterceptors(interceptors)
                .Options;
            var dbContext = new AccountingDbContext(options, companyContext);
            return new StoreScope(dbContext, new AccountingStore(dbContext, companyContext, collector, TimeProvider.System));
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

    private sealed class StoreScope(AccountingDbContext dbContext, AccountingStore store) : IAsyncDisposable
    {
        public IAccountingPeriodStore Periods => store;
        public IAccountingPostingStore Postings => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    /// <summary>Holds each save until both writers have reached it, so both read the same state first.</summary>
    private sealed class BarrierInterceptor(Barrier barrier) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return ValueTask.FromResult(result);
        }
    }
}

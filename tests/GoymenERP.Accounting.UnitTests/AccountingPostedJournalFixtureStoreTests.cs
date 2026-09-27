using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;
using GoymenERP.Modules.Accounting.Infrastructure.Fixtures;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Accounting.UnitTests;

/// <summary>
/// The SENTETIK acc01 posted-journal fixture: one fixed journal through Accounting's own posting path, into the fixed
/// fin01 Open period, bound to GDC, replay-safe. Fakes only (no database); the real-SQL proof is
/// <c>AccountingPostedJournalFixtureSqlServerRelationalAcceptanceTests</c>.
/// </summary>
public sealed class AccountingPostedJournalFixtureStoreTests
{
    private static readonly Guid OtherCompany = Guid.Parse("10000000-0000-0000-0000-000000000002");

    // The fixture's command metadata is written to Audit, whose SourceChannel is a closed set; "Fixture" is not in it
    // (acc01 run 36313248822 and the SQL suite refused it). The fakes above never reach Audit, so pin it here.
    [Fact]
    public void The_fixture_command_names_a_closed_audit_source_channel()
    {
        string[] audited =
        [
            GoymenERP.BuildingBlocks.Observability.SourceChannels.Web,
            GoymenERP.BuildingBlocks.Observability.SourceChannels.Mobile,
            GoymenERP.BuildingBlocks.Observability.SourceChannels.Import,
            GoymenERP.BuildingBlocks.Observability.SourceChannels.Integration,
            GoymenERP.BuildingBlocks.Observability.SourceChannels.AiAssisted,
            GoymenERP.BuildingBlocks.Observability.SourceChannels.System,
        ];
        Assert.Contains(AccountingPostedJournalFixture.Metadata().SourceChannel, audited);
        Assert.Equal(GoymenERP.BuildingBlocks.Observability.SourceChannels.System,
            AccountingPostedJournalFixture.Metadata().SourceChannel);
    }

    [Fact]
    public async Task Ensures_the_open_period_first_then_posts_the_fixed_request_through_the_posting_store()
    {
        var company = new CompanyContext();
        var calls = new List<string>();
        var postings = new FakePostings(calls, PostingOutcomeKind.Posted);
        var store = Store(company, new FakeOpenPeriod(company, calls), postings);

        await store.EnsureAsync(CancellationToken.None);

        Assert.Equal(["open-period", "receive"], calls);
        Assert.Equal(AccountingPostedJournalFixture.CompanyId, company.CompanyId);
        var (request, periodId, metadata) = Assert.Single(postings.Received);
        Assert.Equal(AccountingPostedJournalFixture.PeriodId, periodId);
        Assert.Equal(AccountingPostedJournalFixture.Request(), request, new PostingRequestComparer());
        Assert.Equal(AccountingPostedJournalFixture.FixturePermissionCode, metadata.PermissionCode);
        Assert.Equal(AccountingPostedJournalFixture.MessageId, metadata.IdempotencyKey);
    }

    [Fact]
    public async Task A_rerun_accepts_the_stored_delivery_replay_of_the_same_journal()
    {
        var company = new CompanyContext();
        var calls = new List<string>();
        var store = Store(company, new FakeOpenPeriod(company, calls), new FakePostings(calls, PostingOutcomeKind.DuplicateMessage));

        await store.EnsureAsync(CancellationToken.None);

        Assert.Equal(["open-period", "receive"], calls);
    }

    [Fact]
    public async Task A_rerun_over_its_own_stored_journal_does_not_re_ensure_the_advanced_period()
    {
        // Posting the journal advanced the period's version, so the open-period fixture's fresh-period check would
        // refuse it; the re-run's proof is the stored delivery's replay of the same journal.
        var company = new CompanyContext();
        var calls = new List<string>();
        var store = Store(company, new FakeOpenPeriod(company, calls),
            new FakePostings(calls, PostingOutcomeKind.DuplicateMessage, alreadyStored: true));

        await store.EnsureAsync(CancellationToken.None);

        Assert.Equal(["receive"], calls);
        Assert.Equal(AccountingPostedJournalFixture.CompanyId, company.CompanyId);
    }

    [Theory]
    [InlineData(PostingOutcomeKind.ExceptionQueued, false)]
    [InlineData(PostingOutcomeKind.AlreadyPosted, false)]
    [InlineData(PostingOutcomeKind.Posted, true)]
    public async Task Any_other_outcome_or_another_journal_is_refused(PostingOutcomeKind kind, bool otherJournal)
    {
        var company = new CompanyContext();
        var calls = new List<string>();
        var store = Store(company, new FakeOpenPeriod(company, calls),
            new FakePostings(calls, kind, otherJournal ? Guid.Parse("b7000000-0000-4000-8000-000000000699") : null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.EnsureAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Another_bound_company_is_refused_before_any_write()
    {
        var company = new CompanyContext();
        company.Set(OtherCompany);
        var calls = new List<string>();
        var store = Store(company, new FakeOpenPeriod(company, calls), new FakePostings(calls, PostingOutcomeKind.Posted));

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.EnsureAsync(CancellationToken.None));

        Assert.Empty(calls);
    }

    [Fact]
    public async Task A_dirty_context_is_refused_before_any_write()
    {
        var company = new CompanyContext();
        var calls = new List<string>();
        var context = Context(company);
        context.Periods.Add(new PeriodRow { Id = Guid.NewGuid(), CompanyId = AccountingPostedJournalFixture.CompanyId });
        var store = new AccountingPostedJournalFixtureStore(context, company, new FakeOpenPeriod(company, calls),
            new FakePostings(calls, PostingOutcomeKind.Posted));

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.EnsureAsync(CancellationToken.None));

        Assert.Empty(calls);
    }

    [Fact]
    public void The_fixture_is_synthetic_balanced_and_inside_the_open_period_and_the_money_profile()
    {
        var request = AccountingPostedJournalFixture.Request();
        Assert.Equal(AccountingOpenPeriodFixture.CompanyId, request.Source.CompanyId);
        Assert.InRange(request.Dates.AccountingDate, AccountingOpenPeriodFixture.StartsOn, AccountingOpenPeriodFixture.EndsOn);
        // The approved GDC TRY profile is effective from 2026-09-18 at scale 2 (Settings catalog).
        Assert.True(request.Dates.AccountingDate >= new DateOnly(2026, 9, 18));
        Assert.Equal(2, (decimal.GetBits(AccountingPostedJournalFixture.Amount)[3] >> 16) & 0xFF);
        Assert.Equal(
            request.Lines.Where(line => line.Side == PostingSide.Debit).Sum(line => line.Amount),
            request.Lines.Where(line => line.Side == PostingSide.Credit).Sum(line => line.Amount));
        Assert.All(request.Lines, line => Assert.Equal(AccountingPostedJournalFixture.CompanyId, line.Account!.Value.CompanyId));
        Assert.StartsWith("SENTETIK", AccountingPostedJournalFixture.MessageId, StringComparison.Ordinal);
        Assert.StartsWith("sentetik", AccountingPostedJournalFixture.PostingPurpose, StringComparison.Ordinal);
        Assert.StartsWith("sentetik", AccountingPostedJournalFixture.PostingRuleVersion, StringComparison.Ordinal);
        // Every fixed identity is distinct and none collides with the open-period fixture's.
        Guid[] ids =
        [
            AccountingPostedJournalFixture.JournalId, AccountingPostedJournalFixture.SourceId,
            AccountingPostedJournalFixture.DebitAccountId, AccountingPostedJournalFixture.CreditAccountId,
            AccountingPostedJournalFixture.FixtureActorId, AccountingOpenPeriodFixture.PeriodId,
            AccountingOpenPeriodFixture.PreparedByUserId, AccountingOpenPeriodFixture.ApprovedByUserId,
        ];
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public void The_registration_adds_only_this_scoped_store()
    {
        var services = new ServiceCollection().AddAccountingPostedJournalFixtureStore();
        var descriptor = Assert.Single(services);
        Assert.Equal(typeof(IAccountingPostedJournalFixtureStore), descriptor.ServiceType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public async Task Without_fixture_settings_the_registered_store_posts_through_the_modules_posting_store()
    {
        var calls = new List<string>();
        var company = new CompanyContext();
        var postings = new FakePostings(calls, PostingOutcomeKind.Posted);
        var services = new ServiceCollection();
        services.AddSingleton<ICompanyContext>(company);
        services.AddScoped(_ => Context(company));
        services.AddSingleton<IAccountingOpenPeriodFixtureStore>(new FakeOpenPeriod(company, calls));
        services.AddSingleton<IAccountingPostingStore>(postings);
        services.AddAccountingPostedJournalFixtureStore();
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IAccountingPostedJournalFixtureStore>().EnsureAsync(CancellationToken.None);

        Assert.Equal(["open-period", "receive"], calls);
        Assert.Single(postings.Received);
    }

    [Fact]
    public async Task With_fixture_settings_the_registered_store_checks_the_amount_with_them_and_a_rerun_writes_nothing()
    {
        // The browser seed's host scope has no request user, so Settings would refuse the module's own amount check;
        // the gated host passes the disposable fixture identity's Settings instead. Composed exactly as registered,
        // over the real open-period fixture and a real AccountingStore (EF in-memory; not SQL proof).
        var root = new InMemoryDatabaseRoot();
        var settings = new CountingSettings(SyntheticSettings.Scale(AccountingPostedJournalFixture.CompanyId, 2));
        var services = new ServiceCollection();
        services.AddScoped<CompanyContext>();
        services.AddScoped<ICompanyContext>(provider => provider.GetRequiredService<CompanyContext>());
        services.AddDbContext<AccountingDbContext>(options => options
            .UseInMemoryDatabase("accounting-posted-journal-fixture", root)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddScoped<IAuditEventCollector, NullAuditCollector>();
        services.AddSingleton(TimeProvider.System);
        services.AddAccountingOpenPeriodFixtureStore();
        services.AddAccountingPostedJournalFixtureStore(_ => settings);
        await using var provider = services.BuildServiceProvider(validateScopes: true);

        foreach (var _ in new[] { "first seed run", "re-run" })
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IAccountingPostedJournalFixtureStore>()
                .EnsureAsync(CancellationToken.None);
        }

        // The first run's amount check asked the supplied Settings once; the re-run is the delivery's replay.
        Assert.Equal(1, settings.MoneyProfileReads);
        await using var check = provider.CreateAsyncScope();
        check.ServiceProvider.GetRequiredService<CompanyContext>().Set(AccountingPostedJournalFixture.CompanyId);
        var db = check.ServiceProvider.GetRequiredService<AccountingDbContext>();
        Assert.Equal(AccountingPostedJournalFixture.JournalId, Assert.Single(await db.Journals.Select(row => row.Id).ToListAsync()));
        Assert.Equal(AccountingOpenPeriodFixture.PeriodId, Assert.Single(await db.Periods.Select(row => row.Id).ToListAsync()));
        Assert.Equal(2, await db.JournalLines.CountAsync());
        Assert.Equal(1, await db.Deliveries.CountAsync());
    }

    private static AccountingPostedJournalFixtureStore Store(
        CompanyContext company, IAccountingOpenPeriodFixtureStore openPeriod, IAccountingPostingStore postings) =>
        new(Context(company), company, openPeriod, postings);

    private static AccountingDbContext Context(ICompanyContext company) =>
        new(new DbContextOptionsBuilder<AccountingDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;")
            .Options, company);

    private sealed class FakeOpenPeriod(CompanyContext company, List<string> calls) : IAccountingOpenPeriodFixtureStore
    {
        public Task EnsureAsync(CancellationToken cancellationToken)
        {
            calls.Add("open-period");
            if (!company.IsResolved)
            {
                company.Set(AccountingOpenPeriodFixture.CompanyId);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakePostings(
        List<string> calls, PostingOutcomeKind kind, Guid? journalId = null, bool alreadyStored = false)
        : IAccountingPostingStore
    {
        public List<(PostingRequest Request, Guid PeriodId, AccountingCommandMetadata Metadata)> Received { get; } = [];

        public Task<PostingOutcome> ReceiveAsync(PostingRequest request, Guid periodId, AccountingCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            calls.Add("receive");
            Received.Add((request, periodId, metadata));
            var journal = FixtureJournal(journalId ?? AccountingPostedJournalFixture.JournalId);
            return Task.FromResult(new PostingOutcome(kind, kind == PostingOutcomeKind.ExceptionQueued ? null : journal));
        }

        private static Journal FixtureJournal(Guid id)
        {
            var fixture = AccountingPostedJournalFixture.Request();
            return Journal.Restore(
                id,
                JournalKind.Original,
                fixture.Source,
                fixture.PostingRuleVersion,
                fixture.CurrencyId,
                fixture.Dates,
                fixture.Lines.Select(line => line.Side == PostingSide.Debit
                    ? new JournalLine(line.Account!.Value, line.Amount!.Value, 0m)
                    : new JournalLine(line.Account!.Value, 0m, line.Amount!.Value)),
                null);
        }

        public Task<PostingOutcome> ReverseAsync(string consumer, string messageId, Guid reversedJournalId,
            Guid reversalJournalId, PostingDates dates, Guid periodId, AccountingCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PostingOutcome> CorrectAsync(string consumer, string messageId, Guid correctionId,
            Guid correctedJournalId, Guid correctionJournalId, string postingRuleVersion, PostingDates dates,
            IEnumerable<JournalLine> lines, Guid periodId, AccountingCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <summary>A fresh ledger has no fixture journal; a re-run's ledger holds it.</summary>
        public Task<Journal?> FindJournalAsync(Guid journalId, CancellationToken cancellationToken = default) =>
            Task.FromResult(alreadyStored && journalId == AccountingPostedJournalFixture.JournalId
                ? FixtureJournal(journalId)
                : null);

        public Task<AccountingJournalRecord?> ReadJournalAsync(Guid journalId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AccountingPage<AccountingJournalRecord>> ListJournalsAsync(Guid? periodId, int pageSize, string? cursor,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<PostingException>> ReadOpenExceptionsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PendingValuation>> ReadPendingValuationsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CountingSettings(ISettingsPrerequisiteProvider inner) : ISettingsPrerequisiteProvider
    {
        private int moneyProfileReads;

        public int MoneyProfileReads => moneyProfileReads;

        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => inner.GetUnitProfileAsync(unitId, effectiveOn, cancellationToken);

        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref moneyProfileReads);
            return inner.GetMoneyProfileAsync(currencyId, operation, effectiveOn, cancellationToken);
        }

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn,
            CancellationToken cancellationToken = default) =>
            inner.GetPeriodEligibilityAsync(operation, occurredOn, valueOn, cancellationToken);
    }

    private sealed class NullAuditCollector : IAuditEventCollector
    {
        public void Enqueue(AuditEventDraft auditEvent)
        {
        }
    }

    private sealed class PostingRequestComparer : IEqualityComparer<PostingRequest>
    {
        public bool Equals(PostingRequest? x, PostingRequest? y) =>
            x is not null && y is not null
            && (x.Consumer, x.MessageId, x.JournalId, x.Source, x.PostingRuleVersion, x.CurrencyId, x.Dates)
            == (y.Consumer, y.MessageId, y.JournalId, y.Source, y.PostingRuleVersion, y.CurrencyId, y.Dates)
            && x.Lines.SequenceEqual(y.Lines);

        public int GetHashCode(PostingRequest obj) => obj.MessageId.GetHashCode(StringComparison.Ordinal);
    }
}

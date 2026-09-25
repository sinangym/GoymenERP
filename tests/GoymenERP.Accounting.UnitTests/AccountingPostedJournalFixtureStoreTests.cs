using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;
using GoymenERP.Modules.Accounting.Infrastructure.Fixtures;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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

    private sealed class FakePostings(List<string> calls, PostingOutcomeKind kind, Guid? journalId = null)
        : IAccountingPostingStore
    {
        public List<(PostingRequest Request, Guid PeriodId, AccountingCommandMetadata Metadata)> Received { get; } = [];

        public Task<PostingOutcome> ReceiveAsync(PostingRequest request, Guid periodId, AccountingCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            calls.Add("receive");
            Received.Add((request, periodId, metadata));
            var fixture = AccountingPostedJournalFixture.Request();
            var journal = Journal.Restore(
                journalId ?? AccountingPostedJournalFixture.JournalId,
                JournalKind.Original,
                fixture.Source,
                fixture.PostingRuleVersion,
                fixture.CurrencyId,
                fixture.Dates,
                fixture.Lines.Select(line => line.Side == PostingSide.Debit
                    ? new JournalLine(line.Account!.Value, line.Amount!.Value, 0m)
                    : new JournalLine(line.Account!.Value, 0m, line.Amount!.Value)),
                null);
            return Task.FromResult(new PostingOutcome(kind, kind == PostingOutcomeKind.ExceptionQueued ? null : journal));
        }

        public Task<PostingOutcome> ReverseAsync(string consumer, string messageId, Guid reversedJournalId,
            Guid reversalJournalId, PostingDates dates, Guid periodId, AccountingCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PostingOutcome> CorrectAsync(string consumer, string messageId, Guid correctionId,
            Guid correctedJournalId, Guid correctionJournalId, string postingRuleVersion, PostingDates dates,
            IEnumerable<JournalLine> lines, Guid periodId, AccountingCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Journal?> FindJournalAsync(Guid journalId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AccountingJournalRecord?> ReadJournalAsync(Guid journalId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AccountingPage<AccountingJournalRecord>> ListJournalsAsync(Guid? periodId, int pageSize, string? cursor,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<PostingException>> ReadOpenExceptionsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PendingValuation>> ReadPendingValuationsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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

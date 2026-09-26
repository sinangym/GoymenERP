using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Accounting.UnitTests;

/// <summary>
/// #485 review follow-ups (journal reversal), run over the REAL public adapter, owner lifecycle and
/// <see cref="AccountingStore"/> on the EF in-memory provider: re-reversal, duplicate and racing approvals, the
/// approval-time period refusals and the propose bounds. Every test asserts the money effect itself (reversal
/// journals linked to the source, net movement of the source's accounts), not only the answer.
/// <para>
/// The in-memory provider has no transactions, filtered unique indexes or SQL isolation, so these tests are NOT race
/// proof. Each scenario is refused before the store saves, or (the racing-approval theory) forces ONE deterministic
/// interleaving, a read that precedes the winner's commit, to pin the answer mapping of that interleaving. The real-SQL
/// race evidence is the SQL Server relational acceptance suite:
/// <c>AccountingApiSqlServerRelationalAcceptanceTests.A_journal_reversal_proposal_is_pending_until_another_user_approves_one_linked_reversal</c>
/// sends two approvals of one proposal at once (same key, and two keys) for several fresh proposals under whatever
/// interleaving SQL Server produces, including save-time unique-index and version-token losses, and asserts one
/// reversal journal, zero net effect and the declared answers.
/// </para>
/// </summary>
public sealed class AccountingJournalReversalStoreFollowUpV1Tests
{
    private static readonly Guid Maker = Guid.Parse("e1000000-0000-4000-8000-000000000001");
    private static readonly Guid Checker = Guid.Parse("e1000000-0000-4000-8000-000000000002");
    private static readonly Guid SecondChecker = Guid.Parse("e1000000-0000-4000-8000-000000000003");
    private static readonly Guid PeriodPreparer = Guid.Parse("e1000000-0000-4000-8000-000000000004");
    private static readonly Guid PeriodApprover = Guid.Parse("e1000000-0000-4000-8000-000000000005");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-25T12:00:00Z");
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(30);

    private const int Pending = (int)AccountingJournalReversalProposalStatusV1.Pending;
    private const int Approved = (int)AccountingJournalReversalProposalStatusV1.Approved;

    [Fact]
    public async Task A_journal_already_reversed_by_an_approved_proposal_is_not_reversed_again_by_a_second_proposal()
    {
        var book = await Book.CreateAsync();
        var source = await book.PostSourceAsync("SENTETIK-rereversal-source");
        var reversal = await book.ProposeAndApproveAsync(Guid.NewGuid(), source.Id);

        var secondId = Guid.NewGuid();
        var proposed = await book.ProposeAsync(Maker, Proposal(secondId, source.Id, book.September), "rereversal-propose");
        // Propose admits the source by company identity only; whether an already-reversed source should already be
        // refused there is #485's open propose-time admission question. The refusal that guards the ledger is approval's.
        Assert.Equal((AccountingApiStatus.Created, "Pending"), (proposed.Status, proposed.Resource!.Status));

        var approval = await book.ApproveAsync(Checker, secondId, 1, "rereversal-approve");
        var retried = await book.ApproveAsync(Checker, secondId, 1, "rereversal-approve");

        foreach (var answer in new[] { approval, retried })
        {
            Assert.Equal(
                (AccountingApiStatus.Conflict, AccountingJournalReversalStoreCodesV1.PostingConflict, false),
                (answer.Status, answer.Code, answer.IsReplay));
            Assert.Null(answer.Resource);
        }

        // One reversal of the source, and its accounts net to zero: the second proposal had no money effect.
        Assert.Equal(reversal, Assert.Single(await book.ReversalsOfAsync(source.Id)));
        Assert.Equal(2, await book.JournalCountAsync());
        await book.AssertSourceAccountsNetAsync(0m);
        await book.AssertUndecidedAsync(secondId);
        // A posting conflict is not the key's terminal answer: only the first approval's command exists.
        Assert.Equal(1, await book.ApprovalCommandCountAsync());
        Assert.Equal(1, book.Audit.CountFor(secondId));
    }

    [Fact]
    public async Task A_reversal_journal_is_not_itself_reversed_and_its_approval_answers_reversal_of_reversal()
    {
        var book = await Book.CreateAsync();
        var source = await book.PostSourceAsync("SENTETIK-reversal-of-reversal-source");
        var reversal = await book.ProposeAndApproveAsync(Guid.NewGuid(), source.Id);

        var proposalId = Guid.NewGuid();
        Assert.Equal(AccountingApiStatus.Created,
            (await book.ProposeAsync(Maker, Proposal(proposalId, reversal, book.September), "reverse-the-reversal")).Status);

        var approval = await book.ApproveAsync(Checker, proposalId, 1, "approve-reverse-the-reversal");

        Assert.Equal(
            (AccountingApiStatus.Unprocessable, AccountingErrorCodes.ReversalOfReversal),
            (approval.Status, approval.Code));
        Assert.Null(approval.Resource);
        Assert.Contains(AccountingErrorCodes.ReversalOfReversal, AccountingJournalReversalUnprocessableCodesV1.Approve);
        Assert.Empty(await book.ReversalsOfAsync(reversal));
        Assert.Equal(reversal, Assert.Single(await book.ReversalsOfAsync(source.Id)));
        Assert.Equal(2, await book.JournalCountAsync());
        await book.AssertSourceAccountsNetAsync(0m);
        await book.AssertUndecidedAsync(proposalId);
        Assert.Equal(1, await book.ApprovalCommandCountAsync());
    }

    [Theory]
    [InlineData(1, AccountingApiCodes.VersionConflict, "expectedVersion", true)]
    [InlineData(2, AccountingJournalReversalPolicyCodesV1.NotPending, "proposalId", false)]
    public async Task A_second_approval_under_another_key_is_a_conflict_and_posts_no_second_reversal(
        int expectedVersion,
        string code,
        string field,
        bool terminal)
    {
        var book = await Book.CreateAsync();
        var source = await book.PostSourceAsync("SENTETIK-second-approval-source");
        var proposalId = Guid.NewGuid();
        Assert.Equal(AccountingApiStatus.Created,
            (await book.ProposeAsync(Maker, Proposal(proposalId, source.Id, book.September), "second-approval-propose")).Status);
        var first = await book.ApproveAsync(Checker, proposalId, 1, "first-approval");
        Assert.Equal((AccountingApiStatus.Ok, "Approved", 2), (first.Status, first.Resource!.Status, first.Resource.Version));

        var second = await book.ApproveAsync(SecondChecker, proposalId, expectedVersion, "second-approval");
        var again = await book.ApproveAsync(SecondChecker, proposalId, expectedVersion, "second-approval");

        Assert.Equal((AccountingApiStatus.Conflict, code, field), (second.Status, second.Code, second.Field));
        Assert.Null(second.Resource);
        if (terminal)
        {
            Assert.Equal((1L, 2L), (second.ExpectedVersion!.Value, second.CurrentVersion!.Value));
        }

        // A stale version is the key's terminal answer and replays (§7); not-pending is evaluated afresh.
        Assert.Equal((AccountingApiStatus.Conflict, code, terminal), (again.Status, again.Code, again.IsReplay));
        Assert.Equal(terminal ? 2 : 1, await book.ApprovalCommandCountAsync());

        var reversal = Assert.Single(await book.ReversalsOfAsync(source.Id));
        Assert.Equal(first.Resource.ReversalJournalId, reversal);
        Assert.Equal(2, await book.JournalCountAsync());
        await book.AssertSourceAccountsNetAsync(0m);
        await book.AssertApprovedOnceAsync(proposalId, reversal, Checker);
    }

    [Fact]
    public async Task The_same_approval_key_replays_the_one_reversal_and_binds_its_checker_and_version()
    {
        var book = await Book.CreateAsync();
        var source = await book.PostSourceAsync("SENTETIK-approval-replay-source");
        var proposalId = Guid.NewGuid();
        Assert.Equal(AccountingApiStatus.Created,
            (await book.ProposeAsync(Maker, Proposal(proposalId, source.Id, book.September), "approval-replay-propose")).Status);

        var approved = await book.ApproveAsync(Checker, proposalId, 1, "approval-replay", correlation: "approval-first");
        var replay = await book.ApproveAsync(Checker, proposalId, 1, "approval-replay");
        var otherChecker = await book.ApproveAsync(SecondChecker, proposalId, 1, "approval-replay");
        var otherVersion = await book.ApproveAsync(Checker, proposalId, 2, "approval-replay");

        Assert.Equal((AccountingApiStatus.Ok, false), (approved.Status, approved.IsReplay));
        var reversal = approved.Resource!.ReversalJournalId!.Value;
        Assert.Equal(
            (AccountingApiStatus.Ok, true, "approval-first", reversal, "Approved", 2),
            (replay.Status, replay.IsReplay, replay.OriginalCorrelationId, replay.Resource!.ReversalJournalId!.Value,
                replay.Resource.Status, replay.Resource.Version));
        foreach (var refused in new[] { otherChecker, otherVersion })
        {
            Assert.Equal((AccountingApiStatus.Conflict, AccountingApiCodes.IdempotencyConflict),
                (refused.Status, refused.Code));
            Assert.Null(refused.Resource);
        }

        Assert.Equal(reversal, Assert.Single(await book.ReversalsOfAsync(source.Id)));
        Assert.Equal(2, await book.JournalCountAsync());
        await book.AssertSourceAccountsNetAsync(0m);
        await book.AssertApprovedOnceAsync(proposalId, reversal, Checker);
        Assert.Equal(1, await book.ApprovalCommandCountAsync());
    }

    /// <summary>
    /// Two approvals of one proposal at once, interleaved deterministically: A (the checker, key
    /// <c>racing-approval-a</c>) reads the pending proposal and stops at its first in-store authority fence (after the
    /// read, before the posting); B, which found no stored answer for its key, approves and commits the reversal; then A
    /// resumes on its earlier read, and the store refuses A's posting because B's delivery for the proposal exists.
    /// <list type="bullet">
    /// <item><c>same-key</c>: B is the same checker with the same key and request, so B's recorded answer IS this key's
    /// answer: A is its replay (200, B's reversal journal, B's correlation), as a lost save-time race already was.</item>
    /// <item><c>same-key-other-checker</c>: B used A's key as another actor; that record does not bind A, so A keeps the
    /// declared 409 posting conflict (and its retry is the key's idempotency conflict).</item>
    /// <item><c>other-key</c>: nothing is recorded under A's key, so A keeps the 409 posting conflict (and its retry
    /// meets the stale version).</item>
    /// </list>
    /// In every case exactly one reversal is posted and the second approval has no money effect.
    /// </summary>
    [Theory]
    [InlineData("same-key")]
    [InlineData("same-key-other-checker")]
    [InlineData("other-key")]
    public async Task Two_racing_approvals_of_one_proposal_post_exactly_one_reversal(string race)
    {
        var book = await Book.CreateAsync();
        var source = await book.PostSourceAsync("SENTETIK-racing-approval-source");
        var proposalId = Guid.NewGuid();
        Assert.Equal(AccountingApiStatus.Created,
            (await book.ProposeAsync(Maker, Proposal(proposalId, source.Id, book.September), "racing-propose")).Status);
        const string loserKey = "racing-approval-a";
        var winnerKey = race == "other-key" ? "racing-approval-b" : loserKey;
        var winnerActor = race == "same-key" ? Checker : SecondChecker;

        // Calls 1 and 2 are the lifecycle's own revalidations; call 3 is the store's fence after it read the proposal.
        var gate = new GatedAuthority(gateAtCall: 3);
        var parked = book.ApproveAsync(Checker, proposalId, 1, loserKey, gate);
        await gate.Reached.Task.WaitAsync(GateTimeout);
        var winner = await book.ApproveAsync(winnerActor, proposalId, 1, winnerKey, correlation: "racing-winner");
        gate.Release.SetResult();
        var loser = await parked.WaitAsync(GateTimeout);

        Assert.Equal((AccountingApiStatus.Ok, false, "Approved"), (winner.Status, winner.IsReplay, winner.Resource!.Status));
        var reversal = winner.Resource.ReversalJournalId!.Value;
        if (race == "same-key")
        {
            Assert.Equal(
                (AccountingApiStatus.Ok, true, "racing-winner", reversal, "Approved", 2),
                (loser.Status, loser.IsReplay, loser.OriginalCorrelationId, loser.Resource!.ReversalJournalId!.Value,
                    loser.Resource.Status, loser.Resource.Version));
            Assert.Equal(Checker, loser.Resource.DecidedBy);
        }
        else
        {
            Assert.Equal(
                (AccountingApiStatus.Conflict, AccountingJournalReversalStoreCodesV1.PostingConflict, false),
                (loser.Status, loser.Code, loser.IsReplay));
            Assert.Null(loser.Resource);
        }

        Assert.Equal(reversal, Assert.Single(await book.ReversalsOfAsync(source.Id)));
        Assert.Equal(2, await book.JournalCountAsync());
        await book.AssertSourceAccountsNetAsync(0m);
        await book.AssertApprovedOnceAsync(proposalId, reversal, winnerActor);
        // The replay and the 409 record nothing: the winner's command is the only approval command.
        Assert.Equal(1, await book.ApprovalCommandCountAsync());

        // The loser's retry under its own key answers from the command log or the stored version.
        var retry = await book.ApproveAsync(Checker, proposalId, 1, loserKey);
        switch (race)
        {
            case "same-key":
                Assert.Equal(
                    (AccountingApiStatus.Ok, true, "racing-winner", reversal),
                    (retry.Status, retry.IsReplay, retry.OriginalCorrelationId, retry.Resource!.ReversalJournalId!.Value));
                break;
            case "same-key-other-checker":
                Assert.Equal((AccountingApiStatus.Conflict, AccountingApiCodes.IdempotencyConflict), (retry.Status, retry.Code));
                break;
            default:
                Assert.Equal((AccountingApiStatus.Conflict, AccountingApiCodes.VersionConflict), (retry.Status, retry.Code));
                break;
        }

        Assert.Equal(reversal, Assert.Single(await book.ReversalsOfAsync(source.Id)));
        await book.AssertSourceAccountsNetAsync(0m);
    }

    [Theory]
    [InlineData("proposed", AccountingErrorCodes.PeriodNotOpen)]
    [InlineData("closed", AccountingErrorCodes.PeriodClosed)]
    [InlineData("not-covering", AccountingErrorCodes.PeriodDoesNotCover)]
    public async Task Approval_into_a_period_that_cannot_take_the_reversal_is_422_and_leaves_the_proposal_pending(
        string period,
        string code)
    {
        var book = await Book.CreateAsync();
        var source = await book.PostSourceAsync("SENTETIK-period-refusal-source");
        var october = Guid.NewGuid();
        await book.ProposePeriodAsync(october, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        if (period == "closed")
        {
            await book.ClosePeriodAsync(book.September);
        }

        var (periodId, date) = period switch
        {
            "proposed" => (october, "2026-10-05"),
            "closed" => (book.September, "2026-09-24"),
            _ => (book.September, "2026-10-05"),
        };
        var proposalId = Guid.NewGuid();
        // Admission checks the period's company identity only, so each of these proposals is stored as pending.
        Assert.Equal(AccountingApiStatus.Created, (await book.ProposeAsync(Maker,
            Proposal(proposalId, source.Id, periodId, date), "period-refusal-propose")).Status);

        var approval = await book.ApproveAsync(Checker, proposalId, 1, "period-refusal-approve");
        var again = await book.ApproveAsync(Checker, proposalId, 1, "period-refusal-approve");

        foreach (var answer in new[] { approval, again })
        {
            Assert.Equal((AccountingApiStatus.Unprocessable, code, false), (answer.Status, answer.Code, answer.IsReplay));
            Assert.Null(answer.Resource);
        }

        Assert.Contains(code, AccountingJournalReversalUnprocessableCodesV1.Approve);
        Assert.DoesNotContain(code, AccountingJournalReversalUnprocessableCodesV1.Propose);
        Assert.Empty(await book.ReversalsOfAsync(source.Id));
        Assert.Equal(1, await book.JournalCountAsync());
        await book.AssertSourceAccountsNetAsync(100m);
        await book.AssertUndecidedAsync(proposalId);
        Assert.Equal(0, await book.ApprovalCommandCountAsync());
        Assert.Equal(1, book.Audit.CountFor(proposalId));
    }

    [Fact]
    public async Task A_not_open_refusal_is_not_the_keys_answer_and_the_same_key_approves_once_the_period_opens()
    {
        // Regression pin of the #485 fix (Sales review 5326851870 P2-1): the not-open 422 is a stable refusal that
        // records nothing, so the same approval key succeeds, once, after the period is approved.
        var book = await Book.CreateAsync();
        var source = await book.PostSourceAsync("SENTETIK-not-open-then-open-source");
        var october = Guid.NewGuid();
        await book.ProposePeriodAsync(october, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        var proposalId = Guid.NewGuid();
        Assert.Equal(AccountingApiStatus.Created, (await book.ProposeAsync(Maker,
            Proposal(proposalId, source.Id, october, "2026-10-05"), "not-open-propose")).Status);

        var refused = await book.ApproveAsync(Checker, proposalId, 1, "not-open-approve");
        Assert.Equal((AccountingApiStatus.Unprocessable, AccountingErrorCodes.PeriodNotOpen), (refused.Status, refused.Code));
        Assert.Empty(await book.ReversalsOfAsync(source.Id));

        await book.ApprovePeriodAsync(october);
        var approved = await book.ApproveAsync(Checker, proposalId, 1, "not-open-approve", correlation: "not-open-approved");
        var replay = await book.ApproveAsync(Checker, proposalId, 1, "not-open-approve");

        Assert.Equal((AccountingApiStatus.Ok, false, "Approved"), (approved.Status, approved.IsReplay, approved.Resource!.Status));
        var reversal = approved.Resource.ReversalJournalId!.Value;
        Assert.Equal((AccountingApiStatus.Ok, true, "not-open-approved", reversal),
            (replay.Status, replay.IsReplay, replay.OriginalCorrelationId, replay.Resource!.ReversalJournalId!.Value));
        Assert.Equal(reversal, Assert.Single(await book.ReversalsOfAsync(source.Id)));
        Assert.Equal(october, await book.PeriodOfJournalAsync(reversal));
        await book.AssertSourceAccountsNetAsync(0m);
        await book.AssertApprovedOnceAsync(proposalId, reversal, Checker);
        Assert.Equal(1, await book.ApprovalCommandCountAsync());
    }

    [Theory]
    [InlineData("reason", "2026-09-24", "2026-09-24", null, 1025)]
    [InlineData("transactionDate", "0001-01-01", "2026-09-24", null, 32)]
    [InlineData("accountingDate", "2026-09-24", "0001-01-01", null, 32)]
    [InlineData("valueDate", "2026-09-24", "2026-09-24", "0001-01-01", 32)]
    public async Task Out_of_bound_proposal_is_the_routes_400_and_stores_nothing(
        string field,
        string transactionDate,
        string accountingDate,
        string? valueDate,
        int reasonLength)
    {
        var book = await Book.CreateAsync();
        var source = await book.PostSourceAsync("SENTETIK-propose-bounds-source");
        var proposalId = Guid.NewGuid();
        var request = new ProposeAccountingJournalReversalRequestV1(
            proposalId, source.Id, book.September, transactionDate, accountingDate, valueDate,
            " " + new string('r', reasonLength) + " ");

        var refused = await book.ProposeAsync(Maker, request, "propose-bounds");

        Assert.Equal((AccountingApiStatus.Invalid, AccountingApiCodes.RequestInvalid, field),
            (refused.Status, refused.Code, refused.Field));
        Assert.Null(refused.Resource);
        Assert.Equal(0, await book.ProposalCountAsync());
        Assert.Equal(0, await book.ProposeCommandCountAsync());
        Assert.Equal(0, book.Audit.CountFor(proposalId));

        // The exact bound, surrounded by whitespace, is admitted and stored trimmed at full length.
        var bound = AccountingJournalReversalPolicyV1.ReasonMaximumLength;
        var admitted = await book.ProposeAsync(Maker, request with
        {
            TransactionDate = "2026-09-24",
            AccountingDate = "2026-09-24",
            ValueDate = null,
            Reason = "  " + new string('r', bound) + "  ",
        }, "propose-bounds-exact");
        Assert.Equal(AccountingApiStatus.Created, admitted.Status);
        Assert.Equal(bound, (await book.ProposalRowAsync(proposalId)).Reason.Length);
    }

    private static ProposeAccountingJournalReversalRequestV1 Proposal(
        Guid proposalId,
        Guid sourceJournalId,
        Guid periodId,
        string date = "2026-09-24") =>
        new(proposalId, sourceJournalId, periodId, date, date, null, "SENTETIK ters kayıt gerekçesi");

    private static AccountingCommandMetadata Metadata() => new(
        "synthetic-actor", "synthetic.accounting", ["SYNTHETIC"], "synthetic-correlation", null, "synthetic-key", "Web");

    private static PermissionScope Granted() => new(
        true,
        true,
        new HashSet<string>(["SYNTHETIC-ACCOUNTING-REVERSAL"], StringComparer.Ordinal),
        new HashSet<Guid>(),
        new HashSet<Guid>(),
        new HashSet<Guid>());

    /// <summary>One company's Accounting database, with an open September period, shared by every request scope.</summary>
    private sealed class Book
    {
        private readonly InMemoryDatabaseRoot root = new();
        private readonly string database = Guid.NewGuid().ToString("N");

        public AccountingWorld World { get; } = new();

        public Guid Company => World.Company;

        public Guid September { get; } = Guid.NewGuid();

        public CountingAuditCollector Audit { get; } = new();

        public static async Task<Book> CreateAsync()
        {
            var book = new Book();
            await book.ProposePeriodAsync(book.September, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
            await book.ApprovePeriodAsync(book.September);
            return book;
        }

        /// <summary>One request's scope: its own context, store, lifecycle and adapter, as the host builds them.</summary>
        public RequestScope Open(Guid actor, IErpAuthorityRevalidationService? authority = null, string? correlation = null)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(Company);
            var options = new DbContextOptionsBuilder<AccountingDbContext>()
                .UseInMemoryDatabase(database, root)
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            var dbContext = new AccountingDbContext(options, companyContext);
            var clock = new FixedTimeProvider(Now);
            var store = new AccountingStore(dbContext, companyContext, Audit, clock,
                new AccountingAmountScaleVerifier(SyntheticSettings.Scale(Company, 2)));
            var lifecycle = new AccountingJournalReversalServiceV1(
                store,
                store,
                authority ?? new FixedAuthority(),
                companyContext,
                new FixedUser(actor, Company),
                new FixedCorrelation(correlation ?? Guid.NewGuid().ToString("N")),
                clock);
            return new RequestScope(dbContext, store, new AccountingJournalReversalPublicServiceV1(lifecycle));
        }

        public async Task ProposePeriodAsync(Guid periodId, DateOnly startsOn, DateOnly endsOn)
        {
            await using var scope = Open(PeriodPreparer);
            await scope.Store.ProposePeriodAsync(periodId, startsOn, endsOn, PeriodPreparer, Metadata());
        }

        public async Task ApprovePeriodAsync(Guid periodId)
        {
            await using var scope = Open(PeriodApprover);
            await scope.Store.ApprovePeriodAsync(periodId, 1, PeriodApprover, Metadata());
        }

        public async Task ClosePeriodAsync(Guid periodId)
        {
            await using var scope = Open(PeriodApprover);
            var stored = await scope.Store.FindPeriodAsync(periodId);
            await scope.Store.ClosePeriodAsync(periodId, stored!.Version, PeriodApprover, Metadata());
        }

        public async Task<Journal> PostSourceAsync(string messageId)
        {
            await using var scope = Open(PeriodPreparer);
            var outcome = await scope.Store.ReceiveAsync(World.Request(messageId, amount: 100m), September, Metadata());
            Assert.Equal(PostingOutcomeKind.Posted, outcome.Kind);
            return outcome.Journal!;
        }

        public async Task<AccountingApiResult<AccountingJournalReversalProposalViewV1>> ProposeAsync(
            Guid actor,
            ProposeAccountingJournalReversalRequestV1 request,
            string idempotencyKey)
        {
            await using var scope = Open(actor);
            return await scope.Api.ProposeAsync(request, idempotencyKey);
        }

        public async Task<AccountingApiResult<AccountingJournalReversalProposalViewV1>> ApproveAsync(
            Guid actor,
            Guid proposalId,
            int expectedVersion,
            string idempotencyKey,
            IErpAuthorityRevalidationService? authority = null,
            string? correlation = null)
        {
            await using var scope = Open(actor, authority, correlation);
            return await scope.Api.ApproveAsync(
                proposalId, new ApproveAccountingJournalReversalRequestV1(expectedVersion), idempotencyKey);
        }

        public async Task<Guid> ProposeAndApproveAsync(Guid proposalId, Guid sourceJournalId)
        {
            var proposed = await ProposeAsync(Maker, Proposal(proposalId, sourceJournalId, September), $"propose-{proposalId:N}");
            Assert.Equal(AccountingApiStatus.Created, proposed.Status);
            var approved = await ApproveAsync(Checker, proposalId, 1, $"approve-{proposalId:N}");
            Assert.Equal(AccountingApiStatus.Ok, approved.Status);
            return approved.Resource!.ReversalJournalId!.Value;
        }

        public Task<Guid[]> ReversalsOfAsync(Guid journalId) => ReadAsync(dbContext => dbContext.Journals
            .Where(row => row.Kind == (int)JournalKind.Reversal && row.LinkedJournalId == journalId)
            .Select(row => row.Id)
            .ToArrayAsync());

        public Task<int> JournalCountAsync() => ReadAsync(dbContext => dbContext.Journals.CountAsync());

        public Task<Guid> PeriodOfJournalAsync(Guid journalId) => ReadAsync(dbContext => dbContext.Journals
            .Where(row => row.Id == journalId).Select(row => row.PeriodId).SingleAsync());

        public Task<int> ProposalCountAsync() => ReadAsync(dbContext => dbContext.JournalReversalProposals.CountAsync());

        public Task<int> ApprovalCommandCountAsync() => ReadAsync(dbContext => dbContext.Commands
            .CountAsync(row => row.CommandName == AccountingJournalReversalCommandNamesV1.Approve));

        public Task<int> ProposeCommandCountAsync() => ReadAsync(dbContext => dbContext.Commands
            .CountAsync(row => row.CommandName == AccountingJournalReversalCommandNamesV1.Propose));

        public Task<int> DeliveryCountAsync(Guid proposalId) => ReadAsync(dbContext => dbContext.Deliveries
            .CountAsync(row => row.MessageId == proposalId.ToString("D")));

        internal Task<AccountingJournalReversalProposalRow> ProposalRowAsync(Guid proposalId) =>
            ReadAsync(dbContext => dbContext.JournalReversalProposals.SingleAsync(row => row.Id == proposalId));

        /// <summary>The source's debit and credit accounts after every stored journal (original 100, reversal -100).</summary>
        public async Task AssertSourceAccountsNetAsync(decimal debitSideNet)
        {
            var debit = World.DebitAccount.AccountId;
            var credit = World.CreditAccount.AccountId;
            var lines = await ReadAsync(dbContext => dbContext.JournalLines
                .Where(line => line.AccountId == debit || line.AccountId == credit)
                .ToListAsync());
            Assert.Equal(debitSideNet, lines.Where(line => line.AccountId == debit).Sum(line => line.Debit - line.Credit));
            Assert.Equal(-debitSideNet, lines.Where(line => line.AccountId == credit).Sum(line => line.Debit - line.Credit));
        }

        /// <summary>Still Pending v1 with no decision or reversal link, and no delivery was stored under its id.</summary>
        public async Task AssertUndecidedAsync(Guid proposalId)
        {
            var row = await ProposalRowAsync(proposalId);
            Assert.Equal((Pending, 1), (row.Status, row.Version));
            Assert.Null(row.ReversalJournalId);
            Assert.Null(row.DecidedByUserId);
            Assert.Null(row.DecidedAtUtc);
            Assert.Equal(0, await DeliveryCountAsync(proposalId));
        }

        /// <summary>Approved v2 by the given checker, linked to the one reversal, with exactly one stored delivery.</summary>
        public async Task AssertApprovedOnceAsync(Guid proposalId, Guid reversal, Guid checker)
        {
            var row = await ProposalRowAsync(proposalId);
            Assert.Equal((Approved, 2, reversal, checker),
                (row.Status, row.Version, row.ReversalJournalId!.Value, row.DecidedByUserId!.Value));
            Assert.Equal(1, await DeliveryCountAsync(proposalId));
        }

        private async Task<T> ReadAsync<T>(Func<AccountingDbContext, Task<T>> read)
        {
            await using var scope = Open(Maker);
            return await read(scope.Db);
        }
    }

    private sealed class RequestScope(
        AccountingDbContext dbContext,
        AccountingStore store,
        AccountingJournalReversalPublicServiceV1 api) : IAsyncDisposable
    {
        public AccountingDbContext Db { get; } = dbContext;

        public AccountingStore Store { get; } = store;

        public AccountingJournalReversalPublicServiceV1 Api { get; } = api;

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class CountingAuditCollector : IAuditEventCollector
    {
        private readonly List<AuditEventDraft> events = [];
        private readonly Lock gate = new();

        public void Enqueue(AuditEventDraft auditEvent)
        {
            lock (gate)
            {
                events.Add(auditEvent);
            }
        }

        public int CountFor(Guid entityId)
        {
            lock (gate)
            {
                return events.Count(draft => draft.EntityId == entityId.ToString("D"));
            }
        }
    }

    private sealed class FixedAuthority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default) => Task.FromResult(Granted());
    }

    /// <summary>Grants every call, but parks the given call until <see cref="Release"/> is completed.</summary>
    private sealed class GatedAuthority(int gateAtCall) : IErpAuthorityRevalidationService
    {
        private int calls;

        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref calls) == gateAtCall)
            {
                Reached.TrySetResult();
                await Release.Task.WaitAsync(GateTimeout, cancellationToken);
            }

            return Granted();
        }
    }

    private sealed class FixedUser(Guid actor, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public string UserId => actor.ToString("D");

        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([company]);

        public bool CanAccessCompany(Guid companyId) => companyId == company;
    }

    private sealed class FixedCorrelation(string correlationId) : ICorrelationContext
    {
        public bool IsResolved => true;

        public string CorrelationId => correlationId;

        public string? CausationId => null;

        public string SourceChannel => "UnitTest";

        public string? ClientIpAddress => null;

        public string? UserAgent => null;

        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress,
            string? userAgent) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

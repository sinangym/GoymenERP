using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Accounting.UnitTests;

/// <summary>
/// The #486 review follow-up for normal-journal drafts: two approvals of one draft, where the loser read the draft
/// pending before the winner committed. Run over the REAL owner lifecycle (<see cref="NormalJournalDraftServiceV1"/>),
/// approval policy and <see cref="AccountingStore"/> on the EF in-memory provider.
/// <para>
/// These tests are NOT race proof. The in-memory provider has no transactions, unique indexes or SQL isolation, so each
/// case forces ONE deterministic interleaving (the loser parks at the store's first authority fence, after its draft
/// read) to pin how that interleaving is answered. The real-SQL race evidence is the SQL Server relational acceptance
/// case in <c>AccountingSqlServerRelationalAcceptanceTests.A_normal_draft_is_reopened_posted_once_and_a_late_fence_refusal_rolls_everything_back</c>:
/// it sends two approvals of one draft at once (same key, and two keys) for several fresh drafts under whatever
/// interleaving SQL Server produces, accepting any of the approve route's declared answers for the loser, and asserts
/// one posted journal and one posting's net effect.
/// </para>
/// </summary>
public sealed class NormalJournalDraftApprovalRaceV1Tests
{
    private static readonly Guid Preparer = Guid.Parse("f1000000-0000-4000-8000-000000000001");
    private static readonly Guid Checker = Guid.Parse("f1000000-0000-4000-8000-000000000002");
    private static readonly Guid SecondChecker = Guid.Parse("f1000000-0000-4000-8000-000000000003");
    private static readonly Guid PeriodPreparer = Guid.Parse("f1000000-0000-4000-8000-000000000004");
    private static readonly Guid PeriodApprover = Guid.Parse("f1000000-0000-4000-8000-000000000005");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-25T12:00:00Z");
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(30);
    private const decimal Amount = 10m;

    /// <summary>
    /// A (the checker, key <c>racing-draft-a</c>) reads the pending draft and parks at the store's first authority
    /// fence; B, which found no stored answer for its key, approves and commits the one posting; then A resumes on its
    /// earlier read and the store refuses A's posting because B's delivery of the draft's message exists.
    /// <list type="bullet">
    /// <item><c>same-key</c>: B is the same checker with the same key and request, so B's recorded answer IS this key's
    /// answer: A is its replay (200, the one posted journal, B's correlation).</item>
    /// <item><c>same-key-other-checker</c>: B used A's key as another actor; that record does not bind A, so A keeps the
    /// declared 409 posting conflict (and its retry is the key's idempotency conflict).</item>
    /// <item><c>other-key</c>: nothing is recorded under A's key, so A keeps the 409 posting conflict (and its retry
    /// meets the stale version).</item>
    /// </list>
    /// In every case exactly one journal is posted for the draft and the second approval has no money effect.
    /// </summary>
    [Theory]
    [InlineData("same-key")]
    [InlineData("same-key-other-checker")]
    [InlineData("other-key")]
    public async Task Two_racing_approvals_of_one_normal_draft_post_exactly_one_journal(string race)
    {
        var book = await Book.CreateAsync();
        var draft = await book.CreateDraftAsync();
        const string loserKey = "racing-draft-a";
        var winnerKey = race == "other-key" ? "racing-draft-b" : loserKey;
        var winnerActor = race == "same-key" ? Checker : SecondChecker;

        // Calls 1-2 are the lifecycle's revalidations, 3-4 the approval policy's; call 5 is the store's first fence,
        // after it read the pending draft and its lines.
        var gate = new GatedAuthority(gateAtCall: 5);
        var parked = book.ApproveAsync(Checker, draft.Id, loserKey, gate);
        await gate.Reached.Task.WaitAsync(GateTimeout);
        var winner = await book.ApproveAsync(winnerActor, draft.Id, winnerKey, correlation: "racing-draft-winner");
        gate.Release.SetResult();
        var loser = await parked.WaitAsync(GateTimeout);

        Assert.Equal((AccountingApiStatus.Ok, false, "Approved", 2L, draft.Id),
            (winner.Status, winner.IsReplay, winner.Resource!.Status, winner.Resource.Version,
                winner.Resource.PostedJournalId!.Value));
        if (race == "same-key")
        {
            Assert.Equal(
                (AccountingApiStatus.Ok, true, "racing-draft-winner", draft.Id, "Approved", 2L, Checker),
                (loser.Status, loser.IsReplay, loser.OriginalCorrelationId, loser.Resource!.PostedJournalId!.Value,
                    loser.Resource.Status, loser.Resource.Version, loser.Resource.ApprovedByUserId!.Value));
        }
        else
        {
            Assert.Equal(
                (AccountingApiStatus.Conflict, NormalJournalDraftStoreCodesV1.PostingConflict, false),
                (loser.Status, loser.Code, loser.IsReplay));
            Assert.Null(loser.Resource);
        }

        await book.AssertPostedOnceAsync(draft, winnerActor);
        // The replay and the 409 record nothing: the winner's command is the only approval command.
        Assert.Equal(1, await book.ApprovalCommandCountAsync());

        // The loser's retry under its own key answers from the command log or from the stored version.
        var retry = await book.ApproveAsync(Checker, draft.Id, loserKey);
        switch (race)
        {
            case "same-key":
                Assert.Equal(
                    (AccountingApiStatus.Ok, true, "racing-draft-winner", draft.Id),
                    (retry.Status, retry.IsReplay, retry.OriginalCorrelationId, retry.Resource!.PostedJournalId!.Value));
                break;
            case "same-key-other-checker":
                Assert.Equal((AccountingApiStatus.Conflict, AccountingApiCodes.IdempotencyConflict), (retry.Status, retry.Code));
                break;
            default:
                Assert.Equal((AccountingApiStatus.Conflict, AccountingApiCodes.VersionConflict), (retry.Status, retry.Code));
                break;
        }

        await book.AssertPostedOnceAsync(draft, winnerActor);
    }

    [Fact]
    public async Task A_posting_conflict_without_a_racing_approval_stays_409_and_is_not_recorded()
    {
        // The draft's message was already delivered by another path, so the store refuses the approval's posting with
        // the declared posting conflict. No approval of this key exists to replay: the 409 stays and nothing is recorded.
        var book = await Book.CreateAsync();
        var draft = await book.CreateDraftAsync();
        await book.DeliverDraftMessageDirectlyAsync(draft);

        var first = await book.ApproveAsync(Checker, draft.Id, "conflicting-draft");
        var again = await book.ApproveAsync(Checker, draft.Id, "conflicting-draft");

        foreach (var answer in new[] { first, again })
        {
            Assert.Equal(
                (AccountingApiStatus.Conflict, NormalJournalDraftStoreCodesV1.PostingConflict, false),
                (answer.Status, answer.Code, answer.IsReplay));
            Assert.Null(answer.Resource);
        }

        Assert.Equal(0, await book.ApprovalCommandCountAsync());
        var row = await book.DraftRowAsync(draft.Id);
        Assert.Equal(((int)NormalJournalDraftStatusV1.Draft, 1L), (row.Status, row.Version));
        Assert.Null(row.PostedJournalId);
        Assert.Equal(1, await book.JournalCountAsync());
    }

    private static AccountingCommandMetadata Metadata() => new(
        "synthetic-actor", "synthetic.accounting", ["SYNTHETIC"], "synthetic-correlation", null, "synthetic-key", "Web");

    private static PermissionScope Granted() => new(
        true,
        true,
        new HashSet<string>(["SYNTHETIC-ACCOUNTING-NORMAL-DRAFT"], StringComparer.Ordinal),
        new HashSet<Guid>(),
        new HashSet<Guid>(),
        new HashSet<Guid>());

    /// <summary>One company's Accounting database, with an open September period, shared by every request scope.</summary>
    private sealed class Book
    {
        private readonly InMemoryDatabaseRoot root = new();
        private readonly string database = Guid.NewGuid().ToString("N");

        private Book()
        {
            Debit = new AccountRef(Company, Guid.NewGuid());
            Credit = new AccountRef(Company, Guid.NewGuid());
        }

        public Guid Company { get; } = Guid.NewGuid();

        public Guid September { get; } = Guid.NewGuid();

        public AccountRef Debit { get; }

        public AccountRef Credit { get; }

        public static async Task<Book> CreateAsync()
        {
            var book = new Book();
            await using (var scope = book.Open(PeriodPreparer))
            {
                await scope.Store.ProposePeriodAsync(book.September, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30),
                    PeriodPreparer, Metadata());
            }

            await using (var scope = book.Open(PeriodApprover))
            {
                await scope.Store.ApprovePeriodAsync(book.September, 1, PeriodApprover, Metadata());
            }

            return book;
        }

        public RequestScope Open(Guid actor, IErpAuthorityRevalidationService? authority = null, string? correlation = null)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(Company);
            var options = new DbContextOptionsBuilder<AccountingDbContext>()
                .UseInMemoryDatabase(database, root)
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            var dbContext = new AccountingDbContext(options, companyContext);
            var store = new AccountingStore(dbContext, companyContext, new NullAuditCollector(), new FixedTimeProvider(Now),
                new AccountingAmountScaleVerifier(SyntheticSettings.Scale(Company, 2)));
            var user = new FixedUser(actor, Company);
            var binding = new NormalJournalPermissionBindingV1();
            var grants = authority ?? new FixedAuthority();
            var lifecycle = new NormalJournalDraftServiceV1(
                store,
                store,
                new NormalJournalDraftApprovalPolicyV1(binding, grants, companyContext, user),
                binding,
                grants,
                companyContext,
                user,
                new FixedCorrelation(correlation ?? Guid.NewGuid().ToString("N")));
            return new RequestScope(dbContext, store, lifecycle);
        }

        public async Task<NormalJournalDraftV1> CreateDraftAsync()
        {
            var mirror = new AccountPlanMirrorV1(Company, "synthetic-plan-v1", new string('a', 64), Preparer, Now,
            [
                new AccountPlanMirrorRowV1(Debit, "SYNTHETIC-100", "Synthetic debit"),
                new AccountPlanMirrorRowV1(Credit, "SYNTHETIC-200", "Synthetic credit"),
            ]);
            var rule = new NormalJournalPostingRuleV1(mirror, "synthetic-purpose", "synthetic-rule-v1", Preparer, Now,
            [
                new NormalJournalMappingV1("debit", PostingSide.Debit, Debit),
                new NormalJournalMappingV1("credit", PostingSide.Credit, Credit),
            ]);
            var request = new PostingRequest(
                "synthetic-normal-draft-consumer",
                $"synthetic-normal-draft-{Guid.NewGuid():N}",
                Guid.NewGuid(),
                new SourceReference(Company, Guid.NewGuid(), "synthetic-purpose"),
                "synthetic-rule-v1",
                Guid.NewGuid(),
                new PostingDates(new DateOnly(2026, 9, 24), new DateOnly(2026, 9, 24), null, Now),
                [
                    new ProposedLine("debit", Debit, PostingSide.Debit, Amount),
                    new ProposedLine("credit", Credit, PostingSide.Credit, Amount),
                ]);
            var draft = Assert.IsType<NormalJournalDraftV1>(
                NormalJournalDraftV1.Prepare(JournalKind.Original, request, 1, Preparer, mirror, rule).Draft);
            await using var scope = Open(Preparer);
            var (record, _) = await scope.Store.CreateNormalDraftAsync(draft, static _ => Task.CompletedTask, Metadata());
            Assert.Equal((NormalJournalDraftStatusV1.Draft, 1L), (record.Status, record.Version));
            return draft;
        }

        /// <summary>Delivers the draft's own message through the ledger path, outside any approval.</summary>
        public async Task DeliverDraftMessageDirectlyAsync(NormalJournalDraftV1 draft)
        {
            await using var scope = Open(Preparer);
            var outcome = await scope.Store.ReceiveAsync(draft.Request with { JournalId = Guid.NewGuid() }, September,
                Metadata());
            Assert.Equal(PostingOutcomeKind.Posted, outcome.Kind);
        }

        public async Task<AccountingApiResult<NormalJournalDraftViewV1>> ApproveAsync(
            Guid actor,
            Guid draftId,
            string idempotencyKey,
            IErpAuthorityRevalidationService? authority = null,
            string? correlation = null)
        {
            await using var scope = Open(actor, authority, correlation);
            return await scope.Lifecycle.ApproveAsync(draftId, 1, September, idempotencyKey);
        }

        public Task<int> JournalCountAsync() => ReadAsync(dbContext => dbContext.Journals.CountAsync());

        public Task<int> ApprovalCommandCountAsync() => ReadAsync(dbContext => dbContext.Commands
            .CountAsync(row => row.CommandName == NormalJournalCommandNamesV1.ApproveDraft));

        internal Task<NormalJournalDraftRow> DraftRowAsync(Guid draftId) =>
            ReadAsync(dbContext => dbContext.NormalJournalDrafts.SingleAsync(row => row.Id == draftId));

        /// <summary>
        /// One journal (the draft's id), one delivery of the draft's message, the draft Approved v2 by the winner and
        /// linked to it, and exactly one posting's movement on the draft's accounts.
        /// </summary>
        public async Task AssertPostedOnceAsync(NormalJournalDraftV1 draft, Guid approver)
        {
            Assert.Equal(1, await JournalCountAsync());
            Assert.Equal(1, await ReadAsync(dbContext => dbContext.Journals.CountAsync(row => row.Id == draft.Id)));
            Assert.Equal(1, await ReadAsync(dbContext => dbContext.Deliveries.CountAsync(row =>
                row.Consumer == draft.Request.Consumer && row.MessageId == draft.Request.MessageId)));
            var row = await DraftRowAsync(draft.Id);
            Assert.Equal(((int)NormalJournalDraftStatusV1.Approved, 2L, draft.Id, approver),
                (row.Status, row.Version, row.PostedJournalId!.Value, row.ApprovedByUserId!.Value));
            var debit = Debit.AccountId;
            var credit = Credit.AccountId;
            var lines = await ReadAsync(dbContext => dbContext.JournalLines
                .Where(line => line.AccountId == debit || line.AccountId == credit)
                .ToListAsync());
            Assert.Equal(Amount, lines.Where(line => line.AccountId == debit).Sum(line => line.Debit - line.Credit));
            Assert.Equal(-Amount, lines.Where(line => line.AccountId == credit).Sum(line => line.Debit - line.Credit));
        }

        private async Task<T> ReadAsync<T>(Func<AccountingDbContext, Task<T>> read)
        {
            await using var scope = Open(Preparer);
            return await read(scope.Db);
        }
    }

    private sealed class RequestScope(
        AccountingDbContext dbContext,
        AccountingStore store,
        NormalJournalDraftServiceV1 lifecycle) : IAsyncDisposable
    {
        public AccountingDbContext Db { get; } = dbContext;

        public AccountingStore Store { get; } = store;

        public NormalJournalDraftServiceV1 Lifecycle { get; } = lifecycle;

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class NullAuditCollector : IAuditEventCollector
    {
        public void Enqueue(AuditEventDraft auditEvent)
        {
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

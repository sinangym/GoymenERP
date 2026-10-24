using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Journals;

namespace GoymenERP.Accounting.UnitTests;

/// <summary>BLG-05b: draft accounts are named by the draft's own frozen plan, under the draft read's authority.</summary>
public sealed class NormalJournalDraftAccountReadServiceV1Tests
{
    private static readonly Guid Company = Guid.Parse("a0000000-0000-4000-8000-000000000001");
    private static readonly Guid Draft = Guid.Parse("a0000000-0000-4000-8000-000000000002");
    private static readonly Guid Actor = Guid.Parse("a0000000-0000-4000-8000-000000000003");
    private static readonly Guid Debit = Guid.Parse("a0000000-0000-4000-8000-000000000004");
    private static readonly Guid Credit = Guid.Parse("a0000000-0000-4000-8000-000000000005");
    private static readonly string Sha = new('a', 64);
    private const string Version = "mikro-approved-v1";

    [Fact]
    public async Task Names_each_line_by_the_drafts_frozen_plan()
    {
        var mirrors = new FixedMirrors(Mirror(Version, Sha));
        var service = new NormalJournalDraftAccountReadServiceV1(new SequencedLifecycle(Ok(View(1)), Ok(View(1))), mirrors);

        var result = await service.ReadAsync(Draft);

        Assert.Equal(AccountingApiStatus.Ok, result.Status);
        var view = Assert.IsType<NormalJournalDraftAccountsViewV1>(result.Resource);
        Assert.Equal((Draft, 1L, Version, Sha), (view.DraftId, view.Version, view.AccountPlanSourceVersion, view.AccountPlanSourceSha256));
        Assert.Equal(
            [new NormalJournalDraftAccountV1(1, Debit, "100", "Kasa"), new NormalJournalDraftAccountV1(2, Credit, "120", "Alıcılar")],
            view.Accounts);
        Assert.Equal([(Version, Sha)], mirrors.Requests);
    }

    [Fact]
    public async Task Leaves_names_empty_when_that_exact_plan_is_not_stored_never_borrowing_another_version()
    {
        var other = Mirror("mikro-approved-v2", new string('b', 64));
        var service = new NormalJournalDraftAccountReadServiceV1(
            new SequencedLifecycle(Ok(View(1)), Ok(View(1))), new FixedMirrors(other));

        var result = await service.ReadAsync(Draft);

        var view = Assert.IsType<NormalJournalDraftAccountsViewV1>(result.Resource);
        Assert.All(view.Accounts, account => Assert.Null(account.AccountCode));
        Assert.All(view.Accounts, account => Assert.Null(account.AccountName));
        Assert.Equal([Debit, Credit], view.Accounts.Select(account => account.AccountId));
    }

    [Fact]
    public async Task Releases_nothing_when_the_authority_is_gone_at_the_fence_read()
    {
        var denied = new AccountingApiResult<NormalJournalDraftViewV1>(AccountingApiStatus.Denied, null, false,
            AccountingApiCodes.AuthorityDenied);
        var service = new NormalJournalDraftAccountReadServiceV1(
            new SequencedLifecycle(Ok(View(1)), denied), new FixedMirrors(Mirror(Version, Sha)));

        var result = await service.ReadAsync(Draft);

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Null(result.Resource);
        Assert.Equal(AccountingApiCodes.AuthorityDenied, result.Code);
    }

    [Fact]
    public async Task Answers_a_version_conflict_when_the_draft_moved_between_the_two_reads()
    {
        var service = new NormalJournalDraftAccountReadServiceV1(
            new SequencedLifecycle(Ok(View(1)), Ok(View(2))), new FixedMirrors(Mirror(Version, Sha)));

        var result = await service.ReadAsync(Draft);

        Assert.Equal(AccountingApiStatus.Conflict, result.Status);
        Assert.Null(result.Resource);
        Assert.Equal((AccountingApiCodes.VersionConflict, 1L, 2L), (result.Code, result.ExpectedVersion, result.CurrentVersion));
    }

    [Fact]
    public async Task Reads_no_mirror_when_the_draft_read_itself_is_refused()
    {
        var mirrors = new FixedMirrors(Mirror(Version, Sha));
        var missing = new AccountingApiResult<NormalJournalDraftViewV1>(AccountingApiStatus.NotFound, null, false, null);
        var service = new NormalJournalDraftAccountReadServiceV1(new SequencedLifecycle(missing), mirrors);

        var result = await service.ReadAsync(Draft);

        Assert.Equal(AccountingApiStatus.NotFound, result.Status);
        Assert.Empty(mirrors.Requests);
    }

    private static AccountingApiResult<NormalJournalDraftViewV1> Ok(NormalJournalDraftViewV1 view) =>
        new(AccountingApiStatus.Ok, view, false, null);

    private static NormalJournalDraftViewV1 View(long version) => new(
        Draft, Company, version, "Draft", Actor, DateTimeOffset.UnixEpoch, null, null, null, null,
        Guid.Parse("a0000000-0000-4000-8000-000000000006"), "approved-purpose", "approved-rule-v1", Version, Sha,
        Guid.Parse("a0000000-0000-4000-8000-000000000007"), new DateOnly(2026, 9, 24), new DateOnly(2026, 9, 24), null,
        [
            new NormalJournalDraftLineViewV1(1, "debit", "Debit", Debit, "10.25"),
            new NormalJournalDraftLineViewV1(2, "credit", "Credit", Credit, "10.25"),
        ]);

    private static AccountPlanMirrorV1 Mirror(string version, string sha) => new(
        Company, version, sha, Actor, DateTimeOffset.UnixEpoch,
        [
            new AccountPlanMirrorRowV1(new AccountRef(Company, Debit), "100", "Kasa"),
            new AccountPlanMirrorRowV1(new AccountRef(Company, Credit), "120", "Alıcılar"),
        ]);

    /// <summary>The store's contract: only the exact version and SHA-256 is returned.</summary>
    private sealed class FixedMirrors(AccountPlanMirrorV1 stored) : IAccountPlanMirrorReaderV1
    {
        public List<(string Version, string Sha)> Requests { get; } = [];

        public Task<AccountPlanMirrorV1?> ReadExactAsync(string sourceVersion, string sourceSha256,
            CancellationToken cancellationToken = default)
        {
            Requests.Add((sourceVersion, sourceSha256));
            return Task.FromResult<AccountPlanMirrorV1?>(
                stored.SourceVersion == sourceVersion && stored.SourceSha256 == sourceSha256 ? stored : null);
        }
    }

    private sealed class SequencedLifecycle(params AccountingApiResult<NormalJournalDraftViewV1>[] reads)
        : INormalJournalDraftLifecycleV1
    {
        private int next;

        public Task<AccountingApiResult<NormalJournalDraftViewV1>> ReadAsync(Guid draftId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(reads[Math.Min(next++, reads.Length - 1)]);

        public Task<AccountingApiResult<NormalJournalDraftViewV1>> CreateAsync(NormalJournalDraftV1? draft,
            string idempotencyKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AccountingApiResult<NormalJournalDraftViewV1>?> TryReplayPreparedCreateAsync(
            NormalJournalPreparedCreateCommandV2 command, string idempotencyKey,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AccountingApiResult<NormalJournalDraftViewV1>> CreatePreparedAsync(NormalJournalDraftV1? draft,
            NormalJournalPreparedCreateCommandV2 command, string idempotencyKey,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AccountingApiResult<NormalJournalDraftViewV1>> ApproveAsync(Guid draftId, long expectedVersion,
            Guid periodId, string idempotencyKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

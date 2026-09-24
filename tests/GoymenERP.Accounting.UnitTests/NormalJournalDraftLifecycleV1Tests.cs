using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;

namespace GoymenERP.Accounting.UnitTests;

public sealed class NormalJournalDraftLifecycleV1Tests
{
    private static readonly Guid Company = Guid.Parse("a0000000-0000-4000-8000-0000000000a1");
    private static readonly Guid Actor = Guid.Parse("a0000000-0000-4000-8000-0000000000b1");

    [Fact]
    public async Task Create_only_authority_never_approves_a_normal_draft()
    {
        var store = new MemoryStore(Record());
        var service = Service(store, new PermissionAuthority(create: Granted(), approve: PermissionScope.Denied));

        var result = await service.ApproveAsync(store.Record.Id, 1, Guid.NewGuid(), "key-create-only");

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Equal(0, store.Approvals);
    }

    [Fact]
    public async Task The_preparer_can_approve_the_normal_draft_only_with_the_narrow_approval_grant()
    {
        var store = new MemoryStore(Record());
        var service = Service(store, new PermissionAuthority(create: Granted(), approve: Granted()));

        var result = await service.ApproveAsync(store.Record.Id, 1, Guid.NewGuid(), "key-self-approve");

        Assert.Equal(AccountingApiStatus.Ok, result.Status);
        Assert.Equal(Actor, result.Resource!.ApprovedByUserId);
        Assert.Equal("Approved", result.Resource.Status);
        Assert.Equal(1, store.Approvals);
    }

    [Fact]
    public async Task Approval_authority_lost_at_the_final_store_fence_is_refused()
    {
        var answers = Enumerable.Repeat(Granted(), 5).Append(PermissionScope.Denied).ToArray();
        var store = new MemoryStore(Record());
        var service = Service(store, new SequencedAuthority(answers));

        var result = await service.ApproveAsync(store.Record.Id, 1, Guid.NewGuid(), "key-revoked");

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Equal(0, store.Approvals);
        Assert.Equal(NormalJournalDraftStatusV1.Draft, store.Record.Status);
    }

    private static NormalJournalDraftServiceV1 Service(
        MemoryStore store,
        IErpAuthorityRevalidationService authority)
    {
        var company = new CompanyContext();
        company.Set(Company);
        var user = new FixedUser();
        var binding = new NormalJournalPermissionBindingV1();
        return new NormalJournalDraftServiceV1(
            store,
            store,
            new NormalJournalDraftApprovalPolicyV1(binding, authority, company, user),
            binding,
            authority,
            company,
            user,
            new FixedCorrelation());
    }

    private static NormalJournalDraftRecordV1 Record()
    {
        var draftId = Guid.NewGuid();
        return new NormalJournalDraftRecordV1(
            draftId,
            Company,
            1,
            NormalJournalDraftStatusV1.Draft,
            Actor,
            DateTimeOffset.Parse("2026-09-24T16:00:00Z"),
            null,
            null,
            null,
            null,
            "synthetic-plan-v1",
            new string('a', 64),
            Actor,
            DateTimeOffset.Parse("2026-09-24T15:00:00Z"),
            Actor,
            DateTimeOffset.Parse("2026-09-24T15:30:00Z"),
            new PostingRequest(
                "synthetic-consumer",
                "synthetic-message",
                draftId,
                new SourceReference(Company, Guid.NewGuid(), "synthetic-purpose"),
                "synthetic-rule-v1",
                Guid.NewGuid(),
                new PostingDates(new DateOnly(2026, 9, 24), new DateOnly(2026, 9, 24), null,
                    DateTimeOffset.Parse("2026-09-24T14:00:00Z")),
                [
                    new ProposedLine("debit", new AccountRef(Company, Guid.NewGuid()), PostingSide.Debit, 10m),
                    new ProposedLine("credit", new AccountRef(Company, Guid.NewGuid()), PostingSide.Credit, 10m),
                ]));
    }

    private static PermissionScope Granted() => new(
        true,
        true,
        new HashSet<string>(["SYNTHETIC-ACCOUNTING-APPROVER"], StringComparer.Ordinal),
        new HashSet<Guid>(),
        new HashSet<Guid>(),
        new HashSet<Guid>());

    private sealed class PermissionAuthority(PermissionScope create, PermissionScope approve) : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(permission == NormalJournalPermissionNamesV1.DraftCreate ? create : approve);
    }

    private sealed class SequencedAuthority(params PermissionScope[] answers) : IErpAuthorityRevalidationService
    {
        private int index;

        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(answers[Math.Min(index++, answers.Length - 1)]);
    }

    private sealed class MemoryStore(NormalJournalDraftRecordV1 record)
        : IAccountingNormalJournalDraftStore, IAccountingCommandLog
    {
        public NormalJournalDraftRecordV1 Record { get; private set; } = record;
        public int Approvals { get; private set; }

        public Task<(NormalJournalDraftRecordV1 Record, AccountingWriteOutcome Outcome)> CreateNormalDraftAsync(
            NormalJournalDraftV1 draft,
            AccountingCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<NormalJournalDraftRecordV1?> FindNormalDraftAsync(
            Guid draftId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<NormalJournalDraftRecordV1?>(draftId == Record.Id ? Record : null);

        public async Task<NormalJournalDraftRecordV1> ApproveAndPostNormalDraftAsync(
            Guid draftId,
            long expectedVersion,
            Guid periodId,
            Guid approvedByUserId,
            Func<CancellationToken, Task> freshAuthorityFence,
            AccountingCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            await freshAuthorityFence(cancellationToken);
            await Task.Yield();
            await freshAuthorityFence(cancellationToken);
            Record = Record with
            {
                Version = 2,
                Status = NormalJournalDraftStatusV1.Approved,
                ApprovedByUserId = approvedByUserId,
                ApprovedAtUtc = DateTimeOffset.Parse("2026-09-24T17:00:00Z"),
                PeriodId = periodId,
                PostedJournalId = draftId,
            };
            Approvals++;
            metadata.Completion?.Invoke(Record);
            return Record;
        }

        public Task<AccountingCommandRecord?> FindCommandAsync(
            string commandName,
            string idempotencyKeyHash,
            CancellationToken cancellationToken = default) => Task.FromResult<AccountingCommandRecord?>(null);

        public Task RecordCommandAsync(AccountingCommandRecord record, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FixedUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string UserId => Actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([Company]);
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }

    private sealed class FixedCorrelation : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => "synthetic-normal-journal";
        public string? CausationId => null;
        public string SourceChannel => "UnitTest";
        public string? ClientIpAddress => null;
        public string? UserAgent => null;
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent) =>
            throw new NotSupportedException();
    }
}

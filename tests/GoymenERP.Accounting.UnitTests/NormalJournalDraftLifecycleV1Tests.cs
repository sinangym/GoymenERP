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
    public async Task A_saved_normal_draft_can_be_reopened_and_approved_by_its_preparer_with_the_narrow_grant()
    {
        var draft = Draft();
        var store = new MemoryStore(Record());
        var authority = new PermissionAuthority(create: Granted(), approve: Granted());

        var created = await Service(store, authority).CreateAsync(draft, "key-save-close");

        Assert.Equal(AccountingApiStatus.Created, created.Status);
        Assert.Equal(draft.Id, created.Resource!.DraftId);
        Assert.Equal(1, created.Resource.Version);
        Assert.Equal("Draft", created.Resource.Status);
        Assert.Equal(Actor, created.Resource.PreparedByUserId);

        // A new service instance represents closing and reopening the client while retaining the durable store.
        var reopened = await store.FindNormalDraftAsync(draft.Id);
        Assert.NotNull(reopened);
        Assert.Equal(draft.Id, reopened.Id);
        Assert.Equal(1, reopened.Version);
        Assert.Equal(NormalJournalDraftStatusV1.Draft, reopened.Status);

        var periodId = Guid.Parse("e0000000-0000-4000-8000-000000000001");
        var approved = await Service(store, authority).ApproveAsync(
            reopened.Id, reopened.Version, periodId, "key-reopen-approve");

        Assert.Equal(AccountingApiStatus.Ok, approved.Status);
        Assert.Equal(2, approved.Resource!.Version);
        Assert.Equal("Approved", approved.Resource.Status);
        Assert.Equal(Actor, approved.Resource.ApprovedByUserId);
        Assert.Equal(periodId, approved.Resource.PeriodId);
        Assert.Equal(draft.Id, approved.Resource.PostedJournalId);
        Assert.Equal(1, store.Creates);
        Assert.Equal(1, store.Approvals);
        Assert.Equal(2, store.Completions);
    }

    [Fact]
    public async Task Create_authority_reads_only_its_own_draft_while_approve_authority_reads_company_drafts()
    {
        var anotherPreparer = Guid.Parse("a0000000-0000-4000-8000-0000000000b2");
        var store = new MemoryStore(Record() with { PreparedByUserId = anotherPreparer });

        var createOnly = await Service(store,
            new PermissionAuthority(create: Granted(), approve: PermissionScope.Denied))
            .ReadAsync(store.Record.Id);
        var approver = await Service(store,
            new PermissionAuthority(create: PermissionScope.Denied, approve: Granted()))
            .ReadAsync(store.Record.Id);

        Assert.Equal(AccountingApiStatus.Denied, createOnly.Status);
        Assert.Equal(AccountingApiStatus.Ok, approver.Status);
        Assert.Equal(store.Record.Id, approver.Resource!.DraftId);
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

    [Fact]
    public async Task Create_authority_lost_after_the_owner_read_is_refused_without_effects()
    {
        var draft = Draft();
        var store = new MemoryStore(Record());
        var service = Service(store, new SequencedAuthority(Granted(), Granted(), PermissionScope.Denied));

        var result = await service.CreateAsync(draft, "key-create-revoked");

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Equal(0, store.Creates);
        Assert.Equal(0, store.Completions);
    }

    [Fact]
    public async Task Reordered_lines_with_the_same_idempotency_key_are_refused_and_preserve_the_first_snapshot()
    {
        var store = new MemoryStore(Record());
        var service = Service(store, new PermissionAuthority(create: Granted(), approve: Granted()));

        var first = await service.CreateAsync(Draft(), "key-line-order");
        var reordered = await service.CreateAsync(Draft(reverseLines: true), "key-line-order");

        Assert.Equal(AccountingApiStatus.Created, first.Status);
        Assert.Equal(AccountingApiStatus.Conflict, reordered.Status);
        Assert.Equal(AccountingApiCodes.IdempotencyConflict, reordered.Code);
        Assert.Equal(1, store.Creates);
        Assert.Equal(1, store.Completions);
        Assert.Collection(store.Record.Request.Lines,
            line => Assert.Equal("debit", line.MappingKey),
            line => Assert.Equal("credit", line.MappingKey));
    }

    [Fact]
    public async Task Prepared_v2_replay_is_answered_from_the_command_log_and_changed_fingerprint_conflicts()
    {
        var draft = Draft();
        var command = PreparedCommand(draft, new string('a', 64));
        var store = new MemoryStore(Record());
        var authority = new PermissionAuthority(create: Granted(), approve: Granted());
        var service = Service(store, authority);

        var first = await service.CreatePreparedAsync(draft, command, "prepared-v2-key");
        var replay = await Service(store, authority)
            .TryReplayPreparedCreateAsync(command, "prepared-v2-key");
        var conflict = await Service(store, authority).TryReplayPreparedCreateAsync(
            command with { ExpectedPreparationVersion = new string('b', 64) }, "prepared-v2-key");

        Assert.Equal(AccountingApiStatus.Created, first.Status);
        Assert.NotNull(replay);
        Assert.Equal(AccountingApiStatus.Created, replay!.Status);
        Assert.True(replay.IsReplay);
        Assert.NotNull(conflict);
        Assert.Equal(AccountingApiStatus.Conflict, conflict!.Status);
        Assert.Equal(AccountingApiCodes.IdempotencyConflict, conflict.Code);
        Assert.Equal(1, store.Creates);
        Assert.Equal(1, store.Completions);
    }

    [Fact]
    public async Task Prepared_v2_replay_requires_fresh_create_authority()
    {
        var draft = Draft();
        var command = PreparedCommand(draft, new string('a', 64));
        var store = new MemoryStore(Record());
        var granted = new PermissionAuthority(create: Granted(), approve: Granted());
        await Service(store, granted).CreatePreparedAsync(draft, command, "prepared-authority-key");

        var denied = await Service(store,
            new PermissionAuthority(create: PermissionScope.Denied, approve: Granted()))
            .TryReplayPreparedCreateAsync(command, "prepared-authority-key");

        Assert.NotNull(denied);
        Assert.Equal(AccountingApiStatus.Denied, denied!.Status);
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

    private static NormalJournalPreparedCreateCommandV2 PreparedCommand(
        NormalJournalDraftV1 draft,
        string preparationVersion) => new(
            draft.Id,
            draft.Request.Source.SourceId,
            draft.Request.Dates.TransactionDate,
            draft.Request.Dates.AccountingDate,
            draft.Request.Dates.ValueDate,
            preparationVersion,
            [.. draft.Request.Lines.Select(line => new NormalJournalPreparedCreateLineV2(
                line.Account!.Value.AccountId, line.Side, line.Amount!.Value))]);

    private static NormalJournalDraftRecordV1 Record()
        => Record(Draft());

    private static NormalJournalDraftRecordV1 Record(NormalJournalDraftV1 draft)
    {
        return new NormalJournalDraftRecordV1(
            draft.Id,
            draft.CompanyId,
            draft.Version,
            NormalJournalDraftStatusV1.Draft,
            draft.PreparedByUserId,
            draft.Request.Dates.RecordedAtUtc,
            null,
            null,
            null,
            null,
            draft.Rule.Mirror.SourceVersion,
            draft.Rule.Mirror.SourceSha256,
            draft.Rule.Mirror.ApprovedByUserId,
            draft.Rule.Mirror.ApprovedAtUtc,
            draft.Rule.ApprovedByUserId,
            draft.Rule.ApprovedAtUtc,
            draft.Request);
    }

    private static NormalJournalDraftV1 Draft(bool reverseLines = false)
    {
        var at = DateTimeOffset.Parse("2026-09-24T15:00:00Z");
        var debit = new AccountRef(Company, Guid.Parse("b0000000-0000-4000-8000-000000000001"));
        var credit = new AccountRef(Company, Guid.Parse("b0000000-0000-4000-8000-000000000002"));
        var mirror = new AccountPlanMirrorV1(Company, "synthetic-plan-v1", new string('a', 64), Actor, at,
        [
            new AccountPlanMirrorRowV1(debit, "SYNTHETIC-001", "Synthetic debit"),
            new AccountPlanMirrorRowV1(credit, "SYNTHETIC-002", "Synthetic credit"),
        ]);
        var rule = new NormalJournalPostingRuleV1(mirror, "synthetic-purpose", "synthetic-rule-v1", Actor, at,
        [
            new NormalJournalMappingV1("debit", PostingSide.Debit, debit),
            new NormalJournalMappingV1("credit", PostingSide.Credit, credit),
        ]);
        ProposedLine[] lines =
        [
            new ProposedLine("debit", debit, PostingSide.Debit, 10m),
            new ProposedLine("credit", credit, PostingSide.Credit, 10m),
        ];
        if (reverseLines) Array.Reverse(lines);

        var request = new PostingRequest(
            "synthetic-consumer",
            "synthetic-message",
            Guid.Parse("c0000000-0000-4000-8000-000000000001"),
            new SourceReference(Company, Guid.Parse("c0000000-0000-4000-8000-000000000002"),
                "synthetic-purpose"),
            "synthetic-rule-v1",
            Guid.Parse("d0000000-0000-4000-8000-000000000001"),
            new PostingDates(new DateOnly(2026, 9, 24), new DateOnly(2026, 9, 24), null, at),
            lines);
        return Assert.IsType<NormalJournalDraftV1>(
            NormalJournalDraftV1.Prepare(JournalKind.Original, request, 1, Actor, mirror, rule).Draft);
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
        private readonly List<AccountingCommandRecord> commands = [];
        public NormalJournalDraftRecordV1 Record { get; private set; } = record;
        public int Approvals { get; private set; }
        public int Creates { get; private set; }
        public int Completions { get; private set; }

        public async Task<(NormalJournalDraftRecordV1 Record, AccountingWriteOutcome Outcome)> CreateNormalDraftAsync(
            NormalJournalDraftV1 draft,
            Func<CancellationToken, Task> freshAuthorityFence,
            AccountingCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            await freshAuthorityFence(cancellationToken);
            Creates++;
            Record = NormalJournalDraftLifecycleV1Tests.Record(draft);
            if (metadata.Completion?.Invoke(Record) is { } command)
            {
                commands.Add(command);
                Completions++;
            }

            return (Record, AccountingWriteOutcome.Recorded);
        }

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
            if (metadata.Completion?.Invoke(Record) is { } command)
            {
                commands.Add(command);
                Completions++;
            }
            return Record;
        }

        public Task<AccountingCommandRecord?> FindCommandAsync(
            string commandName,
            string idempotencyKeyHash,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(commands.SingleOrDefault(command =>
                command.CommandName == commandName && command.IdempotencyKeyHash == idempotencyKeyHash));

        public Task RecordCommandAsync(AccountingCommandRecord record, CancellationToken cancellationToken = default)
        {
            commands.Add(record);
            return Task.CompletedTask;
        }
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

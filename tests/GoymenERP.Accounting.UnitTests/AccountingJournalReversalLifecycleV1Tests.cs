using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain;

namespace GoymenERP.Accounting.UnitTests;

public sealed class AccountingJournalReversalLifecycleV1Tests
{
    private static readonly Guid Company = Guid.Parse("a1000000-0000-4000-8000-000000000001");
    private static readonly Guid Maker = Guid.Parse("a1000000-0000-4000-8000-000000000002");
    private static readonly Guid Checker = Guid.Parse("a1000000-0000-4000-8000-000000000003");
    private static readonly Guid Proposal = Guid.Parse("a1000000-0000-4000-8000-000000000004");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-25T12:00:00Z");

    [Fact]
    public async Task Proposal_is_persisted_as_pending_and_same_command_replays_without_a_second_effect()
    {
        var store = new MemoryStore();
        var service = Service(store, Maker, new FixedAuthority(Granted()));

        var first = await service.ProposeAsync(Request(), "proposal-key");
        var replay = await service.ProposeAsync(Request(), "proposal-key");

        Assert.Equal(AccountingApiStatus.Created, first.Status);
        Assert.Equal("Pending", first.Resource!.Status);
        Assert.Equal(1, first.Resource.Version);
        Assert.True(replay.IsReplay);
        Assert.Equal(first.Resource.ProposalId, replay.Resource!.ProposalId);
        Assert.Equal(1, store.ProposalsWritten);
        Assert.Equal(0, store.ReversalsWritten);
    }

    [Fact]
    public async Task Reversal_proposer_cannot_approve_own_proposal_even_with_approve_permission()
    {
        var store = new MemoryStore(Pending());
        var service = Service(store, Maker, new FixedAuthority(Granted()));

        var result = await service.ApproveAsync(Proposal, 1, "maker-approval-key");

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Null(result.Code);
        Assert.Equal(0, store.ReversalsWritten);
        Assert.Equal(AccountingJournalReversalProposalStatusV1.Pending, store.Proposal!.Status);
    }

    [Fact]
    public async Task Another_checker_approves_once_and_keeps_both_journal_links()
    {
        var store = new MemoryStore(Pending());
        var service = Service(store, Checker, new FixedAuthority(Granted()));

        var approved = await service.ApproveAsync(Proposal, 1, "checker-approval-key");
        var replay = await service.ApproveAsync(Proposal, 1, "checker-approval-key");

        Assert.Equal(AccountingApiStatus.Ok, approved.Status);
        Assert.Equal("Approved", approved.Resource!.Status);
        Assert.Equal(Checker, approved.Resource.DecidedBy);
        Assert.Equal(Pending().SourceJournalId, approved.Resource.SourceJournalId);
        Assert.NotNull(approved.Resource.ReversalJournalId);
        Assert.NotEqual(approved.Resource.SourceJournalId, approved.Resource.ReversalJournalId);
        Assert.True(replay.IsReplay);
        Assert.Equal(approved.Resource.ReversalJournalId, replay.Resource!.ReversalJournalId);
        Assert.Equal(1, store.ReversalsWritten);
    }

    [Fact]
    public async Task Approval_authority_revoked_at_final_fence_refuses_without_reversal_or_decision()
    {
        var store = new MemoryStore(Pending());
        var authority = new SequencedAuthority(Granted(), Granted(), Granted(), PermissionScope.Denied);
        var service = Service(store, Checker, authority);

        var result = await service.ApproveAsync(Proposal, 1, "revoked-approval-key");

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Equal(0, store.ReversalsWritten);
        Assert.Equal(AccountingJournalReversalProposalStatusV1.Pending, store.Proposal!.Status);
    }

    [Theory]
    [InlineData(AccountingErrorCodes.InvalidReference)]
    [InlineData(AccountingErrorCodes.ReversalOfReversal)]
    [InlineData(AccountingErrorCodes.PeriodClosed)]
    [InlineData(AccountingErrorCodes.PeriodDoesNotCover)]
    public async Task Reversal_domain_refusals_are_stable_unprocessable_answers(string code)
    {
        var store = new MemoryStore(Pending()) { ApprovalDomainFailureCode = code };
        var service = Service(store, Checker, new FixedAuthority(Granted()));

        var result = await service.ApproveAsync(Proposal, 1, $"domain-{code}");

        Assert.Equal(AccountingApiStatus.Unprocessable, result.Status);
        Assert.Equal(code, result.Code);
        Assert.Equal(0, store.ReversalsWritten);
        Assert.Equal(AccountingJournalReversalProposalStatusV1.Pending, store.Proposal!.Status);
    }

    [Fact]
    public async Task Non_positive_approval_version_reports_expected_version()
    {
        var store = new MemoryStore(Pending());
        var result = await Service(store, Checker, new FixedAuthority(Granted()))
            .ApproveAsync(Proposal, 0, "invalid-version");

        Assert.Equal(AccountingApiStatus.Invalid, result.Status);
        Assert.Equal("expectedVersion", result.Field);
        Assert.Equal(0, store.ReversalsWritten);
    }

    private static AccountingJournalReversalServiceV1 Service(
        MemoryStore store,
        Guid actor,
        IErpAuthorityRevalidationService authority)
    {
        var company = new CompanyContext();
        company.Set(Company);
        return new AccountingJournalReversalServiceV1(
            store,
            store,
            authority,
            company,
            new FixedUser(actor),
            new FixedCorrelation(),
            new FixedTimeProvider(Now));
    }

    private static ProposeAccountingJournalReversalV1 Request() => new(
        Proposal,
        Guid.Parse("a1000000-0000-4000-8000-000000000005"),
        Guid.Parse("a1000000-0000-4000-8000-000000000006"),
        new DateOnly(2026, 9, 25),
        new DateOnly(2026, 9, 25),
        null,
        "Synthetic reversal reason");

    private static AccountingJournalReversalProposalV1 Pending() =>
        AccountingJournalReversalPolicyV1.Propose(Company, Maker, Request(), Now);

    private static PermissionScope Granted() => new(
        true,
        true,
        new HashSet<string>(["SYNTHETIC-ACCOUNTING-REVERSAL-CHECKER"], StringComparer.Ordinal),
        new HashSet<Guid>(),
        new HashSet<Guid>(),
        new HashSet<Guid>());

    private sealed class MemoryStore(AccountingJournalReversalProposalV1? proposal = null)
        : IAccountingJournalReversalProposalStoreV1, IAccountingCommandLog
    {
        private readonly List<AccountingCommandRecord> commands = [];
        public AccountingJournalReversalProposalV1? Proposal { get; private set; } = proposal;
        public int ProposalsWritten { get; private set; }
        public int ReversalsWritten { get; private set; }
        public string? ApprovalDomainFailureCode { get; init; }

        public async Task<(AccountingJournalReversalProposalV1 Proposal, AccountingWriteOutcome Outcome)> CreateAsync(
            AccountingJournalReversalProposalV1 proposal,
            Func<CancellationToken, Task> freshAuthorityFence,
            AccountingCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            await freshAuthorityFence(cancellationToken);
            Proposal = proposal;
            ProposalsWritten++;
            Complete(metadata, proposal);
            return (proposal, AccountingWriteOutcome.Recorded);
        }

        public Task<AccountingJournalReversalProposalV1?> FindAsync(
            Guid proposalId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Proposal?.ProposalId == proposalId ? Proposal : null);

        public async Task<AccountingJournalReversalProposalV1> ApproveAndReverseAsync(
            Guid proposalId,
            int expectedVersion,
            Guid checkerId,
            Func<CancellationToken, Task> freshAuthorityFence,
            AccountingCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            if (ApprovalDomainFailureCode is { } code)
            {
                throw new AccountingDomainException(code, "Synthetic reversal refusal.");
            }

            var current = Proposal ?? throw new AccountingStoreException(
                AccountingJournalReversalStoreCodesV1.NotFound, "missing");
            var approved = AccountingJournalReversalPolicyV1.Approve(
                current,
                new DecideAccountingJournalReversalV1(proposalId, expectedVersion),
                checkerId,
                Guid.Parse("a1000000-0000-4000-8000-000000000007"),
                Now.AddMinutes(1));
            await freshAuthorityFence(cancellationToken);
            await Task.Yield();
            await freshAuthorityFence(cancellationToken);
            Proposal = approved;
            ReversalsWritten++;
            Complete(metadata, approved);
            return approved;
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

        private void Complete(AccountingCommandMetadata metadata, object result)
        {
            if (metadata.Completion?.Invoke(result) is { } command)
            {
                commands.Add(command);
            }
        }
    }

    private sealed class FixedAuthority(PermissionScope scope) : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default) => Task.FromResult(scope);
    }

    private sealed class SequencedAuthority(params PermissionScope[] scopes) : IErpAuthorityRevalidationService
    {
        private int index;

        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(scopes[Math.Min(index++, scopes.Length - 1)]);
    }

    private sealed class FixedUser(Guid actor) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string UserId => actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([Company]);
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }

    private sealed class FixedCorrelation : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => "synthetic-accounting-reversal";
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

using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Accounting.UnitTests;

public sealed class AccountingJournalReversalContextBindingV1Tests
{
    private static readonly Guid Company = Guid.Parse("c1000000-0000-4000-8000-000000000001");
    private static readonly Guid OtherCompany = Guid.Parse("c1000000-0000-4000-8000-000000000002");
    private static readonly Guid Actor = Guid.Parse("c1000000-0000-4000-8000-000000000003");
    private static readonly Guid OtherActor = Guid.Parse("c1000000-0000-4000-8000-000000000004");

    [Fact]
    public async Task Company_changed_during_command_read_is_refused_before_proposal_persistence()
    {
        var company = new MutableCompanyContext(Company);
        var user = new MutableUser(Actor, company);
        var store = new MemoryStore { DuringCommandRead = () => company.CompanyId = OtherCompany };
        var service = Service(store, company, user, new FixedAuthority());

        var result = await service.ProposeAsync(Request(), "company-change");

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Equal(0, store.Writes);
        Assert.Null(store.Proposal);
    }

    [Fact]
    public async Task Actor_changed_at_the_late_authority_fence_is_refused_before_proposal_persistence()
    {
        var company = new MutableCompanyContext(Company);
        var user = new MutableUser(Actor, company);
        var authority = new MutatingAuthority(call =>
        {
            if (call == 3)
            {
                user.Actor = OtherActor;
            }
        });
        var store = new MemoryStore();
        var service = Service(store, company, user, authority);

        var result = await service.ProposeAsync(Request(), "actor-change");

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Equal(0, store.Writes);
        Assert.Null(store.Proposal);
    }

    private static AccountingJournalReversalServiceV1 Service(
        MemoryStore store,
        ICompanyContext company,
        ICurrentUser user,
        IErpAuthorityRevalidationService authority) => new(
        store,
        store,
        authority,
        company,
        user,
        new FixedCorrelation(),
        new FixedTimeProvider(DateTimeOffset.Parse("2026-09-25T15:00:00Z")));

    private static ProposeAccountingJournalReversalV1 Request() => new(
        Guid.Parse("c1000000-0000-4000-8000-000000000005"),
        Guid.Parse("c1000000-0000-4000-8000-000000000006"),
        Guid.Parse("c1000000-0000-4000-8000-000000000007"),
        new DateOnly(2026, 9, 25),
        new DateOnly(2026, 9, 25),
        null,
        "Synthetic reason");

    private static PermissionScope Granted() => new(
        true, true, new HashSet<string>(["SYNTHETIC-REVERSAL"]),
        new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());

    private sealed class MemoryStore : IAccountingJournalReversalProposalStoreV1, IAccountingCommandLog
    {
        public Action? DuringCommandRead { get; init; }
        public AccountingJournalReversalProposalV1? Proposal { get; private set; }
        public int Writes { get; private set; }

        public Task<AccountingCommandRecord?> FindCommandAsync(
            string commandName, string idempotencyKeyHash, CancellationToken cancellationToken = default)
        {
            DuringCommandRead?.Invoke();
            return Task.FromResult<AccountingCommandRecord?>(null);
        }

        public Task RecordCommandAsync(AccountingCommandRecord record, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public async Task<(AccountingJournalReversalProposalV1 Proposal, AccountingWriteOutcome Outcome)> CreateAsync(
            AccountingJournalReversalProposalV1 proposal,
            Func<CancellationToken, Task> freshAuthorityFence,
            AccountingCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            await freshAuthorityFence(cancellationToken);
            Proposal = proposal;
            Writes++;
            return (proposal, AccountingWriteOutcome.Recorded);
        }

        public Task<AccountingJournalReversalProposalV1?> FindAsync(
            Guid proposalId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Proposal);

        public Task<AccountingJournalReversalProposalV1> ApproveAndReverseAsync(
            Guid proposalId,
            int expectedVersion,
            Guid checkerId,
            Func<CancellationToken, Task> freshAuthorityFence,
            AccountingCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class MutableCompanyContext(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; set; } = companyId;
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    private sealed class MutableUser(Guid actor, MutableCompanyContext company) : ICurrentUser
    {
        public Guid Actor { get; set; } = actor;
        public bool IsAuthenticated => true;
        public string UserId => Actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid>([company.CompanyId]);
        public bool CanAccessCompany(Guid companyId) => companyId == company.CompanyId;
    }

    private sealed class FixedAuthority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Granted());
    }

    private sealed class MutatingAuthority(Action<int> mutate) : IErpAuthorityRevalidationService
    {
        private int calls;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            mutate(++calls);
            return Task.FromResult(Granted());
        }
    }

    private sealed class FixedCorrelation : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => "synthetic-reversal-context";
        public string? CausationId => null;
        public string SourceChannel => "UnitTest";
        public string? ClientIpAddress => null;
        public string? UserAgent => null;
        public void Set(string correlationId, string? causationId, string sourceChannel,
            string? clientIpAddress, string? userAgent) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

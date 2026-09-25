using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Accounting.Domain.Periods;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// The period port and the rule that consumes it, over a synthetic owner (no database). The real rows are
/// measured in <c>FinanceRecordSqlServerRelationalAcceptanceTests</c>; these cases pin the ANSWERS the port
/// must give and the mapping the rule must make, including the two that must never look alike.
/// </summary>
public sealed class AccountingPeriodStatusPortTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly DateOnly Asked = new DateOnly(2026, 9, 15);

    [Fact]
    public async Task An_open_period_is_open_a_proposed_or_closed_one_is_not_and_no_period_is_none()
    {
        Assert.Equal(AccountingPeriodStatusOutcomeV1.Open, (await Answer(AccountingPeriodStatus.Open)).Outcome);
        // Ö4: a proposed period covers its dates but is not yet approved, so it takes no record.
        Assert.Equal(AccountingPeriodStatusOutcomeV1.NotOpen, (await Answer(AccountingPeriodStatus.Proposed)).Outcome);
        Assert.Equal(AccountingPeriodStatusOutcomeV1.NotOpen, (await Answer(AccountingPeriodStatus.Closed)).Outcome);
        var none = await Answer(null);
        Assert.Equal((AccountingPeriodStatusOutcomeV1.None, null, (string?)null), (none.Outcome, none.PeriodId, none.Status));
    }

    [Fact]
    public async Task The_answer_carries_the_period_and_its_status_and_never_an_amount()
    {
        var answer = await Answer(AccountingPeriodStatus.Open);

        Assert.NotNull(answer.PeriodId);
        Assert.Equal("Open", answer.Status);
        // The wire type has exactly three members: no journal, amount, balance or queue can travel on it.
        Assert.Equal(new[] { "Outcome", "PeriodId", "Status" },
            typeof(AccountingPeriodStatusAnswerV1).GetProperties().Select(property => property.Name)
                .Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Without_the_periods_read_name_the_owner_is_never_asked()
    {
        var owner = new Owner(AccountingPeriodStatus.Open);

        var answer = await Port(owner, granted: false).GetStatusAsync(Asked);

        Assert.Equal(AccountingPeriodStatusOutcomeV1.Denied, answer.Outcome);
        Assert.Empty(owner.Asked);
    }

    [Fact]
    public async Task An_actor_swapped_while_the_owner_answered_releases_nothing()
    {
        var user = new SwappableUser(Company);
        var owner = new Owner(AccountingPeriodStatus.Open) { DuringRead = () => user.Swap() };

        var answer = await Port(owner, user: user).GetStatusAsync(Asked);

        Assert.Equal(AccountingPeriodStatusOutcomeV1.Denied, answer.Outcome);
        // The owner WAS asked — the swap came during its answer — and the second actor can reach the same
        // company, so the company comparison alone would have let this through (#366).
        Assert.Equal([Asked], owner.Asked);
        Assert.True(user.CanAccessCompany(Company));
    }

    [Fact]
    public async Task The_rule_asks_with_occurredOn_only()
    {
        var owner = new Owner(AccountingPeriodStatus.Open);
        var rule = new OccurredOnFinanceRecordPeriodRule(Port(owner));

        var answer = await rule.CheckAsync(new FinanceContext(Company, "actor", "corr", null),
            occurredOn: new DateOnly(2026, 9, 15), valueOn: new DateOnly(2026, 8, 25),
            recordedAtUtc: new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero), CancellationToken.None);

        Assert.Equal(RecordPeriodOutcome.Open, answer.Outcome);
        // K1: the transaction date and nothing else. A rule that consulted the value date or the recording
        // moment would have asked with 2026-08-25 or 2026-10-02.
        Assert.Equal([new DateOnly(2026, 9, 15)], owner.Asked);
    }

    [Fact]
    public async Task A_period_that_is_not_open_and_a_port_that_could_not_answer_are_DIFFERENT_refusals()
    {
        var closed = await Rule(new Owner(AccountingPeriodStatus.Closed));
        var none = await Rule(new Owner(null));
        var denied = await Rule(new Owner(AccountingPeriodStatus.Open), granted: false);

        Assert.Equal((RecordPeriodOutcome.NotOpen, RecordPeriodAnswer.PeriodNotOpenCode), (closed.Outcome, closed.Code));
        Assert.Equal((RecordPeriodOutcome.NotOpen, RecordPeriodAnswer.PeriodNotOpenCode), (none.Outcome, none.Code));
        Assert.Equal((RecordPeriodOutcome.Unavailable, RecordPeriodAnswer.RuleUnavailableCode), (denied.Outcome, denied.Code));
        // The whole point: a recorder can tell a missing grant from a closed month.
        Assert.NotEqual(closed.Code, denied.Code);
    }

    private static async Task<AccountingPeriodStatusAnswerV1> Answer(AccountingPeriodStatus? status) =>
        await Port(new Owner(status)).GetStatusAsync(Asked);

    private static async Task<RecordPeriodAnswer> Rule(Owner owner, bool granted = true) =>
        await new OccurredOnFinanceRecordPeriodRule(Port(owner, granted)).CheckAsync(
            new FinanceContext(Company, "actor", "corr", null), Asked, null, DateTimeOffset.UtcNow, CancellationToken.None);

    private static AccountingPeriodStatusPort Port(Owner owner, bool granted = true, SwappableUser? user = null)
    {
        var companyContext = new CompanyContext();
        companyContext.Set(Company);
        return new AccountingPeriodStatusPort(companyContext, user ?? new SwappableUser(Company),
            new Authority(granted), owner);
    }

    private sealed class Authority(bool granted) : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(granted && permission == AccountingPermissionManifestV1.PeriodsRead
                ? new PermissionScope(true, true, new HashSet<string>(StringComparer.Ordinal) { "SENTETIK" },
                    new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
    }

    private sealed class SwappableUser(Guid company) : ICurrentUser
    {
        private readonly HashSet<Guid> _allowed = [company];

        public string? UserId { get; private set; } = "synthetic-recorder";

        public bool IsAuthenticated => true;

        public IReadOnlySet<Guid> AllowedCompanyIds => _allowed;

        public bool CanAccessCompany(Guid companyId) => _allowed.Contains(companyId);

        public void Swap() => UserId = "synthetic-other-recorder";
    }

    /// <summary>The period owner: it records which DATE it was asked about, which is what the rule's claim rests on.</summary>
    private sealed class Owner(AccountingPeriodStatus? status) : IAccountingPeriodStore
    {
        private readonly List<DateOnly> _asked = [];

        public IReadOnlyList<DateOnly> Asked => _asked;

        public Action? DuringRead { get; init; }

        public Task<AccountingPeriodRecord?> FindPeriodCoveringAsync(DateOnly on, CancellationToken cancellationToken = default)
        {
            _asked.Add(on);
            DuringRead?.Invoke();
            if (status is not { } value)
            {
                return Task.FromResult<AccountingPeriodRecord?>(null);
            }

            var period = AccountingPeriod.Propose(Guid.NewGuid(), Company, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30),
                Guid.NewGuid(), DateTimeOffset.UtcNow);
            if (value != AccountingPeriodStatus.Proposed)
            {
                period.Approve(Guid.NewGuid(), DateTimeOffset.UtcNow);
            }

            if (value == AccountingPeriodStatus.Closed)
            {
                period.Close(Guid.NewGuid(), DateTimeOffset.UtcNow);
            }

            return Task.FromResult<AccountingPeriodRecord?>(new AccountingPeriodRecord(period, 1));
        }

        public Task<(AccountingPeriodRecord Record, AccountingWriteOutcome Outcome)> ProposePeriodAsync(Guid periodId,
            DateOnly startsOn, DateOnly endsOn, Guid preparedByUserId, AccountingCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException("The port reads only.");

        public Task<AccountingPeriodRecord> ApprovePeriodAsync(Guid periodId, long expectedVersion, Guid approvedByUserId,
            AccountingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The port reads only.");

        public Task<AccountingPeriodRecord> ClosePeriodAsync(Guid periodId, long expectedVersion, Guid closedByUserId,
            AccountingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The port reads only.");

        public Task<AccountingPeriodRecord?> FindPeriodAsync(Guid periodId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The port reads only.");

        public Task<AccountingPage<AccountingPeriodRecord>> ListPeriodsAsync(int pageSize, string? cursor,
            CancellationToken cancellationToken = default) => throw new NotSupportedException("The port reads only.");
    }
}

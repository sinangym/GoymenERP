using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;

namespace GoymenERP.Accounting.UnitTests;

public sealed class AccountingApiFenceTests
{
    private static readonly Guid Company = Guid.Parse("a0000000-0000-4000-8000-0000000000a1");
    private static readonly Guid OtherCompany = Guid.Parse("a0000000-0000-4000-8000-0000000000a2");
    private static readonly Guid Actor = Guid.Parse("a0000000-0000-4000-8000-0000000000b1");
    private static readonly Guid OtherActor = Guid.Parse("a0000000-0000-4000-8000-0000000000b2");

    [Fact]
    public async Task A_grant_lost_after_the_initial_command_lookup_refuses_before_the_write()
    {
        var authority = new SequencedAuthority(Granted(), PermissionScope.Denied);
        var stores = new Stores();
        var result = await Service(authority, stores).ProposePeriodAsync(
            new ProposePeriodRequestV1(Guid.NewGuid(), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)),
            "SENTETIK-fence-1", CancellationToken.None);

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Null(result.Resource);
        Assert.Equal(2, authority.Calls);
        Assert.Equal(["FindCommandAsync"], stores.Calls);
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("close")]
    public async Task Period_transitions_are_fenced_again_before_the_write(string command)
    {
        var authority = new SequencedAuthority(Granted(), PermissionScope.Denied);
        var stores = new Stores();
        var service = Service(authority, stores);
        var request = new PeriodCommandRequestV1(1);
        var result = command == "approve"
            ? await service.ApprovePeriodAsync(Guid.NewGuid(), request, "SENTETIK-fence-2", CancellationToken.None)
            : await service.ClosePeriodAsync(Guid.NewGuid(), request, "SENTETIK-fence-3", CancellationToken.None);

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Null(result.Resource);
        Assert.Equal(2, authority.Calls);
        Assert.Equal(["FindCommandAsync"], stores.Calls);
    }

    [Fact]
    public async Task A_stored_command_is_returned_only_after_its_read_is_fenced()
    {
        var period = View();
        var stores = new Stores { FindCommand = () => Stored(period, "SENTETIK-original-correlation") };
        var authority = new SequencedAuthority(Granted(), Granted());
        var result = await Service(authority, stores).ProposePeriodAsync(
            new ProposePeriodRequestV1(period.PeriodId, period.StartsOn, period.EndsOn), "SENTETIK-replay", CancellationToken.None);

        Assert.Equal(AccountingApiStatus.Created, result.Status);
        Assert.True(result.IsReplay);
        Assert.Equal(period, result.Resource);
        Assert.Equal("SENTETIK-original-correlation", result.OriginalCorrelationId);
        Assert.Equal(2, authority.Calls);
        Assert.Equal(["FindCommandAsync"], stores.Calls);
    }

    [Fact]
    public async Task A_concurrent_winner_is_not_disclosed_after_authority_is_lost()
    {
        var period = View();
        var reads = 0;
        var stores = new Stores
        {
            FindCommand = () => ++reads == 1 ? null : Stored(period, "SENTETIK-winner"),
            Propose = () => throw new AccountingStoreException(AccountingStoreCodes.ConcurrentChange, "SENTETIK-race"),
        };
        var authority = new SequencedAuthority(Granted(), Granted(), PermissionScope.Denied);
        var result = await Service(authority, stores).ProposePeriodAsync(
            new ProposePeriodRequestV1(period.PeriodId, period.StartsOn, period.EndsOn), "SENTETIK-winner-key", CancellationToken.None);

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Null(result.Resource);
        Assert.Equal(3, authority.Calls);
        Assert.Equal(["FindCommandAsync", "ProposePeriodAsync", "FindCommandAsync"], stores.Calls);
        Assert.DoesNotContain("RecordCommandAsync", stores.Calls);
    }

    [Fact]
    public async Task A_terminal_answer_is_not_persisted_after_authority_is_lost()
    {
        var stores = new Stores { Approve = () => throw new AccountingVersionConflictException(7, 8) };
        var authority = new SequencedAuthority(Granted(), Granted(), PermissionScope.Denied);
        var result = await Service(authority, stores).ApprovePeriodAsync(
            Guid.NewGuid(), new PeriodCommandRequestV1(7), "SENTETIK-terminal", CancellationToken.None);

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Null(result.Resource);
        Assert.Equal(3, authority.Calls);
        Assert.Equal(["FindCommandAsync", "ApprovePeriodAsync"], stores.Calls);
        Assert.DoesNotContain("RecordCommandAsync", stores.Calls);
    }

    [Theory]
    [InlineData("period-list")]
    [InlineData("period-detail")]
    [InlineData("journal-list")]
    [InlineData("journal-detail")]
    public async Task Every_period_and_journal_read_is_denied_when_its_post_read_fence_loses_authority(string read)
    {
        var stores = new Stores();
        var authority = new SequencedAuthority(Granted(), PermissionScope.Denied);
        var service = Service(authority, stores);
        var status = read switch
        {
            "period-list" => (await service.ListPeriodsAsync(null, null, CancellationToken.None)).Status,
            "period-detail" => (await service.GetPeriodAsync(Guid.NewGuid(), CancellationToken.None)).Status,
            "journal-list" => (await service.ListJournalsAsync(null, null, null, CancellationToken.None)).Status,
            "journal-detail" => (await service.GetJournalAsync(Guid.NewGuid(), CancellationToken.None)).Status,
            _ => throw new InvalidOperationException(read),
        };

        Assert.Equal(AccountingApiStatus.Denied, status);
        Assert.Equal(2, authority.Calls);
        Assert.Single(stores.Calls);
    }

    [Fact]
    public async Task Each_posting_queue_read_has_its_own_post_read_fence()
    {
        var stores = new Stores();
        var authority = new SequencedAuthority(Granted(), Granted(), PermissionScope.Denied);
        var result = await Service(authority, stores).GetPostingQueuesAsync(CancellationToken.None);

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Null(result.Resource);
        Assert.Equal(3, authority.Calls);
        Assert.Equal(["ReadOpenExceptionsAsync", "ReadPendingValuationsAsync"], stores.Calls);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("company")]
    public async Task Actor_or_company_drift_after_a_read_returns_no_resource(string drift)
    {
        var company = new MutableCompany();
        var user = new MutableUser(company);
        var stores = new Stores
        {
            AfterRead = () =>
            {
                if (drift == "actor") user.ActorId = OtherActor.ToString("D");
                else company.CompanyId = OtherCompany;
            },
        };
        var authority = new SequencedAuthority(Granted(), Granted());
        var result = await Service(authority, stores, company, user).ListPeriodsAsync(null, null, CancellationToken.None);

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Null(result.Resource);
        Assert.Equal(2, authority.Calls);
        Assert.Equal(["ListPeriodsAsync"], stores.Calls);
    }

    [Fact]
    public async Task Cancellation_from_a_store_read_propagates_without_a_revalidation_or_resource()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var stores = new Stores { ListPeriods = () => Task.FromCanceled<AccountingPage<AccountingPeriodRecord>>(cancellation.Token) };
        var authority = new SequencedAuthority(Granted());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Service(authority, stores).ListPeriodsAsync(null, null, CancellationToken.None));

        Assert.Equal(1, authority.Calls);
        Assert.Equal(["ListPeriodsAsync"], stores.Calls);
    }

    private static PeriodViewV1 View() => new(Guid.Parse("a0000000-0000-4000-8000-0000000000c1"), Company,
        new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), "Proposed", 1, Actor,
        new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), null, null, null, null, true);

    private static AccountingCommandRecord Stored(PeriodViewV1 period, string correlation)
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var result = new AccountingApiResult<PeriodViewV1>(AccountingApiStatus.Created, period, false, null);
        var request = new { periodId = period.PeriodId, startsOn = "2026-10-01", endsOn = "2026-10-31" };
        return new AccountingCommandRecord(AccountingCommandNames.ProposePeriod,
            AccountingApiService.Sha256("SENTETIK-replay"), Actor.ToString("D"),
            AccountingApiService.Sha256(AccountingCommandNames.ProposePeriod + "|" + JsonSerializer.Serialize(request, json)),
            JsonSerializer.Serialize(result, json), correlation);
    }

    private static PermissionScope Granted() => new(true, true,
        new HashSet<string>(["SENTETIK-ROLE"], StringComparer.Ordinal), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());

    private static AccountingApiService Service(IErpAuthorityRevalidationService authority, Stores stores,
        MutableCompany? company = null, MutableUser? user = null)
    {
        company ??= new MutableCompany();
        user ??= new MutableUser(company);
        return new AccountingApiService(stores, stores, stores, authority, company, user, new FixedCorrelation());
    }

    private sealed class SequencedAuthority(params PermissionScope[] answers) : IErpAuthorityRevalidationService
    {
        public int Calls { get; private set; }
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(answers[Math.Min(Calls++, answers.Length - 1)]);
        }
    }

    private sealed class Stores : IAccountingPeriodStore, IAccountingPostingStore, IAccountingCommandLog
    {
        public List<string> Calls { get; } = [];
        public Func<AccountingCommandRecord?> FindCommand { get; init; } = () => null;
        public Func<(AccountingPeriodRecord Record, AccountingWriteOutcome Outcome)>? Propose { get; init; }
        public Func<AccountingPeriodRecord>? Approve { get; init; }
        public Func<Task<AccountingPage<AccountingPeriodRecord>>>? ListPeriods { get; init; }
        public Action? AfterRead { get; init; }

        public Task<AccountingCommandRecord?> FindCommandAsync(string commandName, string idempotencyKeyHash,
            CancellationToken cancellationToken = default)
        { Calls.Add(nameof(FindCommandAsync)); var result = FindCommand(); AfterRead?.Invoke(); return Task.FromResult(result); }

        public Task RecordCommandAsync(AccountingCommandRecord record, CancellationToken cancellationToken = default)
        { Calls.Add(nameof(RecordCommandAsync)); return Task.CompletedTask; }

        public Task<(AccountingPeriodRecord Record, AccountingWriteOutcome Outcome)> ProposePeriodAsync(Guid periodId,
            DateOnly startsOn, DateOnly endsOn, Guid preparedByUserId, AccountingCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        { Calls.Add(nameof(ProposePeriodAsync)); return Task.FromResult(Propose?.Invoke() ?? throw new InvalidOperationException("Unexpected proposal write.")); }

        public Task<AccountingPeriodRecord> ApprovePeriodAsync(Guid periodId, long expectedVersion, Guid approvedByUserId,
            AccountingCommandMetadata metadata, CancellationToken cancellationToken = default)
        { Calls.Add(nameof(ApprovePeriodAsync)); return Task.FromResult(Approve?.Invoke() ?? throw new InvalidOperationException("Unexpected approval write.")); }

        public Task<AccountingPeriodRecord> ClosePeriodAsync(Guid periodId, long expectedVersion, Guid closedByUserId,
            AccountingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Unexpected close write.");

        public Task<AccountingPeriodRecord?> FindPeriodAsync(Guid periodId, CancellationToken cancellationToken = default)
        { Calls.Add(nameof(FindPeriodAsync)); AfterRead?.Invoke(); return Task.FromResult<AccountingPeriodRecord?>(null); }

        public Task<AccountingPage<AccountingPeriodRecord>> ListPeriodsAsync(int pageSize, string? cursor,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(nameof(ListPeriodsAsync));
            if (ListPeriods is not null) return ListPeriods();
            AfterRead?.Invoke();
            return Task.FromResult(new AccountingPage<AccountingPeriodRecord>([], null));
        }

        public Task<AccountingPeriodRecord?> FindPeriodCoveringAsync(DateOnly on, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Unexpected covering-period read.");
        public Task<PostingOutcome> ReceiveAsync(PostingRequest request, Guid periodId, AccountingCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected receive.");
        public Task<PostingOutcome> ReverseAsync(string consumer, string messageId, Guid reversedJournalId, Guid reversalJournalId,
            PostingDates dates, Guid periodId, AccountingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Unexpected reverse.");
        public Task<PostingOutcome> CorrectAsync(string consumer, string messageId, Guid correctionId, Guid correctedJournalId,
            Guid correctionJournalId, string postingRuleVersion, PostingDates dates, IEnumerable<JournalLine> lines,
            Guid periodId, AccountingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Unexpected correct.");
        public Task<Journal?> FindJournalAsync(Guid journalId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Unexpected journal find.");

        public Task<AccountingJournalRecord?> ReadJournalAsync(Guid journalId, CancellationToken cancellationToken = default)
        { Calls.Add(nameof(ReadJournalAsync)); AfterRead?.Invoke(); return Task.FromResult<AccountingJournalRecord?>(null); }

        public Task<AccountingPage<AccountingJournalRecord>> ListJournalsAsync(Guid? periodId, int pageSize, string? cursor,
            CancellationToken cancellationToken = default)
        { Calls.Add(nameof(ListJournalsAsync)); AfterRead?.Invoke(); return Task.FromResult(new AccountingPage<AccountingJournalRecord>([], null)); }

        public Task<IReadOnlyList<PostingException>> ReadOpenExceptionsAsync(CancellationToken cancellationToken = default)
        { Calls.Add(nameof(ReadOpenExceptionsAsync)); return Task.FromResult<IReadOnlyList<PostingException>>([]); }
        public Task<IReadOnlyList<PendingValuation>> ReadPendingValuationsAsync(CancellationToken cancellationToken = default)
        { Calls.Add(nameof(ReadPendingValuationsAsync)); return Task.FromResult<IReadOnlyList<PendingValuation>>([]); }
    }

    private sealed class MutableCompany : ICompanyContext
    {
        public bool IsResolved { get; set; } = true;
        public Guid CompanyId { get; set; } = Company;
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    private sealed class MutableUser(MutableCompany company) : ICurrentUser
    {
        public bool IsAuthenticated { get; set; } = true;
        public string? ActorId { get; set; } = Actor.ToString("D");
        public string? UserId => ActorId;
        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid>([company.CompanyId]);
        public bool CanAccessCompany(Guid companyId) => IsAuthenticated && companyId == company.CompanyId;
    }

    private sealed class FixedCorrelation : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => "SENTETIK-correlation";
        public string? CausationId => null;
        public string SourceChannel => "Test";
        public string? ClientIpAddress => null;
        public string? UserAgent => null;
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent) =>
            throw new NotSupportedException();
    }
}

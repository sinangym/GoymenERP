using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Ai;

namespace GoymenERP.Accounting.UnitTests;

/// <summary>
/// WP-A3c Accounting READ tools over a SENTETIK owner read (the narrow view of the authorized ACC-V0 service): no database,
/// no model. An owner refusal is a refusal, another company releases nothing, and an incomplete read withholds counts.
/// </summary>
public sealed class AccountingAiReadToolsTests
{
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000a1");
    private static readonly Guid OtherCompany = Guid.Parse("bbbbbbbb-0000-4000-8000-0000000000a2");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);

    // ---- accounting.period-status.v1 -----------------------------------------------------------

    [Fact]
    public async Task The_period_covering_today_is_named_with_its_state_and_exact_counts_without_person_ids()
    {
        var owner = new Owner();
        owner.Period(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), "Closed");
        var current = owner.Period(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), "Open");
        owner.Period(new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 31), "Proposed", isOwn: true);

        var result = await new AccountingPeriodStatusAiTool(owner, new AmbientCompany(Company), new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        using var content = JsonDocument.Parse(result.ContentJson!);
        var root = content.RootElement;
        Assert.True(root.GetProperty("complete").GetBoolean());
        Assert.Equal("Open", root.GetProperty("currentPeriodState").GetString());
        Assert.Equal(current.PeriodId, root.GetProperty("currentPeriod").GetProperty("periodId").GetGuid());
        Assert.Equal(1, root.GetProperty("openCount").GetInt32());
        Assert.Equal(1, root.GetProperty("proposedCount").GetInt32());
        Assert.Equal(1, root.GetProperty("closedCount").GetInt32());
        Assert.Equal(1, root.GetProperty("proposedByMeCount").GetInt32());
        Assert.DoesNotContain(owner.Preparer.ToString("D"), result.ContentJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_period_covering_today_is_said_as_NoPeriod_and_two_covering_periods_are_not_complete()
    {
        var none = new Owner();
        none.Period(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), "Closed");
        using (var content = JsonDocument.Parse((await PeriodStatus(none).ReadAsync(new AiToolInputV1("{}"), Context())).ContentJson!))
        {
            Assert.Equal(AccountingPeriodStatusAiTool.NoPeriod, content.RootElement.GetProperty("currentPeriodState").GetString());
        }

        var overlapping = new Owner();
        overlapping.Period(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), "Open");
        overlapping.Period(new DateOnly(2026, 11, 15), new DateOnly(2026, 12, 15), "Proposed");
        using (var content = JsonDocument.Parse((await PeriodStatus(overlapping).ReadAsync(new AiToolInputV1("{}"), Context())).ContentJson!))
        {
            Assert.False(content.RootElement.GetProperty("complete").GetBoolean());
            Assert.Equal(JsonValueKind.Null, content.RootElement.GetProperty("currentPeriodState").ValueKind);
            Assert.Equal(JsonValueKind.Null, content.RootElement.GetProperty("openCount").ValueKind);
        }
    }

    [Fact]
    public async Task Without_the_period_read_the_owner_refusal_is_denied()
    {
        var owner = new Owner { Status = AccountingApiStatus.Denied };

        var result = await PeriodStatus(owner).ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(AiToolOutcomeV1.Denied, result.Outcome);
        Assert.Null(result.ContentJson);
    }

    [Fact]
    public async Task A_period_of_another_company_or_another_ambient_company_releases_nothing()
    {
        var foreign = new Owner();
        foreign.Period(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), "Open", company: OtherCompany);
        Assert.Equal(AiToolOutcomeV1.Unavailable, (await PeriodStatus(foreign).ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);

        var owner = new Owner();
        owner.Period(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), "Open");
        var result = await new AccountingPeriodStatusAiTool(owner, new AmbientCompany(OtherCompany), new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context());
        Assert.Equal(AiToolOutcomeV1.Unavailable, result.Outcome);
        Assert.Equal(0, owner.PeriodCalls);
    }

    // ---- accounting.posting-queues.v1 ----------------------------------------------------------

    [Fact]
    public async Task Posting_queues_are_counted_by_queue_and_purpose_and_the_message_ids_are_not_released()
    {
        var owner = new Owner();
        owner.Exceptions.Add(new PostingExceptionViewV1(Guid.NewGuid(), "sales.invoice", "r1", ["600", "391"], "SENTETIK-MSG-1"));
        owner.Exceptions.Add(new PostingExceptionViewV1(Guid.NewGuid(), "sales.invoice", "r1", ["600"], "SENTETIK-MSG-2"));
        owner.Pending.Add(new PendingValuationViewV1(Guid.NewGuid(), "inventory.receipt", "r2", 3, "SENTETIK-MSG-3"));

        var result = await new AccountingPostingQueuesAiTool(owner, new AmbientCompany(Company), new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        using var content = JsonDocument.Parse(result.ContentJson!);
        Assert.Equal(2, content.RootElement.GetProperty("missingMappingCount").GetInt32());
        Assert.Equal(1, content.RootElement.GetProperty("pendingValuationCount").GetInt32());
        Assert.Contains(content.RootElement.GetProperty("byPostingPurpose").EnumerateArray(),
            group => group.GetProperty("postingPurpose").GetString() == "sales.invoice" && group.GetProperty("count").GetInt32() == 2);
        Assert.DoesNotContain("SENTETIK-MSG", result.ContentJson, StringComparison.Ordinal);

        var onlyPending = await new AccountingPostingQueuesAiTool(owner, new AmbientCompany(Company), new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{\"queue\":\"PendingValuation\"}"), Context());
        using var pending = JsonDocument.Parse(onlyPending.ContentJson!);
        Assert.Equal(JsonValueKind.Null, pending.RootElement.GetProperty("missingMappingCount").ValueKind);
        Assert.Equal(1, pending.RootElement.GetProperty("listedCount").GetInt32());
    }

    [Fact]
    public async Task Posting_queues_without_the_queue_read_are_denied_and_an_invalid_argument_does_not_read()
    {
        var denied = new Owner { Status = AccountingApiStatus.Denied };
        Assert.Equal(AiToolOutcomeV1.Denied, (await new AccountingPostingQueuesAiTool(denied, new AmbientCompany(Company), new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);

        var owner = new Owner();
        var invalid = await new AccountingPostingQueuesAiTool(owner, new AmbientCompany(Company), new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{\"companyId\":\"x\"}"), Context());
        Assert.Equal(AiToolOutcomeV1.Invalid, invalid.Outcome);
        Assert.Equal(0, owner.QueueCalls);
    }

    // ---- accounting.period-journals.v1 ---------------------------------------------------------

    [Fact]
    public async Task Journals_of_the_current_period_are_counted_by_kind_without_amounts_and_drafts_are_said_not_counted()
    {
        var owner = new Owner();
        var current = owner.Period(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), "Open");
        owner.Journal(current.PeriodId, "Original", "sales.invoice");
        owner.Journal(current.PeriodId, "Original", "sales.invoice");
        owner.Journal(current.PeriodId, "Reversal", "sales.invoice");

        var result = await Journals(owner).ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        using var content = JsonDocument.Parse(result.ContentJson!);
        var root = content.RootElement;
        Assert.Equal(3, root.GetProperty("journalCount").GetInt32());
        Assert.Equal(2, root.GetProperty("originalCount").GetInt32());
        Assert.Equal(1, root.GetProperty("reversalCount").GetInt32());
        Assert.Equal(0, root.GetProperty("correctionCount").GetInt32());
        Assert.False(root.GetProperty("draftJournalsCounted").GetBoolean());
        Assert.DoesNotContain("123.45", result.ContentJson, StringComparison.Ordinal);
        Assert.All(owner.JournalPeriods, period => Assert.Equal(current.PeriodId, period));
    }

    [Fact]
    public async Task The_previous_period_is_the_latest_one_ending_before_the_current_period()
    {
        var owner = new Owner();
        owner.Period(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), "Closed");
        var october = owner.Period(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), "Closed");
        owner.Period(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), "Open");
        owner.Journal(october.PeriodId, "Correction", "finance.receipt");

        var result = await Journals(owner).ReadAsync(new AiToolInputV1("{\"period\":\"Previous\"}"), Context());

        using var content = JsonDocument.Parse(result.ContentJson!);
        Assert.Equal(october.PeriodId, content.RootElement.GetProperty("period").GetProperty("periodId").GetGuid());
        Assert.Equal(1, content.RootElement.GetProperty("correctionCount").GetInt32());
    }

    [Fact]
    public async Task A_journal_of_another_period_releases_nothing_and_a_journal_read_refusal_is_denied()
    {
        var owner = new Owner();
        var current = owner.Period(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), "Open");
        owner.Journal(Guid.NewGuid(), "Original", "sales.invoice", listedUnder: current.PeriodId);
        Assert.Equal(AiToolOutcomeV1.Unavailable, (await Journals(owner).ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);

        var denied = new Owner { JournalStatus = AccountingApiStatus.Denied };
        denied.Period(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), "Open");
        Assert.Equal(AiToolOutcomeV1.Denied, (await Journals(denied).ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);
    }

    [Fact]
    public async Task At_the_journal_scan_cap_the_counts_are_withheld()
    {
        var owner = new Owner();
        var current = owner.Period(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), "Open");
        for (var index = 0; index <= AccountingPeriodJournalsAiTool.JournalScanLimit; index++)
        {
            owner.Journal(current.PeriodId, "Original", "sales.invoice");
        }

        var result = await Journals(owner).ReadAsync(new AiToolInputV1("{}"), Context());

        using var content = JsonDocument.Parse(result.ContentJson!);
        Assert.False(content.RootElement.GetProperty("complete").GetBoolean());
        Assert.Equal(JsonValueKind.Null, content.RootElement.GetProperty("journalCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, content.RootElement.GetProperty("originalCount").ValueKind);
    }

    [Fact]
    public void The_owner_service_is_the_read_view_and_the_descriptors_name_the_narrow_accounting_permissions()
    {
        Assert.True(typeof(IAccountingOwnerReadV1).IsAssignableFrom(typeof(AccountingApiService)));
        var owner = new Owner();
        var company = new AmbientCompany(Company);
        Assert.Equal([AccountingPermissionManifestV1.PeriodsRead],
            new AccountingPeriodStatusAiTool(owner, company, new FixedTime(Now)).Descriptor.RequiredPermissions);
        Assert.Equal([AccountingPermissionManifestV1.PostingQueuesRead],
            new AccountingPostingQueuesAiTool(owner, company, new FixedTime(Now)).Descriptor.RequiredPermissions);
        Assert.Equal([AccountingPermissionManifestV1.JournalsRead, AccountingPermissionManifestV1.PeriodsRead],
            Journals(owner).Descriptor.RequiredPermissions);
    }

    // ---- fixtures -------------------------------------------------------------------------------

    private static AccountingPeriodStatusAiTool PeriodStatus(Owner owner) => new(owner, new AmbientCompany(Company), new FixedTime(Now));

    private static AccountingPeriodJournalsAiTool Journals(Owner owner) => new(owner, new AmbientCompany(Company), new FixedTime(Now));

    private static AiServerContextV1 Context() => new(Company, "11111111-1111-4111-8111-1111111111a1", "v1.session-sentetik",
        $"company:{Company:D}", "roles:sentetik", "corr-wpa3c-sentetik", null, null, null);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class AmbientCompany(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId => companyId;
        public void Set(Guid value) => throw new NotSupportedException();
    }

    /// <summary>A SENTETIK owner read: pages of the asked size by an index cursor, as the owner pages them.</summary>
    private sealed class Owner : IAccountingOwnerReadV1
    {
        private readonly List<PeriodViewV1> periods = [];
        private readonly List<(Guid ListedUnder, JournalSummaryV1 Journal)> journals = [];

        public Guid Preparer { get; } = Guid.Parse("22222222-2222-4222-8222-2222222222a2");
        public AccountingApiStatus Status { get; init; } = AccountingApiStatus.Ok;
        public AccountingApiStatus JournalStatus { get; init; } = AccountingApiStatus.Ok;
        public List<PostingExceptionViewV1> Exceptions { get; } = [];
        public List<PendingValuationViewV1> Pending { get; } = [];
        public List<Guid?> JournalPeriods { get; } = [];
        public int PeriodCalls { get; private set; }
        public int QueueCalls { get; private set; }

        public PeriodViewV1 Period(DateOnly startsOn, DateOnly endsOn, string status, bool isOwn = false, Guid? company = null)
        {
            var period = new PeriodViewV1(Guid.NewGuid(), company ?? Company, startsOn, endsOn, status, 1, Preparer, Now.AddDays(-60),
                null, null, null, null, isOwn);
            periods.Add(period);
            return period;
        }

        public void Journal(Guid periodId, string kind, string purpose, Guid? listedUnder = null) =>
            journals.Add((listedUnder ?? periodId, new JournalSummaryV1(Guid.NewGuid(), kind, Guid.NewGuid(), purpose, "r1", Guid.NewGuid(),
                new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 2), null, periodId, null, "123.45", "123.45", 2, Now.AddDays(-1))));

        public Task<AccountingApiResult<PeriodPageV1>> ListPeriodsAsync(int? pageSize, string? cursor, CancellationToken cancellationToken)
        {
            PeriodCalls++;
            if (Status != AccountingApiStatus.Ok)
            {
                return Task.FromResult(new AccountingApiResult<PeriodPageV1>(Status, null, false, null));
            }

            var (page, next) = Page(periods, pageSize, cursor);
            return Task.FromResult(new AccountingApiResult<PeriodPageV1>(AccountingApiStatus.Ok, new PeriodPageV1(page, next), false, null));
        }

        public Task<AccountingApiResult<JournalPageV1>> ListJournalsAsync(Guid? periodId, int? pageSize, string? cursor,
            CancellationToken cancellationToken)
        {
            JournalPeriods.Add(periodId);
            if (JournalStatus != AccountingApiStatus.Ok)
            {
                return Task.FromResult(new AccountingApiResult<JournalPageV1>(JournalStatus, null, false, null));
            }

            var (page, next) = Page(journals.Where(entry => entry.ListedUnder == periodId).Select(entry => entry.Journal).ToList(),
                pageSize, cursor);
            return Task.FromResult(new AccountingApiResult<JournalPageV1>(AccountingApiStatus.Ok, new JournalPageV1(page, next), false, null));
        }

        public Task<AccountingApiResult<PostingQueuesV1>> GetPostingQueuesAsync(CancellationToken cancellationToken)
        {
            QueueCalls++;
            return Task.FromResult(Status == AccountingApiStatus.Ok
                ? new AccountingApiResult<PostingQueuesV1>(AccountingApiStatus.Ok, new PostingQueuesV1(Exceptions, Pending), false, null)
                : new AccountingApiResult<PostingQueuesV1>(Status, null, false, null));
        }

        private static (IReadOnlyList<T> Page, string? Next) Page<T>(IReadOnlyList<T> rows, int? pageSize, string? cursor)
        {
            var size = pageSize ?? 50;
            var skip = cursor is null ? 0 : int.Parse(cursor, System.Globalization.CultureInfo.InvariantCulture);
            var page = rows.Skip(skip).Take(size).ToArray();
            var next = skip + page.Length < rows.Count ? (skip + page.Length).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
            return (page, next);
        }
    }
}

using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.Collaboration;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Work.UnitTests;

public sealed partial class WorkCollaborationServiceTests
{
    [Fact]
    public async Task Morning_summary_already_cancelled_read_never_calls_people_schedule_or_record_owners()
    {
        var harness = await MorningHarnessAsync(); await using var database = harness.Database();
        var probe = new C5ReadProbe(); var company = new MorningMutableCompany(Company); var user = new C5ReadUser();
        var before = (await database.InboxDigests.CountAsync(), await database.InboxDigestItems.CountAsync(), harness.Audit.Events.Count);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WorkMorningSummaryReadV1(database, company, user, probe, probe, probe, harness.Time).ReadAsync(cancellation.Token));
        Assert.Equal(0, probe.TotalCalls); Assert.False(database.ChangeTracker.HasChanges());
        Assert.Equal(before, (await database.InboxDigests.CountAsync(), await database.InboxDigestItems.CountAsync(), harness.Audit.Events.Count));
    }

    [Theory]
    [InlineData("people", 1)] [InlineData("people", 3)]
    [InlineData("schedule", 1)] [InlineData("schedule", 2)]
    [InlineData("record", 1)] [InlineData("record", 2)]
    public async Task Morning_summary_token_ignoring_completed_await_cannot_release_counts_or_continue_after_cancellation(string stage, int ordinal)
    {
        var harness = await MorningHarnessAsync(); await using var database = harness.Database();
        using var cancellation = new CancellationTokenSource(); var fired = false;
        var probe = new C5ReadProbe { AfterRead = (actual, count) => { if (actual == stage && count == ordinal) { fired = true; cancellation.Cancel(); } } };
        var company = new MorningMutableCompany(Company); var user = new C5ReadUser();
        var before = (await database.InboxDigests.CountAsync(), await database.InboxDigestItems.CountAsync(), await database.CollaborationAttempts.CountAsync(), harness.Audit.Events.Count);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WorkMorningSummaryReadV1(database, company, user, probe, probe, probe, harness.Time).ReadAsync(cancellation.Token));
        Assert.True(fired); Assert.Equal(stage, probe.LastStage); Assert.Equal(ordinal, probe.LastOrdinal);
        Assert.False(database.ChangeTracker.HasChanges());
        Assert.Equal(before, (await database.InboxDigests.CountAsync(), await database.InboxDigestItems.CountAsync(), await database.CollaborationAttempts.CountAsync(), harness.Audit.Events.Count));
    }

    [Theory]
    [InlineData("people", 1, "actor")] [InlineData("people", 3, "company")]
    [InlineData("schedule", 1, "membership")] [InlineData("schedule", 2, "actor")]
    [InlineData("record", 1, "company")] [InlineData("record", 2, "membership")]
    public async Task Morning_summary_completed_owner_callback_cannot_release_to_changed_company_actor_or_membership(string stage, int ordinal, string fault)
    {
        var harness = await MorningHarnessAsync(); await using var database = harness.Database();
        var company = new MorningMutableCompany(Company); var user = new C5ReadUser(); var fired = false;
        var probe = new C5ReadProbe { AfterRead = (actual, count) =>
        {
            if (actual != stage || count != ordinal) return;
            fired = true;
            if (fault == "company") company.Set(OtherCompany);
            else if (fault == "actor") user.Actor = Writer;
            else user.Accessible = false;
        } };
        var error = await Assert.ThrowsAsync<WorkCollaborationException>(() => new WorkMorningSummaryReadV1(database, company, user, probe, probe, probe, harness.Time).ReadAsync());
        Assert.Equal(403, error.Status); Assert.True(fired); Assert.Equal(stage, probe.LastStage); Assert.Equal(ordinal, probe.LastOrdinal);
        Assert.False(database.ChangeTracker.HasChanges());
        Assert.Single(await database.InboxDigests.ToArrayAsync()); Assert.Single(await database.InboxDigestItems.ToArrayAsync());
    }

    [Fact]
    public async Task Morning_summary_cancelled_read_does_not_poison_the_next_authorized_read_or_create_a_digest()
    {
        var harness = await MorningHarnessAsync(); await using var database = harness.Database();
        using var cancellation = new CancellationTokenSource();
        var probe = new C5ReadProbe { AfterRead = (_, _) => cancellation.Cancel() };
        var company = new MorningMutableCompany(Company); var user = new C5ReadUser();
        var service = new WorkMorningSummaryReadV1(database, company, user, probe, probe, probe, harness.Time);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadAsync(cancellation.Token));
        probe.AfterRead = null;
        var answer = Assert.IsType<WorkMorningSummaryV1>(await service.ReadAsync());
        Assert.Equal(Reader, user.Actor); Assert.Equal(Company, answer.CompanyId);
        Assert.Equal(new WorkMorningSummaryCountV1("task-assigned", 1), Assert.Single(answer.Counts));
        Assert.Single(await database.InboxDigests.ToArrayAsync()); Assert.Single(await database.InboxDigestItems.ToArrayAsync());
        Assert.False(database.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Morning_summary_rechecks_owner_grant_revoked_by_the_final_completed_membership_read()
    {
        var harness = await MorningHarnessAsync(); await using var database = harness.Database();
        var company = new MorningMutableCompany(Company); var user = new C5ReadUser(); var fired = false;
        var probe = new C5ReadProbe();
        probe.AfterRead = (stage, ordinal) =>
        {
            if (stage != "people" || ordinal != 3) return;
            fired = true; probe.OwnerReadable = false;
        };
        var before = (await database.InboxDigests.CountAsync(), await database.InboxDigestItems.CountAsync(),
            await database.CollaborationAttempts.CountAsync(), harness.Audit.Events.Count);
        var error = await Assert.ThrowsAsync<WorkCollaborationException>(() =>
            new WorkMorningSummaryReadV1(database, company, user, probe, probe, probe, harness.Time).ReadAsync());
        Assert.Equal(403, error.Status); Assert.True(fired);
        Assert.Equal("record", probe.LastStage); Assert.Equal(2, probe.LastOrdinal);
        Assert.Equal(Reader, user.Actor); Assert.True(user.Accessible); Assert.Equal(Company, company.CompanyId);
        Assert.False(database.ChangeTracker.HasChanges());
        Assert.Equal(before, (await database.InboxDigests.CountAsync(), await database.InboxDigestItems.CountAsync(),
            await database.CollaborationAttempts.CountAsync(), harness.Audit.Events.Count));
    }

    // Deliberately completes asynchronously even if the caller's token is cancelled.
    // These are source-boundary unit doubles, not scheduler/SQL/provider acceptance.
    private sealed class C5ReadProbe : IWorkPersonDirectoryV1, IWorkRecordAccessPortV1, IWorkDigestScheduleReaderV1
    {
        private readonly Dictionary<string, int> calls = [];
        internal Action<string, int>? AfterRead { get; set; }
        internal bool OwnerReadable { get; set; } = true;
        internal int TotalCalls { get; private set; }
        internal string? LastStage { get; private set; }
        internal int LastOrdinal { get; private set; }
        private async Task Completed(string stage)
        {
            await Task.Yield();
            var ordinal = calls.GetValueOrDefault(stage) + 1; calls[stage] = ordinal;
            TotalCalls++; LastStage = stage; LastOrdinal = ordinal; AfterRead?.Invoke(stage, ordinal);
        }
        public async Task<WorkPersonOutcomeV1> IsActiveMemberAsync(Guid companyId, Guid userId, CancellationToken token = default)
        { await Completed("people"); return companyId == Company && userId == Reader ? WorkPersonOutcomeV1.Found : WorkPersonOutcomeV1.Denied; }
        public async Task<WorkRecordAccessOutcomeV1> CheckCurrentActorAsync(Guid companyId, WorkRecordRefV1 record, CancellationToken token = default)
        { await Completed("record"); return OwnerReadable && companyId == Company && record.RecordId == Draft ? WorkRecordAccessOutcomeV1.Readable : WorkRecordAccessOutcomeV1.NotReadable; }
        public async Task<WorkDigestScheduleV1> ReadAsync(CancellationToken token = default)
        { await Completed("schedule"); return MorningSchedule(); }
        public Task<WorkPeopleAnswerV1> ResolveAsync(Guid companyId, IReadOnlyList<Guid> ids, CancellationToken token = default) => throw new NotSupportedException();
        public Task<WorkPeopleAnswerV1> SearchAsync(Guid companyId, string? query, CancellationToken token = default) => throw new NotSupportedException();
        public Task<WorkRecordAccessOutcomeV1> CheckUserAsync(Guid companyId, WorkRecordRefV1 record, Guid userId, CancellationToken token = default) => throw new InvalidOperationException("Cannot run as another task owner.");
    }
    private sealed class C5ReadUser : ICurrentUser
    {
        internal Guid Actor = Reader; internal bool Accessible = true;
        public bool IsAuthenticated => true;
        public string UserId => Actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid>([Company]);
        public bool CanAccessCompany(Guid companyId) => Accessible && companyId == Company;
    }
}

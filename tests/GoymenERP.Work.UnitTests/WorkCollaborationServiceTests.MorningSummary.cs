using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure;
using GoymenERP.Modules.Work.Infrastructure.Collaboration;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Work.UnitTests;

public sealed partial class WorkCollaborationServiceTests
{
    [Fact]
    public async Task Morning_summary_counts_the_existing_native_task_digest_once_without_AI_or_new_writes()
    {
        var harness = new Harness();
        await harness.Service(Writer, WorkPermissionManifestV1.TasksAssign).CreateTaskAsync(
            new("SENTETIK", null, Reader, Now.AddDays(1), "sales-order-draft", Draft), Meta("morning-task"));
        var schedule = MorningSchedule();
        await harness.Maintenance(schedule).RunCycleAsync();
        await using var database = harness.Database();
        var before = (await database.InboxDigests.CountAsync(), await database.InboxDigestItems.CountAsync(),
            await database.CollaborationAttempts.CountAsync(), harness.Audit.Events.Count);
        var records = new MorningRecords();
        var service = MorningReader(harness, database, records);
        var first = Assert.IsType<WorkMorningSummaryV1>(await service.ReadAsync());
        var again = Assert.IsType<WorkMorningSummaryV1>(await service.ReadAsync());
        Assert.Equal(Company, first.CompanyId);
        Assert.Equal(Assert.Single(await database.InboxDigests.ToArrayAsync()).Id, first.DigestId);
        Assert.Equal(first.DigestId, again.DigestId);
        Assert.Equal(new WorkMorningSummaryCountV1("task-assigned", 1), Assert.Single(first.Counts));
        Assert.Equal(first.Counts, again.Counts);
        Assert.Equal(4, records.Calls); // Every source item is freshly checked both before and after the second owner snapshot.
        Assert.Equal(before, (await database.InboxDigests.CountAsync(), await database.InboxDigestItems.CountAsync(),
            await database.CollaborationAttempts.CountAsync(), harness.Audit.Events.Count));
        Assert.False(database.ChangeTracker.HasChanges());
        var services = new ServiceCollection().AddWorkMorningSummaryV1();
        Assert.Single(services, item => item.ServiceType == typeof(IWorkMorningSummaryReadV1));
        Assert.DoesNotContain(services, item => item.ServiceType.FullName!.Contains("Ai", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Morning_summary_uses_the_first_settings_slot_not_an_afternoon_digest_and_does_not_release_held_items()
    {
        var harness = new Harness();
        await harness.Service(Writer, WorkPermissionManifestV1.TasksAssign).CreateTaskAsync(
            new("SENTETIK", null, Reader, Now.AddDays(1), null, null), Meta("morning-held"));
        await using var database = harness.Database();
        Assert.Null(await MorningReader(harness, database).ReadAsync());
        Assert.Empty(await database.InboxDigests.ToArrayAsync());
        await harness.Maintenance(MorningSchedule()).RunCycleAsync();
        Assert.NotNull(await MorningReader(harness, database).ReadAsync());
        harness.Time.Now = Now.AddHours(5);
        Assert.NotNull(await MorningReader(harness, database).ReadAsync());
        Assert.Single(await database.InboxDigests.ToArrayAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Morning_summary_refuses_current_or_late_owner_read_permission_loss_without_partial_counts(bool late)
    {
        var harness = await MorningHarnessAsync();
        await using var database = harness.Database();
        var records = new MorningRecords { DenyAt = late ? 2 : 1 };
        var error = await Assert.ThrowsAsync<WorkCollaborationException>(() => MorningReader(harness, database, records).ReadAsync());
        Assert.Equal(403, error.Status);
        Assert.Equal(late ? 2 : 1, records.Calls);
        Assert.False(database.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Morning_summary_refuses_late_membership_revocation_and_company_switch()
    {
        var harness = await MorningHarnessAsync();
        await using var database = harness.Database();
        var member = new MorningPeople { DenyAt = 3 };
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkCollaborationException>(() =>
            MorningReader(harness, database, people: member).ReadAsync())).Status);
        var company = new CompanyContext();
        company.Set(Company);
        var records = new MorningRecords { AfterRead = () => company.Set(OtherCompany) };
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkCollaborationException>(() =>
            MorningReader(harness, database, records, company: company).ReadAsync())).Status);
    }

    [Fact]
    public async Task Morning_summary_is_recipient_scoped_and_fails_closed_without_a_valid_company_schedule()
    {
        var harness = await MorningHarnessAsync();
        await using var database = harness.Database();
        Assert.Null(await MorningReader(harness, database, actor: Writer).ReadAsync());
        var company = new CompanyContext();
        company.Set(OtherCompany);
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkCollaborationException>(() =>
            MorningReader(harness, database, company: company).ReadAsync())).Status);
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkCollaborationException>(() =>
            MorningReader(harness, database, schedule: WorkDigestScheduleV1.Unavailable(Company)).ReadAsync())).Status);
    }

    [Fact]
    public async Task Morning_summary_rejects_more_than_the_existing_page_bound_instead_of_returning_partial_totals()
    {
        var harness = new Harness();
        for (var index = 0; index <= WorkCollaborationLimitsV1.PageSize; index++)
            await harness.Service(Writer, WorkPermissionManifestV1.TasksAssign).CreateTaskAsync(
                new("SENTETIK", null, Reader, Now.AddDays(1), null, null), Meta("morning-bound-" + index));
        await harness.Maintenance(MorningSchedule()).RunCycleAsync();
        await using var database = harness.Database();
        var records = new MorningRecords();
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkCollaborationException>(() =>
            MorningReader(harness, database, records).ReadAsync())).Status);
        Assert.Equal(0, records.Calls);
        Assert.False(database.ChangeTracker.HasChanges());
        Assert.Equal(WorkCollaborationLimitsV1.PageSize + 1, await database.InboxDigestItems.CountAsync());
    }

    [Fact]
    public async Task Morning_summary_owner_failure_is_unavailable_and_does_not_reuse_the_previous_success()
    {
        var harness = await MorningHarnessAsync();
        await using var database = harness.Database();
        Assert.NotNull(await MorningReader(harness, database).ReadAsync());
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkCollaborationException>(() =>
            MorningReader(harness, database, new MorningRecords { Unavailable = true }).ReadAsync())).Status);
        Assert.False(database.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Morning_summary_event_without_a_current_owner_read_proof_is_unavailable_not_an_authorized_count()
    {
        var harness = await MorningHarnessAsync();
        await using var database = harness.Database();
        // SENTETIK boundary corruption: native delivered events have no WorkRecordRef yet. Do not infer their owner's permission.
        var digest = Assert.Single(await database.InboxDigests.ToArrayAsync());
        var id = Guid.NewGuid();
        database.InboxNotifications.Add(new WorkInboxNotificationRow
        {
            CompanyId = Company, Id = id, RecipientUserId = Reader, Kind = (int)WorkInboxNotificationKindV1.AllocationReleased,
            Class = (int)WorkInboxNotificationClassV1.Digest, SourceId = Guid.NewGuid(), RecordedAtUtc = Now, CorrelationId = "SENTETIK",
        });
        database.InboxDigestItems.Add(new WorkInboxDigestItemRow { CompanyId = Company, DigestId = digest.Id, NotificationId = id });
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkCollaborationException>(() => MorningReader(harness, database).ReadAsync())).Status);
        Assert.False(database.ChangeTracker.HasChanges());
    }

    private static WorkDigestScheduleV1 MorningSchedule() => new(true, Company,
        [new TimeOnly(9, 0), new TimeOnly(13, 0), new TimeOnly(17, 0)], "UTC", "settings-v1");

    private static async Task<Harness> MorningHarnessAsync()
    {
        var harness = new Harness();
        await harness.Service(Writer, WorkPermissionManifestV1.TasksAssign).CreateTaskAsync(
            new("SENTETIK", null, Reader, Now.AddDays(1), "sales-order-draft", Draft), Meta("morning-source"));
        await harness.Maintenance(MorningSchedule()).RunCycleAsync();
        return harness;
    }

    private static WorkMorningSummaryReadV1 MorningReader(Harness harness, WorkDbContext database,
        MorningRecords? records = null, MorningPeople? people = null, CompanyContext? company = null,
        Guid? actor = null, WorkDigestScheduleV1? schedule = null)
    {
        if (company is null) { company = new CompanyContext(); company.Set(Company); }
        return new(database, company, new FakeUser(actor ?? Reader, Company), people ?? new MorningPeople(),
            records ?? new MorningRecords(), new FixedSchedule(schedule ?? MorningSchedule()), harness.Time);
    }

    private sealed class MorningRecords : IWorkRecordAccessPortV1
    {
        public int Calls { get; private set; }
        public int DenyAt { get; init; } = int.MaxValue;
        public bool Unavailable { get; init; }
        public Action? AfterRead { get; init; }
        public Task<WorkRecordAccessOutcomeV1> CheckCurrentActorAsync(Guid companyId, WorkRecordRefV1 record, CancellationToken cancellationToken = default)
        {
            Calls++;
            AfterRead?.Invoke();
            return Task.FromResult(Unavailable ? WorkRecordAccessOutcomeV1.Unavailable : companyId == Company && record.RecordId == Draft && Calls < DenyAt
                ? WorkRecordAccessOutcomeV1.Readable : WorkRecordAccessOutcomeV1.NotReadable);
        }
        public Task<WorkRecordAccessOutcomeV1> CheckUserAsync(Guid companyId, WorkRecordRefV1 record, Guid userId,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Summary cannot impersonate another user.");
    }

    private sealed class MorningPeople : IWorkPersonDirectoryV1
    {
        private int calls;
        public int DenyAt { get; init; } = int.MaxValue;
        public Task<WorkPersonOutcomeV1> IsActiveMemberAsync(Guid companyId, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(++calls < DenyAt && companyId == Company ? WorkPersonOutcomeV1.Found : WorkPersonOutcomeV1.Denied);
        public Task<WorkPeopleAnswerV1> ResolveAsync(Guid companyId, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkPeopleAnswerV1> SearchAsync(Guid companyId, string? query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

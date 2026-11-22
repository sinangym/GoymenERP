using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.Collaboration;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Work.UnitTests;

/// <summary>SENTETIK data only. In-memory Work store; owners, directory and authority are test doubles.</summary>
public sealed class WorkCollaborationServiceTests
{
    private static readonly Guid Company = Guid.Parse("c0000000-0000-4000-8000-000000000001");
    private static readonly Guid OtherCompany = Guid.Parse("c0000000-0000-4000-8000-000000000002");
    private static readonly Guid Writer = Guid.Parse("a0000000-0000-4000-8000-000000000001");
    private static readonly Guid Reader = Guid.Parse("a0000000-0000-4000-8000-000000000002");
    private static readonly Guid Outsider = Guid.Parse("a0000000-0000-4000-8000-000000000003");
    private static readonly Guid NonMember = Guid.Parse("a0000000-0000-4000-8000-000000000004");
    private static readonly Guid Draft = Guid.Parse("d0000000-0000-4000-8000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_note_with_a_readable_mention_is_saved_once_with_one_now_notification_and_the_same_key_replays_it()
    {
        var harness = new Harness();
        var service = harness.Service(Writer, WorkPermissionManifestV1.NotesWrite);
        var first = await service.AddNoteAsync("sales-order-draft", Draft, new("Teslim tarihini @Okur kontrol etsin.", [Reader], null), Meta("k1"));
        Assert.False(first.IsReplay);
        // TEKNIK_TUZAKLAR 1: a collection inside a tuple compares by reference, so the members are asserted one by one.
        Assert.Equal((Writer, "Yazar"), (first.Note.AuthorUserId, first.Note.AuthorDisplayName));
        Assert.Equal(new[] { Reader }, first.Note.MentionedUserIds);
        var replay = await harness.Service(Writer, WorkPermissionManifestV1.NotesWrite)
            .AddNoteAsync("sales-order-draft", Draft, new("Teslim tarihini @Okur kontrol etsin.", [Reader], null), Meta("k1", "corr-2"));
        Assert.True(replay.IsReplay);
        Assert.Equal(("corr-k1", first.Note.Id), (replay.OriginalCorrelationId, replay.Note.Id));
        await using var database = harness.Database();
        Assert.Single(await database.RecordNotes.ToListAsync());
        var notification = Assert.Single(await database.InboxNotifications.ToListAsync());
        Assert.Equal((Reader, (int)WorkInboxNotificationKindV1.Mention, (int)WorkInboxNotificationClassV1.Now, first.Note.Id),
            (notification.RecipientUserId, notification.Kind, notification.Class, notification.SourceId));
        Assert.Single(harness.Audit.Events, item => item.EventType == "work.note.added.v1" && item.IdempotencyKey == "k1");
    }

    [Fact]
    public async Task A_mention_of_someone_who_cannot_read_the_record_or_is_not_a_member_refuses_the_whole_note_without_saving()
    {
        var harness = new Harness();
        foreach (var person in new[] { Outsider, NonMember })
        {
            var error = await Assert.ThrowsAsync<WorkCollaborationException>(() => harness.Service(Writer, WorkPermissionManifestV1.NotesWrite)
                .AddNoteAsync("sales-order-draft", Draft, new("not", [person], null), Meta("k-" + person)));
            Assert.Equal((422, WorkCollaborationProblemCodesV1.MentionRefused), (error.Status, error.Code));
        }

        await using var database = harness.Database();
        Assert.Empty(await database.RecordNotes.ToListAsync());
        Assert.Empty(await database.InboxNotifications.ToListAsync());
        Assert.Empty(await database.CollaborationAttempts.ToListAsync());
    }

    [Fact]
    public async Task Notes_need_the_permission_a_readable_record_and_answer_404_for_an_unreadable_or_unknown_record()
    {
        var harness = new Harness();
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkCollaborationException>(() => harness.Service(Writer)
            .AddNoteAsync("sales-order-draft", Draft, new("x", null, null), Meta("a")))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<WorkCollaborationException>(() => harness.Service(Writer, WorkPermissionManifestV1.NotesWrite)
            .AddNoteAsync("sales-order-draft", Guid.NewGuid(), new("x", null, null), Meta("b")))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<WorkCollaborationException>(() => harness.Service(Writer, WorkPermissionManifestV1.NotesWrite)
            .AddNoteAsync("unknown-type", Draft, new("x", null, null), Meta("c")))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<WorkCollaborationException>(() => harness.Service(Outsider)
            .ListNotesAsync("sales-order-draft", Draft, false))).Status);
        harness.Records.Unavailable = true;
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkCollaborationException>(() => harness.Service(Writer)
            .ListNotesAsync("sales-order-draft", Draft, false))).Status);
    }

    [Fact]
    public async Task An_unregistered_permission_is_a_503_and_an_unauthenticated_caller_a_401()
    {
        var harness = new Harness();
        Assert.Equal(401, (await Assert.ThrowsAsync<WorkCollaborationException>(() => harness.Service(null)
            .ListMyTasksAsync(false))).Status);
        var wrongCompany = harness.Service(Writer, WorkPermissionManifestV1.NotesWrite, allowedCompany: OtherCompany);
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkCollaborationException>(() => wrongCompany.ListMyTasksAsync(false))).Status);
    }

    [Fact]
    public async Task A_correction_is_a_new_note_and_passivation_keeps_the_note_and_is_only_for_its_author()
    {
        var harness = new Harness();
        var original = await harness.Service(Writer, WorkPermissionManifestV1.NotesWrite).AddNoteAsync("sales-order-draft", Draft, new("ilk", null, null), Meta("n1"));
        var correction = await harness.Service(Writer, WorkPermissionManifestV1.NotesWrite)
            .AddNoteAsync("sales-order-draft", Draft, new("duzeltme", null, original.Note.Id), Meta("n2"));
        Assert.Equal(original.Note.Id, correction.Note.CorrectsNoteId);
        var refused = await Assert.ThrowsAsync<WorkCollaborationException>(() => harness.Service(Reader, WorkPermissionManifestV1.NotesWrite)
            .PassivateNoteAsync(original.Note.Id, new("yanlis"), Meta("p-reader")));
        Assert.Equal((409, WorkCollaborationProblemCodesV1.NoteNotOwn, false), (refused.Status, refused.Code, refused.IsReplay));
        var replayedRefusal = await Assert.ThrowsAsync<WorkCollaborationException>(() => harness.Service(Reader, WorkPermissionManifestV1.NotesWrite)
            .PassivateNoteAsync(original.Note.Id, new("yanlis"), Meta("p-reader")));
        Assert.Equal((409, WorkCollaborationProblemCodesV1.NoteNotOwn, true), (replayedRefusal.Status, replayedRefusal.Code, replayedRefusal.IsReplay));
        var passive = await harness.Service(Writer, WorkPermissionManifestV1.NotesWrite).PassivateNoteAsync(original.Note.Id, new("yerine yenisi yazildi"), Meta("p1"));
        Assert.NotNull(passive.Note.PassivatedAtUtc);
        var active = await harness.Service(Writer).ListNotesAsync("sales-order-draft", Draft, false);
        Assert.Equal(new[] { correction.Note.Id }, active.Items.Select(note => note.Id));
        var all = await harness.Service(Writer).ListNotesAsync("sales-order-draft", Draft, true);
        Assert.Equal(2, all.Items.Count);
        var again = await Assert.ThrowsAsync<WorkCollaborationException>(() => harness.Service(Writer, WorkPermissionManifestV1.NotesWrite)
            .PassivateNoteAsync(original.Note.Id, new("tekrar"), Meta("p2")));
        Assert.Equal(WorkCollaborationProblemCodesV1.NoteAlreadyPassive, again.Code);
    }

    [Fact]
    public async Task The_same_key_with_another_payload_is_a_409_mismatch()
    {
        var harness = new Harness();
        await harness.Service(Writer, WorkPermissionManifestV1.NotesWrite).AddNoteAsync("sales-order-draft", Draft, new("bir", null, null), Meta("same"));
        var error = await Assert.ThrowsAsync<WorkCollaborationException>(() => harness.Service(Writer, WorkPermissionManifestV1.NotesWrite)
            .AddNoteAsync("sales-order-draft", Draft, new("iki", null, null), Meta("same")));
        Assert.Equal((409, WorkCollaborationProblemCodesV1.SameKeyPayloadMismatch), (error.Status, error.Code));
    }

    [Fact]
    public async Task A_task_goes_only_to_a_member_shows_in_my_tasks_notifies_as_digest_and_closes_once_by_the_assignee()
    {
        var harness = new Harness();
        var refused = await Assert.ThrowsAsync<WorkCollaborationException>(() => harness.Service(Writer, WorkPermissionManifestV1.TasksAssign)
            .CreateTaskAsync(new("Fiyati kontrol et", null, NonMember, Now.AddDays(1), null, null), Meta("t0")));
        Assert.Equal((422, WorkCollaborationProblemCodesV1.AssigneeRefused), (refused.Status, refused.Code));
        var created = await harness.Service(Writer, WorkPermissionManifestV1.TasksAssign)
            .CreateTaskAsync(new("Fiyati kontrol et", "SENTETIK", Reader, Now.AddDays(1), "sales-order-draft", Draft), Meta("t1"));
        Assert.Equal(("open", "1", "Okur"), (created.Task.Status, created.Task.Version, created.Task.AssigneeDisplayName));
        var mine = await harness.Service(Reader).ListMyTasksAsync(false);
        Assert.Equal(new[] { created.Task.Id }, mine.Items.Select(task => task.Id));
        Assert.Empty((await harness.Service(Writer).ListMyTasksAsync(false)).Items);

        var notAssignee = await Assert.ThrowsAsync<WorkCollaborationException>(() => harness.Service(Writer, WorkPermissionManifestV1.TasksComplete)
            .CompleteTaskAsync(created.Task.Id, new("1"), Meta("c-writer")));
        Assert.Equal((409, WorkCollaborationProblemCodesV1.TaskNotAssignee), (notAssignee.Status, notAssignee.Code));
        var stale = await Assert.ThrowsAsync<WorkCollaborationException>(() => harness.Service(Reader, WorkPermissionManifestV1.TasksComplete)
            .CompleteTaskAsync(created.Task.Id, new("5"), Meta("c-stale")));
        Assert.Equal(WorkCollaborationProblemCodesV1.VersionConflict, stale.Code);
        var done = await harness.Service(Reader, WorkPermissionManifestV1.TasksComplete).CompleteTaskAsync(created.Task.Id, new("1"), Meta("c1"));
        Assert.Equal(("completed", "2"), (done.Task.Status, done.Task.Version));
        var replay = await harness.Service(Reader, WorkPermissionManifestV1.TasksComplete).CompleteTaskAsync(created.Task.Id, new("1"), Meta("c1"));
        Assert.True(replay.IsReplay);
        var cancelClosed = await Assert.ThrowsAsync<WorkCollaborationException>(() => harness.Service(Writer, WorkPermissionManifestV1.TasksAssign)
            .CancelTaskAsync(created.Task.Id, new("2", "gerek kalmadi"), Meta("x1")));
        Assert.Equal(WorkCollaborationProblemCodesV1.TaskNotOpen, cancelClosed.Code);
        Assert.Equal(404, (await Assert.ThrowsAsync<WorkCollaborationException>(() => harness.Service(Outsider, WorkPermissionManifestV1.TasksAssign)
            .CancelTaskAsync(created.Task.Id, new("2", "x"), Meta("x2")))).Status);

        await using var database = harness.Database();
        var notification = Assert.Single(await database.InboxNotifications.ToListAsync());
        Assert.Equal(((int)WorkInboxNotificationKindV1.TaskAssigned, (int)WorkInboxNotificationClassV1.Digest),
            (notification.Kind, notification.Class));
        Assert.Contains(harness.Audit.Events, item => item.EventType == "work.task.completed.v1" && item.EntityVersionAfter == 2);
    }

    [Fact]
    public async Task Digest_notifications_stay_held_without_a_schedule_and_are_released_once_at_the_slot_while_overdue_tasks_notify_now_once()
    {
        var harness = new Harness();
        var created = await harness.Service(Writer, WorkPermissionManifestV1.TasksAssign)
            .CreateTaskAsync(new("Sayim", null, Reader, Now.AddHours(1), null, null), Meta("d1"));
        Assert.Empty((await harness.Service(Reader).ListMyInboxAsync(null, false)).Items);

        harness.Time.Now = Now.AddHours(2);
        var held = await harness.Maintenance(WorkDigestScheduleV1.Unavailable(Company)).RunCycleAsync();
        Assert.Equal((1, 0, 0, false), (held.OverdueNotifications, held.DigestsReleased, held.DigestItems, held.ScheduleAvailable));
        var inbox = await harness.Service(Reader).ListMyInboxAsync(null, false);
        var overdue = Assert.Single(inbox.Items);
        Assert.Equal(("task-overdue", "now", created.Task.Id, 1, 0), (overdue.Kind, overdue.Class, overdue.SourceId, inbox.UnreadNowCount, inbox.UnreadDigestCount));

        var schedule = new WorkDigestScheduleV1(true, Company, [new TimeOnly(10, 30)], "UTC", "v1");
        var released = await harness.Maintenance(schedule).RunCycleAsync();
        Assert.Equal((0, 1, 1, true), (released.OverdueNotifications, released.DigestsReleased, released.DigestItems, released.ScheduleAvailable));
        var again = await harness.Maintenance(schedule).RunCycleAsync();
        Assert.Equal((0, 0, 0), (again.OverdueNotifications, again.DigestsReleased, again.DigestItems));
        var digestOnly = await harness.Service(Reader).ListMyInboxAsync("digest", false);
        var digest = Assert.Single(digestOnly.Items);
        Assert.Equal(("task-assigned", "digest"), (digest.Kind, digest.Class));

        var read = await harness.Service(Reader).MarkReadAsync(digest.Id);
        var readAgain = await harness.Service(Reader).MarkReadAsync(digest.Id);
        Assert.Equal(read.ReadAtUtc, readAgain.ReadAtUtc);
        Assert.Single((await harness.Service(Reader).ListMyInboxAsync(null, true)).Items);
        Assert.Equal(404, (await Assert.ThrowsAsync<WorkCollaborationException>(() => harness.Service(Writer).MarkReadAsync(digest.Id))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkCollaborationException>(() => harness.Service(Reader).ListMyInboxAsync("later", false))).Status);
    }

    private static WorkCommandMetadataV1 Meta(string key, string? correlationId = null) =>
        new(key, correlationId ?? "corr-" + key, null);

    private sealed class Harness
    {
        private readonly string name = Guid.NewGuid().ToString("N");
        public FakeRecords Records { get; } = new();
        public CollectingAudit Audit { get; } = new();
        public MutableTime Time { get; } = new() { Now = Now };

        public WorkDbContext Database()
        {
            var company = new CompanyContext();
            company.Set(Company);
            return new WorkDbContext(new DbContextOptionsBuilder<WorkDbContext>().UseInMemoryDatabase(name).Options, company);
        }

        public WorkCollaborationServiceV1 Service(Guid? actor, string? permission = null, Guid? allowedCompany = null)
        {
            var company = new CompanyContext();
            company.Set(Company);
            var user = new FakeUser(actor, allowedCompany ?? Company);
            Records.Actor = actor;
            return new WorkCollaborationServiceV1(new WorkDbContext(new DbContextOptionsBuilder<WorkDbContext>().UseInMemoryDatabase(name).Options, company),
                company, user, new FakeAuthority(permission), Records, new FakePeople(), Audit, new FakeCorrelation(), Time);
        }

        public WorkCollaborationMaintenanceV1 Maintenance(WorkDigestScheduleV1 schedule)
        {
            var company = new CompanyContext();
            company.Set(Company);
            return new WorkCollaborationMaintenanceV1(new WorkDbContext(new DbContextOptionsBuilder<WorkDbContext>().UseInMemoryDatabase(name).Options, company),
                company, new FixedSchedule(schedule), Time);
        }
    }

    private sealed class FakeRecords : IWorkRecordAccessPortV1
    {
        public Guid? Actor { get; set; }
        public bool Unavailable { get; set; }

        public Task<WorkRecordAccessOutcomeV1> CheckCurrentActorAsync(Guid companyId, WorkRecordRefV1 record, CancellationToken cancellationToken = default) =>
            Task.FromResult(Unavailable ? WorkRecordAccessOutcomeV1.Unavailable
                : companyId == Company && record.RecordId == Draft && Actor is { } actor && actor != Outsider
                    ? WorkRecordAccessOutcomeV1.Readable : WorkRecordAccessOutcomeV1.NotReadable);

        public Task<WorkRecordAccessOutcomeV1> CheckUserAsync(Guid companyId, WorkRecordRefV1 record, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(companyId == Company && (userId == Writer || userId == Reader)
                ? WorkRecordAccessOutcomeV1.Readable : WorkRecordAccessOutcomeV1.NotReadable);
    }

    private sealed class FakePeople : IWorkPersonDirectoryV1
    {
        private static readonly Dictionary<Guid, string> Names = new() { [Writer] = "Yazar", [Reader] = "Okur", [Outsider] = "Disarida" };

        public Task<WorkPeopleAnswerV1> ResolveAsync(Guid companyId, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkPeopleAnswerV1(WorkPersonOutcomeV1.Found,
                userIds.Where(Names.ContainsKey).Select(id => new WorkPersonV1(id, Names[id], true)).ToArray(), false));

        public Task<WorkPeopleAnswerV1> SearchAsync(Guid companyId, string? query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkPeopleAnswerV1(WorkPersonOutcomeV1.Found, Names.Select(pair => new WorkPersonV1(pair.Key, pair.Value, true)).ToArray(), false));

        public Task<WorkPersonOutcomeV1> IsActiveMemberAsync(Guid companyId, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(companyId == Company && Names.ContainsKey(userId) ? WorkPersonOutcomeV1.Found : WorkPersonOutcomeV1.Denied);
    }

    private sealed class FakeAuthority(string? granted) : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(permission == granted && companyId == Company
                ? new PermissionScope(true, true, new HashSet<string>(["SENTETIK_ROLE"]), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
    }

    private sealed class FakeUser(Guid? actor, Guid allowed) : ICurrentUser
    {
        public bool IsAuthenticated => actor is not null;
        public string? UserId => actor?.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([allowed]);
        public bool CanAccessCompany(Guid companyId) => companyId == allowed;
    }

    private sealed class FakeCorrelation : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => "corr-request";
        public string? CausationId => null;
        public string SourceChannel => "api";
        public string? ClientIpAddress => null;
        public string? UserAgent => null;
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent) { }
    }

    public sealed class CollectingAudit : IAuditEventCollector
    {
        public List<AuditEventDraft> Events { get; } = [];
        public void Enqueue(AuditEventDraft auditEvent) => Events.Add(auditEvent);
    }

    public sealed class MutableTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FixedSchedule(WorkDigestScheduleV1 schedule) : IWorkDigestScheduleReaderV1
    {
        public Task<WorkDigestScheduleV1> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(schedule);
    }
}

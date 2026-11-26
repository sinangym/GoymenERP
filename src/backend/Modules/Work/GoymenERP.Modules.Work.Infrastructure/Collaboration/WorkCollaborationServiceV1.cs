using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Work.Infrastructure.Collaboration;

/// <summary>
/// W1-C record notes, mentions, tasks and the personal collaboration inbox. Critical command skeleton (AGENTS.md): permission
/// fence (503 for an unregistered name, company-wide grant revalidated now), expected version on task transitions, the
/// idempotency key per actor with request-hash replay before any not-found or validation, correlation/causation in every row
/// and audit, refusals of state recorded so the same key replays them, and one SaveChanges per command. The notifications a
/// command causes are written in the same transaction as the command itself (no external effect leaves Work here, so there is
/// nothing to hand to the outbox). Every query names the server-resolved company; record access is the record owner's answer
/// through <see cref="IWorkRecordAccessPortV1"/>.
/// </summary>
public sealed class WorkCollaborationServiceV1(
    WorkDbContext database,
    ICompanyContext company,
    ICurrentUser user,
    IErpAuthorityRevalidationService authority,
    IWorkRecordAccessPortV1 records,
    IWorkPersonDirectoryV1 people,
    IAuditEventCollector audit,
    ICorrelationContext correlation,
    TimeProvider time) : IWorkCollaborationServiceV1
{
    private static readonly JsonSerializerOptions HashJson = new(JsonSerializerDefaults.Web);

    // ---- Notes ---------------------------------------------------------------------------------------------------------

    public async Task<WorkNotePageV1> ListNotesAsync(string recordType, Guid recordId, bool includePassive,
        CancellationToken cancellationToken = default)
    {
        var actor = RequireActor();
        var record = ParseRecord(recordType, recordId);
        await RequireRecordReadableAsync(actor, record, cancellationToken);
        var rows = await (from note in database.RecordNotes.AsNoTracking()
                join passive in database.RecordNotePassivations.AsNoTracking()
                    on new { note.CompanyId, NoteId = note.Id } equals new { passive.CompanyId, passive.NoteId } into passives
                from passive in passives.DefaultIfEmpty()
                where note.CompanyId == actor.CompanyId && note.RecordType == (int)record.Type && note.RecordId == record.RecordId
                    && (includePassive || passive == null)
                orderby note.CreatedAtUtc descending, note.Id
                select new { note, passive })
            .Take(WorkCollaborationLimitsV1.PageSize + 1).ToArrayAsync(cancellationToken);
        var page = rows.Take(WorkCollaborationLimitsV1.PageSize).ToArray();
        var ids = page.Select(row => row.note.Id).ToArray();
        var mentions = await database.RecordNoteMentions.AsNoTracking()
            .Where(row => row.CompanyId == actor.CompanyId && ids.Contains(row.NoteId)).ToArrayAsync(cancellationToken);
        var reads = await database.RecordNoteReads.AsNoTracking()
            .Where(row => row.CompanyId == actor.CompanyId && ids.Contains(row.NoteId)).ToArrayAsync(cancellationToken);
        var names = await NamesAsync(actor.CompanyId, page.Select(row => row.note.AuthorUserId).Concat(reads.Select(read => read.ReaderUserId)),
            cancellationToken);
        RequireStill(actor);
        var items = page.Select(row => View(row.note, names, mentions.Where(mention => mention.NoteId == row.note.Id)
            .Select(mention => mention.MentionedUserId).Order().ToArray(), row.passive,
            reads.Where(read => read.NoteId == row.note.Id).ToArray(), actor.ActorUserId)).ToArray();
        return new WorkNotePageV1(actor.CompanyId, WorkRecordTypesV1.Wire(record.Type), record.RecordId, items,
            rows.Length > WorkCollaborationLimitsV1.PageSize);
    }

    public async Task<WorkNoteResponseV1> AddNoteAsync(string recordType, Guid recordId, AddWorkNoteRequestV1 request,
        WorkCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequirePermissionAsync(WorkPermissionManifestV1.NotesWrite, cancellationToken);
        var mentioned = request.MentionedUserIds ?? [];
        var requestHash = Hash(WorkCollaborationCommandsV1.AddNote, recordType, recordId, request.Body,
            mentioned.Order().ToArray(), request.CorrectsNoteId);
        var replay = await FindReplayAsync(scope, metadata, WorkCollaborationCommandsV1.AddNote, requestHash, cancellationToken);
        if (replay is not null) return new(await NoteViewAsync(scope, replay.TargetId, cancellationToken), true, replay.CorrelationId);

        var record = ParseRecord(recordType, recordId);
        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > WorkCollaborationLimitsV1.NoteBodyMaximumLength
            || mentioned.Count > WorkCollaborationLimitsV1.MentionMaximumCount || mentioned.Any(id => id == Guid.Empty)
            || mentioned.Distinct().Count() != mentioned.Count || mentioned.Contains(scope.ActorUserId)
            || request.CorrectsNoteId == Guid.Empty)
            throw new WorkCollaborationException(422, WorkCollaborationProblemCodesV1.ValidationFailed);
        await RequireRecordReadableAsync(scope, record, cancellationToken);
        if (request.CorrectsNoteId is { } corrected && !await database.RecordNotes.AsNoTracking().AnyAsync(row =>
                row.CompanyId == scope.CompanyId && row.Id == corrected && row.RecordType == (int)record.Type
                && row.RecordId == record.RecordId, cancellationToken))
            throw new WorkCollaborationException(422, WorkCollaborationProblemCodesV1.CorrectedNoteInvalid);
        foreach (var mentionedUser in mentioned)
        {
            // AI-D20/W1-C: a person who cannot read the record is never told about it; the whole note is refused and the
            // answer does not say whether the person exists.
            if (!await IsMemberAsync(scope.CompanyId, mentionedUser, cancellationToken)
                || await CheckUserAsync(scope.CompanyId, record, mentionedUser, cancellationToken) != WorkRecordAccessOutcomeV1.Readable)
                throw new WorkCollaborationException(422, WorkCollaborationProblemCodesV1.MentionRefused);
        }

        await RequireStillPermissionAsync(scope, cancellationToken);
        var now = time.GetUtcNow();
        var note = new WorkRecordNoteRow
        {
            CompanyId = scope.CompanyId, Id = Guid.NewGuid(), RecordType = (int)record.Type, RecordId = record.RecordId,
            AuthorUserId = scope.ActorUserId, Body = request.Body, CorrectsNoteId = request.CorrectsNoteId, CreatedAtUtc = now,
            CorrelationId = metadata.CorrelationId, CausationId = metadata.CausationId,
        };
        database.RecordNotes.Add(note);
        foreach (var mentionedUser in mentioned.Order())
        {
            database.RecordNoteMentions.Add(new WorkRecordNoteMentionRow { CompanyId = scope.CompanyId, NoteId = note.Id, MentionedUserId = mentionedUser });
            database.InboxNotifications.Add(Notification(scope.CompanyId, mentionedUser, WorkInboxNotificationKindV1.Mention, note.Id,
                record, scope.ActorUserId, now, metadata));
        }

        AddAttempt(scope, metadata, WorkCollaborationCommandsV1.AddNote, requestHash, note.Id, 201, null, now);
        Audit(scope, metadata, "work.note.added.v1", "Work.RecordNote", note.Id, "AddNote", null, 1, "{}",
            JsonSerializer.Serialize(new { recordType = WorkRecordTypesV1.Wire(record.Type), record.RecordId, mentions = mentioned.Order(),
                correctsNoteId = request.CorrectsNoteId, bodyLength = request.Body.Length }, HashJson),
            "Kayda not eklendi.");
        var saved = await SaveAsync(scope, metadata, WorkCollaborationCommandsV1.AddNote, requestHash, cancellationToken);
        if (saved is not null) return new(await NoteViewAsync(scope, saved.TargetId, cancellationToken), true, saved.CorrelationId);
        return new(await NoteViewAsync(scope, note.Id, cancellationToken), false, metadata.CorrelationId);
    }

    public async Task<WorkNoteResponseV1> PassivateNoteAsync(Guid noteId, PassivateWorkNoteRequestV1 request,
        WorkCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequirePermissionAsync(WorkPermissionManifestV1.NotesWrite, cancellationToken);
        var requestHash = Hash(WorkCollaborationCommandsV1.PassivateNote, noteId, request.Reason);
        var replay = await FindReplayAsync(scope, metadata, WorkCollaborationCommandsV1.PassivateNote, requestHash, cancellationToken);
        if (replay is not null) return new(await NoteViewAsync(scope, replay.TargetId, cancellationToken), true, replay.CorrelationId);
        if (noteId == Guid.Empty) throw new WorkCollaborationException(404, "not_found");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > WorkCollaborationLimitsV1.ReasonMaximumLength)
            throw new WorkCollaborationException(422, WorkCollaborationProblemCodesV1.ValidationFailed);
        var note = await database.RecordNotes.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CompanyId == scope.CompanyId && row.Id == noteId, cancellationToken)
            ?? throw new WorkCollaborationException(404, "not_found");
        await RequireRecordReadableAsync(scope, new WorkRecordRefV1((WorkRecordTypeV1)note.RecordType, note.RecordId), cancellationToken);
        if (note.AuthorUserId != scope.ActorUserId)
            throw await TerminalAsync(scope, metadata, WorkCollaborationCommandsV1.PassivateNote, requestHash, noteId,
                WorkCollaborationProblemCodesV1.NoteNotOwn, cancellationToken);
        if (await database.RecordNotePassivations.AsNoTracking().AnyAsync(row => row.CompanyId == scope.CompanyId && row.NoteId == noteId, cancellationToken))
            throw await TerminalAsync(scope, metadata, WorkCollaborationCommandsV1.PassivateNote, requestHash, noteId,
                WorkCollaborationProblemCodesV1.NoteAlreadyPassive, cancellationToken);
        await RequireStillPermissionAsync(scope, cancellationToken);
        var now = time.GetUtcNow();
        database.RecordNotePassivations.Add(new WorkRecordNotePassivationRow
        {
            CompanyId = scope.CompanyId, NoteId = noteId, PassivatedByUserId = scope.ActorUserId, PassivatedAtUtc = now, Reason = request.Reason,
        });
        AddAttempt(scope, metadata, WorkCollaborationCommandsV1.PassivateNote, requestHash, noteId, 200, null, now);
        Audit(scope, metadata, "work.note.passivated.v1", "Work.RecordNote", noteId, "PassivateNote", 1, 2,
            JsonSerializer.Serialize(new { passive = false }, HashJson), JsonSerializer.Serialize(new { passive = true }, HashJson), request.Reason);
        var saved = await SaveAsync(scope, metadata, WorkCollaborationCommandsV1.PassivateNote, requestHash, cancellationToken);
        if (saved is not null) return new(await NoteViewAsync(scope, saved.TargetId, cancellationToken), true, saved.CorrelationId);
        return new(await NoteViewAsync(scope, noteId, cancellationToken), false, metadata.CorrelationId);
    }

    /// <summary>
    /// WP9 read receipt. Not a critical command (it changes no business state and causes no external effect): it is
    /// idempotent by its key (note, reader) — the first read wins and every later call answers that first time — so it takes
    /// no Idempotency-Key. Whoever can read the record can mark its note read; the author's own note is answered 409.
    /// </summary>
    public async Task<WorkNoteReadResponseV1> MarkNoteReadAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        var actor = RequireActor();
        if (noteId == Guid.Empty) throw new WorkCollaborationException(404, "not_found");
        var note = await database.RecordNotes.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CompanyId == actor.CompanyId && row.Id == noteId, cancellationToken)
            ?? throw new WorkCollaborationException(404, "not_found");
        await RequireRecordReadableAsync(actor, new WorkRecordRefV1((WorkRecordTypeV1)note.RecordType, note.RecordId), cancellationToken);
        await RequireMemberAsync(actor, cancellationToken);
        if (note.AuthorUserId == actor.ActorUserId) throw new WorkCollaborationException(409, WorkCollaborationProblemCodesV1.NoteOwnRead);
        var existing = await database.RecordNoteReads.AsNoTracking().SingleOrDefaultAsync(row =>
            row.CompanyId == actor.CompanyId && row.NoteId == noteId && row.ReaderUserId == actor.ActorUserId, cancellationToken);
        if (existing is not null) return new(noteId, existing.ReadAtUtc);
        RequireStill(actor);
        var now = time.GetUtcNow();
        database.RecordNoteReads.Add(new WorkRecordNoteReadRow { CompanyId = actor.CompanyId, NoteId = noteId, ReaderUserId = actor.ActorUserId, ReadAtUtc = now });
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            var winner = await database.RecordNoteReads.AsNoTracking().SingleOrDefaultAsync(row =>
                row.CompanyId == actor.CompanyId && row.NoteId == noteId && row.ReaderUserId == actor.ActorUserId, cancellationToken);
            if (winner is null) throw new WorkCollaborationException(503, "service_unavailable");
            return new(noteId, winner.ReadAtUtc);
        }
        finally { database.ChangeTracker.Clear(); }

        return new(noteId, now);
    }

    // ---- Tasks ---------------------------------------------------------------------------------------------------------

    public async Task<WorkTaskPageV1> ListMyTasksAsync(bool includeClosed, CancellationToken cancellationToken = default)
    {
        var actor = RequireActor();
        await RequireMemberAsync(actor, cancellationToken);
        var rows = await database.Tasks.AsNoTracking()
            .Where(row => row.CompanyId == actor.CompanyId && row.AssigneeUserId == actor.ActorUserId
                && (includeClosed || row.Status == (int)WorkTaskStatusV1.Open))
            .OrderBy(row => row.Status).ThenBy(row => row.DueAtUtc).ThenBy(row => row.Id)
            .Take(WorkCollaborationLimitsV1.PageSize + 1).ToArrayAsync(cancellationToken);
        return await TaskPageAsync(actor, rows, cancellationToken);
    }

    public async Task<WorkTaskPageV1> ListRecordTasksAsync(string recordType, Guid recordId, CancellationToken cancellationToken = default)
    {
        var actor = RequireActor();
        var record = ParseRecord(recordType, recordId);
        await RequireRecordReadableAsync(actor, record, cancellationToken);
        var rows = await database.Tasks.AsNoTracking()
            .Where(row => row.CompanyId == actor.CompanyId && row.RecordType == (int)record.Type && row.RecordId == record.RecordId)
            .OrderByDescending(row => row.CreatedAtUtc).ThenBy(row => row.Id)
            .Take(WorkCollaborationLimitsV1.PageSize + 1).ToArrayAsync(cancellationToken);
        return await TaskPageAsync(actor, rows, cancellationToken);
    }

    public async Task<WorkTaskResponseV1> CreateTaskAsync(CreateWorkTaskRequestV1 request, WorkCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequirePermissionAsync(WorkPermissionManifestV1.TasksAssign, cancellationToken);
        var requestHash = Hash(WorkCollaborationCommandsV1.CreateTask, request.Title, request.Details, request.AssigneeUserId,
            request.DueAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), request.RecordType, request.RecordId);
        var replay = await FindReplayAsync(scope, metadata, WorkCollaborationCommandsV1.CreateTask, requestHash, cancellationToken);
        if (replay is not null) return new(await TaskViewAsync(scope, replay.TargetId, cancellationToken), true, replay.CorrelationId);

        var now = time.GetUtcNow();
        WorkRecordRefV1? record = null;
        if (request.RecordType is not null || request.RecordId is not null)
        {
            if (request.RecordType is null || request.RecordId is not { } boundId)
                throw new WorkCollaborationException(422, WorkCollaborationProblemCodesV1.ValidationFailed);
            record = ParseRecord(request.RecordType, boundId);
        }

        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > WorkCollaborationLimitsV1.TaskTitleMaximumLength
            || request.Details?.Length > WorkCollaborationLimitsV1.TaskDetailsMaximumLength
            || (request.Details is not null && string.IsNullOrWhiteSpace(request.Details))
            || request.AssigneeUserId == Guid.Empty || request.DueAtUtc <= now)
            throw new WorkCollaborationException(422, WorkCollaborationProblemCodesV1.ValidationFailed);
        if (record is not null) await RequireRecordReadableAsync(scope, record, cancellationToken);
        // W1-C: the assignee is only an active member of the same company and, for a bound task, can read the record.
        if (!await IsMemberAsync(scope.CompanyId, request.AssigneeUserId, cancellationToken)
            || (record is not null && request.AssigneeUserId != scope.ActorUserId
                && await CheckUserAsync(scope.CompanyId, record, request.AssigneeUserId, cancellationToken) != WorkRecordAccessOutcomeV1.Readable))
            throw new WorkCollaborationException(422, WorkCollaborationProblemCodesV1.AssigneeRefused);

        await RequireStillPermissionAsync(scope, cancellationToken);
        var task = new WorkTaskRow
        {
            CompanyId = scope.CompanyId, Id = Guid.NewGuid(), RecordType = record is null ? null : (int)record.Type, RecordId = record?.RecordId,
            Title = request.Title, Details = request.Details, AssignerUserId = scope.ActorUserId, AssigneeUserId = request.AssigneeUserId,
            DueAtUtc = request.DueAtUtc.ToUniversalTime(), Status = (int)WorkTaskStatusV1.Open, Version = 1, CreatedAtUtc = now,
        };
        database.Tasks.Add(task);
        if (request.AssigneeUserId != scope.ActorUserId)
            database.InboxNotifications.Add(Notification(scope.CompanyId, request.AssigneeUserId, WorkInboxNotificationKindV1.TaskAssigned,
                task.Id, record, scope.ActorUserId, now, metadata));
        AddAttempt(scope, metadata, WorkCollaborationCommandsV1.CreateTask, requestHash, task.Id, 201, null, now);
        Audit(scope, metadata, "work.task.created.v1", "Work.Task", task.Id, "CreateTask", null, 1, "{}", TaskAudit(task), "Görev atandı.");
        var saved = await SaveAsync(scope, metadata, WorkCollaborationCommandsV1.CreateTask, requestHash, cancellationToken);
        if (saved is not null) return new(await TaskViewAsync(scope, saved.TargetId, cancellationToken), true, saved.CorrelationId);
        return new(await TaskViewAsync(scope, task.Id, cancellationToken), false, metadata.CorrelationId);
    }

    public Task<WorkTaskResponseV1> CompleteTaskAsync(Guid taskId, CompleteWorkTaskRequestV1 request, WorkCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return TransitionAsync(taskId, request.ExpectedVersion, null, WorkPermissionManifestV1.TasksComplete,
            WorkCollaborationCommandsV1.CompleteTask, "work.task.completed.v1", "CompleteTask", "Görev tamamlandı.",
            (task, version, actor, now) => task.Complete(version, actor, now), metadata, cancellationToken);
    }

    public Task<WorkTaskResponseV1> CancelTaskAsync(Guid taskId, CancelWorkTaskRequestV1 request, WorkCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return TransitionAsync(taskId, request.ExpectedVersion, request.Reason ?? "", WorkPermissionManifestV1.TasksAssign,
            WorkCollaborationCommandsV1.CancelTask, "work.task.cancelled.v1", "CancelTask", request.Reason ?? "",
            (task, version, actor, now) => task.Cancel(version, actor, request.Reason!, now), metadata, cancellationToken);
    }

    private async Task<WorkTaskResponseV1> TransitionAsync(Guid taskId, string? expectedVersion, string? reason, string permission,
        string command, string eventType, string operation, string auditReason, Action<WorkTaskV1, long, Guid, DateTimeOffset> mutate,
        WorkCommandMetadataV1 metadata, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequirePermissionAsync(permission, cancellationToken);
        var version = ParseVersion(expectedVersion);
        var requestHash = reason is null ? Hash(command, taskId, expectedVersion) : Hash(command, taskId, expectedVersion, reason);
        var replay = await FindReplayAsync(scope, metadata, command, requestHash, cancellationToken);
        if (replay is not null) return new(await TaskViewAsync(scope, replay.TargetId, cancellationToken), true, replay.CorrelationId);
        if (taskId == Guid.Empty) throw new WorkCollaborationException(404, "not_found");
        if (reason is not null && (string.IsNullOrWhiteSpace(reason) || reason.Length > WorkCollaborationLimitsV1.ReasonMaximumLength))
            throw new WorkCollaborationException(422, WorkCollaborationProblemCodesV1.ValidationFailed);
        var row = await database.Tasks.SingleOrDefaultAsync(item => item.CompanyId == scope.CompanyId && item.Id == taskId, cancellationToken);
        // A task is visible only to the two people on it; anyone else gets the same answer as for no task at all.
        if (row is null || (row.AssigneeUserId != scope.ActorUserId && row.AssignerUserId != scope.ActorUserId))
            throw new WorkCollaborationException(404, "not_found");
        var task = WorkTaskV1.Restore(row.AssignerUserId, row.AssigneeUserId, (WorkTaskStatusV1)row.Status, row.Version);
        var before = TaskAudit(row);
        var now = time.GetUtcNow();
        try { mutate(task, version, scope.ActorUserId, now); }
        catch (WorkTaskRuleException rule)
        {
            throw await TerminalAsync(scope, metadata, command, requestHash, taskId, rule.Code, cancellationToken);
        }

        if (task.Version != row.Version + 1) throw new InvalidOperationException("A task transition must move exactly one version.");
        await RequireStillPermissionAsync(scope, cancellationToken);
        row.Status = (int)task.Status;
        row.Version = task.Version;
        row.ClosedAtUtc = task.ClosedAtUtc;
        row.ClosedByUserId = task.ClosedByUserId;
        row.CancelReason = task.CancelReason;
        AddAttempt(scope, metadata, command, requestHash, taskId, 200, null, now);
        Audit(scope, metadata, eventType, "Work.Task", taskId, operation, task.Version - 1, task.Version, before, TaskAudit(row), auditReason);
        var saved = await SaveAsync(scope, metadata, command, requestHash, cancellationToken);
        if (saved is not null) return new(await TaskViewAsync(scope, saved.TargetId, cancellationToken), true, saved.CorrelationId);
        return new(await TaskViewAsync(scope, taskId, cancellationToken), false, metadata.CorrelationId);
    }

    // ---- Inbox (AI-D29) -----------------------------------------------------------------------------------------------

    public async Task<WorkInboxNotificationPageV1> ListMyInboxAsync(string? notificationClass, bool unreadOnly,
        CancellationToken cancellationToken = default)
    {
        var actor = RequireActor();
        int? classFilter = notificationClass switch
        {
            null => null,
            "now" => (int)WorkInboxNotificationClassV1.Now,
            "digest" => (int)WorkInboxNotificationClassV1.Digest,
            _ => throw new WorkCollaborationException(400, WorkCollaborationProblemCodesV1.ValidationFailed),
        };
        await RequireMemberAsync(actor, cancellationToken);
        var visible = Visible(actor);
        var counts = await visible.Where(row => row.ReadAtUtc == null).GroupBy(row => row.Class)
            .Select(group => new { Class = group.Key, Count = group.Count() }).ToArrayAsync(cancellationToken);
        var filtered = visible.Where(row => (classFilter == null || row.Class == classFilter) && (!unreadOnly || row.ReadAtUtc == null));
        var rows = await filtered.OrderByDescending(row => row.VisibleAtUtc).ThenBy(row => row.Id)
            .Take(WorkCollaborationLimitsV1.PageSize + 1).ToArrayAsync(cancellationToken);
        // WP2: an approval notification names its follow-up row (its source id); the row carries the owner's label and link.
        var followUpIds = rows.Where(row => row.Kind >= (int)WorkInboxNotificationKindV1.ApprovalWaiting).Select(row => row.SourceId)
            .Distinct().ToArray();
        var followUps = followUpIds.Length == 0
            ? new Dictionary<Guid, WorkApprovalFollowUpRow>()
            : await database.ApprovalFollowUps.AsNoTracking()
                .Where(row => row.CompanyId == actor.CompanyId && followUpIds.Contains(row.Id))
                .ToDictionaryAsync(row => row.Id, cancellationToken);
        RequireStill(actor);
        var items = rows.Take(WorkCollaborationLimitsV1.PageSize).Select(row => new WorkInboxNotificationViewV1(row.Id, row.CompanyId,
            WorkInboxNotificationClassesV1.Wire((WorkInboxNotificationKindV1)row.Kind),
            WorkInboxNotificationClassesV1.Wire((WorkInboxNotificationClassV1)row.Class), row.SourceId,
            row.RecordType is { } type ? WorkRecordTypesV1.Wire((WorkRecordTypeV1)type) : null, row.RecordId,
            row.ActorUserId, row.RecordedAtUtc, row.VisibleAtUtc, row.ReadAtUtc,
            followUps.TryGetValue(row.SourceId, out var followUp) && row.Kind >= (int)WorkInboxNotificationKindV1.ApprovalWaiting
                ? new WorkInboxApprovalRefV1(followUp.Kind, followUp.ItemId, followUp.DocumentLabel, followUp.Link,
                    followUp.CycleStartedAtUtc, WorkApprovalFollowUpStepsV1.Wire(followUp.Step), followUp.DueAtUtc)
                : null,
            row.EventId is not null
                ? new WorkInboxEventRefV1(row.SourceEntityId!.Value, row.SourceVersion!, row.ReasonCode!, row.ReasonText!,
                    row.OccurredAtUtc!.Value, row.SalesOrderId, row.SalesOrderLineId, row.PurchaseOrderId, row.PurchaseOrderLineId, row.CostKind)
                : null)).ToArray();
        return new WorkInboxNotificationPageV1(actor.CompanyId, items, rows.Length > WorkCollaborationLimitsV1.PageSize,
            counts.Where(count => count.Class == (int)WorkInboxNotificationClassV1.Now).Sum(count => count.Count),
            counts.Where(count => count.Class == (int)WorkInboxNotificationClassV1.Digest).Sum(count => count.Count));
    }

    public async Task<WorkInboxNotificationReadResponseV1> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        var actor = RequireActor();
        await RequireMemberAsync(actor, cancellationToken);
        var found = await Visible(actor).Where(row => row.Id == notificationId)
            .Select(row => new { row.Id, row.ReadAtUtc }).SingleOrDefaultAsync(cancellationToken)
            ?? throw new WorkCollaborationException(404, "not_found");
        if (found.ReadAtUtc is { } already) return new(found.Id, already);
        RequireStill(actor);
        var now = time.GetUtcNow();
        database.InboxNotificationReads.Add(new WorkInboxNotificationReadRow { CompanyId = actor.CompanyId, NotificationId = found.Id, ReadAtUtc = now });
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            var winner = await database.InboxNotificationReads.AsNoTracking()
                .SingleOrDefaultAsync(row => row.CompanyId == actor.CompanyId && row.NotificationId == found.Id, cancellationToken);
            if (winner is null) throw new WorkCollaborationException(503, "service_unavailable");
            return new(found.Id, winner.ReadAtUtc);
        }
        finally { database.ChangeTracker.Clear(); }

        return new(found.Id, now);
    }

    /// <summary>
    /// WP9 / D7: the picker answers only a caller who holds <c>work.tasks.assign</c> or <c>work.notes.write</c> company-wide
    /// now (the two commands that name a person); anyone else gets 403 before the directory is asked. The answer carries the id
    /// and display name of active members of the resolved company and nothing else.
    /// </summary>
    public async Task<WorkPeoplePageV1> SearchPeopleAsync(string? query, CancellationToken cancellationToken = default)
    {
        var actor = RequireActor();
        await RequireAnyPermissionAsync(actor, [WorkPermissionManifestV1.TasksAssign, WorkPermissionManifestV1.NotesWrite], cancellationToken);
        var trimmed = query?.Trim();
        if (trimmed?.Length > WorkCollaborationLimitsV1.PeopleQueryMaximumLength)
            throw new WorkCollaborationException(400, WorkCollaborationProblemCodesV1.ValidationFailed);
        var answer = await people.SearchAsync(actor.CompanyId, string.IsNullOrEmpty(trimmed) ? null : trimmed, cancellationToken);
        RequireStill(actor);
        return answer.Outcome switch
        {
            WorkPersonOutcomeV1.Found => new WorkPeoplePageV1(actor.CompanyId,
                answer.People.Where(person => person.IsActiveInCompany && person.UserId != Guid.Empty)
                    .Select(person => new WorkPersonSummaryV1(person.UserId, person.DisplayName)).ToArray(), answer.HasMore),
            WorkPersonOutcomeV1.Denied => throw new WorkCollaborationException(403, "forbidden"),
            _ => throw new WorkCollaborationException(503, "service_unavailable"),
        };
    }

    // ---- Shared skeleton -----------------------------------------------------------------------------------------------

    private sealed record Actor(Guid CompanyId, string ActorId, Guid ActorUserId);

    private sealed record Scope(Guid CompanyId, string ActorId, Guid ActorUserId, string Permission, IReadOnlyList<string> RoleCodes)
    {
        public Actor AsActor => new(CompanyId, ActorId, ActorUserId);
        public static implicit operator Actor(Scope scope) => scope.AsActor;
    }

    private sealed record Replay(Guid TargetId, string CorrelationId);

    private sealed class VisibleRow
    {
        public Guid Id { get; init; }
        public Guid CompanyId { get; init; }
        public int Kind { get; init; }
        public int Class { get; init; }
        public Guid SourceId { get; init; }
        public int? RecordType { get; init; }
        public Guid? RecordId { get; init; }
        public Guid? ActorUserId { get; init; }
        public DateTimeOffset RecordedAtUtc { get; init; }
        public DateTimeOffset VisibleAtUtc { get; init; }
        public DateTimeOffset? ReadAtUtc { get; init; }
        public Guid? EventId { get; init; }
        public Guid? SourceEntityId { get; init; }
        public string? SourceVersion { get; init; }
        public string? ReasonCode { get; init; }
        public string? ReasonText { get; init; }
        public DateTimeOffset? OccurredAtUtc { get; init; }
        public Guid? SalesOrderId { get; init; }
        public Guid? SalesOrderLineId { get; init; }
        public Guid? PurchaseOrderId { get; init; }
        public Guid? PurchaseOrderLineId { get; init; }
        public int? CostKind { get; init; }
    }

    private IQueryable<VisibleRow> Visible(Actor actor)
    {
        var released = from link in database.InboxDigestItems.AsNoTracking()
            join digest in database.InboxDigests.AsNoTracking() on new { link.CompanyId, Id = link.DigestId }
                equals new { digest.CompanyId, digest.Id }
            where link.CompanyId == actor.CompanyId
            select new { link.CompanyId, link.NotificationId, ReleasedAtUtc = (DateTimeOffset?)digest.ReleasedAtUtc };
        return from item in database.InboxNotifications.AsNoTracking()
            join release in released on new { item.CompanyId, NotificationId = item.Id }
                equals new { release.CompanyId, release.NotificationId } into releases
            from release in releases.DefaultIfEmpty()
            join read in database.InboxNotificationReads.AsNoTracking() on new { item.CompanyId, NotificationId = item.Id }
                equals new { read.CompanyId, read.NotificationId } into reads
            from read in reads.DefaultIfEmpty()
            where item.CompanyId == actor.CompanyId && item.RecipientUserId == actor.ActorUserId
                && (item.Class == (int)WorkInboxNotificationClassV1.Now || release.ReleasedAtUtc != null)
            select new VisibleRow
            {
                Id = item.Id, CompanyId = item.CompanyId, Kind = item.Kind, Class = item.Class, SourceId = item.SourceId,
                RecordType = item.RecordType, RecordId = item.RecordId, ActorUserId = item.ActorUserId, RecordedAtUtc = item.RecordedAtUtc,
                VisibleAtUtc = item.Class == (int)WorkInboxNotificationClassV1.Now ? item.RecordedAtUtc : release.ReleasedAtUtc ?? item.RecordedAtUtc,
                ReadAtUtc = (DateTimeOffset?)read.ReadAtUtc,
                EventId = item.EventId, SourceEntityId = item.SourceEntityId, SourceVersion = item.SourceVersion,
                ReasonCode = item.ReasonCode, ReasonText = item.ReasonText, OccurredAtUtc = item.OccurredAtUtc,
                SalesOrderId = item.SalesOrderId, SalesOrderLineId = item.SalesOrderLineId,
                PurchaseOrderId = item.PurchaseOrderId, PurchaseOrderLineId = item.PurchaseOrderLineId, CostKind = item.CostKind,
            };
    }

    private Actor RequireActor()
    {
        if (!user.IsAuthenticated || !Guid.TryParse(user.UserId, out var actorUserId) || actorUserId == Guid.Empty)
            throw new WorkCollaborationException(401, "unauthenticated");
        if (!company.IsResolved || company.CompanyId == Guid.Empty || !user.CanAccessCompany(company.CompanyId))
            throw new WorkCollaborationException(403, "forbidden");
        return new Actor(company.CompanyId, user.UserId!, actorUserId);
    }

    private void RequireStill(Actor actor)
    {
        if (!user.IsAuthenticated || user.UserId != actor.ActorId || !company.IsResolved || company.CompanyId != actor.CompanyId
            || !user.CanAccessCompany(actor.CompanyId))
            throw new WorkCollaborationException(403, "forbidden");
    }

    private async Task<Scope> RequirePermissionAsync(string permission, CancellationToken cancellationToken)
    {
        var actor = RequireActor();
        if (!ErpPermissions.IsKnown(permission))
            throw new WorkCollaborationException(503, WorkCollaborationProblemCodesV1.PermissionUnregistered);
        var granted = await authority.RevalidatePermissionScopeAsync(permission, actor.CompanyId, cancellationToken);
        RequireStill(actor);
        if (granted is not { IsGranted: true, IsCompanyWide: true }) throw new WorkCollaborationException(403, "forbidden");
        return new Scope(actor.CompanyId, actor.ActorId, actor.ActorUserId, permission,
            granted.RoleCodes.Order(StringComparer.Ordinal).ToArray());
    }

    private async Task RequireAnyPermissionAsync(Actor actor, IReadOnlyList<string> permissions, CancellationToken cancellationToken)
    {
        foreach (var permission in permissions)
        {
            if (!ErpPermissions.IsKnown(permission))
                throw new WorkCollaborationException(503, WorkCollaborationProblemCodesV1.PermissionUnregistered);
        }

        foreach (var permission in permissions)
        {
            var granted = await authority.RevalidatePermissionScopeAsync(permission, actor.CompanyId, cancellationToken);
            RequireStill(actor);
            if (granted is { IsGranted: true, IsCompanyWide: true }) return;
        }

        throw new WorkCollaborationException(403, "forbidden");
    }

    private async Task RequireStillPermissionAsync(Scope scope, CancellationToken cancellationToken)
    {
        RequireStill(scope);
        var granted = await authority.RevalidatePermissionScopeAsync(scope.Permission, scope.CompanyId, cancellationToken);
        RequireStill(scope);
        if (granted is not { IsGranted: true, IsCompanyWide: true }) throw new WorkCollaborationException(403, "forbidden");
    }

    private async Task RequireMemberAsync(Actor actor, CancellationToken cancellationToken)
    {
        if (!await IsMemberAsync(actor.CompanyId, actor.ActorUserId, cancellationToken)) throw new WorkCollaborationException(403, "forbidden");
        RequireStill(actor);
    }

    private async Task<bool> IsMemberAsync(Guid companyId, Guid userId, CancellationToken cancellationToken) =>
        await people.IsActiveMemberAsync(companyId, userId, cancellationToken) switch
        {
            WorkPersonOutcomeV1.Found => true,
            WorkPersonOutcomeV1.Denied => false,
            _ => throw new WorkCollaborationException(503, "service_unavailable"),
        };

    private async Task RequireRecordReadableAsync(Actor actor, WorkRecordRefV1 record, CancellationToken cancellationToken)
    {
        var outcome = await records.CheckCurrentActorAsync(actor.CompanyId, record, cancellationToken);
        RequireStill(actor);
        if (outcome == WorkRecordAccessOutcomeV1.Unavailable) throw new WorkCollaborationException(503, "service_unavailable");
        if (outcome != WorkRecordAccessOutcomeV1.Readable) throw new WorkCollaborationException(404, "not_found");
    }

    private async Task<WorkRecordAccessOutcomeV1> CheckUserAsync(Guid companyId, WorkRecordRefV1 record, Guid userId,
        CancellationToken cancellationToken)
    {
        var outcome = await records.CheckUserAsync(companyId, record, userId, cancellationToken);
        if (outcome == WorkRecordAccessOutcomeV1.Unavailable) throw new WorkCollaborationException(503, "service_unavailable");
        return outcome;
    }

    private static WorkRecordRefV1 ParseRecord(string? recordType, Guid recordId) =>
        WorkRecordTypesV1.TryParse(recordType, out var type) && recordId != Guid.Empty
            ? new WorkRecordRefV1(type, recordId)
            : throw new WorkCollaborationException(404, "not_found");

    private static long ParseVersion(string? value) =>
        value is not null && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version) && version >= 1
            && value == version.ToString(CultureInfo.InvariantCulture)
            ? version
            : throw new WorkCollaborationException(400, WorkCollaborationProblemCodesV1.ValidationFailed);

    private static string Hash(params object?[] parts) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(parts, HashJson))));

    private static string KeyHash(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    private async Task<Replay?> FindReplayAsync(Scope scope, WorkCommandMetadataV1 metadata, string command, string requestHash,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(metadata.IdempotencyKey) || metadata.IdempotencyKey.Length > 128
            || string.IsNullOrWhiteSpace(metadata.CorrelationId) || metadata.CorrelationId.Length > 128 || metadata.CausationId?.Length > 128)
            throw new WorkCollaborationException(400, WorkCollaborationProblemCodesV1.CommandMetadataInvalid);
        var keyHash = KeyHash(metadata.IdempotencyKey);
        var attempt = await database.CollaborationAttempts.AsNoTracking().SingleOrDefaultAsync(row =>
            row.CompanyId == scope.CompanyId && row.ActorUserId == scope.ActorUserId && row.IdempotencyKeySha256 == keyHash, cancellationToken);
        RequireStill(scope);
        if (attempt is null) return null;
        if (attempt.Command != command || attempt.RequestSha256 != requestHash)
            throw new WorkCollaborationException(409, WorkCollaborationProblemCodesV1.SameKeyPayloadMismatch);
        if (attempt.Status == 409)
            throw new WorkCollaborationException(409, attempt.RefusalCode!, isReplay: true, attempt.CorrelationId);
        return new Replay(attempt.TargetId, attempt.CorrelationId);
    }

    private void AddAttempt(Scope scope, WorkCommandMetadataV1 metadata, string command, string requestHash, Guid targetId, int status,
        string? refusalCode, DateTimeOffset now) => database.CollaborationAttempts.Add(new WorkCollaborationAttemptRow
        {
            CompanyId = scope.CompanyId, ActorUserId = scope.ActorUserId, IdempotencyKeySha256 = KeyHash(metadata.IdempotencyKey),
            Command = command, RequestSha256 = requestHash, TargetId = targetId, Status = status, RefusalCode = refusalCode,
            CorrelationId = metadata.CorrelationId, RecordedAtUtc = now,
        });

    /// <summary>A refusal of state is durable: recorded with its key, then thrown; a lost race answers with the winner.</summary>
    private async Task<WorkCollaborationException> TerminalAsync(Scope scope, WorkCommandMetadataV1 metadata, string command,
        string requestHash, Guid targetId, string code, CancellationToken cancellationToken)
    {
        RequireStill(scope);
        database.ChangeTracker.Clear();
        AddAttempt(scope, metadata, command, requestHash, targetId, 409, code, time.GetUtcNow());
        // SaveAsync answers a lost race with the winner's stored answer; a winner that succeeded is not this refusal.
        var saved = await SaveAsync(scope, metadata, command, requestHash, cancellationToken);
        return saved is null ? new WorkCollaborationException(409, code)
            : new WorkCollaborationException(409, WorkCollaborationProblemCodesV1.ConcurrentChange);
    }

    /// <summary>
    /// One SaveChanges in one transaction. A lost race on the attempt key or the task version is answered with the winner's
    /// stored answer (replay) when the same key won, otherwise 409 <c>concurrent_change</c>. Returns the replay or null.
    /// </summary>
    private async Task<Replay?> SaveAsync(Scope scope, WorkCommandMetadataV1 metadata, string command, string requestHash,
        CancellationToken cancellationToken)
    {
        RequireStill(scope);
        try
        {
            if (database.Database.IsRelational())
            {
                await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                await database.SaveChangesAsync(cancellationToken);
                RequireStill(scope);
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await database.SaveChangesAsync(cancellationToken);
            }

            return null;
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            var replay = await FindReplayAsync(scope, metadata, command, requestHash, cancellationToken);
            return replay ?? throw new WorkCollaborationException(409, WorkCollaborationProblemCodesV1.ConcurrentChange);
        }
        finally
        {
            database.ChangeTracker.Clear();
        }
    }

    private void Audit(Scope scope, WorkCommandMetadataV1 metadata, string eventType, string entityType, Guid entityId, string operation,
        long? before, long? after, string beforeJson, string afterJson, string reason) =>
        audit.Enqueue(new AuditEventDraft(scope.CompanyId, eventType, entityType, entityId.ToString("D"), operation, before, after,
            AuditActorTypes.User, scope.ActorId, scope.Permission, scope.RoleCodes, beforeJson, afterJson, reason, null,
            AuditAiInvolvement.None, null, correlation.IsResolved ? correlation.SourceChannel : "api",
            correlation.IsResolved ? correlation.ClientIpAddress : null, correlation.IsResolved ? correlation.UserAgent : null,
            metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));

    private static WorkInboxNotificationRow Notification(Guid companyId, Guid recipient, WorkInboxNotificationKindV1 kind, Guid sourceId,
        WorkRecordRefV1? record, Guid? actor, DateTimeOffset now, WorkCommandMetadataV1 metadata) => new()
        {
            CompanyId = companyId, Id = Guid.NewGuid(), RecipientUserId = recipient, Kind = (int)kind,
            Class = (int)WorkInboxNotificationClassesV1.Of(kind), SourceId = sourceId, RecordType = record is null ? null : (int)record.Type,
            RecordId = record?.RecordId, ActorUserId = actor, RecordedAtUtc = now, CorrelationId = metadata.CorrelationId,
            CausationId = metadata.CausationId,
        };

    private static string TaskAudit(WorkTaskRow task) => JsonSerializer.Serialize(new
    {
        task.Id, task.RecordType, task.RecordId, task.AssignerUserId, task.AssigneeUserId, task.DueAtUtc, task.Status, task.Version,
        task.ClosedAtUtc, task.ClosedByUserId, task.CancelReason, titleLength = task.Title.Length,
    }, HashJson);

    private async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(Guid companyId, IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var distinct = ids.Distinct().ToArray();
        if (distinct.Length == 0) return new Dictionary<Guid, string>();
        var answer = await people.ResolveAsync(companyId, distinct, cancellationToken);
        if (answer.Outcome != WorkPersonOutcomeV1.Found) throw new WorkCollaborationException(503, "service_unavailable");
        // A person the directory no longer answers keeps an id, never a guessed name.
        return answer.People.Where(person => distinct.Contains(person.UserId))
            .GroupBy(person => person.UserId).ToDictionary(group => group.Key, group => group.First().DisplayName);
    }

    private static WorkNoteViewV1 View(WorkRecordNoteRow note, IReadOnlyDictionary<Guid, string> names, IReadOnlyList<Guid> mentions,
        WorkRecordNotePassivationRow? passive, IReadOnlyList<WorkRecordNoteReadRow> reads, Guid viewer)
    {
        string Name(Guid id) => names.TryGetValue(id, out var name) ? name : id.ToString("D");
        var ordered = reads.OrderBy(read => read.ReadAtUtc).ThenBy(read => read.ReaderUserId).ToArray();
        return new(note.Id, note.CompanyId, WorkRecordTypesV1.Wire((WorkRecordTypeV1)note.RecordType), note.RecordId, note.AuthorUserId,
            Name(note.AuthorUserId), note.Body, note.CorrectsNoteId, mentions, note.CreatedAtUtc, passive?.PassivatedAtUtc, passive?.Reason,
            ordered.FirstOrDefault(read => read.ReaderUserId == viewer)?.ReadAtUtc,
            ordered.Take(WorkCollaborationLimitsV1.NoteReadsMaximumCount)
                .Select(read => new WorkNoteReadV1(read.ReaderUserId, Name(read.ReaderUserId), read.ReadAtUtc)).ToArray());
    }

    private async Task<WorkNoteViewV1> NoteViewAsync(Actor actor, Guid noteId, CancellationToken cancellationToken)
    {
        var companyId = actor.CompanyId;
        var note = await database.RecordNotes.AsNoTracking().SingleOrDefaultAsync(row => row.CompanyId == companyId && row.Id == noteId, cancellationToken)
            ?? throw new WorkCollaborationException(503, "service_unavailable");
        var passive = await database.RecordNotePassivations.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CompanyId == companyId && row.NoteId == noteId, cancellationToken);
        var mentions = await database.RecordNoteMentions.AsNoTracking().Where(row => row.CompanyId == companyId && row.NoteId == noteId)
            .Select(row => row.MentionedUserId).OrderBy(id => id).ToArrayAsync(cancellationToken);
        var reads = await database.RecordNoteReads.AsNoTracking().Where(row => row.CompanyId == companyId && row.NoteId == noteId)
            .ToArrayAsync(cancellationToken);
        var names = await NamesAsync(companyId, reads.Select(read => read.ReaderUserId).Append(note.AuthorUserId), cancellationToken);
        return View(note, names, mentions, passive, reads, actor.ActorUserId);
    }

    private async Task<WorkTaskViewV1> TaskViewAsync(Actor actor, Guid taskId, CancellationToken cancellationToken)
    {
        var row = await database.Tasks.AsNoTracking().SingleOrDefaultAsync(item => item.CompanyId == actor.CompanyId && item.Id == taskId, cancellationToken)
            ?? throw new WorkCollaborationException(503, "service_unavailable");
        var page = await TaskPageAsync(actor, [row], cancellationToken);
        return page.Items[0];
    }

    private async Task<WorkTaskPageV1> TaskPageAsync(Actor actor, IReadOnlyList<WorkTaskRow> rows, CancellationToken cancellationToken)
    {
        var page = rows.Take(WorkCollaborationLimitsV1.PageSize).ToArray();
        var names = await NamesAsync(actor.CompanyId, page.SelectMany(row => new[] { row.AssignerUserId, row.AssigneeUserId }), cancellationToken);
        RequireStill(actor);
        string Name(Guid id) => names.TryGetValue(id, out var name) ? name : id.ToString("D");
        return new WorkTaskPageV1(actor.CompanyId, page.Select(row => new WorkTaskViewV1(row.Id, row.CompanyId,
                row.RecordType is { } type ? WorkRecordTypesV1.Wire((WorkRecordTypeV1)type) : null, row.RecordId, row.Title, row.Details,
                row.AssignerUserId, Name(row.AssignerUserId), row.AssigneeUserId, Name(row.AssigneeUserId), row.DueAtUtc,
                WorkTaskV1.Wire((WorkTaskStatusV1)row.Status), row.Version.ToString(CultureInfo.InvariantCulture), row.CreatedAtUtc,
                row.ClosedAtUtc, row.ClosedByUserId, row.CancelReason)).ToArray(),
            rows.Count > WorkCollaborationLimitsV1.PageSize);
    }
}

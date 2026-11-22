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
using Microsoft.Extensions.Options;

namespace GoymenERP.Modules.Work.Infrastructure.AssistantMemory;

/// <summary>
/// WP-C6 personal assistant memory (AI-D30). Critical command skeleton (AGENTS.md): permission fence revalidated before the
/// save (503 for an unregistered name), expected version on every change of an existing entry or closure, the idempotency key
/// per actor with request-hash replay before any not-found or validation, correlation/causation in rows and audit, refusals of
/// state recorded so the same key replays them, and one SaveChanges per command. Every query names the server-resolved company
/// and, for own memory, the actor; no answer distinguishes "another person's entry" from "no entry".
/// <para>
/// Authority: seeing and deleting one's own entries needs only a signed-in member of the company (a person can always see and
/// remove what is kept about them). Writing one's own entries needs <c>ai.panel.use</c> (the assistant capability) and the
/// open pilot (<see cref="WorkAssistantMemoryOptions"/>). Viewing and closing another person's memory needs
/// <c>work.assistant-memory.oversee</c>; each view is saved as a view row plus an audit event BEFORE any entry is returned, so a
/// view that cannot be recorded is not served. Nothing here talks to a model; the assistant read is
/// <see cref="IWorkAssistantMemoryReaderV1"/> and its only output form is <see cref="WorkAssistantMemoryDataBlockV1"/>.
/// </para>
/// </summary>
public sealed class WorkAssistantMemoryServiceV1(
    WorkDbContext database,
    ICompanyContext company,
    ICurrentUser user,
    IErpAuthorityRevalidationService authority,
    IWorkPersonDirectoryV1 people,
    IAuditEventCollector audit,
    ICorrelationContext correlation,
    IOptions<WorkAssistantMemoryOptions> options,
    TimeProvider time) : IWorkAssistantMemoryServiceV1, IWorkAssistantMemoryReaderV1
{
    /// <summary>
    /// The audit basis of an owner's own deletion. It is not a permission (no role grants it and none is checked): the right to
    /// remove one's own memory is the owner's, and the audit row names that basis instead of borrowing a permission.
    /// </summary>
    public const string OwnerAuditBasis = "self:work.assistant-memory";

    private const string EntryEntityType = "Work.AssistantMemoryEntry";
    private const string MemoryEntityType = "Work.AssistantMemory";
    private static readonly JsonSerializerOptions HashJson = new(JsonSerializerDefaults.Web);

    // ---- Own memory ----------------------------------------------------------------------------------------------------

    public async Task<WorkAssistantMemoryStatusV1> GetMyStatusAsync(CancellationToken cancellationToken = default)
    {
        var actor = RequireActor();
        await RequireMemberAsync(actor, cancellationToken);
        var settings = Settings();
        var closed = await IsClosedForUserAsync(actor.CompanyId, actor.ActorUserId, cancellationToken);
        var now = time.GetUtcNow();
        var active = await database.AssistantMemoryEntries.AsNoTracking().CountAsync(row => row.CompanyId == actor.CompanyId
            && row.OwnerUserId == actor.ActorUserId && row.State == (int)WorkAssistantMemoryStateV1.Active && row.ExpiresAtUtc > now,
            cancellationToken);
        RequireStill(actor);
        var reasons = DisabledReasons(settings, closed);
        return new WorkAssistantMemoryStatusV1(actor.CompanyId, actor.ActorUserId, reasons.Count == 0, reasons, settings?.PilotEnabled == true,
            settings?.RetentionDays is > 0 ? settings.RetentionDays : null,
            settings?.MaxEntriesPerUser is > 0 ? settings.MaxEntriesPerUser : null,
            string.IsNullOrWhiteSpace(settings?.InformationNoticeVersion) ? null : settings.InformationNoticeVersion, active);
    }

    public async Task<WorkAssistantMemoryEntryPageV1> ListMyEntriesAsync(bool includeInactive, CancellationToken cancellationToken = default)
    {
        var actor = RequireActor();
        await RequireMemberAsync(actor, cancellationToken);
        var now = time.GetUtcNow();
        var rows = await database.AssistantMemoryEntries.AsNoTracking()
            .Where(row => row.CompanyId == actor.CompanyId && row.OwnerUserId == actor.ActorUserId
                && (row.State == (int)WorkAssistantMemoryStateV1.Active && (includeInactive || row.ExpiresAtUtc > now)
                    || includeInactive && row.State == (int)WorkAssistantMemoryStateV1.ClosedByOverseer))
            .OrderByDescending(row => row.UpdatedAtUtc).ThenBy(row => row.Id)
            .Take(WorkAssistantMemoryLimitsV1.PageSize + 1).ToArrayAsync(cancellationToken);
        RequireStill(actor);
        return new WorkAssistantMemoryEntryPageV1(actor.CompanyId, actor.ActorUserId,
            rows.Take(WorkAssistantMemoryLimitsV1.PageSize).Select(row => View(row, now)).ToArray(),
            rows.Length > WorkAssistantMemoryLimitsV1.PageSize);
    }

    public async Task<WorkAssistantMemoryEntryResponseV1> CreateEntryAsync(CreateWorkAssistantMemoryEntryRequestV1 request,
        WorkCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequirePermissionAsync(ErpPermissions.AiPanelUse, cancellationToken);
        var command = WorkAssistantMemoryCommandsV1.CreateEntry;
        var requestHash = Hash(command, request.Kind, request.Body);
        var replay = await FindReplayAsync(scope, metadata, command, requestHash, cancellationToken);
        if (replay is not null) return new(await EntryViewAsync(scope, replay.TargetId, cancellationToken), true, replay.CorrelationId);
        if (!WorkAssistantMemoryKindsV1.TryParse(request.Kind, out var kind) || !WorkAssistantMemoryTextV1.IsValidBody(request.Body))
            throw new WorkCollaborationException(422, WorkCollaborationProblemCodesV1.ValidationFailed);
        var settings = RequireWritable();
        var now = time.GetUtcNow();
        var entryId = Guid.NewGuid();
        if (await IsClosedForUserAsync(scope.CompanyId, scope.ActorUserId, cancellationToken))
            throw await TerminalAsync(scope, metadata, command, requestHash, entryId, WorkAssistantMemoryProblemCodesV1.ClosedForUser, cancellationToken);
        var active = await database.AssistantMemoryEntries.AsNoTracking().CountAsync(row => row.CompanyId == scope.CompanyId
            && row.OwnerUserId == scope.ActorUserId && row.State == (int)WorkAssistantMemoryStateV1.Active && row.ExpiresAtUtc > now,
            cancellationToken);
        if (active >= settings.MaxEntries)
            throw await TerminalAsync(scope, metadata, command, requestHash, entryId, WorkAssistantMemoryProblemCodesV1.LimitReached, cancellationToken);

        await RequireStillPermissionAsync(scope, cancellationToken);
        var row = new WorkAssistantMemoryEntryRow
        {
            CompanyId = scope.CompanyId, Id = entryId, OwnerUserId = scope.ActorUserId, Kind = (int)kind, Body = request.Body,
            State = (int)WorkAssistantMemoryStateV1.Active, Version = 1, CreatedAtUtc = now, UpdatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(settings.RetentionDays), CorrelationId = metadata.CorrelationId, CausationId = metadata.CausationId,
        };
        database.AssistantMemoryEntries.Add(row);
        AddAttempt(scope, metadata, command, requestHash, entryId, 201, null, now);
        Audit(scope, metadata, "work.assistant-memory.entry.created.v1", EntryEntityType, entryId, "CreateAssistantMemoryEntry",
            null, 1, "{}", EntryAudit(row), "Kişisel asistan hafızasına kayıt eklendi.");
        var saved = await SaveAsync(scope, metadata, command, requestHash, cancellationToken);
        if (saved is not null) return new(await EntryViewAsync(scope, saved.TargetId, cancellationToken), true, saved.CorrelationId);
        return new(await EntryViewAsync(scope, entryId, cancellationToken), false, metadata.CorrelationId);
    }

    public async Task<WorkAssistantMemoryEntryResponseV1> ReviseEntryAsync(Guid entryId, ReviseWorkAssistantMemoryEntryRequestV1 request,
        WorkCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequirePermissionAsync(ErpPermissions.AiPanelUse, cancellationToken);
        var version = ParseVersion(request.ExpectedVersion, allowZero: false);
        var command = WorkAssistantMemoryCommandsV1.ReviseEntry;
        var requestHash = Hash(command, entryId, request.ExpectedVersion, request.Kind, request.Body);
        var replay = await FindReplayAsync(scope, metadata, command, requestHash, cancellationToken);
        if (replay is not null) return new(await EntryViewAsync(scope, replay.TargetId, cancellationToken), true, replay.CorrelationId);
        if (entryId == Guid.Empty) throw new WorkCollaborationException(404, "not_found");
        if (!WorkAssistantMemoryKindsV1.TryParse(request.Kind, out var kind) || !WorkAssistantMemoryTextV1.IsValidBody(request.Body))
            throw new WorkCollaborationException(422, WorkCollaborationProblemCodesV1.ValidationFailed);
        var row = await OwnEntryAsync(scope, entryId, cancellationToken);
        var settings = RequireWritable();
        if (await IsClosedForUserAsync(scope.CompanyId, scope.ActorUserId, cancellationToken))
            throw await TerminalAsync(scope, metadata, command, requestHash, entryId, WorkAssistantMemoryProblemCodesV1.ClosedForUser, cancellationToken);
        if (row.State != (int)WorkAssistantMemoryStateV1.Active)
            throw await TerminalAsync(scope, metadata, command, requestHash, entryId, WorkAssistantMemoryProblemCodesV1.EntryNotActive, cancellationToken);
        if (row.Version != version)
            throw await TerminalAsync(scope, metadata, command, requestHash, entryId, WorkCollaborationProblemCodesV1.VersionConflict, cancellationToken);

        await RequireStillPermissionAsync(scope, cancellationToken);
        var before = EntryAudit(row);
        var now = time.GetUtcNow();
        row.Kind = (int)kind;
        row.Body = request.Body;
        row.UpdatedAtUtc = now;
        row.ExpiresAtUtc = now.AddDays(settings.RetentionDays);
        row.Version = version + 1;
        AddAttempt(scope, metadata, command, requestHash, entryId, 200, null, now);
        Audit(scope, metadata, "work.assistant-memory.entry.revised.v1", EntryEntityType, entryId, "ReviseAssistantMemoryEntry",
            version, row.Version, before, EntryAudit(row), "Kişisel asistan hafızası kaydı düzeltildi.");
        var saved = await SaveAsync(scope, metadata, command, requestHash, cancellationToken);
        if (saved is not null) return new(await EntryViewAsync(scope, saved.TargetId, cancellationToken), true, saved.CorrelationId);
        return new(await EntryViewAsync(scope, entryId, cancellationToken), false, metadata.CorrelationId);
    }

    public async Task<WorkAssistantMemoryDeletionResponseV1> DeleteEntryAsync(Guid entryId, DeleteWorkAssistantMemoryEntryRequestV1 request,
        WorkCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var actor = RequireActor();
        await RequireMemberAsync(actor, cancellationToken);
        var scope = new Scope(actor.CompanyId, actor.ActorId, actor.ActorUserId, OwnerAuditBasis, [], RevalidatePermission: false);
        var version = ParseVersion(request.ExpectedVersion, allowZero: false);
        var command = WorkAssistantMemoryCommandsV1.DeleteEntry;
        var requestHash = Hash(command, entryId, request.ExpectedVersion);
        var replay = await FindReplayAsync(scope, metadata, command, requestHash, cancellationToken);
        if (replay is not null) return new(await DeletionViewAsync(scope, replay.TargetId, cancellationToken), true, replay.CorrelationId);
        if (entryId == Guid.Empty) throw new WorkCollaborationException(404, "not_found");
        var row = await OwnEntryAsync(scope, entryId, cancellationToken);
        // Deletion is never closed by the pilot flag or by a closure: a person can always remove their own memory.
        if (row.State != (int)WorkAssistantMemoryStateV1.Active)
            throw await TerminalAsync(scope, metadata, command, requestHash, entryId, WorkAssistantMemoryProblemCodesV1.EntryNotActive, cancellationToken);
        if (row.Version != version)
            throw await TerminalAsync(scope, metadata, command, requestHash, entryId, WorkCollaborationProblemCodesV1.VersionConflict, cancellationToken);

        RequireStill(scope);
        var before = EntryAudit(row);
        var now = time.GetUtcNow();
        row.State = (int)WorkAssistantMemoryStateV1.DeletedByOwner;
        row.EndedAtUtc = now;
        row.EndedByUserId = scope.ActorUserId;
        row.Version = version + 1;
        AddAttempt(scope, metadata, command, requestHash, entryId, 200, null, now);
        Audit(scope, metadata, "work.assistant-memory.entry.deleted.v1", EntryEntityType, entryId, "DeleteAssistantMemoryEntry",
            version, row.Version, before, EntryAudit(row), "Kişisel asistan hafızası kaydı sahibi tarafından silindi.");
        var saved = await SaveAsync(scope, metadata, command, requestHash, cancellationToken);
        if (saved is not null) return new(await DeletionViewAsync(scope, saved.TargetId, cancellationToken), true, saved.CorrelationId);
        return new(await DeletionViewAsync(scope, entryId, cancellationToken), false, metadata.CorrelationId);
    }

    public async Task<WorkAssistantMemoryOversightViewPageV1> ListViewsOfMyMemoryAsync(CancellationToken cancellationToken = default)
    {
        var actor = RequireActor();
        await RequireMemberAsync(actor, cancellationToken);
        var rows = await database.AssistantMemoryOversightViews.AsNoTracking()
            .Where(row => row.CompanyId == actor.CompanyId && row.SubjectUserId == actor.ActorUserId)
            .OrderByDescending(row => row.ViewedAtUtc).ThenBy(row => row.Id)
            .Take(WorkAssistantMemoryLimitsV1.PageSize + 1).ToArrayAsync(cancellationToken);
        RequireStill(actor);
        return new WorkAssistantMemoryOversightViewPageV1(actor.CompanyId, actor.ActorUserId,
            rows.Take(WorkAssistantMemoryLimitsV1.PageSize)
                .Select(row => new WorkAssistantMemoryOversightViewV1(row.Id, row.ViewerUserId, row.ViewedAtUtc, row.EntryCount)).ToArray(),
            rows.Length > WorkAssistantMemoryLimitsV1.PageSize);
    }

    // ---- Oversight (AI-D30) ---------------------------------------------------------------------------------------------

    public async Task<WorkAssistantMemoryOversightPageV1> ViewUserMemoryAsync(Guid subjectUserId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequirePermissionAsync(WorkPermissionManifestV1.AssistantMemoryOversee, cancellationToken);
        if (subjectUserId == Guid.Empty) throw new WorkCollaborationException(404, "not_found");
        if (!correlation.IsResolved || string.IsNullOrWhiteSpace(correlation.CorrelationId) || correlation.CorrelationId.Length > 128)
            throw new WorkCollaborationException(400, WorkCollaborationProblemCodesV1.CommandMetadataInvalid);
        var now = time.GetUtcNow();
        // A deleted entry is the owner's removal: its text is shown to nobody, the overseer included.
        var rows = await database.AssistantMemoryEntries.AsNoTracking()
            .Where(row => row.CompanyId == scope.CompanyId && row.OwnerUserId == subjectUserId
                && (row.State == (int)WorkAssistantMemoryStateV1.Active || row.State == (int)WorkAssistantMemoryStateV1.ClosedByOverseer))
            .OrderByDescending(row => row.UpdatedAtUtc).ThenBy(row => row.Id)
            .Take(WorkAssistantMemoryLimitsV1.PageSize + 1).ToArrayAsync(cancellationToken);
        var closed = await IsClosedForUserAsync(scope.CompanyId, subjectUserId, cancellationToken);
        var page = rows.Take(WorkAssistantMemoryLimitsV1.PageSize).ToArray();

        // The view is recorded first; only a saved record releases the entries.
        await RequireStillPermissionAsync(scope, cancellationToken);
        var viewId = Guid.NewGuid();
        var metadata = new WorkCommandMetadataV1("work.assistant-memory.view:" + viewId.ToString("D"), correlation.CorrelationId,
            correlation.CausationId is { Length: > 0 and <= 128 } causation ? causation : null);
        database.AssistantMemoryOversightViews.Add(new WorkAssistantMemoryOversightViewRow
        {
            CompanyId = scope.CompanyId, Id = viewId, ViewerUserId = scope.ActorUserId, SubjectUserId = subjectUserId, ViewedAtUtc = now,
            EntryCount = page.Length, CorrelationId = metadata.CorrelationId,
        });
        Audit(scope, metadata, "work.assistant-memory.viewed.v1", MemoryEntityType, subjectUserId, "ViewAssistantMemory", null, null, "{}",
            JsonSerializer.Serialize(new { viewId, subjectUserId, entryIds = page.Select(row => row.Id), entryCount = page.Length, closedForUser = closed },
                HashJson),
            "Yönetici kişisel asistan hafızasını görüntüledi.");
        await SaveOnceAsync(scope, cancellationToken);
        return new WorkAssistantMemoryOversightPageV1(scope.CompanyId, subjectUserId, viewId, closed, page.Select(row => View(row, now)).ToArray(),
            rows.Length > WorkAssistantMemoryLimitsV1.PageSize);
    }

    public async Task<WorkAssistantMemoryEntryResponseV1> CloseEntryAsync(Guid entryId, CloseWorkAssistantMemoryEntryRequestV1 request,
        WorkCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequirePermissionAsync(WorkPermissionManifestV1.AssistantMemoryOversee, cancellationToken);
        var version = ParseVersion(request.ExpectedVersion, allowZero: false);
        var command = WorkAssistantMemoryCommandsV1.CloseEntry;
        var requestHash = Hash(command, entryId, request.ExpectedVersion, request.Reason);
        var replay = await FindReplayAsync(scope, metadata, command, requestHash, cancellationToken);
        if (replay is not null) return new(await EntryViewAsync(scope, replay.TargetId, cancellationToken, anyOwner: true), true, replay.CorrelationId);
        if (entryId == Guid.Empty) throw new WorkCollaborationException(404, "not_found");
        if (!WorkAssistantMemoryTextV1.IsValidReason(request.Reason))
            throw new WorkCollaborationException(422, WorkCollaborationProblemCodesV1.ValidationFailed);
        var row = await database.AssistantMemoryEntries.SingleOrDefaultAsync(item => item.CompanyId == scope.CompanyId && item.Id == entryId
                && item.State != (int)WorkAssistantMemoryStateV1.DeletedByOwner, cancellationToken)
            ?? throw new WorkCollaborationException(404, "not_found");
        if (row.State != (int)WorkAssistantMemoryStateV1.Active)
            throw await TerminalAsync(scope, metadata, command, requestHash, entryId, WorkAssistantMemoryProblemCodesV1.EntryNotActive, cancellationToken);
        if (row.Version != version)
            throw await TerminalAsync(scope, metadata, command, requestHash, entryId, WorkCollaborationProblemCodesV1.VersionConflict, cancellationToken);

        await RequireStillPermissionAsync(scope, cancellationToken);
        var before = EntryAudit(row);
        var now = time.GetUtcNow();
        row.State = (int)WorkAssistantMemoryStateV1.ClosedByOverseer;
        row.EndedAtUtc = now;
        row.EndedByUserId = scope.ActorUserId;
        row.CloseReason = request.Reason;
        row.Version = version + 1;
        AddAttempt(scope, metadata, command, requestHash, entryId, 200, null, now);
        Audit(scope, metadata, "work.assistant-memory.entry.closed.v1", EntryEntityType, entryId, "CloseAssistantMemoryEntry", version,
            row.Version, before, EntryAudit(row), request.Reason);
        var saved = await SaveAsync(scope, metadata, command, requestHash, cancellationToken);
        if (saved is not null) return new(await EntryViewAsync(scope, saved.TargetId, cancellationToken, anyOwner: true), true, saved.CorrelationId);
        return new(await EntryViewAsync(scope, entryId, cancellationToken, anyOwner: true), false, metadata.CorrelationId);
    }

    public async Task<WorkAssistantMemoryUserClosureResponseV1> SetUserClosureAsync(Guid subjectUserId,
        SetWorkAssistantMemoryUserClosureRequestV1 request, WorkCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequirePermissionAsync(WorkPermissionManifestV1.AssistantMemoryOversee, cancellationToken);
        var version = ParseVersion(request.ExpectedVersion, allowZero: true);
        var command = WorkAssistantMemoryCommandsV1.SetUserClosure;
        var requestHash = Hash(command, subjectUserId, request.ExpectedVersion, request.Closed, request.Reason);
        var replay = await FindReplayAsync(scope, metadata, command, requestHash, cancellationToken);
        if (replay is not null) return new(await ClosureViewAsync(scope, replay.TargetId, cancellationToken), true, replay.CorrelationId);
        if (subjectUserId == Guid.Empty) throw new WorkCollaborationException(404, "not_found");
        if (!WorkAssistantMemoryTextV1.IsValidReason(request.Reason))
            throw new WorkCollaborationException(422, WorkCollaborationProblemCodesV1.ValidationFailed);
        var row = await database.AssistantMemoryUserClosures.SingleOrDefaultAsync(item => item.CompanyId == scope.CompanyId
            && item.SubjectUserId == subjectUserId, cancellationToken);
        var current = row?.Version ?? 0;
        if (current != version)
            throw await TerminalAsync(scope, metadata, command, requestHash, subjectUserId, WorkCollaborationProblemCodesV1.VersionConflict, cancellationToken);
        if ((row?.IsClosed ?? false) == request.Closed)
            throw await TerminalAsync(scope, metadata, command, requestHash, subjectUserId, WorkAssistantMemoryProblemCodesV1.UserClosureUnchanged,
                cancellationToken);

        await RequireStillPermissionAsync(scope, cancellationToken);
        var now = time.GetUtcNow();
        var before = row is null ? "{}" : ClosureAudit(row);
        if (row is null)
        {
            row = new WorkAssistantMemoryUserClosureRow { CompanyId = scope.CompanyId, SubjectUserId = subjectUserId };
            database.AssistantMemoryUserClosures.Add(row);
        }

        row.IsClosed = request.Closed;
        row.Version = version + 1;
        row.ChangedAtUtc = now;
        row.ChangedByUserId = scope.ActorUserId;
        row.Reason = request.Reason;
        row.CorrelationId = metadata.CorrelationId;
        AddAttempt(scope, metadata, command, requestHash, subjectUserId, 200, null, now);
        Audit(scope, metadata, request.Closed ? "work.assistant-memory.user.closed.v1" : "work.assistant-memory.user.reopened.v1",
            MemoryEntityType, subjectUserId, request.Closed ? "CloseAssistantMemoryForUser" : "ReopenAssistantMemoryForUser",
            version == 0 ? null : version, row.Version, before, ClosureAudit(row), request.Reason);
        var saved = await SaveAsync(scope, metadata, command, requestHash, cancellationToken);
        if (saved is not null) return new(await ClosureViewAsync(scope, saved.TargetId, cancellationToken), true, saved.CorrelationId);
        return new(await ClosureViewAsync(scope, subjectUserId, cancellationToken), false, metadata.CorrelationId);
    }

    // ---- Assistant read --------------------------------------------------------------------------------------------------

    public async Task<WorkAssistantMemoryContextV1> ReadForCurrentActorAsync(CancellationToken cancellationToken = default)
    {
        Actor actor;
        try { actor = RequireActor(); }
        catch (WorkCollaborationException) { return new(WorkAssistantMemoryReadOutcomeV1.Unavailable, []); }
        var settings = Settings();
        if (DisabledReasons(settings, closedForUser: false).Count > 0) return new(WorkAssistantMemoryReadOutcomeV1.Disabled, []);
        try
        {
            if (await people.IsActiveMemberAsync(actor.CompanyId, actor.ActorUserId, cancellationToken) != WorkPersonOutcomeV1.Found)
                return new(WorkAssistantMemoryReadOutcomeV1.Unavailable, []);
            if (await IsClosedForUserAsync(actor.CompanyId, actor.ActorUserId, cancellationToken))
                return new(WorkAssistantMemoryReadOutcomeV1.Disabled, []);
            var now = time.GetUtcNow();
            var rows = await database.AssistantMemoryEntries.AsNoTracking()
                .Where(row => row.CompanyId == actor.CompanyId && row.OwnerUserId == actor.ActorUserId
                    && row.State == (int)WorkAssistantMemoryStateV1.Active && row.ExpiresAtUtc > now)
                .OrderByDescending(row => row.UpdatedAtUtc).ThenBy(row => row.Id)
                .Take(settings!.MaxEntriesPerUser!.Value).ToArrayAsync(cancellationToken);
            RequireStill(actor);
            return new(WorkAssistantMemoryReadOutcomeV1.Ready,
                rows.Select(row => new WorkAssistantMemoryItemV1(WorkAssistantMemoryKindsV1.Wire((WorkAssistantMemoryKindV1)row.Kind), row.Body)).ToArray());
        }
        catch (WorkCollaborationException)
        {
            return new(WorkAssistantMemoryReadOutcomeV1.Unavailable, []);
        }
    }

    // ---- Settings ----------------------------------------------------------------------------------------------------------

    private sealed record WritableSettings(int RetentionDays, int MaxEntries);

    /// <summary>A section that cannot be bound is treated as closed, never as a crash or as open.</summary>
    private WorkAssistantMemoryOptions? Settings()
    {
        try { return options.Value; }
        catch (InvalidOperationException) { return null; }
        catch (OptionsValidationException) { return null; }
    }

    private static List<string> DisabledReasons(WorkAssistantMemoryOptions? settings, bool closedForUser)
    {
        var reasons = new List<string>();
        if (settings?.PilotEnabled != true) reasons.Add(WorkAssistantMemoryDisabledReasonsV1.PilotOff);
        if (settings?.RetentionDays is not > 0) reasons.Add(WorkAssistantMemoryDisabledReasonsV1.RetentionUndecided);
        if (settings?.MaxEntriesPerUser is not > 0) reasons.Add(WorkAssistantMemoryDisabledReasonsV1.EntryLimitUndecided);
        if (string.IsNullOrWhiteSpace(settings?.InformationNoticeVersion)) reasons.Add(WorkAssistantMemoryDisabledReasonsV1.InformationNoticePending);
        if (closedForUser) reasons.Add(WorkAssistantMemoryDisabledReasonsV1.ClosedForUser);
        return reasons;
    }

    /// <summary>The pilot gate is configuration, not record state: it is not recorded with the key and is re-read each time.</summary>
    private WritableSettings RequireWritable()
    {
        var settings = Settings();
        if (DisabledReasons(settings, closedForUser: false).Count > 0)
            throw new WorkCollaborationException(409, WorkAssistantMemoryProblemCodesV1.Disabled);
        return new WritableSettings(settings!.RetentionDays!.Value, settings.MaxEntriesPerUser!.Value);
    }

    private Task<bool> IsClosedForUserAsync(Guid companyId, Guid subjectUserId, CancellationToken cancellationToken) =>
        database.AssistantMemoryUserClosures.AsNoTracking()
            .AnyAsync(row => row.CompanyId == companyId && row.SubjectUserId == subjectUserId && row.IsClosed, cancellationToken);

    // ---- Shared skeleton ---------------------------------------------------------------------------------------------------

    private sealed record Actor(Guid CompanyId, string ActorId, Guid ActorUserId);

    private sealed record Scope(Guid CompanyId, string ActorId, Guid ActorUserId, string Permission, IReadOnlyList<string> RoleCodes,
        bool RevalidatePermission = true)
    {
        public static implicit operator Actor(Scope scope) => new(scope.CompanyId, scope.ActorId, scope.ActorUserId);
    }

    private sealed record Replay(Guid TargetId, string CorrelationId);

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

    private async Task RequireMemberAsync(Actor actor, CancellationToken cancellationToken)
    {
        var outcome = await people.IsActiveMemberAsync(actor.CompanyId, actor.ActorUserId, cancellationToken);
        RequireStill(actor);
        if (outcome == WorkPersonOutcomeV1.Unavailable) throw new WorkCollaborationException(503, "service_unavailable");
        if (outcome != WorkPersonOutcomeV1.Found) throw new WorkCollaborationException(403, "forbidden");
    }

    private async Task<Scope> RequirePermissionAsync(string permission, CancellationToken cancellationToken)
    {
        var actor = RequireActor();
        if (!ErpPermissions.IsKnown(permission))
            throw new WorkCollaborationException(503, WorkCollaborationProblemCodesV1.PermissionUnregistered);
        var granted = await authority.RevalidatePermissionScopeAsync(permission, actor.CompanyId, cancellationToken);
        RequireStill(actor);
        if (granted is not { IsGranted: true, IsCompanyWide: true }) throw new WorkCollaborationException(403, "forbidden");
        return new Scope(actor.CompanyId, actor.ActorId, actor.ActorUserId, permission, granted.RoleCodes.Order(StringComparer.Ordinal).ToArray());
    }

    private async Task RequireStillPermissionAsync(Scope scope, CancellationToken cancellationToken)
    {
        RequireStill(scope);
        if (!scope.RevalidatePermission) return;
        var granted = await authority.RevalidatePermissionScopeAsync(scope.Permission, scope.CompanyId, cancellationToken);
        RequireStill(scope);
        if (granted is not { IsGranted: true, IsCompanyWide: true }) throw new WorkCollaborationException(403, "forbidden");
    }

    private async Task<WorkAssistantMemoryEntryRow> OwnEntryAsync(Scope scope, Guid entryId, CancellationToken cancellationToken) =>
        // Another person's entry, a deleted entry and no entry are the same answer.
        await database.AssistantMemoryEntries.SingleOrDefaultAsync(row => row.CompanyId == scope.CompanyId && row.Id == entryId
            && row.OwnerUserId == scope.ActorUserId && row.State != (int)WorkAssistantMemoryStateV1.DeletedByOwner, cancellationToken)
        ?? throw new WorkCollaborationException(404, "not_found");

    private static long ParseVersion(string? value, bool allowZero) =>
        value is not null && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
        && version >= (allowZero ? 0 : 1) && value == version.ToString(CultureInfo.InvariantCulture)
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

    private async Task<WorkCollaborationException> TerminalAsync(Scope scope, WorkCommandMetadataV1 metadata, string command,
        string requestHash, Guid targetId, string code, CancellationToken cancellationToken)
    {
        RequireStill(scope);
        database.ChangeTracker.Clear();
        AddAttempt(scope, metadata, command, requestHash, targetId, 409, code, time.GetUtcNow());
        var saved = await SaveAsync(scope, metadata, command, requestHash, cancellationToken);
        return saved is null ? new WorkCollaborationException(409, code)
            : new WorkCollaborationException(409, WorkCollaborationProblemCodesV1.ConcurrentChange);
    }

    private async Task<Replay?> SaveAsync(Scope scope, WorkCommandMetadataV1 metadata, string command, string requestHash,
        CancellationToken cancellationToken)
    {
        try
        {
            await SaveOnceAsync(scope, cancellationToken);
            return null;
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            var replay = await FindReplayAsync(scope, metadata, command, requestHash, cancellationToken);
            return replay ?? throw new WorkCollaborationException(409, WorkCollaborationProblemCodesV1.ConcurrentChange);
        }
    }

    /// <summary>One SaveChanges in one transaction; the tracker is always cleared afterwards.</summary>
    private async Task SaveOnceAsync(Scope scope, CancellationToken cancellationToken)
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

    /// <summary>The audit trail never copies the personal text: kind, state, times, version and the text length only.</summary>
    private static string EntryAudit(WorkAssistantMemoryEntryRow row) => JsonSerializer.Serialize(new
    {
        row.Id, row.OwnerUserId, row.Kind, row.State, row.Version, row.UpdatedAtUtc, row.ExpiresAtUtc, row.EndedAtUtc, row.EndedByUserId,
        bodyLength = row.Body.Length, closeReasonLength = row.CloseReason?.Length,
    }, HashJson);

    private static string ClosureAudit(WorkAssistantMemoryUserClosureRow row) => JsonSerializer.Serialize(new
    {
        row.SubjectUserId, row.IsClosed, row.Version, row.ChangedAtUtc, row.ChangedByUserId,
    }, HashJson);

    private static WorkAssistantMemoryEntryViewV1 View(WorkAssistantMemoryEntryRow row, DateTimeOffset now) =>
        new(row.Id, row.CompanyId, row.OwnerUserId, WorkAssistantMemoryKindsV1.Wire((WorkAssistantMemoryKindV1)row.Kind), row.Body,
            WorkAssistantMemoryStatesV1.Wire((WorkAssistantMemoryStateV1)row.State, row.ExpiresAtUtc, now),
            row.Version.ToString(CultureInfo.InvariantCulture), row.CreatedAtUtc, row.UpdatedAtUtc, row.ExpiresAtUtc,
            row.State == (int)WorkAssistantMemoryStateV1.ClosedByOverseer ? row.EndedAtUtc : null, row.CloseReason);

    private async Task<WorkAssistantMemoryEntryViewV1> EntryViewAsync(Scope scope, Guid entryId, CancellationToken cancellationToken,
        bool anyOwner = false)
    {
        var row = await database.AssistantMemoryEntries.AsNoTracking().SingleOrDefaultAsync(item => item.CompanyId == scope.CompanyId
                && item.Id == entryId && (anyOwner || item.OwnerUserId == scope.ActorUserId), cancellationToken);
        // A replay of an entry its owner deleted since then has nothing to show: the text is not served again.
        if (row is null || row.State == (int)WorkAssistantMemoryStateV1.DeletedByOwner) throw new WorkCollaborationException(404, "not_found");
        return View(row, time.GetUtcNow());
    }

    private async Task<WorkAssistantMemoryDeletionViewV1> DeletionViewAsync(Scope scope, Guid entryId, CancellationToken cancellationToken)
    {
        var row = await database.AssistantMemoryEntries.AsNoTracking().SingleOrDefaultAsync(item => item.CompanyId == scope.CompanyId
                && item.Id == entryId && item.OwnerUserId == scope.ActorUserId && item.State == (int)WorkAssistantMemoryStateV1.DeletedByOwner,
                cancellationToken)
            ?? throw new WorkCollaborationException(503, "service_unavailable");
        return new(row.Id, row.Version.ToString(CultureInfo.InvariantCulture), row.EndedAtUtc!.Value);
    }

    private async Task<WorkAssistantMemoryUserClosureViewV1> ClosureViewAsync(Scope scope, Guid subjectUserId, CancellationToken cancellationToken)
    {
        var row = await database.AssistantMemoryUserClosures.AsNoTracking().SingleOrDefaultAsync(item => item.CompanyId == scope.CompanyId
                && item.SubjectUserId == subjectUserId, cancellationToken)
            ?? throw new WorkCollaborationException(503, "service_unavailable");
        return new(row.CompanyId, row.SubjectUserId, row.IsClosed, row.Version.ToString(CultureInfo.InvariantCulture), row.ChangedAtUtc,
            row.ChangedByUserId, row.Reason);
    }
}

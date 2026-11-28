using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.AiKnowledge.Domain;

namespace GoymenERP.Modules.AiKnowledge.Infrastructure.ScheduledTasks;

/// <summary>No timer, worker, impersonation, model call, outbound send or business write is introduced.</summary>
public sealed class AiScheduledTaskServiceV1(ICompanyContext company, ICurrentUser user, TimeProvider time,
    IAiScheduledTaskPersistenceV1? persistence = null, IAiScheduledTaskAuthorityV1? authority = null,
    IAiScheduledTaskSettingsV1? settings = null, IAiScheduledTaskMorningReadV1? morning = null) : IAiScheduledTaskServiceV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public Task<AiScheduledTaskResponseV1> CreateAsync(Guid id, AiScheduledTaskDefinitionV1 definition, AiScheduledTaskMetadataV1 metadata, CancellationToken token = default) =>
        Command(id, null, "create", new { definition }, metadata, AiScheduledTaskActionV1.ManageOwn, async (scope, source, ct) =>
        {
            if (source.Task is not null) return Refusal("ai_scheduled_task_exists", source.Task, scope);
            Validate(definition); var config = await Config(scope, ct);
            if (definition.ScheduleVersion != config.ScheduleVersion) throw Conflict("ai_scheduled_task_schedule_changed");
            if (source.ActiveTaskCount >= config.MaximumActiveTasks!.Value) throw Conflict("ai_scheduled_task_active_limit");
            var now = time.GetUtcNow();
            return new(201, null, new(scope.Company, id, scope.Owner, definition.Purpose, definition.Effect,
                AiScheduledTaskStateV1.Active, 1, definition.ScheduleVersion, now, now), null, "created", scope.Permission);
        }, token);

    public Task<AiScheduledTaskResponseV1> UpdateAsync(Guid id, long version, AiScheduledTaskDefinitionV1 definition, bool active,
        AiScheduledTaskMetadataV1 metadata, CancellationToken token = default) =>
        Command(id, null, "update", new { version, definition, active }, metadata, AiScheduledTaskActionV1.ManageOwn, async (scope, source, ct) =>
        {
            var current = Current(source, scope, version); Validate(definition);
            if (current.Purpose != definition.Purpose || current.Effect != definition.Effect) throw Conflict("ai_scheduled_task_purpose_changed");
            var config = await Config(scope, ct);
            if (config.ScheduleVersion != definition.ScheduleVersion) throw Conflict("ai_scheduled_task_schedule_changed");
            if (active && current.State != AiScheduledTaskStateV1.Active && source.ActiveTaskCount >= config.MaximumActiveTasks!.Value)
                throw Conflict("ai_scheduled_task_active_limit");
            return new(200, null, current with { Version = version + 1, ScheduleVersion = definition.ScheduleVersion,
                State = active ? AiScheduledTaskStateV1.Active : AiScheduledTaskStateV1.Paused, UpdatedAtUtc = time.GetUtcNow() }, null, "updated", scope.Permission);
        }, token);
    public Task<AiScheduledTaskResponseV1> PauseAsync(Guid id, long version, AiScheduledTaskMetadataV1 metadata, CancellationToken token = default) =>
        SetState(id, null, version, AiScheduledTaskStateV1.Paused, metadata, AiScheduledTaskActionV1.ManageOwn, token);
    public Task<AiScheduledTaskResponseV1> CloseAsync(Guid id, long version, AiScheduledTaskMetadataV1 metadata, CancellationToken token = default) =>
        SetState(id, null, version, AiScheduledTaskStateV1.Closed, metadata, AiScheduledTaskActionV1.ManageOwn, token);
    public Task<AiScheduledTaskResponseV1> CloseForManagerAsync(Guid id, string owner, long version, AiScheduledTaskMetadataV1 metadata, CancellationToken token = default) =>
        SetState(id, owner, version, AiScheduledTaskStateV1.Closed, metadata, AiScheduledTaskActionV1.Oversee, token);
    private Task<AiScheduledTaskResponseV1> SetState(Guid id, string? owner, long version, AiScheduledTaskStateV1 state,
        AiScheduledTaskMetadataV1 metadata, AiScheduledTaskActionV1 action, CancellationToken token) =>
        Command(id, owner, state == AiScheduledTaskStateV1.Closed ? "close" : "pause", new { version, state }, metadata, action,
            (scope, source, _) => Task.FromResult(new AiScheduledTaskMutationV1(200, null,
                Current(source, scope, version) with { Version = version + 1, State = state, UpdatedAtUtc = time.GetUtcNow() }, null,
                state == AiScheduledTaskStateV1.Closed ? "closed" : "paused", scope.Permission)), token);

    public async Task<AiScheduledTaskViewV1?> ReadOwnAsync(Guid id, CancellationToken token = default)
    {
        var scope = Capture(null, AiScheduledTaskActionV1.ReadOwn, token);
        var row = await Store().ReadAsync(scope.Company, id, ct => Check(scope, ct), token);
        await Check(scope, token); Same(scope, token);
        if (row is not null && row.OwnerActorId != scope.Actor) throw Denied();
        return row;
    }
    public Task<AiScheduledTaskResponseV1> ViewForManagerAsync(Guid id, string owner, AiScheduledTaskMetadataV1 metadata, CancellationToken token = default)
    {
        // Every served oversight view is audited, including repeated views. No owner notification is produced (AI-D39).
        ArgumentNullException.ThrowIfNull(metadata);
        return Command(id, owner, "view", new { owner }, metadata with { IdempotencyKey = "manager-view:" + Guid.NewGuid().ToString("N") },
            AiScheduledTaskActionV1.Oversee, (scope, source, _) => Task.FromResult(new AiScheduledTaskMutationV1(200, null,
                Owned(source, scope), null, "viewed", scope.Permission)), token);
    }
    public Task<AiScheduledTaskResponseV1> RunCurrentAsync(Guid id, AiScheduledTaskRunRequestV1 request, AiScheduledTaskMetadataV1 metadata, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExpectedVersion <= 0 || request.ExpectedVersion == long.MaxValue || request.SlotUtc.Offset != TimeSpan.Zero
            || !Text(request.ScheduleVersion, 256)) throw new AiScheduledTaskException(400, "invalid_run_request");
        return Command(id, null, "run-current", new { request }, metadata, AiScheduledTaskActionV1.ExecuteCurrent, async (scope, source, ct) =>
        {
            var current = Current(source, scope, request.ExpectedVersion);
            if (source.RunSlotAlreadyRecorded) return Refusal("ai_scheduled_task_slot_already_recorded", current, scope);
            if (current.State != AiScheduledTaskStateV1.Active) return Refusal("ai_scheduled_task_not_active", current, scope);
            var access = await Access(scope, ct);
            if (access.Outcome == AiScheduledTaskAuthorityOutcomeV1.Denied) return Stop(scope, current);
            var config = await Config(scope, ct);
            if (config.ExecutionBudgetAvailable != true) throw Unavailable();
            access = await Access(scope, ct);
            if (access.Outcome == AiScheduledTaskAuthorityOutcomeV1.Denied) return Stop(scope, current);
            if (request.SlotUtc.Offset != TimeSpan.Zero || request.SlotUtc != config.SlotUtc
                || request.ScheduleVersion != config.ScheduleVersion || current.ScheduleVersion != config.ScheduleVersion)
                throw Conflict("ai_scheduled_task_schedule_changed");
            if (current.Purpose != AiScheduledTaskPurposesV1.MorningSummary || current.Effect != AiScheduledTaskEffectV1.Read || morning is null)
                throw Unavailable();
            var result = await morning.ReadCurrentAsync(ct); Same(scope, ct);
            access = await Access(scope, ct);
            if (access.Outcome == AiScheduledTaskAuthorityOutcomeV1.Denied || result?.Outcome == AiScheduledTaskMorningReadOutcomeV1.Denied)
                return Stop(scope, current);
            if (result is null || result.Outcome != AiScheduledTaskMorningReadOutcomeV1.Available || result.CompanyId != scope.Company
                || result.ScheduleVersion != request.ScheduleVersion || result.SlotUtc != request.SlotUtc) throw Unavailable();
            scope.AllowDeniedStop = false;
            return new(200, null, current, new(scope.Company, id, scope.Owner, current.Version, request.SlotUtc, request.ScheduleVersion,
                result.SourceReceiptId is null ? "no-content" : "read", result.SourceReceiptId, time.GetUtcNow()), "read", scope.Permission);
        }, token, request.SlotUtc);
    }
    private AiScheduledTaskMutationV1 Stop(Scope scope, AiScheduledTaskViewV1 current)
    {
        scope.AllowDeniedStop = true;
        var stopped = current with { State = AiScheduledTaskStateV1.StoppedForAuthority, Version = checked(current.Version + 1), UpdatedAtUtc = time.GetUtcNow() };
        // Revocation writes a stop event/command receipt, never a fabricated completed run or Settings slot.
        return new(403, "ai_scheduled_task_owner_authority_revoked", stopped, null, "stopped-authority", scope.Permission);
    }
    private async Task<AiScheduledTaskResponseV1> Command(Guid id, string? owner, string command, object payload, AiScheduledTaskMetadataV1 metadata,
        AiScheduledTaskActionV1 action, Func<Scope, AiScheduledTaskStoredSourceV1, CancellationToken, Task<AiScheduledTaskMutationV1>> decide,
        CancellationToken token, DateTimeOffset? slot = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (id == Guid.Empty || !Text(metadata.IdempotencyKey, 128) || !Text(metadata.CorrelationId, 128)
            || metadata.CausationId is not null && !Text(metadata.CausationId, 128)) throw new AiScheduledTaskException(400, "invalid_command_metadata");
        var scope = Capture(owner, action, token); var store = Store(); var cleanAtEntry = store.IsCleanEntry;
        scope.AllowDeniedStop = action == AiScheduledTaskActionV1.ExecuteCurrent;
        await Check(scope, token);
        if (!cleanAtEntry || !store.IsCleanEntry) throw Unavailable();
        var request = new AiScheduledTaskCommandV1(scope.Company, id, scope.Actor, scope.Owner, command, Hash(metadata.IdempotencyKey),
            Hash(JsonSerializer.Serialize(new { id, owner = scope.Owner, command, payload }, Json)), metadata, time.GetUtcNow(), slot);
        var result = await store.ExecuteAsync(request, (source, ct) => decide(scope, source, ct), ct => Check(scope, ct), token);
        scope.AllowDeniedStop = result.Status == 403 && result.Code == "ai_scheduled_task_owner_authority_revoked";
        await Check(scope, token); Same(scope, token); return result;
    }
    private static AiScheduledTaskMutationV1 Refusal(string code, AiScheduledTaskViewV1? current, Scope scope) => new(409, code, current, null, "refused", scope.Permission);
    private static AiScheduledTaskViewV1 Owned(AiScheduledTaskStoredSourceV1 source, Scope scope)
    {
        if (source.Task is not { } task || task.OwnerActorId != scope.Owner || task.CompanyId != scope.Company)
            throw new AiScheduledTaskException(404, "not_found");
        return task;
    }
    private static AiScheduledTaskViewV1 Current(AiScheduledTaskStoredSourceV1 source, Scope scope, long version)
    {
        var current = Owned(source, scope);
        if (version <= 0 || version == long.MaxValue || current.Version != version) throw Conflict("ai_scheduled_task_version_conflict");
        if (current.State == AiScheduledTaskStateV1.Closed) throw Conflict("ai_scheduled_task_closed");
        return current;
    }
    private async Task<AiScheduledTaskSettingsV1> Config(Scope scope, CancellationToken token)
    {
        await Check(scope, token);
        if (settings is null) throw Unavailable();
        var result = await settings.ReadAsync(scope.Company, scope.Owner, token);
        await Check(scope, token);
        if (result is null || result.CompanyId != scope.Company || !Text(result.ScheduleVersion, 256)
            || result.MaximumActiveTasks is not > 0 || result.ExecutionBudgetAvailable is null || result.SlotUtc.Offset != TimeSpan.Zero)
            throw Unavailable();
        return result;
    }
    private async Task Check(Scope scope, CancellationToken token)
    {
        var result = await Access(scope, token);
        if (result.Outcome == AiScheduledTaskAuthorityOutcomeV1.Denied && !scope.AllowDeniedStop) throw Denied();
    }
    private async Task<AiScheduledTaskAuthorityV1> Access(Scope scope, CancellationToken token)
    {
        Same(scope, token); if (authority is null) throw Unavailable();
        var result = await authority.CheckAsync(scope.Company, scope.Actor, scope.Owner, scope.Action, token);
        Same(scope, token);
        if (result is null || result.Outcome == AiScheduledTaskAuthorityOutcomeV1.Unavailable || !Text(result.PermissionCode, 128)) throw Unavailable();
        if (result.Outcome is not (AiScheduledTaskAuthorityOutcomeV1.Allowed or AiScheduledTaskAuthorityOutcomeV1.Denied)) throw Unavailable();
        scope.Permission = result.PermissionCode!; return result;
    }
    private Scope Capture(string? owner, AiScheduledTaskActionV1 action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!user.IsAuthenticated || !Text(user.UserId, 128)) throw new AiScheduledTaskException(401, "unauthenticated");
        if (!company.IsResolved || company.CompanyId == Guid.Empty || !user.CanAccessCompany(company.CompanyId)) throw Denied();
        if (owner is not null && !Text(owner, 128)) throw new AiScheduledTaskException(400, "invalid_owner");
        return new(company.CompanyId, user.UserId!, owner ?? user.UserId!, action);
    }
    private void Same(Scope scope, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!user.IsAuthenticated || user.UserId != scope.Actor || !company.IsResolved || company.CompanyId != scope.Company
            || !user.CanAccessCompany(scope.Company)) throw Denied();
    }
    private static void Validate(AiScheduledTaskDefinitionV1 definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!Text(definition.ScheduleVersion, 256)) throw new AiScheduledTaskException(400, "invalid_schedule_version");
        if (definition.Purpose != AiScheduledTaskPurposesV1.MorningSummary || definition.Effect != AiScheduledTaskEffectV1.Read) throw Unavailable();
    }
    private IAiScheduledTaskPersistenceV1 Store() => persistence is { IsAvailable: true } ? persistence : throw Unavailable();
    private static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max
        && value == value.Trim() && !value.Any(char.IsControl);
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static AiScheduledTaskException Conflict(string code) => new(409, code);
    private static AiScheduledTaskException Unavailable() => new(503, "ai_scheduled_task_unavailable");
    private static AiScheduledTaskException Denied() => new(403, "forbidden");
    private sealed class Scope(Guid company, string actor, string owner, AiScheduledTaskActionV1 action)
    {
        internal Guid Company { get; } = company; internal string Actor { get; } = actor; internal string Owner { get; } = owner;
        internal AiScheduledTaskActionV1 Action { get; } = action; internal string Permission { get; set; } = string.Empty;
        internal bool AllowDeniedStop { get; set; }
    }
}

using System.Data;
using System.Text.Json;
using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.AiKnowledge.Domain;
using GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.AiKnowledge.Infrastructure.ScheduledTasks;

public sealed class AiScheduledTaskPersistenceV1(AiKnowledgeDbContext database, ICompanyContext company,
    IAuditEventCollector? audit = null, IAuditTransactionWriterV1? writer = null) : IAiScheduledTaskPersistenceV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private bool owned;
    public bool IsAvailable => database.Database.IsSqlServer() && audit is not null && writer is not null;
    public bool IsCleanEntry => IsAvailable && !owned && Transaction.Current is null
        && database.Database.CurrentTransaction is null && !database.ChangeTracker.HasChanges() && !writer!.HasPendingDrafts;

    public async Task<AiScheduledTaskViewV1?> ReadAsync(Guid companyId, Guid taskId, Func<CancellationToken, Task> fence, CancellationToken token = default)
    {
        if (!IsAvailable) throw Unavailable();
        await Fence(companyId, fence, token);
        var row = await database.ScheduledTasks.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.TaskId == taskId, token);
        await Fence(companyId, fence, token);
        return row is null ? null : View(row);
    }

    public async Task<AiScheduledTaskResponseV1> ExecuteAsync(AiScheduledTaskCommandV1 command,
        Func<AiScheduledTaskStoredSourceV1, CancellationToken, Task<AiScheduledTaskMutationV1>> decide,
        Func<CancellationToken, Task> fence, CancellationToken token = default)
    {
        if (!IsCleanEntry) throw Unavailable();
        await Fence(command.CompanyId, fence, token);
        // The authority await may have yielded to another writer using this scoped context.
        if (!IsCleanEntry) throw Unavailable();
        owned = true; IDbContextTransaction? transaction = null; var committed = false; var cleaned = false;
        try
        {
            transaction = await database.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
            await Fence(command.CompanyId, fence, token);
            var replay = await Replay(command, fence, token);
            if (replay is not null)
            {
                await transaction.CommitAsync(token); committed = true;
                var replayTransaction = transaction; transaction = null; cleaned = true; await Cleanup(replayTransaction, false, null);
                await Fence(command.CompanyId, fence, token); return replay;
            }
            var row = await database.ScheduledTasks.SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.TaskId == command.TaskId, token);
            await Fence(command.CompanyId, fence, token);
            var activeCount = await database.ScheduledTasks.CountAsync(x => x.CompanyId == command.CompanyId
                && x.OwnerActorId == command.OwnerActorId && x.State == (int)AiScheduledTaskStateV1.Active, token);
            await Fence(command.CompanyId, fence, token);
            var slotRecorded = command.RunSlotUtc is { } slot && await database.ScheduledTaskRuns.AnyAsync(x => x.CompanyId == command.CompanyId
                && x.TaskId == command.TaskId && x.SlotUtc == slot, token);
            await Fence(command.CompanyId, fence, token);
            var mutation = await decide(new(row is null ? null : View(row), activeCount, slotRecorded), token);
            await Fence(command.CompanyId, fence, token);
            if (mutation.Task is { } next)
            {
                if (next.CompanyId != command.CompanyId || next.TaskId != command.TaskId || next.OwnerActorId != command.OwnerActorId
                    || (row is null ? next.Version != 1 : next != View(row) && (row.Version == long.MaxValue || next.Version != row.Version + 1)))
                    throw Unavailable();
                if (row is null) { row = new() { CompanyId = next.CompanyId, TaskId = next.TaskId }; database.ScheduledTasks.Add(row); }
                Apply(row, next);
            }
            if (mutation.Run is { } run)
            {
                if (slotRecorded || run.CompanyId != command.CompanyId || run.TaskId != command.TaskId || run.OwnerActorId != command.OwnerActorId
                    || run.SlotUtc != command.RunSlotUtc || row is null || run.TaskVersion != row.Version) throw Unavailable();
                database.ScheduledTaskRuns.Add(new() { CompanyId = run.CompanyId, TaskId = run.TaskId, SlotUtc = run.SlotUtc,
                    ReceiptJson = JsonSerializer.Serialize(run, Json) });
            }
            var response = new AiScheduledTaskResponseV1(mutation.Status, mutation.Code, mutation.Status == 403 ? null : mutation.Task,
                mutation.Run, false, command.Metadata.CorrelationId);
            database.ScheduledTaskCommands.Add(new() { CompanyId = command.CompanyId, ActorId = command.ActorId, KeyHash = command.KeyHash,
                TaskId = command.TaskId, Command = command.Command, PayloadHash = command.PayloadHash,
                CorrelationId = command.Metadata.CorrelationId, ResponseJson = JsonSerializer.Serialize(response, Json) });
            database.ScheduledTaskEvents.Add(new() { CompanyId = command.CompanyId, EventId = Guid.NewGuid(), TaskId = command.TaskId,
                ActorId = command.ActorId, Kind = mutation.EventKind, Version = mutation.Task?.Version,
                CorrelationId = command.Metadata.CorrelationId, RecordedAtUtc = command.RecordedAtUtc });
            await database.SaveChangesAsync(token);
            await Fence(command.CompanyId, fence, token);
            // Enqueue only after the owner save: an optional legacy SaveChanges interceptor cannot consume this batch.
            audit!.Enqueue(new AuditEventDraft(command.CompanyId, "ai.scheduled-task." + mutation.EventKind + ".v1", "Ai.ScheduledTask",
                command.TaskId.ToString("D"), command.Command, null, mutation.Task?.Version, AuditActorTypes.User, command.ActorId,
                mutation.PermissionCode, [], "{}", JsonSerializer.Serialize(new { command.TaskId, mutation.EventKind,
                    mutation.Code, Version = mutation.Task?.Version }, Json), mutation.EventKind, null, AuditAiInvolvement.None,
                null, "api", null, null, command.Metadata.CorrelationId, command.Metadata.CausationId, command.Metadata.IdempotencyKey));
            await writer!.WritePendingAsync(transaction.GetDbTransaction(), token);
            await Fence(command.CompanyId, fence, token);
            await transaction.CommitAsync(token); committed = true;
            var committedTransaction = transaction; transaction = null; cleaned = true; await Cleanup(committedTransaction, false, null);
            await Fence(command.CompanyId, fence, token); return response;
        }
        catch (Exception error)
        {
            if (!cleaned) { var aborted = transaction; transaction = null; cleaned = true; await Cleanup(aborted, !committed, error); }
            if (!committed && (error is DbUpdateConcurrencyException || SqlNumber(error) is 1205 or 1222 or 2601 or 2627))
            {
                // Exactly one clean original-key read; never retry the mutation or synthesize a terminal winner.
                return await Replay(command, fence, token) ?? throw Unavailable();
            }
            if (error is DbUpdateException or SqlException) throw new AiScheduledTaskException(503, "ai_scheduled_task_unavailable", error);
            throw;
        }
        finally { owned = false; }
    }

    private async Task<AiScheduledTaskResponseV1?> Replay(AiScheduledTaskCommandV1 c, Func<CancellationToken, Task> fence, CancellationToken token)
    {
        await Fence(c.CompanyId, fence, token);
        var stored = await database.ScheduledTaskCommands.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == c.CompanyId
            && x.ActorId == c.ActorId && x.KeyHash == c.KeyHash, token);
        await Fence(c.CompanyId, fence, token);
        if (stored is null) return null;
        if (stored.TaskId != c.TaskId || stored.Command != c.Command || stored.PayloadHash != c.PayloadHash)
            throw new AiScheduledTaskException(409, "ai_scheduled_task_idempotency_conflict");
        AiScheduledTaskResponseV1? answer;
        try { answer = JsonSerializer.Deserialize<AiScheduledTaskResponseV1>(stored.ResponseJson, Json); }
        catch (JsonException) { throw Unavailable(); }
        if (answer is null || answer.IsReplay || answer.OriginalCorrelationId != stored.CorrelationId
            || !System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(stored.ResponseJson),
                System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(answer, Json)))
            || answer.Task is { } t && (t.CompanyId != c.CompanyId || t.TaskId != c.TaskId || t.OwnerActorId != c.OwnerActorId)
            || answer.Run is { } r && (r.CompanyId != c.CompanyId || r.TaskId != c.TaskId || r.OwnerActorId != c.OwnerActorId)) throw Unavailable();
        return answer with { IsReplay = true };
    }
    private async Task Fence(Guid id, Func<CancellationToken, Task> callback, CancellationToken token)
    { token.ThrowIfCancellationRequested(); Same(id); await callback(token); token.ThrowIfCancellationRequested(); Same(id); }
    private void Same(Guid id)
    { if (id == Guid.Empty || !company.IsResolved || company.CompanyId != id) throw new AiScheduledTaskException(403, "forbidden"); }
    private async Task Cleanup(IDbContextTransaction? transaction, bool rollback, Exception? original)
    {
        List<Exception> faults = [];
        if (transaction is not null)
        {
            // SQL1205 may already have ended the provider transaction. Other rollback failures remain failures.
            var completedDeadlock = original is not null && SqlNumber(original) == 1205
                && transaction.GetDbTransaction() is SqlTransaction { Connection: null };
            if (rollback && !completedDeadlock) try { await transaction.RollbackAsync(CancellationToken.None); } catch (Exception e) { faults.Add(e); }
            try { await transaction.DisposeAsync(); } catch (Exception e) { faults.Add(e); }
        }
        try { database.ChangeTracker.Clear(); } catch (Exception e) { faults.Add(e); }
        try { writer!.DiscardPending(); } catch (Exception e) { faults.Add(e); }
        if (faults.Count != 0) { if (original is not null) faults.Insert(0, original); throw new AggregateException(faults); }
    }
    private static int? SqlNumber(Exception error)
    { for (Exception? next = error; next is not null; next = next.InnerException) if (next is SqlException sql) return sql.Number; return null; }
    private static AiScheduledTaskViewV1 View(AiScheduledTaskRow r) => new(r.CompanyId, r.TaskId, r.OwnerActorId, r.Purpose,
        (AiScheduledTaskEffectV1)r.Effect, (AiScheduledTaskStateV1)r.State, r.Version, r.ScheduleVersion, r.CreatedAtUtc, r.UpdatedAtUtc);
    private static void Apply(AiScheduledTaskRow r, AiScheduledTaskViewV1 v)
    { r.OwnerActorId = v.OwnerActorId; r.Purpose = v.Purpose; r.Effect = (int)v.Effect; r.State = (int)v.State; r.Version = v.Version;
        r.ScheduleVersion = v.ScheduleVersion; r.CreatedAtUtc = v.CreatedAtUtc; r.UpdatedAtUtc = v.UpdatedAtUtc; }
    private static AiScheduledTaskException Unavailable() => new(503, "ai_scheduled_task_unavailable");
}

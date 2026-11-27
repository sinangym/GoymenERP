using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.AiKnowledge.Domain;
using GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.AiKnowledge.Infrastructure;

/// <summary>Append-only learning evidence. Owns one clean transaction, never invokes a business command.</summary>
public sealed partial class AiFeedbackPersistenceV1(AiKnowledgeDbContext database, ICompanyContext company,
    ICurrentUser user, IAuditTransactionWriterV1 auditWriter, TimeProvider clock) : IAiFeedbackPersistenceV1
{
    public async Task<AiFeedbackWriteResultV1> AppendAsync(AiFeedbackAppendV1 request,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(freshConsumerFence);
        if (!AiFeedbackRulesV1.Valid(request)) return new(AiFeedbackWriteStatusV1.Invalid);
        if (!Current(request.CompanyId, request.ActorId)) return new(AiFeedbackWriteStatusV1.Refused);
        if (!CleanEntry()) return new(AiFeedbackWriteStatusV1.Unavailable);
        var keyHash = AiFeedbackRulesV1.KeyHash(request.IdempotencyKey); var hash = AiFeedbackRulesV1.Fingerprint(request);
        var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var cleanupAttempted = false; var committed = false;
        try
        {
            await Fence(request.CompanyId, request.ActorId, freshConsumerFence, cancellationToken);
            if (!await OwnsAnswer(request.CompanyId, request.AnswerId, request.ActorId, cancellationToken))
                return new(AiFeedbackWriteStatusV1.NotFound);
            var replay = await Replay(request, keyHash, hash, cancellationToken);
            if (replay is not null)
            {
                await Fence(request.CompanyId, request.ActorId, freshConsumerFence, cancellationToken);
                return replay;
            }
            var previous = await database.FeedbackEvents.AsNoTracking().Where(row => row.CompanyId == request.CompanyId
                && row.AnswerId == request.AnswerId).OrderByDescending(row => row.Version).FirstOrDefaultAsync(cancellationToken);
            if (previous is not null && !AiFeedbackRulesV1.Valid(Event(previous))) throw new InvalidOperationException("Invalid feedback history.");
            if ((previous?.Version ?? 0) != request.ExpectedVersion) return new(AiFeedbackWriteStatusV1.VersionConflict);
            if (!AiFeedbackRulesV1.CanAppend(request.Kind, previous is null ? null : (AiFeedbackKindV1)previous.Kind))
                return new(AiFeedbackWriteStatusV1.Invalid);
            var now = clock.GetUtcNow();
            if (now.Offset != TimeSpan.Zero || now == default || now > DateTimeOffset.MaxValue.AddDays(-AiAnswerReceiptRulesV1.RetentionDays)
                || (previous is not null && now < previous.CreatedAtUtc)) return new(AiFeedbackWriteStatusV1.Unavailable);
            var next = new AiFeedbackEventRow { CompanyId = request.CompanyId, AnswerId = request.AnswerId,
                Version = request.ExpectedVersion + 1, PriorVersion = previous?.Version, Kind = (int)request.Kind,
                ReasonCode = request.ReasonCode, CreatedAtUtc = now, RetainUntilUtc = now.AddDays(AiAnswerReceiptRulesV1.RetentionDays) };
            database.FeedbackEvents.Add(next);
            database.FeedbackIdentities.Add(new AiFeedbackIdentityRow { CompanyId = request.CompanyId, AnswerId = request.AnswerId,
                Version = next.Version, ActorId = request.ActorId, CorrelationId = request.CorrelationId,
                IdempotencyKeySha256 = keyHash, RequestSha256 = hash });
            await Fence(request.CompanyId, request.ActorId, freshConsumerFence, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            await Fence(request.CompanyId, request.ActorId, freshConsumerFence, cancellationToken);
            if (auditWriter.HasPendingDrafts) throw new InvalidOperationException("Feedback cannot flush caller audit.");
            await Fence(request.CompanyId, request.ActorId, freshConsumerFence, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            committed = true;
            return new(AiFeedbackWriteStatusV1.Recorded, Event(next), request.CorrelationId);
        }
        catch (Exception exception)
        {
            cleanupAttempted = true; // Set before cleanup so even a disposal fault never schedules a second disposal.
            await CleanupOwnedAsync(transaction, rollback: !committed, clear: true);
            // One clean read-only winner check; never rerun a mutation after a constraint/deadlock race.
            if (IsConstraintRace(exception))
            {
                await Fence(request.CompanyId, request.ActorId, freshConsumerFence, cancellationToken);
                if (!await OwnsAnswer(request.CompanyId, request.AnswerId, request.ActorId, cancellationToken))
                    return new(AiFeedbackWriteStatusV1.NotFound);
                var winner = await Replay(request, keyHash, hash, cancellationToken);
                if (winner is not null)
                {
                    await Fence(request.CompanyId, request.ActorId, freshConsumerFence, cancellationToken);
                    return winner;
                }
                var currentVersion = await database.FeedbackEvents.AsNoTracking().Where(row => row.CompanyId == request.CompanyId
                    && row.AnswerId == request.AnswerId).Select(row => (long?)row.Version).MaxAsync(cancellationToken) ?? 0;
                await Fence(request.CompanyId, request.ActorId, freshConsumerFence, cancellationToken);
                return new(currentVersion != request.ExpectedVersion ? AiFeedbackWriteStatusV1.VersionConflict : AiFeedbackWriteStatusV1.Unavailable);
            }
            throw;
        }
        finally
        {
            if (!cleanupAttempted)
            {
                cleanupAttempted = true;
                await CleanupOwnedAsync(transaction, rollback: !committed, clear: false);
            }
        }
    }

    private async Task CleanupOwnedAsync(IDbContextTransaction transaction, bool rollback, bool clear)
    {
        var failures = new List<Exception>();
        if (rollback)
        {
            try { await transaction.RollbackAsync(CancellationToken.None); }
            catch (Exception error) { failures.Add(error); }
        }
        try { await transaction.DisposeAsync(); }
        catch (Exception error) { failures.Add(error); }
        if (clear || failures.Count > 0)
        {
            try { database.ChangeTracker.Clear(); }
            catch (Exception error) { failures.Add(error); }
        }
        // Entry was clean and borrowed/pending callers were refused outside this owned boundary.
        // Always discard this attempt's drafts, including after rollback/disposal faults.
        try { auditWriter.DiscardPending(); }
        catch (Exception error) { failures.Add(error); }
        if (failures.Count > 0) throw new AggregateException("Feedback owned transaction cleanup failed.", failures);
    }

    public async Task<AiFeedbackCurrentV1?> GetOwnCurrentAsync(Guid companyId, Guid answerId, Guid actorId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence);
        if (answerId == Guid.Empty || !Current(companyId, actorId)) return null;
        await Fence(companyId, actorId, freshConsumerFence, cancellationToken);
        if (!await OwnsAnswer(companyId, answerId, actorId, cancellationToken)) return null;
        var latest = await database.FeedbackEvents.AsNoTracking().Where(row => row.CompanyId == companyId && row.AnswerId == answerId)
            .OrderByDescending(row => row.Version).FirstOrDefaultAsync(cancellationToken);
        await Fence(companyId, actorId, freshConsumerFence, cancellationToken);
        if (latest is not null && !AiFeedbackRulesV1.Valid(Event(latest))) throw new InvalidOperationException("Invalid feedback history.");
        return new(answerId, latest?.Version ?? 0, latest is null ? null : (AiFeedbackKindV1)latest.Kind, latest?.ReasonCode);
    }

    private async Task<bool> OwnsAnswer(Guid companyId, Guid answerId, Guid actorId, CancellationToken token)
    {
        var actor = actorId.ToString("D"); var now = clock.GetUtcNow();
        var receipt = await (from answer in database.AnswerReceipts.AsNoTracking()
            join identity in database.AnswerReceiptIdentities.AsNoTracking() on new { answer.CompanyId, answer.AnswerId }
                equals new { identity.CompanyId, identity.AnswerId }
            where answer.CompanyId == companyId && answer.AnswerId == answerId && identity.ActorId == actor
            select answer).SingleOrDefaultAsync(token);
        return receipt is not null && receipt.CreatedAtUtc <= now && receipt.RetainUntilUtc > now
            && AiAnswerReceiptRulesV1.IsValid(new(receipt.CompanyId, receipt.AnswerId, receipt.AnswerSha256, receipt.CitationsSha256,
                receipt.Feature, receipt.ModelDeployment, receipt.PolicyVersion, receipt.CreatedAtUtc, receipt.RetainUntilUtc));
    }
    private async Task<AiFeedbackWriteResultV1?> Replay(AiFeedbackAppendV1 request, string key, string hash, CancellationToken token)
    {
        var stored = await (from identity in database.FeedbackIdentities.AsNoTracking()
            join feedback in database.FeedbackEvents.AsNoTracking() on new { identity.CompanyId, identity.AnswerId, identity.Version }
                equals new { feedback.CompanyId, feedback.AnswerId, feedback.Version }
            where identity.CompanyId == request.CompanyId && identity.ActorId == request.ActorId && identity.IdempotencyKeySha256 == key
            select new { Identity = identity, Feedback = feedback }).SingleOrDefaultAsync(token);
        if (stored is null) return null;
        if (stored.Identity.RequestSha256 != hash) return new(AiFeedbackWriteStatusV1.IdempotencyConflict);
        var value = Event(stored.Feedback);
        if (value.CompanyId != request.CompanyId || value.AnswerId != request.AnswerId || value.Version != request.ExpectedVersion + 1
            || value.Kind != request.Kind || value.ReasonCode != request.ReasonCode || !AiFeedbackRulesV1.Valid(value)
            || !AiAnswerReceiptRulesV1.Token(stored.Identity.CorrelationId)) throw new InvalidOperationException("Invalid feedback receipt.");
        return new(AiFeedbackWriteStatusV1.Replayed, value, stored.Identity.CorrelationId);
    }
    private async Task Fence(Guid companyId, Guid actorId, Func<CancellationToken, Task> consumer, CancellationToken token)
    {
        if (!Current(companyId, actorId)) throw new AiFeedbackAuthorityLostException();
        await consumer(token);
        if (!Current(companyId, actorId)) throw new AiFeedbackAuthorityLostException();
    }
    private static bool IsConstraintRace(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is SqlException { Number: 1205 or 2601 or 2627 }) return true;
        return false;
    }
    private bool Current(Guid companyId, Guid actorId) => actorId != Guid.Empty && companyId != Guid.Empty && company.IsResolved
        && company.CompanyId == companyId && user.IsAuthenticated && Guid.TryParse(user.UserId, out var current) && current == actorId && user.CanAccessCompany(companyId);
    private bool CleanEntry() => System.Transactions.Transaction.Current is null && database.Database.IsSqlServer()
        && database.Database.CurrentTransaction is null && !database.ChangeTracker.HasChanges() && !auditWriter.HasPendingDrafts;
    private static AiFeedbackEventV1 Event(AiFeedbackEventRow row) => new(row.CompanyId, row.AnswerId, row.Version, row.PriorVersion,
        (AiFeedbackKindV1)row.Kind, row.ReasonCode, row.CreatedAtUtc, row.RetainUntilUtc);
}

using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>Purchasing-owned occurrence and original receipt only. No order, Inventory or Finance write.
/// Same scoped Audit writer borrows the one owned transaction; caller state is never joined or discarded.</summary>
public sealed class PurchaseUnorderedAcceptanceStoreV1(PurchasingDbContext db, ICompanyContext company,
    IAuditEventCollector audit, IAuditTransactionWriterV1 auditWriter)
    : IPurchaseUnorderedAcceptanceStoreV1, IPurchaseUnorderedAcceptanceSourceV1
{
    internal static readonly JsonSerializerOptions Json = new(PurchasingJson.Options)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public async Task<PurchaseUnorderedAcceptanceWriteV1> RecordAsync(Guid companyId, RecordPurchaseUnorderedAcceptanceV1 request,
        PurchasingWriteEnvelope envelope, string requestHash, Func<CancellationToken, Task<PurchaseUnorderedAcceptanceRecordV1>> prepare,
        Func<CancellationToken, Task> requireStill, CancellationToken token) =>
        await RecordCoreAsync(companyId, request, null, envelope, requestHash, prepare, requireStill, token);

    public async Task<PurchaseUnorderedAcceptanceWriteV1> RecordOccurrenceAsync(Guid companyId, RecordPurchaseUnorderedAcceptanceV2 request,
        PurchasingWriteEnvelope envelope, string requestHash, Func<CancellationToken, Task<PurchaseUnorderedAcceptanceRecordV1>> prepare,
        Func<CancellationToken, Task> requireStill, CancellationToken token)
    {
        if (!PurchaseUnorderedAcceptanceOwnerContractV1.Valid(request)) throw Failure(400);
        return await RecordCoreAsync(companyId, request.Occurrence, new(request.BusinessDate, request.Reason), envelope,
            requestHash, prepare, requireStill, token);
    }

    private async Task<PurchaseUnorderedAcceptanceWriteV1> RecordCoreAsync(Guid companyId, RecordPurchaseUnorderedAcceptanceV1 request,
        PurchaseUnorderedAcceptanceDeclarationV2? declaration, PurchasingWriteEnvelope envelope, string requestHash,
        Func<CancellationToken, Task<PurchaseUnorderedAcceptanceRecordV1>> prepare, Func<CancellationToken, Task> requireStill, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(prepare); ArgumentNullException.ThrowIfNull(requireStill);
        if (!PurchaseUnorderedAcceptanceOwnerContractV1.Valid(request) || !Scope(companyId)
            || envelope is null || envelope.PermissionCode != PurchasingPermissionManifestV1.GoodsReceiptsRecord
            || !Text(envelope.ActorId) || !Text(envelope.CorrelationId) || !Text(envelope.IdempotencyKey)
            || requestHash != (declaration is null ? RequestHash(request) : PurchaseUnorderedAcceptanceOwnerContractV1.RequestHash(
                new RecordPurchaseUnorderedAcceptanceV2(request, declaration.BusinessDate, declaration.Reason)))) throw Failure(400);
        var key = Hash(envelope.IdempotencyKey);
        // Both sides of the first awaited authority call precede ownership. Refusal preserves caller WIP/audit/transaction.
        Entry(); await Fence(); Entry();
        IDbContextTransaction? transaction = null; var owned = false; var committed = false;
        try
        {
            owned = true;
            transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            await Fence();
            var prior = await ReplayAsync(); await Fence();
            PurchaseUnorderedAcceptanceWriteV1 result;
            if (prior is not null) result = prior;
            else
            {
                var existing = await db.UnorderedAcceptances.AsNoTracking().AnyAsync(x => x.CompanyId == companyId
                    && x.AcceptanceId == request.AcceptanceId && x.AcceptanceLineId == request.AcceptanceLineId, token);
                await Fence();
                if (existing) throw Failure(409);
                var record = await prepare(token);
                if (record?.Source?.ControlEvidence is null) throw Failure(503);
                record = PurchaseUnorderedAcceptanceOwnerContractV1.Freeze(record);
                await Fence();
                if (!PurchaseUnorderedAcceptanceOwnerContractV1.Valid(record) || record.Source.CompanyId != companyId
                    || record.RecordedBy != envelope.ActorId || PurchaseUnorderedAcceptanceOwnerContractV1.RequestHash(record) != requestHash)
                    throw Failure(503);
                var json = JsonSerializer.Serialize(record, Json);
                db.UnorderedAcceptances.Add(new() { CompanyId = companyId, AcceptanceId = request.AcceptanceId,
                    AcceptanceLineId = request.AcceptanceLineId, Version = 1, RecordJson = json, RecordHash = Hash(json) });
                db.UnorderedAcceptanceAttempts.Add(new() { CompanyId = companyId, KeyHash = key, RequestHash = requestHash,
                    AcceptanceId = request.AcceptanceId, AcceptanceLineId = request.AcceptanceLineId,
                    ActorId = envelope.ActorId, CorrelationId = envelope.CorrelationId, RecordJson = json });
                await db.SaveChangesAsync(token); await Fence();
                audit.Enqueue(new AuditEventDraft(companyId, declaration is null ? "purchasing.unordered-acceptance.recorded.v1" : "purchasing.unordered-acceptance.recorded.v2",
                    "Purchasing.UnorderedAcceptance", $"{request.AcceptanceId:D}:{request.AcceptanceLineId:D}", "RecordOccurrence", 0, 1,
                    AuditActorTypes.User, envelope.ActorId, envelope.PermissionCode, envelope.RoleCodes, "{}", json,
                    "Siparişsiz alış kabul olgusu kaydedildi", null, AuditAiInvolvement.None, null, envelope.SourceChannel, envelope.ClientIpAddress, envelope.UserAgent,
                    envelope.CorrelationId, envelope.CausationId, envelope.IdempotencyKey));
                await auditWriter.WritePendingAsync(transaction.GetDbTransaction(), token); await Fence();
                if (auditWriter.HasPendingDrafts) throw Failure(503);
                result = new(record, false, envelope.CorrelationId);
            }
            await transaction.CommitAsync(token); committed = true;
            var finished = transaction; transaction = null; await finished.DisposeAsync();
            await Fence(); // Suppress a stale response; a committed occurrence is not claimed rolled back.
            return result;
        }
        catch (Exception error)
        {
            var recover = !committed && Retryable(error);
            if (owned)
            {
                var failed = transaction; transaction = null;
                // Cleanup faults propagate; never enter a winner read after an incomplete abort.
                await CleanupAsync(failed, !committed, Deadlock(error));
            }
            if (recover)
            {
                // One fresh read-only original-key pass. No mutation retry or recovery loop.
                Entry(); await Fence(); Entry(); var winner = await ReplayAsync(); await Fence();
                if (winner is not null) return winner;
                throw Failure(409);
            }
            throw;
        }

        async Task Fence()
        {
            token.ThrowIfCancellationRequested(); await requireStill(token); token.ThrowIfCancellationRequested();
            if (!Scope(companyId)) throw Failure(403);
        }
        async Task<PurchaseUnorderedAcceptanceWriteV1?> ReplayAsync()
        {
            var row = await (from a in db.UnorderedAcceptanceAttempts.AsNoTracking()
                join f in db.UnorderedAcceptances.AsNoTracking() on new { a.CompanyId, a.AcceptanceId, a.AcceptanceLineId }
                    equals new { f.CompanyId, f.AcceptanceId, f.AcceptanceLineId }
                where a.CompanyId == companyId && a.KeyHash == key
                select new { Attempt = a, Fact = f }).SingleOrDefaultAsync(token);
            await Fence();
            if (row is null) return null;
            if (row.Attempt.ActorId != envelope.ActorId || row.Attempt.RequestHash != requestHash
                || row.Attempt.AcceptanceId != request.AcceptanceId || row.Attempt.AcceptanceLineId != request.AcceptanceLineId)
                throw new PurchasingStoreException(PurchasingStoreCodes.SameKeyPayloadMismatch, "Aynı anahtar farklı içerikle kullanılamaz.");
            var record = Restore(row.Fact);
            if (row.Attempt.RecordJson != row.Fact.RecordJson || !Text(row.Attempt.CorrelationId)
                || record.RecordedBy != envelope.ActorId || PurchaseUnorderedAcceptanceOwnerContractV1.RequestHash(record) != requestHash)
                throw Failure(503);
            return new(record, true, row.Attempt.CorrelationId);
        }
    }

    public async Task<PurchaseUnorderedAcceptanceSourceAnswerV1> ReadAsync(Guid companyId, Guid acceptanceId,
        Guid acceptanceLineId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!company.IsResolved || company.CompanyId == Guid.Empty) return new(PurchaseUnorderedAcceptanceSourceOutcomeV1.Unavailable, null);
        if (!Scope(companyId) || acceptanceId == Guid.Empty || acceptanceLineId == Guid.Empty)
            return new(PurchaseUnorderedAcceptanceSourceOutcomeV1.NotFound, null);
        try
        {
            var row = await (from f in db.UnorderedAcceptances.AsNoTracking()
                from a in db.UnorderedAcceptanceAttempts.AsNoTracking().Where(a => a.CompanyId == f.CompanyId
                    && a.AcceptanceId == f.AcceptanceId && a.AcceptanceLineId == f.AcceptanceLineId).DefaultIfEmpty()
                where f.CompanyId == companyId && f.AcceptanceId == acceptanceId && f.AcceptanceLineId == acceptanceLineId
                select new { Fact = f, Attempt = a }).SingleOrDefaultAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!Scope(companyId)) return new(PurchaseUnorderedAcceptanceSourceOutcomeV1.Unavailable, null);
            if (row is null) return new(PurchaseUnorderedAcceptanceSourceOutcomeV1.NotFound, null);
            var record = Restore(row.Fact);
            if (row.Attempt is null || row.Attempt.RecordJson != row.Fact.RecordJson || row.Attempt.ActorId != record.RecordedBy
                || !Text(row.Attempt.CorrelationId) || row.Attempt.RequestHash != PurchaseUnorderedAcceptanceOwnerContractV1.RequestHash(record))
                return new(PurchaseUnorderedAcceptanceSourceOutcomeV1.Unavailable, null);
            return new(PurchaseUnorderedAcceptanceSourceOutcomeV1.Found, record.Source);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or System.Data.Common.DbException or PurchasingServiceException)
        { return new(PurchaseUnorderedAcceptanceSourceOutcomeV1.Unavailable, null); }
    }

    internal static PurchaseUnorderedAcceptanceRecordV1 Restore(PurchaseUnorderedAcceptanceRow row)
    {
        var record = Restore(row.RecordJson);
        if (record.Source.CompanyId != row.CompanyId || record.Source.AcceptanceId != row.AcceptanceId
            || record.Source.AcceptanceLineId != row.AcceptanceLineId || row.Version != 1 || row.RecordHash != Hash(row.RecordJson))
            throw Failure(503);
        return record;
    }
    internal static PurchaseUnorderedAcceptanceRecordV1 Restore(string json)
    {
        var record = JsonSerializer.Deserialize<PurchaseUnorderedAcceptanceRecordV1>(json, Json);
        if (!PurchaseUnorderedAcceptanceOwnerContractV1.Valid(record)) throw Failure(503);
        return PurchaseUnorderedAcceptanceOwnerContractV1.Freeze(record!);
    }
    private void Entry()
    {
        if (!db.Database.IsRelational() || System.Transactions.Transaction.Current is not null
            || db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges() || auditWriter.HasPendingDrafts) throw Failure(503);
    }
    private async Task CleanupAsync(IDbContextTransaction? transaction, bool rollback, bool knownDeadlock)
    {
        try
        {
            if (transaction is not null && rollback && !(knownDeadlock && transaction.GetDbTransaction() is SqlTransaction { Connection: null }))
                await transaction.RollbackAsync(CancellationToken.None);
        }
        finally
        {
            try { if (transaction is not null) await transaction.DisposeAsync(); }
            finally { try { db.ChangeTracker.Clear(); } finally { auditWriter.DiscardPending(); } }
        }
    }
    private bool Scope(Guid id) => id != Guid.Empty && company.IsResolved && company.CompanyId == id;
    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= 128;
    private static string RequestHash(RecordPurchaseUnorderedAcceptanceV1 request) => PurchaseUnorderedAcceptanceOwnerContractV1.RequestHash(request);
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static bool Deadlock(Exception error) => error is SqlException { Number: 1205 }
        || error is DbUpdateException { InnerException: SqlException { Number: 1205 } };
    private static bool Retryable(Exception error) => Deadlock(error) || error is DbUpdateConcurrencyException
        || error is DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } };
    private static PurchasingServiceException Failure(int status) => new(status, status == 400 ? "validation_failed"
        : status == 403 ? "forbidden" : status == 409 ? PurchasingProblemCodesV1.ConcurrentChange : "service_unavailable");
}

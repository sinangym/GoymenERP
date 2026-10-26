using System.Data;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Transfers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

public sealed class LogisticsTransferOutgoingDocumentStore(
    LogisticsDbContext dbContext,
    ICompanyContext companyContext,
    IAuditEventCollector auditEvents,
    TimeProvider timeProvider)
    : ILogisticsTransferOutgoingDocumentStoreV1, ILogisticsTransferOutgoingDocumentProofSourceV1
{
    private const string EntityType = "Logistics.TransferOutgoingDocument";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<TransferOutgoingDocumentStoreResultV1> StoreDraftAsync(LogisticsWriteEnvelope envelope,
        string requestHash, TransferOutgoingDocument document, Func<CancellationToken, Task> beforeCommit,
        CancellationToken cancellationToken = default)
    {
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(beforeCommit);
        var companyId = RequireCompany();
        if (document.CompanyId != companyId || document.Status != TransferOutgoingDocumentStatus.Draft
            || document.Version != 1 || !string.Equals(document.CreatedBy, envelope.ActorId.Trim(), StringComparison.Ordinal))
            throw Refused(LogisticsStoreCodes.CompanyContextMismatch, "Transfer cikis belgesi aktif sirketin yeni taslagi degil.");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var keyHash = LogisticsStore.IdempotencyKeyHash(envelope.IdempotencyKey);
            var replay = await FindAttemptAsync(LogisticsTransferOutgoingDocumentCommandsV1.CreateDraft,
                keyHash, cancellationToken);
            if (replay is not null)
            {
                RequireSameAttempt(replay, envelope.ActorId.Trim(), requestHash);
                var restored = RestoreResult(replay.ResultJson);
                await beforeCommit(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new(restored, LogisticsWriteOutcome.Replayed, replay.CorrelationId);
            }

            if (await dbContext.TransferOutgoingDocuments.AsNoTracking()
                .AnyAsync(item => item.TransferId == document.TransferId || item.DocumentId == document.DocumentId,
                    cancellationToken))
                throw Refused(LogisticsTransferOutgoingDocumentCodesV1.TransferAlreadyBound,
                    "Transfer veya belge kimligi daha once bir cikis belgesine baglandi.");

            dbContext.TransferOutgoingDocuments.Add(ToRow(document));
            dbContext.TransferOutgoingDocumentLines.AddRange(document.OrderedTransferLineIds.Select((lineId, index) =>
                new TransferOutgoingDocumentLineRow
                {
                    CompanyId = companyId,
                    DocumentId = document.DocumentId,
                    Ordinal = index + 1,
                    TransferLineId = lineId,
                }));
            dbContext.TransferOutgoingDocumentAttempts.Add(Attempt(companyId,
                LogisticsTransferOutgoingDocumentCommandsV1.CreateDraft, keyHash, requestHash, envelope, document));
            Audit(envelope, document, "logistics.transfer-outgoing-document.draft-created.v1",
                "CreateDraft", null, document.Version, "Transfer cikis belgesi stok etkisi olmadan taslak olusturuldu");

            await beforeCommit(cancellationToken);
            await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(document, LogisticsWriteOutcome.Recorded, envelope.CorrelationId);
        }
        catch
        {
            await RollbackAsync(transaction, cancellationToken);
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<TransferOutgoingDocumentStoreResultV1> MutateAsync(LogisticsWriteEnvelope envelope,
        string requestHash, string command, Guid documentId, Action<TransferOutgoingDocument, DateTimeOffset> mutate,
        Func<CancellationToken, Task> beforeCommit, CancellationToken cancellationToken = default)
    {
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireMutation(command);
        if (documentId == Guid.Empty) throw new ArgumentException("Belge kimligi zorunludur.", nameof(documentId));
        ArgumentNullException.ThrowIfNull(mutate);
        ArgumentNullException.ThrowIfNull(beforeCommit);
        var companyId = RequireCompany();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var keyHash = LogisticsStore.IdempotencyKeyHash(envelope.IdempotencyKey);
            var replay = await FindAttemptAsync(command, keyHash, cancellationToken);
            if (replay is not null)
            {
                RequireSameAttempt(replay, envelope.ActorId.Trim(), requestHash);
                var restored = RestoreResult(replay.ResultJson);
                await beforeCommit(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new(restored, LogisticsWriteOutcome.Replayed, replay.CorrelationId);
            }

            var row = await dbContext.TransferOutgoingDocuments.SingleOrDefaultAsync(
                item => item.DocumentId == documentId, cancellationToken)
                ?? throw Refused(LogisticsTransferOutgoingDocumentCodesV1.NotFound, "Transfer cikis belgesi bulunamadi.");
            var lines = await dbContext.TransferOutgoingDocumentLines.AsNoTracking()
                .Where(item => item.DocumentId == documentId).OrderBy(item => item.Ordinal)
                .ToListAsync(cancellationToken);
            var before = Restore(row, lines);
            var beforeVersion = before.Version;
            mutate(before, timeProvider.GetUtcNow());
            Apply(row, before);
            dbContext.TransferOutgoingDocumentAttempts.Add(Attempt(companyId, command, keyHash, requestHash,
                envelope, before));
            Audit(envelope, before,
                command == LogisticsTransferOutgoingDocumentCommandsV1.Finalize
                    ? "logistics.transfer-outgoing-document.finalized.v1"
                    : "logistics.transfer-outgoing-document.cancelled.v1",
                command == LogisticsTransferOutgoingDocumentCommandsV1.Finalize ? "Finalize" : "Cancel",
                beforeVersion, before.Version,
                command == LogisticsTransferOutgoingDocumentCommandsV1.Finalize
                    ? "Kesin transfer cikis belgesi Inventory proofu icin etkinlestirildi; stok etkisi uretilmedi"
                    : "Transfer cikis belgesi iptal edildi; proof yetkisi kapandi");

            await beforeCommit(cancellationToken);
            await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(before, LogisticsWriteOutcome.Recorded, envelope.CorrelationId);
        }
        catch
        {
            await RollbackAsync(transaction, cancellationToken);
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<LogisticsTransferOutgoingDocumentOwnerSnapshotV1?> FindAsync(Guid documentId,
        CancellationToken cancellationToken = default) => await FindProofSnapshotAsync(documentId, cancellationToken);

    public async Task<LogisticsTransferOutgoingDocumentOwnerSnapshotV1?> FindProofSnapshotAsync(Guid documentId,
        CancellationToken cancellationToken = default)
    {
        RequireCompany();
        if (documentId == Guid.Empty) return null;
        var row = await dbContext.TransferOutgoingDocuments.AsNoTracking()
            .SingleOrDefaultAsync(item => item.DocumentId == documentId, cancellationToken);
        if (row is null) return null;
        var lines = await dbContext.TransferOutgoingDocumentLines.AsNoTracking()
            .Where(item => item.DocumentId == documentId).OrderBy(item => item.Ordinal)
            .Select(item => item.TransferLineId).ToListAsync(cancellationToken);
        var state = (TransferOutgoingDocumentStatus)row.Status switch
        {
            TransferOutgoingDocumentStatus.Draft => LogisticsTransferOutgoingDocumentProofStateV1.Draft,
            TransferOutgoingDocumentStatus.Effective => LogisticsTransferOutgoingDocumentProofStateV1.CurrentEffective,
            TransferOutgoingDocumentStatus.Cancelled => LogisticsTransferOutgoingDocumentProofStateV1.Cancelled,
            _ => throw new InvalidOperationException("Stored transfer outgoing-document state is invalid."),
        };
        return new(row.CompanyId, row.TransferId, row.DocumentId, row.Version, lines, state);
    }

    private async Task<TransferOutgoingDocumentAttemptRow?> FindAttemptAsync(string command, string keyHash,
        CancellationToken cancellationToken) => await dbContext.TransferOutgoingDocumentAttempts.AsNoTracking()
        .SingleOrDefaultAsync(item => item.CommandName == command && item.IdempotencyKeyHash == keyHash,
            cancellationToken);

    private static TransferOutgoingDocumentRow ToRow(TransferOutgoingDocument item) => new()
    {
        CompanyId = item.CompanyId,
        DocumentId = item.DocumentId,
        TransferId = item.TransferId,
        Status = (int)item.Status,
        Version = item.Version,
        EDispatchNumber = item.EDispatchNumber,
        CreatedBy = item.CreatedBy,
        CreatedAtUtc = item.CreatedAtUtc,
        EffectiveBy = item.EffectiveBy,
        EffectiveAtUtc = item.EffectiveAtUtc,
        CancelledBy = item.CancelledBy,
        CancelledAtUtc = item.CancelledAtUtc,
        CancellationReason = item.CancellationReason,
    };

    private static void Apply(TransferOutgoingDocumentRow row, TransferOutgoingDocument item)
    {
        row.Status = (int)item.Status;
        row.Version = item.Version;
        row.EffectiveBy = item.EffectiveBy;
        row.EffectiveAtUtc = item.EffectiveAtUtc;
        row.CancelledBy = item.CancelledBy;
        row.CancelledAtUtc = item.CancelledAtUtc;
        row.CancellationReason = item.CancellationReason;
    }

    private static TransferOutgoingDocument Restore(TransferOutgoingDocumentRow row,
        IEnumerable<TransferOutgoingDocumentLineRow> lines) => TransferOutgoingDocument.Restore(
        row.CompanyId, row.DocumentId, row.TransferId, lines.OrderBy(item => item.Ordinal).Select(item => item.TransferLineId),
        row.EDispatchNumber, (TransferOutgoingDocumentStatus)row.Status, row.Version, row.CreatedBy,
        row.CreatedAtUtc, row.EffectiveBy, row.EffectiveAtUtc, row.CancelledBy, row.CancelledAtUtc,
        row.CancellationReason);

    private TransferOutgoingDocumentAttemptRow Attempt(Guid companyId, string command, string keyHash,
        string requestHash, LogisticsWriteEnvelope envelope, TransferOutgoingDocument item) => new()
    {
        CompanyId = companyId,
        CommandName = command,
        IdempotencyKeyHash = keyHash,
        RequestHash = requestHash,
        ActorId = envelope.ActorId.Trim(),
        DocumentId = item.DocumentId,
        ResultJson = JsonSerializer.Serialize(ResultSnapshot.From(item), Json),
        CorrelationId = envelope.CorrelationId.Trim(),
        RecordedAtUtc = timeProvider.GetUtcNow(),
    };

    private static TransferOutgoingDocument RestoreResult(string json)
    {
        var snapshot = JsonSerializer.Deserialize<ResultSnapshot>(json, Json)
            ?? throw new InvalidOperationException("Stored transfer outgoing-document result is invalid.");
        return TransferOutgoingDocument.Restore(snapshot.CompanyId, snapshot.DocumentId, snapshot.TransferId,
            snapshot.OrderedTransferLineIds, snapshot.EDispatchNumber, snapshot.Status, snapshot.Version,
            snapshot.CreatedBy, snapshot.CreatedAtUtc, snapshot.EffectiveBy, snapshot.EffectiveAtUtc,
            snapshot.CancelledBy, snapshot.CancelledAtUtc, snapshot.CancellationReason);
    }

    private void Audit(LogisticsWriteEnvelope envelope, TransferOutgoingDocument item, string eventType,
        string operation, long? beforeVersion, long afterVersion, string reason) => auditEvents.Enqueue(new AuditEventDraft(
        item.CompanyId, eventType, EntityType, item.DocumentId.ToString("D"), operation, beforeVersion, afterVersion,
        AuditActorTypes.User, envelope.ActorId, envelope.PermissionCode, envelope.RoleCodes,
        beforeVersion is null ? "{}" : JsonSerializer.Serialize(new { version = beforeVersion }, Json),
        JsonSerializer.Serialize(new { status = item.Status, version = afterVersion, item.TransferId }, Json),
        reason, ApprovalContextJson: null, AuditAiInvolvement.None, AiContextJson: null, envelope.SourceChannel,
        envelope.ClientIpAddress, envelope.UserAgent, envelope.CorrelationId, envelope.CausationId,
        envelope.IdempotencyKey));

    private Guid RequireCompany() => companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
        ? companyContext.CompanyId
        : throw Refused(LogisticsStoreCodes.CompanyContextMismatch,
            "Aktif sirket baglami olmadan transfer cikis belgesi islenmez.");

    private static void RequireMutation(string command)
    {
        if (command is not (LogisticsTransferOutgoingDocumentCommandsV1.Finalize
            or LogisticsTransferOutgoingDocumentCommandsV1.Cancel))
            throw new ArgumentException("Unknown transfer outgoing-document command.", nameof(command));
    }

    private static void RequireEnvelope(LogisticsWriteEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (string.IsNullOrWhiteSpace(envelope.ActorId) || string.IsNullOrWhiteSpace(envelope.PermissionCode)
            || string.IsNullOrWhiteSpace(envelope.CorrelationId) || string.IsNullOrWhiteSpace(envelope.IdempotencyKey)
            || string.IsNullOrWhiteSpace(envelope.SourceChannel) || envelope.RoleCodes is null)
            throw new ArgumentException("Transfer outgoing-document write requires the complete server envelope.",
                nameof(envelope));
    }

    private static void RequireHash(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash) || hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            throw new ArgumentException("Request hash must be 64 hexadecimal characters.", nameof(hash));
    }

    private static void RequireSameAttempt(TransferOutgoingDocumentAttemptRow attempt, string actor,
        string requestHash)
    {
        if (!string.Equals(attempt.ActorId, actor, StringComparison.Ordinal)
            || !string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
            throw Refused(LogisticsStoreCodes.SameKeyPayloadMismatch,
                "The idempotency key was already used by another actor or payload.");
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw Refused(LogisticsStoreCodes.ConcurrentChange,
                "The transfer outgoing document changed concurrently.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw Refused(LogisticsTransferOutgoingDocumentCodesV1.TransferAlreadyBound,
                "The transfer or document is already bound to an outgoing document.", exception);
        }
        catch (Exception exception) when (ContainsSqlException(exception, 1205))
        {
            throw Refused(LogisticsStoreCodes.ConcurrentChange,
                "The transfer outgoing-document write lost a serialization race.", exception);
        }
    }

    private static async Task RollbackAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        try { await transaction.RollbackAsync(cancellationToken); }
        catch { }
    }

    private static bool ContainsSqlException(Exception exception, int number)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is SqlException sql && sql.Number == number) return true;
        return false;
    }

    private static LogisticsStoreException Refused(string code, string message, Exception? inner = null) =>
        new(code, message, inner);

    private sealed record ResultSnapshot(
        Guid CompanyId,
        Guid DocumentId,
        Guid TransferId,
        Guid[] OrderedTransferLineIds,
        string? EDispatchNumber,
        TransferOutgoingDocumentStatus Status,
        int Version,
        string CreatedBy,
        DateTimeOffset CreatedAtUtc,
        string? EffectiveBy,
        DateTimeOffset? EffectiveAtUtc,
        string? CancelledBy,
        DateTimeOffset? CancelledAtUtc,
        string? CancellationReason)
    {
        public static ResultSnapshot From(TransferOutgoingDocument item) => new(
            item.CompanyId, item.DocumentId, item.TransferId, item.OrderedTransferLineIds.ToArray(),
            item.EDispatchNumber, item.Status, item.Version, item.CreatedBy, item.CreatedAtUtc,
            item.EffectiveBy, item.EffectiveAtUtc, item.CancelledBy, item.CancelledAtUtc,
            item.CancellationReason);
    }
}

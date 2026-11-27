using System.Data;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Domain.WeighingDifferences;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

/// <summary>
/// STK-S24: Logistics's store of the sales weighing-difference record, over its own company-filtered tables only. A record is
/// stored in one serializable transaction together with its attempt row and audit event, after the dispatch note it was
/// evaluated against is re-read and found unchanged (still a draft, same lines, same actors). It also answers the Sales read
/// port (<see cref="ILogisticsWeighingDifferenceReadPortV1"/>) for the server-resolved company only.
/// </summary>
public sealed class LogisticsWeighingDifferenceStore(
    LogisticsDbContext dbContext,
    ICompanyContext companyContext,
    IAuditEventCollector auditEvents,
    TimeProvider timeProvider,
    IAuditTransactionWriterV1? auditWriter = null)
    : ILogisticsWeighingDifferenceStoreV1, ILogisticsWeighingDifferenceReadPortV1
{
    private const string EntityType = "Logistics.WeighingDifference";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<WeighingDifferenceStoreResultV1?> FindReplayAsync(LogisticsWriteEnvelope envelope, string commandName,
        string requestHash, CancellationToken cancellationToken = default)
    {
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireCommand(commandName);
        RequireCompany();
        var attempt = await FindAttemptAsync(commandName, LogisticsStore.IdempotencyKeyHash(envelope.IdempotencyKey),
            cancellationToken);
        if (attempt is null) return null;
        RequireSameAttempt(attempt, envelope.ActorId.Trim(), requestHash);
        return new(RestoreResult(attempt.ResultJson), LogisticsWriteOutcome.Replayed, attempt.CorrelationId);
    }

    public async Task<WeighingDifferenceSourceV1?> ReadSourceAsync(Guid dispatchNoteId,
        CancellationToken cancellationToken = default)
    {
        RequireCompany();
        return dispatchNoteId == Guid.Empty ? null : await ReadSourceCoreAsync(dispatchNoteId, cancellationToken);
    }

    public async Task<WeighingDifferenceStoreResultV1> StoreAsync(LogisticsWriteEnvelope envelope, string requestHash,
        WeighingDifferenceRecord record, WeighingDifferenceSourceV1 expectedSource, Func<CancellationToken, Task> beforeCommit,
        CancellationToken cancellationToken = default)
    {
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(expectedSource);
        ArgumentNullException.ThrowIfNull(beforeCommit);
        var companyId = RequireCompany();
        if (record.CompanyId != companyId || record.Version != 1 || record.Status == WeighingDifferenceRecordStatus.Approved
            || record.DispatchNoteId != expectedSource.DispatchNoteId || record.InstructionId != expectedSource.InstructionId
            || !string.Equals(record.RecordedBy, envelope.ActorId.Trim(), StringComparison.Ordinal))
            throw Refused(LogisticsStoreCodes.CompanyContextMismatch, "Kantar farki kaydi aktif sirketin yeni kaydi degil.");

        RequireOwnedEntry();
        Task Fence(CancellationToken token) => FenceAsync(companyId, beforeCommit, token);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await Fence(cancellationToken);
            var keyHash = LogisticsStore.IdempotencyKeyHash(envelope.IdempotencyKey);
            var replay = await FindAttemptAsync(LogisticsWeighingDifferenceCommandsV1.Record, keyHash, cancellationToken);
            await Fence(cancellationToken);
            if (replay is not null)
            {
                RequireSameAttempt(replay, envelope.ActorId.Trim(), requestHash);
                var restored = RestoreResult(replay.ResultJson);
                await CompleteOwnedAsync(transaction, Fence, cancellationToken);
                return new(restored, LogisticsWriteOutcome.Replayed, replay.CorrelationId);
            }

            var exists = await dbContext.WeighingDifferences.AsNoTracking()
                .AnyAsync(item => item.DispatchNoteId == record.DispatchNoteId || item.RecordId == record.RecordId,
                    cancellationToken);
            await Fence(cancellationToken);
            if (exists)
                throw Refused(LogisticsWeighingDifferenceCodesV1.AlreadyRecorded,
                    "Irsaliye icin kantar farki kaydi zaten var.");

            var current = await ReadSourceCoreAsync(record.DispatchNoteId, cancellationToken);
            await Fence(cancellationToken);
            if (current is null || !SameSource(current, expectedSource))
                throw Refused(LogisticsStoreCodes.ConcurrentChange,
                    "Irsaliye kantar farki degerlendirmesinden sonra degisti; hicbir sey kaydedilmedi.");

            dbContext.WeighingDifferences.Add(ToRow(record));
            dbContext.WeighingDifferenceProducts.AddRange(ToProductRows(record));
            dbContext.WeighingDifferenceEnteredBy.AddRange(record.EnteredBy.Select(actor => new WeighingDifferenceEnteredByRow
            {
                CompanyId = companyId,
                RecordId = record.RecordId,
                ActorId = actor,
            }));
            dbContext.WeighingDifferenceAttempts.Add(Attempt(companyId, LogisticsWeighingDifferenceCommandsV1.Record, keyHash,
                requestHash, envelope, record));
            Audit(envelope, record, "logistics.weighing-difference.recorded.v1", "Record", null,
                record.Status == WeighingDifferenceRecordStatus.PendingApproval
                    ? "Kantar farki kaydedildi; onay bekliyor, stok etkisi uretilmedi"
                    : "Kantar farki kaydedildi; onay gerekmiyor, stok etkisi uretilmedi");

            await Fence(cancellationToken);
            await SaveAsync(cancellationToken);
            await CompleteOwnedAsync(transaction, Fence, cancellationToken);
            return new(record, LogisticsWriteOutcome.Recorded, envelope.CorrelationId);
        }
        catch (Exception exception)
        {
            await AbortOwnedAsync(transaction);
            if (IsDeadlockVictim(exception)) throw LostSerializationRead(exception);
            throw;
        }
    }

    public async Task<WeighingDifferenceStoreResultV1> MutateAsync(LogisticsWriteEnvelope envelope, string requestHash,
        string command, Guid recordId, Action<WeighingDifferenceRecord, DateTimeOffset> mutate,
        Func<CancellationToken, Task> beforeCommit, CancellationToken cancellationToken = default)
    {
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        if (command != LogisticsWeighingDifferenceCommandsV1.Approve)
            throw new ArgumentException("Unknown weighing-difference transition.", nameof(command));
        if (recordId == Guid.Empty) throw new ArgumentException("Kayit kimligi zorunludur.", nameof(recordId));
        ArgumentNullException.ThrowIfNull(mutate);
        ArgumentNullException.ThrowIfNull(beforeCommit);
        var companyId = RequireCompany();

        RequireOwnedEntry();
        Task Fence(CancellationToken token) => FenceAsync(companyId, beforeCommit, token);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await Fence(cancellationToken);
            var keyHash = LogisticsStore.IdempotencyKeyHash(envelope.IdempotencyKey);
            var replay = await FindAttemptAsync(command, keyHash, cancellationToken);
            await Fence(cancellationToken);
            if (replay is not null)
            {
                RequireSameAttempt(replay, envelope.ActorId.Trim(), requestHash);
                var restored = RestoreResult(replay.ResultJson);
                await CompleteOwnedAsync(transaction, Fence, cancellationToken);
                return new(restored, LogisticsWriteOutcome.Replayed, replay.CorrelationId);
            }

            var row = await dbContext.WeighingDifferences.SingleOrDefaultAsync(item => item.RecordId == recordId,
                    cancellationToken);
            await Fence(cancellationToken);
            if (row is null) throw Refused(LogisticsWeighingDifferenceCodesV1.NotFound, "Kantar farki kaydi bulunamadi.");
            var record = await RestoreAsync(row, cancellationToken);
            await Fence(cancellationToken);
            var beforeVersion = record.Version;
            mutate(record, timeProvider.GetUtcNow());
            row.Status = (int)record.Status;
            row.Version = record.Version;
            row.ApprovedBy = record.ApprovedBy;
            row.ApprovedAtUtc = record.ApprovedAtUtc;
            dbContext.WeighingDifferenceAttempts.Add(Attempt(companyId, command, keyHash, requestHash, envelope, record));
            Audit(envelope, record, "logistics.weighing-difference.approved.v1", "Approve", beforeVersion,
                "Kantar farki onaylandi; stok etkisi uretilmedi");

            await Fence(cancellationToken);
            await SaveAsync(cancellationToken);
            await CompleteOwnedAsync(transaction, Fence, cancellationToken);
            return new(record, LogisticsWriteOutcome.Recorded, envelope.CorrelationId);
        }
        catch (Exception exception)
        {
            await AbortOwnedAsync(transaction);
            if (IsDeadlockVictim(exception)) throw LostSerializationRead(exception);
            throw;
        }
    }

    public async Task<WeighingDifferenceRecord?> FindByDispatchNoteAsync(Guid dispatchNoteId,
        CancellationToken cancellationToken = default)
    {
        RequireCompany();
        if (dispatchNoteId == Guid.Empty) return null;
        var row = await dbContext.WeighingDifferences.AsNoTracking()
            .SingleOrDefaultAsync(item => item.DispatchNoteId == dispatchNoteId, cancellationToken);
        return row is null ? null : await RestoreAsync(row, cancellationToken);
    }

    /// <summary>
    /// The Sales read: only for the server-resolved company. A company that is not the resolved one is refused, never answered
    /// with "no record" (which a consumer could read as "nothing waits").
    /// </summary>
    public async Task<LogisticsWeighingDifferenceReadV1?> FindByDispatchNoteAsync(Guid companyId, Guid dispatchNoteId,
        CancellationToken cancellationToken = default)
    {
        var resolved = RequireCompany();
        if (companyId != resolved)
            throw Refused(LogisticsStoreCodes.CompanyContextMismatch, "Kantar farki yalniz aktif sirket icin okunur.");
        var record = await FindByDispatchNoteAsync(dispatchNoteId, cancellationToken);
        return record is null
            ? null
            : new LogisticsWeighingDifferenceReadV1(record.CompanyId, record.DispatchNoteId, record.RecordId,
                record.Evaluation.TheoreticalKg.Value, record.Evaluation.ScaleKg.Value, record.Evaluation.DifferenceKg,
                record.Evaluation.BandLevel.ToString(), record.Evaluation.TotalExcessKg.Value,
                record.Evaluation.Decision.ToString(), record.Status.ToString(),
                record.Status != WeighingDifferenceRecordStatus.PendingApproval, record.Version, record.KilogramAllocation);
    }

    private async Task<WeighingDifferenceSourceV1?> ReadSourceCoreAsync(Guid dispatchNoteId,
        CancellationToken cancellationToken)
    {
        var note = await dbContext.DispatchNotes.AsNoTracking()
            .SingleOrDefaultAsync(item => item.DispatchNoteId == dispatchNoteId, cancellationToken);
        if (note is null) return null;
        var lines = await (from line in dbContext.DispatchNoteLines.AsNoTracking()
                           join instructionLine in dbContext.Lines.AsNoTracking()
                               on new { line.CompanyId, line.InstructionId, LineId = line.InstructionLineId }
                               equals new { instructionLine.CompanyId, instructionLine.InstructionId, instructionLine.LineId }
                           where line.InstructionId == note.InstructionId && line.DispatchNoteId == dispatchNoteId
                           orderby line.LineNo
                           select new WeighingDifferenceSourceLineV1(line.LineNo, line.ShipmentId, line.ProductId,
                               line.Quantity, instructionLine.UnitIsKilogram, line.QuantityKg))
            .ToListAsync(cancellationToken);
        var shipmentIds = lines.Select(line => line.ShipmentId).Distinct().ToList();
        var shipped = (int)LoadingShareKind.Shipped;
        var shipments = await dbContext.Shares.AsNoTracking()
            .Where(share => share.InstructionId == note.InstructionId && share.Kind == shipped
                && shipmentIds.Contains(share.ShareId))
            .Select(share => new { share.ShareId, share.RecordedBy })
            .ToListAsync(cancellationToken);
        var weighers = await dbContext.Weighings.AsNoTracking()
            .Where(weighing => weighing.InstructionId == note.InstructionId)
            .Select(weighing => weighing.RecordedBy)
            .ToListAsync(cancellationToken);

        // SAT-D321 (2): the shipment and weighing actors. A shipment without its share or its actor makes the list incomplete.
        var complete = shipments.Count == shipmentIds.Count
            && shipments.All(share => !string.IsNullOrWhiteSpace(share.RecordedBy))
            && weighers.All(actor => !string.IsNullOrWhiteSpace(actor));
        var enteredBy = shipments.Select(share => share.RecordedBy).Concat(weighers)
            .Where(actor => !string.IsNullOrWhiteSpace(actor))
            .Select(actor => actor!.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        return new WeighingDifferenceSourceV1(note.DispatchNoteId, note.InstructionId,
            note.Status == (int)DispatchNoteStatus.Draft, lines, enteredBy, complete);
    }

    private static bool SameSource(WeighingDifferenceSourceV1 left, WeighingDifferenceSourceV1 right) =>
        left.DispatchNoteId == right.DispatchNoteId && left.InstructionId == right.InstructionId
        && left.IsDraft && right.IsDraft && left.EnteredByComplete == right.EnteredByComplete
        && left.Lines.SequenceEqual(right.Lines) && left.EnteredBy.SequenceEqual(right.EnteredBy, StringComparer.Ordinal);

    private async Task<WeighingDifferenceAttemptRow?> FindAttemptAsync(string command, string keyHash,
        CancellationToken cancellationToken) => await dbContext.WeighingDifferenceAttempts.AsNoTracking()
        .SingleOrDefaultAsync(item => item.CommandName == command && item.IdempotencyKeyHash == keyHash, cancellationToken);

    private async Task<WeighingDifferenceRecord> RestoreAsync(WeighingDifferenceRecordRow row,
        CancellationToken cancellationToken)
    {
        var products = await dbContext.WeighingDifferenceProducts.AsNoTracking()
            .Where(item => item.RecordId == row.RecordId).OrderBy(item => item.Ordinal).ToListAsync(cancellationToken);
        var enteredBy = await dbContext.WeighingDifferenceEnteredBy.AsNoTracking()
            .Where(item => item.RecordId == row.RecordId).Select(item => item.ActorId).ToListAsync(cancellationToken);
        return ResultSnapshot.From(row, products, enteredBy).Restore();
    }

    private static WeighingDifferenceRecordRow ToRow(WeighingDifferenceRecord item) => new()
    {
        CompanyId = item.CompanyId,
        RecordId = item.RecordId,
        InstructionId = item.InstructionId,
        DispatchNoteId = item.DispatchNoteId,
        TheoreticalKg = item.Evaluation.TheoreticalKg.Value,
        ScaleKg = item.Evaluation.ScaleKg.Value,
        DifferenceKg = item.Evaluation.DifferenceKg,
        BandLevel = (int)item.Evaluation.BandLevel,
        WarningBandPercent = item.Evaluation.Bands?.WarningPercent,
        ApprovalBandPercent = item.Evaluation.Bands?.ApprovalPercent,
        BlockBandPercent = item.Evaluation.Bands?.BlockPercent,
        TotalExcessKg = item.Evaluation.TotalExcessKg.Value,
        ApprovalRatePercent = item.Evaluation.ApprovalRatePercent,
        Decision = (int)item.Evaluation.Decision,
        EnteredByComplete = item.EnteredByComplete,
        Status = (int)item.Status,
        Version = item.Version,
        KilogramAllocationJson = item.KilogramAllocation is null ? null : JsonSerializer.Serialize(item.KilogramAllocation, Json),
        RecordedBy = item.RecordedBy,
        RecordedAtUtc = item.RecordedAtUtc,
        ApprovedBy = item.ApprovedBy,
        ApprovedAtUtc = item.ApprovedAtUtc,
    };

    private static IEnumerable<WeighingDifferenceProductRow> ToProductRows(WeighingDifferenceRecord item) =>
        item.Products.Select((product, index) => new WeighingDifferenceProductRow
        {
            CompanyId = item.CompanyId,
            RecordId = item.RecordId,
            Ordinal = index + 1,
            ProductId = product.ProductId,
            NoteKg = product.NoteKg.Value,
            ScaleKg = product.ScaleKg.Value,
            DifferenceKg = product.DifferenceKg,
            RemainingPhysicalKg = product.RemainingPhysicalKg.Value,
            PhysicallyCoveredKg = product.PhysicallyCoveredKg.Value,
            ExcessKg = product.ExcessKg.Value,
            TargetLineNo = product.TargetLineNo,
        });

    private WeighingDifferenceAttemptRow Attempt(Guid companyId, string command, string keyHash, string requestHash,
        LogisticsWriteEnvelope envelope, WeighingDifferenceRecord item) => new()
    {
        CompanyId = companyId,
        CommandName = command,
        IdempotencyKeyHash = keyHash,
        RequestHash = requestHash,
        ActorId = envelope.ActorId.Trim(),
        RecordId = item.RecordId,
        ResultJson = JsonSerializer.Serialize(ResultSnapshot.From(item), Json),
        CorrelationId = envelope.CorrelationId.Trim(),
        RecordedAtUtc = timeProvider.GetUtcNow(),
    };

    private static WeighingDifferenceRecord RestoreResult(string json) =>
        (JsonSerializer.Deserialize<ResultSnapshot>(json, Json)
            ?? throw new InvalidOperationException("Stored weighing-difference result is invalid.")).Restore();

    private void Audit(LogisticsWriteEnvelope envelope, WeighingDifferenceRecord item, string eventType, string operation,
        long? beforeVersion, string reason) => auditEvents.Enqueue(new AuditEventDraft(
        item.CompanyId, eventType, EntityType, item.RecordId.ToString("D"), operation, beforeVersion, item.Version,
        AuditActorTypes.User, envelope.ActorId, envelope.PermissionCode, envelope.RoleCodes,
        beforeVersion is null ? "{}" : JsonSerializer.Serialize(new { version = beforeVersion }, Json),
        JsonSerializer.Serialize(new
        {
            status = item.Status,
            version = item.Version,
            item.DispatchNoteId,
            decision = item.Evaluation.Decision,
            bandLevel = item.Evaluation.BandLevel,
            theoreticalKg = item.Evaluation.TheoreticalKg.Value,
            scaleKg = item.Evaluation.ScaleKg.Value,
            totalExcessKg = item.Evaluation.TotalExcessKg.Value,
            kilogramAllocation = item.KilogramAllocation,
        }, Json),
        reason, ApprovalContextJson: null, AuditAiInvolvement.None, AiContextJson: null, envelope.SourceChannel,
        envelope.ClientIpAddress, envelope.UserAgent, envelope.CorrelationId, envelope.CausationId,
        envelope.IdempotencyKey));

    private Guid RequireCompany() => companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
        ? companyContext.CompanyId
        : throw Refused(LogisticsStoreCodes.CompanyContextMismatch,
            "Aktif sirket baglami olmadan kantar farki islenmez.");

    private static void RequireCommand(string command)
    {
        if (command is not (LogisticsWeighingDifferenceCommandsV1.Record or LogisticsWeighingDifferenceCommandsV1.Approve))
            throw new ArgumentException("Unknown weighing-difference command.", nameof(command));
    }

    private static void RequireEnvelope(LogisticsWriteEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (string.IsNullOrWhiteSpace(envelope.ActorId) || string.IsNullOrWhiteSpace(envelope.PermissionCode)
            || string.IsNullOrWhiteSpace(envelope.CorrelationId) || string.IsNullOrWhiteSpace(envelope.IdempotencyKey)
            || string.IsNullOrWhiteSpace(envelope.SourceChannel) || envelope.RoleCodes is null)
            throw new ArgumentException("Weighing-difference write requires the complete server envelope.", nameof(envelope));
    }

    private static void RequireHash(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash) || hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            throw new ArgumentException("Request hash must be 64 hexadecimal characters.", nameof(hash));
    }

    private static void RequireSameAttempt(WeighingDifferenceAttemptRow attempt, string actor, string requestHash)
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
            throw Refused(LogisticsStoreCodes.ConcurrentChange, "The weighing-difference record changed concurrently.",
                exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw Refused(LogisticsWeighingDifferenceCodesV1.AlreadyRecorded,
                "The dispatch note already has a weighing-difference record.", exception);
        }
        catch (Exception exception) when (ContainsSqlException(exception, 1205))
        {
            throw Refused(LogisticsStoreCodes.ConcurrentChange,
                "The weighing-difference write lost a serialization race.", exception);
        }
    }

    // Refuse before owning cleanup: another caller's transaction, changes and audit drafts are untouched.
    private void RequireOwnedEntry()
    {
        if (auditWriter is null || System.Transactions.Transaction.Current is not null
            || !dbContext.Database.IsRelational() || dbContext.Database.CurrentTransaction is not null
            || dbContext.ChangeTracker.HasChanges() || auditWriter.HasPendingDrafts)
            throw Refused(LogisticsWeighingDifferenceCodesV1.Unavailable,
                "Kantar farki komutu temiz, sahipli bir islem gerektirir.");
    }

    private async Task FenceAsync(Guid companyId, Func<CancellationToken, Task> freshAuthority, CancellationToken token)
    {
        if (RequireCompany() != companyId) throw Refused(LogisticsStoreCodes.CompanyContextMismatch, "Aktif sirket degisti.");
        await freshAuthority(token);
        if (RequireCompany() != companyId) throw Refused(LogisticsStoreCodes.CompanyContextMismatch, "Aktif sirket degisti.");
    }

    private async Task CompleteOwnedAsync(IDbContextTransaction transaction, Func<CancellationToken, Task> fence,
        CancellationToken token)
    {
        await fence(token); // SaveChanges may already have flushed the same scoped collector through its interceptor.
        if (auditWriter!.HasPendingDrafts)
        {
            await auditWriter.WritePendingAsync(transaction.GetDbTransaction(), token);
            await fence(token);
        }
        if (auditWriter.HasPendingDrafts) throw new InvalidOperationException("Pending weighing-difference audit drafts.");
        await fence(token);
        await transaction.CommitAsync(token);
    }

    private async Task AbortOwnedAsync(IDbContextTransaction transaction)
    {
        try { await transaction.RollbackAsync(CancellationToken.None); }
        finally
        {
            try { await transaction.DisposeAsync(); }
            finally { dbContext.ChangeTracker.Clear(); auditWriter!.DiscardPending(); }
        }
    }

    private static bool IsDeadlockVictim(Exception exception) =>
        exception is not LogisticsStoreException && ContainsSqlException(exception, 1205);

    private static LogisticsStoreException LostSerializationRead(Exception exception) =>
        Refused(LogisticsStoreCodes.ConcurrentChange,
            "Kantar farki okuma yarisi kaybedildi; hicbir sey kaydedilmedi.", exception);

    private static bool ContainsSqlException(Exception exception, int number)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is SqlException sql && sql.Number == number) return true;
        return false;
    }

    private static LogisticsStoreException Refused(string code, string message, Exception? inner = null) =>
        new(code, message, inner);

    private sealed record ProductSnapshot(
        Guid ProductId,
        decimal NoteKg,
        decimal ScaleKg,
        decimal DifferenceKg,
        decimal RemainingPhysicalKg,
        decimal PhysicallyCoveredKg,
        decimal ExcessKg,
        int? TargetLineNo);

    /// <summary>The flat record shape shared by the attempt result and the row restore.</summary>
    private sealed record ResultSnapshot(
        Guid CompanyId,
        Guid RecordId,
        Guid InstructionId,
        Guid DispatchNoteId,
        decimal TheoreticalKg,
        decimal ScaleKg,
        decimal DifferenceKg,
        WeighingDifferenceBandLevel BandLevel,
        decimal? WarningBandPercent,
        decimal? ApprovalBandPercent,
        decimal? BlockBandPercent,
        decimal TotalExcessKg,
        decimal? ApprovalRatePercent,
        WeighingDifferenceDecision Decision,
        ProductSnapshot[] Products,
        string[] EnteredBy,
        bool EnteredByComplete,
        WeighingDifferenceRecordStatus Status,
        int Version,
        string RecordedBy,
        DateTimeOffset RecordedAtUtc,
        string? ApprovedBy,
        DateTimeOffset? ApprovedAtUtc, DispatchKilogramSnapshot? KilogramAllocation = null)
    {
        public static ResultSnapshot From(WeighingDifferenceRecord item) => new(
            item.CompanyId, item.RecordId, item.InstructionId, item.DispatchNoteId, item.Evaluation.TheoreticalKg.Value,
            item.Evaluation.ScaleKg.Value, item.Evaluation.DifferenceKg, item.Evaluation.BandLevel,
            item.Evaluation.Bands?.WarningPercent, item.Evaluation.Bands?.ApprovalPercent, item.Evaluation.Bands?.BlockPercent,
            item.Evaluation.TotalExcessKg.Value, item.Evaluation.ApprovalRatePercent, item.Evaluation.Decision,
            [.. item.Products.Select(product => new ProductSnapshot(product.ProductId, product.NoteKg.Value,
                product.ScaleKg.Value, product.DifferenceKg, product.RemainingPhysicalKg.Value,
                product.PhysicallyCoveredKg.Value, product.ExcessKg.Value, product.TargetLineNo))],
            [.. item.EnteredBy], item.EnteredByComplete, item.Status, item.Version, item.RecordedBy, item.RecordedAtUtc,
            item.ApprovedBy, item.ApprovedAtUtc, item.KilogramAllocation);

        public static ResultSnapshot From(WeighingDifferenceRecordRow row, IEnumerable<WeighingDifferenceProductRow> products,
            IEnumerable<string> enteredBy) => new(
            row.CompanyId, row.RecordId, row.InstructionId, row.DispatchNoteId, row.TheoreticalKg, row.ScaleKg,
            row.DifferenceKg, (WeighingDifferenceBandLevel)row.BandLevel, row.WarningBandPercent, row.ApprovalBandPercent,
            row.BlockBandPercent, row.TotalExcessKg, row.ApprovalRatePercent, (WeighingDifferenceDecision)row.Decision,
            [.. products.OrderBy(item => item.Ordinal).Select(item => new ProductSnapshot(item.ProductId, item.NoteKg,
                item.ScaleKg, item.DifferenceKg, item.RemainingPhysicalKg, item.PhysicallyCoveredKg, item.ExcessKg,
                item.TargetLineNo))],
            [.. enteredBy], row.EnteredByComplete, (WeighingDifferenceRecordStatus)row.Status, row.Version, row.RecordedBy,
            row.RecordedAtUtc, row.ApprovedBy, row.ApprovedAtUtc,
            row.KilogramAllocationJson is null ? null : JsonSerializer.Deserialize<DispatchKilogramSnapshot>(row.KilogramAllocationJson, Json)
                ?? throw new InvalidOperationException("Stored dispatch kilogram distribution is unreadable."));

        public WeighingDifferenceRecord Restore()
        {
            var bands = WarningBandPercent is { } warning && ApprovalBandPercent is { } approval && BlockBandPercent is { } block
                ? new WeighingDifferenceBands(warning, approval, block)
                : null;
            var evaluation = new WeighingDifferenceEvaluation(Kg(TheoreticalKg), Kg(ScaleKg), DifferenceKg, BandLevel, bands,
                [.. Products.Select(product => new WeighingDifferenceProductResult(product.ProductId, Kg(product.NoteKg),
                    Kg(product.ScaleKg), product.DifferenceKg, Kg(product.RemainingPhysicalKg),
                    Kg(product.PhysicallyCoveredKg), Kg(product.ExcessKg), product.TargetLineNo))],
                Kg(TotalExcessKg), ApprovalRatePercent, Decision);
            return WeighingDifferenceRecord.Restore(CompanyId, RecordId, DispatchNoteId, InstructionId, evaluation, EnteredBy,
                EnteredByComplete, Status, Version, RecordedBy, RecordedAtUtc, ApprovedBy, ApprovedAtUtc, KilogramAllocation);
        }

        private static Quantity Kg(decimal value) => Quantity.Of(value, QuantityUnit.Kilogram);
    }
}

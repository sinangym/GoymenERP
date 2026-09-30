using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Domain.Quantities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

/// <summary>
/// The Logistics store of the server-resolved company (LOG-4a; the Sales SALES-P2 shape). One call is
/// one save; a stored instruction is rebuilt through the domain in recording order (lines, issue, then
/// every share as the command that recorded it, the weighing at its share position and every dispatch-note step — create, approve,
/// cancel, e-İrsaliye number — in its order), so a corrupt row is refused, not repaired, and the
/// stored version must be the one the rebuild reaches. Block toggles are not an event in this slice
/// (no owner source yet): a blocked line is restored with one block at the end, and a line that was
/// blocked and unblocked cannot be represented — LOG-5 stores the block source's events.
/// </summary>
public sealed class LogisticsStore(LogisticsDbContext dbContext, ICompanyContext companyContext, IAuditEventCollector auditEvents, TimeProvider timeProvider)
    : ILogisticsInstructionStore
{
    private const string EntityType = "Logistics.LoadingInstruction";
    private const int ActorMaximumLength = 128;
    private const int KeyMaximumLength = 200;
    private const int MaximumPageSize = 100;
    private const int AttemptOutcomeRecorded = 1;
    private const int AttemptOutcomeRefused = 2;
    // decimal(18,3): 15 integer digits and 3 decimals; a value outside is refused before the save, never a provider exception (FİNANS #350 P3-2).
    private const decimal ColumnBound = 1_000_000_000_000_000m;
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public async Task<LogisticsStoreResult> StoreInstructionAsync(LogisticsWriteEnvelope envelope, string requestHash, DateOnly effectiveOn,
        Func<LoadingInstruction> build, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(build);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        var companyId = RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await FindAttemptAsync(LogisticsCommands.CreateV1, keyHash, cancellationToken);
        if (attempt is not null)
        {
            RequireSameContent(attempt, actorId, requestHash);
            if (attempt.Outcome != AttemptOutcomeRecorded || attempt.InstructionId is not { } recordedId)
            {
                throw Refused(LogisticsStoreCodes.ConcurrentChange, "Bu anahtar kayıtlı bir terminal reddi taşıyor; tekrar aynı reddi alır.");
            }

            var stored = await FindInstructionAsync(recordedId, cancellationToken) ?? throw new InvalidOperationException("Kayıtlı denemenin talimatı bulunamadı.");
            return new LogisticsStoreResult(stored, LogisticsWriteOutcome.Replayed, attempt.CorrelationId);
        }

        var instruction = build() ?? throw new ArgumentException("Talimat kurulmadı.", nameof(build));
        if (instruction.CompanyId != companyId)
        {
            throw Refused(LogisticsStoreCodes.CompanyContextMismatch, "Talimat aktif şirket bağlamının şirketine ait değil.");
        }

        if (instruction.Status != LoadingInstructionStatus.Draft || instruction.Lines.Count == 0 || instruction.Shares.Count > 0)
        {
            throw new ArgumentException("Yeni talimat satırlı bir taslaktır; payı olamaz.", nameof(build));
        }

        if (await dbContext.Instructions.AsNoTracking().AnyAsync(row => row.Id == instruction.Id, cancellationToken))
        {
            throw Refused(LogisticsStoreCodes.ConcurrentChange, "Bu talimat kimliği şirketin başka bir talimatında kullanılıyor.");
        }

        var now = timeProvider.GetUtcNow();
        dbContext.Instructions.Add(new LoadingInstructionRow
        {
            Id = instruction.Id,
            CompanyId = companyId,
            CustomerPartyId = instruction.CustomerPartyId,
            CompanyAccountId = instruction.CompanyAccountId,
            Status = (int)instruction.Status,
            Version = instruction.Version,
            EffectiveOn = effectiveOn,
            CreatedAtUtc = instruction.CreatedAtUtc,
            UpdatedAtUtc = instruction.UpdatedAtUtc,
            CreatedBy = actorId,
            CorrelationId = envelope.CorrelationId.Trim(),
        });
        var lineNo = 0;
        foreach (var line in instruction.Lines)
        {
            dbContext.Lines.Add(new LoadingInstructionLineRow
            {
                CompanyId = companyId,
                InstructionId = instruction.Id,
                LineNo = ++lineNo,
                LineId = line.Id,
                SourceOrderId = line.Source.OrderId,
                SourceOrderLineId = line.Source.LineId,
                SourceOrderRevision = line.Source.Revision,
                ProductId = line.ProductId,
                UnitId = line.UnitId,
                UnitIsKilogram = line.InstructedQuantity.Unit == QuantityUnit.Kilogram,
                AllocationId = line.AllocationId,
                Instructed = RequireFits(line.InstructedQuantity.Value),
                Loaded = 0m,
                Shipped = 0m,
                Delivered = 0m,
                IsBlocked = line.IsBlocked,
            });
        }

        dbContext.Attempts.Add(Attempt(companyId, LogisticsCommands.CreateV1, keyHash, requestHash, actorId, envelope, instruction.Id, now));
        Audit(envelope, companyId, instruction.Id, "logistics.loading-instruction.created.v1", "Create", null, instruction.Version,
            "{}", Snapshot(instruction), "Yükleme talimatı açıldı");
        await SaveAsync(cancellationToken);
        return new LogisticsStoreResult(new StoredLoadingInstruction(instruction, effectiveOn), LogisticsWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    public async Task<LogisticsReplay?> FindReplayAsync(LogisticsWriteEnvelope envelope, string requestHash, string commandName,
        CancellationToken cancellationToken = default)
    {
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireCommand(commandName);
        RequireResolvedCompany();
        var attempt = await FindAttemptAsync(commandName, IdempotencyKeyHash(envelope.IdempotencyKey), cancellationToken);
        if (attempt is null)
        {
            return null;
        }

        RequireSameContent(attempt, envelope.ActorId.Trim(), requestHash);
        if (attempt.Outcome == AttemptOutcomeRecorded && attempt.InstructionId is { } instructionId)
        {
            var stored = await FindInstructionAsync(instructionId, cancellationToken) ?? throw new InvalidOperationException("Kayıtlı denemenin talimatı bulunamadı.");
            return new LogisticsReplay(stored, null, attempt.CorrelationId);
        }

        var refusal = attempt.RefusalJson is null ? null : JsonSerializer.Deserialize<LogisticsTerminalRefusal>(attempt.RefusalJson, Json);
        if (refusal is null || refusal.Status is < 400 or > 599 || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null)
        {
            throw new InvalidOperationException("Kayıtlı terminal ret okunamadı.");
        }

        return new LogisticsReplay(null, refusal, attempt.CorrelationId);
    }

    public async Task RecordRefusalAsync(LogisticsWriteEnvelope envelope, string requestHash, string commandName, LogisticsTerminalRefusal refusal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireCommand(commandName);
        var companyId = RequireResolvedCompany();
        if (refusal.Status is < 400 or > 599 || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null
            || refusal.Errors.Any(error => error is null || string.IsNullOrWhiteSpace(error.Field) || string.IsNullOrWhiteSpace(error.Code)))
        {
            throw new ArgumentException("Terminal ret bir durum, bir kod ve tam alan hataları taşır.", nameof(refusal));
        }

        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        if (await FindAttemptAsync(commandName, keyHash, cancellationToken) is not null)
        {
            throw Refused(LogisticsStoreCodes.ConcurrentChange, "Bu anahtar zaten bir deneme taşıyor; tekrar onu alır.");
        }

        var row = Attempt(companyId, commandName, keyHash, requestHash, envelope.ActorId.Trim(), envelope, null, timeProvider.GetUtcNow());
        row.Outcome = AttemptOutcomeRefused;
        row.RefusalJson = JsonSerializer.Serialize(refusal, Json);
        dbContext.Attempts.Add(row);
        await SaveAsync(cancellationToken);
    }

    public async Task<LogisticsStoreResult?> MutateInstructionAsync(LogisticsWriteEnvelope envelope, string requestHash, string commandName,
        Guid instructionId, Action<LoadingInstruction, DateTimeOffset> mutate, LogisticsMutationAudit audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        ArgumentNullException.ThrowIfNull(audit);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireCommand(commandName);
        if (!LogisticsCommands.IsMutation(commandName))
        {
            throw new ArgumentException("Yalnız yayımlama, pay, kantar tartımı ve irsaliye komutları bir talimatı değiştirir.", nameof(commandName));
        }

        if (string.IsNullOrWhiteSpace(audit.EventType) || string.IsNullOrWhiteSpace(audit.Operation) || string.IsNullOrWhiteSpace(audit.Reason))
        {
            throw new ArgumentException("Komutun audit olayı tür, işlem ve gerekçe ister.", nameof(audit));
        }

        var companyId = RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await FindAttemptAsync(commandName, keyHash, cancellationToken);
        if (attempt is not null)
        {
            RequireSameContent(attempt, actorId, requestHash);
            if (attempt.Outcome != AttemptOutcomeRecorded || attempt.InstructionId is not { } recordedId)
            {
                throw Refused(LogisticsStoreCodes.ConcurrentChange, "Bu anahtar kayıtlı bir terminal reddi taşıyor; tekrar aynı reddi alır.");
            }

            var stored = await FindInstructionAsync(recordedId, cancellationToken) ?? throw new InvalidOperationException("Kayıtlı denemenin talimatı bulunamadı.");
            return new LogisticsStoreResult(stored, LogisticsWriteOutcome.Replayed, attempt.CorrelationId);
        }

        if (instructionId == Guid.Empty)
        {
            return null;
        }

        // The header and the lines are tracked (the header's version is this write's concurrency token); the shares are read as before.
        var row = await dbContext.Instructions.SingleOrDefaultAsync(candidate => candidate.Id == instructionId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var lineRows = await dbContext.Lines.Where(line => line.InstructionId == instructionId).OrderBy(line => line.LineNo).ToListAsync(cancellationToken);
        var shareRows = await dbContext.Shares.AsNoTracking().Where(share => share.InstructionId == instructionId).OrderBy(share => share.ShareNo)
            .ToListAsync(cancellationToken);
        var weighingRow = await dbContext.Weighings.AsNoTracking().SingleOrDefaultAsync(weighing => weighing.InstructionId == instructionId, cancellationToken);
        // AT-055: the dispatch notes and their lines are tracked too — a lifecycle step writes its columns and releases lines in place.
        var noteRows = await dbContext.DispatchNotes.Where(note => note.InstructionId == instructionId).OrderBy(note => note.NoteNo)
            .ToListAsync(cancellationToken);
        var noteLineRows = await dbContext.DispatchNoteLines.Where(line => line.InstructionId == instructionId)
            .ToListAsync(cancellationToken);
        var instruction = Restore(row, lineRows, shareRows, weighingRow, noteRows, noteLineRows);
        var versionBefore = instruction.Version;
        var before = Snapshot(instruction);
        var sharesBefore = instruction.Shares.Count;
        var weighingBefore = instruction.Weighing;
        var notesBefore = instruction.DispatchNotes.ToArray();
        var stepsBefore = instruction.DispatchSteps;
        var now = timeProvider.GetUtcNow();
        // The domain decides; a refusal leaves the tracked rows untouched (nothing below has run).
        mutate(instruction, now);
        var weighingAdded = weighingBefore is null && instruction.Weighing is not null;
        var noteAdded = instruction.DispatchNotes.Count == notesBefore.Length + 1;
        var notesChanged = Enumerable.Range(0, Math.Min(notesBefore.Length, instruction.DispatchNotes.Count))
            .Where(index => !ReferenceEquals(notesBefore[index], instruction.DispatchNotes[index])).ToArray();
        var dispatchStep = noteAdded || notesChanged.Length > 0;
        if (instruction.Version != versionBefore + 1 || instruction.Id != row.Id || instruction.CompanyId != companyId
            || instruction.Shares.Count > sharesBefore + 1 || instruction.Lines.Count != lineRows.Count
            || (weighingBefore is not null && !ReferenceEquals(weighingBefore, instruction.Weighing))
            || (weighingAdded && instruction.Shares.Count != sharesBefore)
            || (instruction.DispatchNotes.Count != notesBefore.Length && !noteAdded)
            || (noteAdded && notesChanged.Length > 0) || notesChanged.Length > 1
            || instruction.DispatchSteps != stepsBefore + (dispatchStep ? 1 : 0)
            || (dispatchStep && (instruction.Shares.Count != sharesBefore || weighingAdded)))
        {
            throw new InvalidOperationException(
                "Bir komut tam olarak bir sürüm ilerletir ve en fazla bir pay, bir kantar tartımı ya da bir irsaliye adımı (taslak, onay, iptal, e-İrsaliye no) ekler.");
        }

        if (noteAdded && await DispatchNoteIdTakenAsync(instruction.DispatchNotes[^1].DispatchNoteId, instruction.Id, cancellationToken))
        {
            // #518 P2: a note id is one note in the COMPANY. Reused on another instruction it is the same terminal refusal as on this
            // one (recorded under the key), never a unique-key race answered as concurrent_change.
            dbContext.ChangeTracker.Clear();
            throw new LogisticsRuleException(LogisticsRuleCodes.DuplicateDispatchNoteId, "Bu irsaliye kimliği şirketin başka bir talimatında kullanılıyor.");
        }

        DispatchNoteDraft? changedNote = notesChanged.Length == 1 ? instruction.DispatchNotes[notesChanged[0]] : null;
        if (changedNote is { EDispatchNumber: { } eDispatchNumber } && notesBefore[notesChanged[0]].EDispatchNumber is null
            && await dbContext.DispatchNotes.AsNoTracking().AnyAsync(note => note.EDispatchNumber == eDispatchNumber
                && note.DispatchNoteId != changedNote.DispatchNoteId, cancellationToken))
        {
            // Unique per company: another instruction's note carries it (this instruction's own were refused by the aggregate).
            dbContext.ChangeTracker.Clear();
            throw new LogisticsRuleException(LogisticsRuleCodes.EDispatchNumberInUse, "Bu e-İrsaliye numarası şirketin başka bir irsaliyesinde kayıtlı.");
        }

        if (changedNote is { Status: DispatchNoteStatus.Approved, Number: null })
        {
            // The system number, in this same save: the company series moves by one under its concurrency token.
            changedNote = instruction.AssignDispatchNoteNumber(changedNote.DispatchNoteId, await NextDispatchNoteNumberAsync(companyId, now, cancellationToken));
        }

        row.Status = (int)instruction.Status;
        row.Version = instruction.Version;
        row.UpdatedAtUtc = instruction.UpdatedAtUtc;
        foreach (var lineRow in lineRows)
        {
            var line = instruction.Lines.Single(candidate => candidate.Id == lineRow.LineId);
            lineRow.Loaded = line.LoadedQuantity.Value;
            lineRow.Shipped = line.ShippedQuantity.Value;
            lineRow.Delivered = line.DeliveredQuantity.Value;
            lineRow.IsBlocked = line.IsBlocked;
        }

        if (instruction.Shares.Count == sharesBefore + 1)
        {
            var share = instruction.Shares[^1];
            dbContext.Shares.Add(new LoadingShareRow
            {
                CompanyId = companyId,
                InstructionId = instruction.Id,
                ShareNo = sharesBefore + 1,
                ShareId = share.ShareId,
                LineId = share.LineId,
                Kind = (int)share.Kind,
                Quantity = RequireFits(share.Quantity.Value),
                Measurement = share.Measurement is { } measurement ? (int)measurement : null,
                SourceShipmentId = share.SourceShipmentId,
                RecordedAtUtc = share.RecordedAtUtc,
            });
        }

        if (weighingAdded && instruction.Weighing is { } weighing)
        {
            // AT-053: appended once; the key (company, instruction) refuses a second record in SQL Server too.
            dbContext.Weighings.Add(new VehicleWeighingRow
            {
                CompanyId = companyId,
                InstructionId = instruction.Id,
                WeighingId = weighing.WeighingId,
                Direction = (int)weighing.Direction,
                FirstWeighingKg = RequireFits(weighing.FirstWeighing.Value),
                SecondWeighingKg = RequireFits(weighing.SecondWeighing.Value),
                NetKg = RequireFits(weighing.Net.Value),
                LoadedKgAtWeighing = RequireFits(weighing.LoadedAtWeighing.Value),
                DifferenceKg = weighing.DifferenceKg < 0m ? -RequireFits(-weighing.DifferenceKg) : RequireFits(weighing.DifferenceKg),
                SharesBefore = weighing.SharesBefore,
                // The operator is this write's actor (its column width is the envelope's, measured above).
                RecordedBy = string.Equals(weighing.RecordedBy, actorId, StringComparison.Ordinal)
                    ? actorId
                    : throw new InvalidOperationException("Kantar tartımını kaydeden, bu yazmanın aktörüdür."),
                RecordedAtUtc = weighing.RecordedAtUtc,
            });
        }

        if (noteAdded)
        {
            AddDispatchNote(companyId, instruction, instruction.DispatchNotes[^1], notesBefore.Length + 1, actorId);
        }

        if (changedNote is not null)
        {
            WriteDispatchNoteStep(noteRows.Single(candidate => candidate.DispatchNoteId == changedNote.DispatchNoteId), changedNote, noteLineRows, actorId);
        }

        dbContext.Attempts.Add(Attempt(companyId, commandName, keyHash, requestHash, actorId, envelope, instruction.Id, now));
        Audit(envelope, companyId, instruction.Id, audit.EventType, audit.Operation, versionBefore, instruction.Version, before, Snapshot(instruction), audit.Reason);
        await SaveAsync(cancellationToken);
        return new LogisticsStoreResult(new StoredLoadingInstruction(instruction, row.EffectiveOn), LogisticsWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    /// <summary>
    /// AT-055: appends the draft and its lines once. Each line copies its instruction line's Sales binding, product and unit (so
    /// the Sales order read needs no rebuild); the key (company, instruction, shipment) refuses a second link in SQL Server too.
    /// </summary>
    private void AddDispatchNote(Guid companyId, LoadingInstruction instruction, DispatchNoteDraft note, int noteNo, string actorId)
    {
        dbContext.DispatchNotes.Add(new DispatchNoteRow
        {
            CompanyId = companyId,
            InstructionId = instruction.Id,
            DispatchNoteId = note.DispatchNoteId,
            NoteNo = noteNo,
            Status = (int)note.Status,
            SharesBefore = note.SharesBefore,
            CreatedStep = note.CreatedStep,
            // The preparer is this write's actor (its column width is the envelope's, measured above).
            CreatedBy = string.Equals(note.CreatedBy, actorId, StringComparison.Ordinal)
                ? actorId
                : throw new InvalidOperationException("İrsaliye taslağını hazırlayan, bu yazmanın aktörüdür."),
            CreatedAtUtc = note.CreatedAtUtc,
        });
        foreach (var line in note.Lines)
        {
            var source = instruction.Lines.Single(candidate => candidate.Id == line.InstructionLineId);
            dbContext.DispatchNoteLines.Add(new DispatchNoteLineRow
            {
                CompanyId = companyId,
                InstructionId = instruction.Id,
                DispatchNoteId = note.DispatchNoteId,
                LineNo = line.LineNo,
                ShipmentId = line.ShipmentId,
                InstructionLineId = line.InstructionLineId,
                SourceOrderId = source.Source.OrderId,
                SourceOrderLineId = source.Source.LineId,
                ProductId = source.ProductId,
                UnitId = source.UnitId,
                Quantity = RequireFits(line.Quantity.Value),
            });
        }
    }

    /// <summary>
    /// AT-055 lifecycle: writes the one step the domain took on a stored note — its status and the group that step fills (approval
    /// with the system number, cancellation, or the e-İrsaliye number), each by this write's actor; a cancellation releases the
    /// note's lines so the filtered single-link key lets their shipments onto a new note. The write guard refuses anything else.
    /// </summary>
    private static void WriteDispatchNoteStep(DispatchNoteRow row, DispatchNoteDraft note, IReadOnlyList<DispatchNoteLineRow> lineRows, string actorId)
    {
        string Actor(string? value) => string.Equals(value, actorId, StringComparison.Ordinal)
            ? actorId
            : throw new InvalidOperationException("İrsaliye adımını yapan, bu yazmanın aktörüdür.");

        if (row.ApprovedStep is null && note.ApprovedStep is { } approvedStep)
        {
            row.ApprovedBy = Actor(note.ApprovedBy);
            row.ApprovedAtUtc = note.ApprovedAtUtc;
            row.ApprovedStep = approvedStep;
            row.SeriesCode = note.Number?.SeriesCode ?? throw new InvalidOperationException("Onaylanan irsaliyenin numarası yok.");
            row.SequenceNo = note.Number.SequenceNo;
        }

        if (row.CancelledStep is null && note.CancelledStep is { } cancelledStep)
        {
            row.CancelledBy = Actor(note.CancelledBy);
            row.CancelledAtUtc = note.CancelledAtUtc;
            row.CancelReason = note.CancelReason;
            row.CancelledStep = cancelledStep;
            foreach (var line in lineRows.Where(candidate => candidate.DispatchNoteId == note.DispatchNoteId))
            {
                line.Released = true;
            }
        }

        if (row.EDispatchNumberStep is null && note.EDispatchNumberStep is { } eDispatchStep)
        {
            row.EDispatchNumber = note.EDispatchNumber;
            row.EDispatchNumberSetBy = Actor(note.EDispatchNumberSetBy);
            row.EDispatchNumberSetAtUtc = note.EDispatchNumberSetAtUtc;
            row.EDispatchNumberStep = eDispatchStep;
        }

        row.Status = (int)note.Status;
    }

    /// <summary>
    /// The company's next system number (user decision 2026-09-30: company-based series, gap-free, concurrency-safe). The series row
    /// is read tracked and moved by one in the approval's own save; its last number is the concurrency token, so a concurrent
    /// approval that read the same number fails its UPDATE (or, for a company's first number, its INSERT on the key) and nothing
    /// of it is saved — the loser answers a retryable <c>concurrent_change</c>, never a gap or a duplicate. A stored note already
    /// at or above the next number means the series was tampered with: refused, not repaired.
    /// </summary>
    private async Task<DispatchNoteNumber> NextDispatchNoteNumberAsync(Guid companyId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string seriesCode = DispatchNoteNumber.DefaultSeriesCode;
        var series = await dbContext.DispatchNoteSeries.SingleOrDefaultAsync(candidate => candidate.SeriesCode == seriesCode, cancellationToken);
        if (series is null)
        {
            series = new DispatchNoteSeriesRow { CompanyId = companyId, SeriesCode = seriesCode, LastSequenceNo = 1, CreatedAtUtc = now, UpdatedAtUtc = now };
            dbContext.DispatchNoteSeries.Add(series);
        }
        else
        {
            series.LastSequenceNo = checked(series.LastSequenceNo + 1);
            series.UpdatedAtUtc = now;
        }

        var next = series.LastSequenceNo;
        if (await dbContext.DispatchNotes.AsNoTracking().AnyAsync(note => note.SeriesCode == seriesCode && note.SequenceNo >= next, cancellationToken))
        {
            throw new InvalidOperationException("İrsaliye serisinin son numarası kayıtlı irsaliyelerin gerisinde; seri onarılmaz, reddedilir.");
        }

        return DispatchNoteNumber.Of(seriesCode, next);
    }

    private Task<bool> DispatchNoteIdTakenAsync(Guid dispatchNoteId, Guid instructionId, CancellationToken cancellationToken) =>
        dbContext.DispatchNotes.AsNoTracking().AnyAsync(note => note.DispatchNoteId == dispatchNoteId && note.InstructionId != instructionId,
            cancellationToken);

    public async Task<StoredLoadingInstruction?> FindInstructionAsync(Guid instructionId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var row = await dbContext.Instructions.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == instructionId, cancellationToken);
        return row is null ? null : (await RestoreAsync([row], cancellationToken)).Single();
    }

    public async Task<IReadOnlyList<StoredDispatchNoteReference>> ListSalesOrderDispatchNotesAsync(Guid salesOrderId, int limit,
        CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        if (limit is < 1 or > MaximumPageSize + 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        if (salesOrderId == Guid.Empty)
        {
            return [];
        }

        // Company-filtered and untracked: the drafts with a line on this order, oldest first, then only this order's lines.
        var noteIds = dbContext.DispatchNoteLines.AsNoTracking().Where(line => line.SourceOrderId == salesOrderId).Select(line => line.DispatchNoteId);
        var notes = await dbContext.DispatchNotes.AsNoTracking().Where(note => noteIds.Contains(note.DispatchNoteId))
            .OrderBy(note => note.CreatedAtUtc).ThenBy(note => note.DispatchNoteId).Take(limit).ToListAsync(cancellationToken);
        if (notes.Count == 0)
        {
            return [];
        }

        var ids = notes.Select(note => note.DispatchNoteId).ToArray();
        var lines = await dbContext.DispatchNoteLines.AsNoTracking()
            .Where(line => line.SourceOrderId == salesOrderId && ids.Contains(line.DispatchNoteId)).ToListAsync(cancellationToken);
        return [.. notes.Select(note => new StoredDispatchNoteReference(note.DispatchNoteId, note.InstructionId, (DispatchNoteStatus)note.Status,
            note.CreatedAtUtc,
            [.. lines.Where(line => line.DispatchNoteId == note.DispatchNoteId).OrderBy(line => line.LineNo)
                .Select(line => new StoredDispatchNoteOrderLine(line.LineNo, line.SourceOrderLineId, line.ShipmentId, line.UnitId, line.Quantity))],
            note.SeriesCode is { } series && note.SequenceNo is { } sequence ? DispatchNoteNumber.Of(series, sequence) : null,
            note.EDispatchNumber))];
    }

    public async Task<LogisticsInstructionPage> ListInstructionsAsync(int pageSize, string? cursor, LoadingInstructionStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var companyId = RequireResolvedCompany();
        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), $"Sayfa boyutu 1–{MaximumPageSize} olmalıdır.");
        }

        var query = dbContext.Instructions.AsNoTracking();
        if (status is { } wanted)
        {
            if (!Enum.IsDefined(wanted))
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }

            var value = (int)wanted;
            query = query.Where(row => row.Status == value);
        }

        if (cursor is not null)
        {
            var (createdAtUtc, id) = ListCursor.Decode(cursor, companyId);
            query = query.Where(row => row.CreatedAtUtc < createdAtUtc || (row.CreatedAtUtc == createdAtUtc && row.Id.CompareTo(id) < 0));
        }

        var rows = await query.OrderByDescending(row => row.CreatedAtUtc).ThenByDescending(row => row.Id).Take(pageSize + 1).ToListAsync(cancellationToken);
        var hasMore = rows.Count > pageSize;
        var page = hasMore ? rows.Take(pageSize).ToList() : rows;
        var last = page.LastOrDefault();
        return new LogisticsInstructionPage(await RestoreAsync(page, cancellationToken),
            hasMore && last is not null ? ListCursor.Encode(companyId, last.CreatedAtUtc, last.Id) : null);
    }

    // ---- restore --------------------------------------------------------------------------------

    private async Task<IReadOnlyList<StoredLoadingInstruction>> RestoreAsync(IReadOnlyList<LoadingInstructionRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(row => row.Id).ToArray();
        var lines = await dbContext.Lines.AsNoTracking().Where(line => ids.Contains(line.InstructionId)).ToListAsync(cancellationToken);
        var shares = await dbContext.Shares.AsNoTracking().Where(share => ids.Contains(share.InstructionId)).ToListAsync(cancellationToken);
        var weighings = await dbContext.Weighings.AsNoTracking().Where(weighing => ids.Contains(weighing.InstructionId)).ToListAsync(cancellationToken);
        var notes = await dbContext.DispatchNotes.AsNoTracking().Where(note => ids.Contains(note.InstructionId)).ToListAsync(cancellationToken);
        var noteLines = await dbContext.DispatchNoteLines.AsNoTracking().Where(line => ids.Contains(line.InstructionId)).ToListAsync(cancellationToken);
        return [.. rows.Select(row => new StoredLoadingInstruction(
            Restore(row, [.. lines.Where(line => line.InstructionId == row.Id).OrderBy(line => line.LineNo)],
                [.. shares.Where(share => share.InstructionId == row.Id).OrderBy(share => share.ShareNo)],
                weighings.SingleOrDefault(weighing => weighing.InstructionId == row.Id),
                [.. notes.Where(note => note.InstructionId == row.Id).OrderBy(note => note.NoteNo)],
                [.. noteLines.Where(line => line.InstructionId == row.Id)]),
            row.EffectiveOn))];
    }

    /// <summary>
    /// Rebuilds the instruction through the domain in recording order; the stored version and totals must be the ones the rebuild
    /// reaches. The weighbridge record (AT-053) is replayed after the shares it was recorded after, so the domain recalculates its
    /// net, loaded snapshot and difference; the stored ones must be the same. The dispatch-note drafts (AT-055) are replayed the
    /// same way, in their order, after the weighing of the same position; the domain rebuilds each line from its shipment, and the
    /// stored lines must be the ones it reaches.
    /// </summary>
    private static LoadingInstruction Restore(LoadingInstructionRow row, IReadOnlyList<LoadingInstructionLineRow> lines, IReadOnlyList<LoadingShareRow> shares,
        VehicleWeighingRow? weighing, IReadOnlyList<DispatchNoteRow> notes, IReadOnlyList<DispatchNoteLineRow> noteLines)
    {
        var instruction = LoadingInstruction.Create(row.Id, row.CompanyId, row.CustomerPartyId, row.CompanyAccountId, row.CreatedAtUtc);
        foreach (var line in lines)
        {
            var unit = line.UnitIsKilogram ? QuantityUnit.Kilogram : QuantityUnit.Piece;
            instruction.AddLine(instruction.Version, line.LineId, new SourceOrderLineRef(line.SourceOrderId, line.SourceOrderLineId, line.SourceOrderRevision),
                line.ProductId, line.UnitId, line.AllocationId, Quantity.Of(line.Instructed, unit), row.CreatedAtUtc);
        }

        if (row.Status == (int)LoadingInstructionStatus.Issued)
        {
            instruction.Issue(instruction.Version, row.UpdatedAtUtc);
        }

        if (weighing is not null && (weighing.SharesBefore < 0 || weighing.SharesBefore > shares.Count))
        {
            throw new InvalidOperationException("Kayıtlı kantar tartımının pay sırası paylarla uyuşmuyor.");
        }

        if (notes.Where((note, index) => note.NoteNo != index + 1 || note.SharesBefore < 1 || note.SharesBefore > shares.Count
                || (index > 0 && (note.SharesBefore < notes[index - 1].SharesBefore || note.CreatedStep <= notes[index - 1].CreatedStep))).Any()
            || noteLines.Any(line => !notes.Any(note => note.DispatchNoteId == line.DispatchNoteId)))
        {
            throw new InvalidOperationException("Kayıtlı irsaliye taslaklarının sırası paylarla uyuşmuyor.");
        }

        // AT-055 lifecycle: every dispatch-note step (create, approve, cancel, e-İrsaliye number) in the order it was taken — the
        // steps are exactly 1..n. A create waits for its share position; the other steps need no share, so each follows the step
        // before it.
        var steps = DispatchNoteSteps(notes);
        var nextStep = 0;

        for (var index = 0; index <= shares.Count; index++)
        {
            if (weighing is not null && weighing.SharesBefore == index)
            {
                RestoreWeighing(instruction, weighing);
            }

            while (nextStep < steps.Count && (steps[nextStep].Kind != DispatchStepKind.Create || steps[nextStep].Note.SharesBefore == index))
            {
                var (_, kind, note) = steps[nextStep++];
                RestoreDispatchStep(instruction, kind, note,
                    [.. noteLines.Where(line => line.DispatchNoteId == note.DispatchNoteId).OrderBy(line => line.LineNo)]);
            }

            if (index == shares.Count)
            {
                break;
            }

            var share = shares[index];
            var line = lines.Single(candidate => candidate.LineId == share.LineId);
            var quantity = Quantity.Of(share.Quantity, line.UnitIsKilogram ? QuantityUnit.Kilogram : QuantityUnit.Piece);
            switch ((LoadingShareKind)share.Kind)
            {
                case LoadingShareKind.Loaded:
                    instruction.RecordLoad(instruction.Version, share.ShareId, share.LineId, quantity, (MeasurementKind)(share.Measurement ?? 0), share.RecordedAtUtc);
                    break;
                case LoadingShareKind.Shipped:
                    instruction.MarkShipped(instruction.Version, share.ShareId, share.LineId, quantity, share.RecordedAtUtc);
                    break;
                case LoadingShareKind.Delivered:
                    instruction.MarkDelivered(instruction.Version, share.ShareId, share.LineId, share.SourceShipmentId ?? Guid.Empty, quantity, share.RecordedAtUtc);
                    break;
                default:
                    throw new InvalidOperationException("Kayıtlı pay türü bilinmiyor.");
            }
        }

        if (nextStep != steps.Count
            || notes.Any(note => instruction.DispatchNotes.Single(candidate => candidate.DispatchNoteId == note.DispatchNoteId).Status != (DispatchNoteStatus)note.Status)
            || noteLines.Any(line => line.Released != (notes.Single(note => note.DispatchNoteId == line.DispatchNoteId).Status == (int)DispatchNoteStatus.Cancelled)))
        {
            throw new InvalidOperationException("Kayıtlı irsaliye adımları, durumları ya da satırların serbest bırakılışı yeniden kurulanla uyuşmuyor.");
        }

        foreach (var line in lines.Where(line => line.IsBlocked))
        {
            instruction.BlockLine(instruction.Version, line.LineId, row.UpdatedAtUtc);
        }

        if (instruction.Version != row.Version || instruction.Status != (LoadingInstructionStatus)row.Status)
        {
            throw new InvalidOperationException("Kayıtlı talimatın sürümü paylarından yeniden kurulan sürümle uyuşmuyor.");
        }

        foreach (var line in lines)
        {
            var rebuilt = instruction.Lines.Single(candidate => candidate.Id == line.LineId);
            if (rebuilt.LoadedQuantity.Value != line.Loaded || rebuilt.ShippedQuantity.Value != line.Shipped || rebuilt.DeliveredQuantity.Value != line.Delivered)
            {
                throw new InvalidOperationException("Kayıtlı satır toplamları paylarından yeniden kurulan toplamlarla uyuşmuyor.");
            }
        }

        return instruction;
    }

    private enum DispatchStepKind
    {
        Create = 0,
        Approve = 1,
        Cancel = 2,
        EDispatchNumber = 3,
    }

    /// <summary>The stored notes' steps in their order; a gap, a repeat or an unknown status is a corrupt record, refused.</summary>
    private static List<(int Step, DispatchStepKind Kind, DispatchNoteRow Note)> DispatchNoteSteps(IReadOnlyList<DispatchNoteRow> notes)
    {
        var steps = new List<(int Step, DispatchStepKind Kind, DispatchNoteRow Note)>();
        foreach (var note in notes)
        {
            if (!Enum.IsDefined((DispatchNoteStatus)note.Status))
            {
                throw new InvalidOperationException("Kayıtlı irsaliye durumu bilinmiyor.");
            }

            steps.Add((note.CreatedStep, DispatchStepKind.Create, note));
            if (note.ApprovedStep is { } approved)
            {
                steps.Add((approved, DispatchStepKind.Approve, note));
            }

            if (note.CancelledStep is { } cancelled)
            {
                steps.Add((cancelled, DispatchStepKind.Cancel, note));
            }

            if (note.EDispatchNumberStep is { } eDispatch)
            {
                steps.Add((eDispatch, DispatchStepKind.EDispatchNumber, note));
            }
        }

        steps.Sort((left, right) => left.Step.CompareTo(right.Step));
        if (steps.Where((step, index) => step.Step != index + 1).Any())
        {
            throw new InvalidOperationException("Kayıtlı irsaliye adımları 1'den başlayan kesintisiz bir sıra değil.");
        }

        return steps;
    }

    private static void RestoreDispatchStep(LoadingInstruction instruction, DispatchStepKind kind, DispatchNoteRow row, IReadOnlyList<DispatchNoteLineRow> lines)
    {
        DispatchNoteDraft restored;
        switch (kind)
        {
            case DispatchStepKind.Create:
                RestoreDispatchNote(instruction, row, lines);
                return;
            case DispatchStepKind.Approve:
                instruction.ApproveDispatchNote(instruction.Version, row.DispatchNoteId, row.ApprovedBy!, row.ApprovedAtUtc!.Value);
                restored = instruction.AssignDispatchNoteNumber(row.DispatchNoteId, DispatchNoteNumber.Of(row.SeriesCode!, row.SequenceNo!.Value));
                if (restored.ApprovedStep != row.ApprovedStep)
                {
                    throw new InvalidOperationException("Kayıtlı irsaliye onayının adımı yeniden kurulanla uyuşmuyor.");
                }

                return;
            case DispatchStepKind.Cancel:
                restored = instruction.CancelDispatchNote(instruction.Version, row.DispatchNoteId, row.CancelledBy!, row.CancelReason!, row.CancelledAtUtc!.Value);
                if (restored.CancelledStep != row.CancelledStep || restored.CancelReason != row.CancelReason)
                {
                    throw new InvalidOperationException("Kayıtlı irsaliye iptalinin adımı ya da gerekçesi yeniden kurulanla uyuşmuyor.");
                }

                return;
            default:
                restored = instruction.SetEDispatchNumber(instruction.Version, row.DispatchNoteId, row.EDispatchNumber!, row.EDispatchNumberSetBy!,
                    row.EDispatchNumberSetAtUtc!.Value);
                if (restored.EDispatchNumberStep != row.EDispatchNumberStep)
                {
                    throw new InvalidOperationException("Kayıtlı e-İrsaliye numarasının adımı yeniden kurulanla uyuşmuyor.");
                }

                return;
        }
    }

    private static void RestoreDispatchNote(LoadingInstruction instruction, DispatchNoteRow row, IReadOnlyList<DispatchNoteLineRow> lines)
    {
        if (lines.Count == 0)
        {
            throw new InvalidOperationException("Kayıtlı irsaliye taslağının satırları okunamadı.");
        }

        var restored = instruction.CreateDispatchNoteDraft(instruction.Version, row.DispatchNoteId, [.. lines.Select(line => line.ShipmentId)],
            row.CreatedBy, row.CreatedAtUtc);
        if (restored.Lines.Count != lines.Count || restored.SharesBefore != row.SharesBefore || restored.CreatedStep != row.CreatedStep)
        {
            throw new InvalidOperationException("Kayıtlı irsaliye taslağı yeniden kurulan taslakla uyuşmuyor.");
        }

        foreach (var line in lines)
        {
            var rebuilt = restored.Lines.SingleOrDefault(candidate => candidate.LineNo == line.LineNo);
            var source = rebuilt is null ? null : instruction.Lines.Single(candidate => candidate.Id == rebuilt.InstructionLineId);
            if (rebuilt is null || source is null || rebuilt.ShipmentId != line.ShipmentId || rebuilt.InstructionLineId != line.InstructionLineId
                || rebuilt.Quantity.Value != line.Quantity || source.Source.OrderId != line.SourceOrderId || source.Source.LineId != line.SourceOrderLineId
                || source.ProductId != line.ProductId || source.UnitId != line.UnitId)
            {
                throw new InvalidOperationException("Kayıtlı irsaliye taslağı satırı sevkinden yeniden kurulan satırla uyuşmuyor.");
            }
        }
    }

    private static void RestoreWeighing(LoadingInstruction instruction, VehicleWeighingRow row)
    {
        var restored = instruction.RecordWeighing(instruction.Version, row.WeighingId, (WeighingDirection)row.Direction,
            Quantity.Of(row.FirstWeighingKg, QuantityUnit.Kilogram), Quantity.Of(row.SecondWeighingKg, QuantityUnit.Kilogram), row.RecordedBy,
            row.RecordedAtUtc);
        if (restored.Net.Value != row.NetKg || restored.LoadedAtWeighing.Value != row.LoadedKgAtWeighing || restored.DifferenceKg != row.DifferenceKg)
        {
            throw new InvalidOperationException("Kayıtlı kantar tartımı yeniden hesaplanan net, yüklenen ve fark ile uyuşmuyor.");
        }
    }

    // ---- helpers --------------------------------------------------------------------------------

    private Task<LoadingAttemptRow?> FindAttemptAsync(string commandName, string keyHash, CancellationToken cancellationToken) =>
        dbContext.Attempts.AsNoTracking().SingleOrDefaultAsync(row => row.CommandName == commandName && row.IdempotencyKeyHash == keyHash, cancellationToken);

    private static void RequireSameContent(LoadingAttemptRow attempt, string actorId, string requestHash)
    {
        // ADR-011 §3: another content or another actor under the same key is one 409 that discloses nothing.
        if (!string.Equals(attempt.ActorId, actorId, StringComparison.Ordinal) || !string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw Refused(LogisticsStoreCodes.SameKeyPayloadMismatch, "Aynı Idempotency-Key başka bir istekle geldi; ilk cevap bu isteğe verilmez.");
        }
    }

    private static LoadingAttemptRow Attempt(Guid companyId, string commandName, string keyHash, string requestHash, string actorId,
        LogisticsWriteEnvelope envelope, Guid? instructionId, DateTimeOffset now) => new LoadingAttemptRow()
    {
        CompanyId = companyId,
        CommandName = commandName,
        IdempotencyKeyHash = keyHash,
        RequestHash = requestHash,
        ActorId = actorId,
        Outcome = AttemptOutcomeRecorded,
        InstructionId = instructionId,
        CorrelationId = envelope.CorrelationId.Trim(),
        RecordedAtUtc = now,
    };

    private void Audit(LogisticsWriteEnvelope envelope, Guid companyId, Guid instructionId, string eventType, string operation,
        long? versionBefore, long versionAfter, string before, string after, string reason) =>
        auditEvents.Enqueue(new AuditEventDraft(companyId, eventType, EntityType, instructionId.ToString("D"), operation, versionBefore, versionAfter,
            AuditActorTypes.User, envelope.ActorId, envelope.PermissionCode, envelope.RoleCodes, before, after, reason, ApprovalContextJson: null,
            AuditAiInvolvement.None, AiContextJson: null, envelope.SourceChannel, envelope.ClientIpAddress, envelope.UserAgent, envelope.CorrelationId,
            envelope.CausationId, envelope.IdempotencyKey));

    private static string Snapshot(LoadingInstruction instruction) => JsonSerializer.Serialize(new
    {
        status = instruction.Status.ToString(),
        version = instruction.Version,
        lines = instruction.Lines.Select(line => new
        {
            lineId = line.Id, instructed = line.InstructedQuantity.Value, loaded = line.LoadedQuantity.Value,
            shipped = line.ShippedQuantity.Value, delivered = line.DeliveredQuantity.Value, blocked = line.IsBlocked,
        }),
        shares = instruction.Shares.Count,
        // AT-053: the weighbridge facts in the audit trail; no tolerance verdict exists to record (OPEN-004).
        weighing = instruction.Weighing is { } weighing
            ? new
            {
                weighingId = weighing.WeighingId, direction = weighing.Direction.ToString(), first = weighing.FirstWeighing.Value,
                second = weighing.SecondWeighing.Value, net = weighing.Net.Value, loaded = weighing.LoadedAtWeighing.Value,
                difference = weighing.DifferenceKg, recordedBy = weighing.RecordedBy,
            }
            : null,
        // AT-055: the dispatch notes in the audit trail — which shipment is on which note, with its shipped quantity, and the
        // lifecycle: the system number and approver, the canceller and the reason, the e-İrsaliye number.
        dispatchNotes = instruction.DispatchNotes.Select(note => new
        {
            dispatchNoteId = note.DispatchNoteId, status = note.Status.ToString(), createdBy = note.CreatedBy,
            number = note.Number?.Text, approvedBy = note.ApprovedBy, cancelledBy = note.CancelledBy, cancelReason = note.CancelReason,
            eDispatchNumber = note.EDispatchNumber,
            lines = note.Lines.Select(line => new { line.LineNo, line.ShipmentId, line.InstructionLineId, quantity = line.Quantity.Value }),
        }),
    }, Json);

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            dbContext.ChangeTracker.Clear();
            throw Refused(LogisticsStoreCodes.ConcurrentChange, "Talimat başka bir yazmayla değişti; hiçbir şey kaydedilmedi.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Only unique-key violations are recognised here (the Idempotency / Inventory / Sales pattern): the attempt key,
            // the instruction id or a line / share id lost a race; the loser re-reads the winner's result afresh.
            dbContext.ChangeTracker.Clear();
            throw Refused(LogisticsStoreCodes.ConcurrentChange, "Kalıcı tekillik kuralı başka bir yazmanın lehine sonuçlandı; hiçbir şey kaydedilmedi.", exception);
        }
    }

    private Guid RequireResolvedCompany() =>
        companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
            ? companyContext.CompanyId
            : throw Refused(LogisticsStoreCodes.CompanyContextMismatch, "Aktif şirket bağlamı olmadan yükleme talimatı işlenmez.");

    private static void RequireEnvelope(LogisticsWriteEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (string.IsNullOrWhiteSpace(envelope.ActorId) || string.IsNullOrWhiteSpace(envelope.PermissionCode) || string.IsNullOrWhiteSpace(envelope.CorrelationId)
            || string.IsNullOrWhiteSpace(envelope.SourceChannel) || envelope.RoleCodes is null)
        {
            throw new ArgumentException("Talimat yazması aktör, izin, correlation, idempotency ve kanal bilgisi ister.", nameof(envelope));
        }

        if (string.IsNullOrWhiteSpace(envelope.IdempotencyKey) || !string.Equals(envelope.IdempotencyKey, envelope.IdempotencyKey.Trim(), StringComparison.Ordinal)
            || envelope.IdempotencyKey.Length > KeyMaximumLength)
        {
            throw new ArgumentException("Idempotency-Key kanonik ve en fazla 200 karakter olmalıdır.", nameof(envelope));
        }

        if (envelope.ActorId.Trim().Length > ActorMaximumLength || envelope.CorrelationId.Trim().Length > 128)
        {
            throw new LogisticsStoreException(LogisticsStoreCodes.ValueDoesNotFit, "Aktör veya correlation kimliği sütununa sığmıyor.");
        }
    }

    private static void RequireHash(string requestHash)
    {
        if (string.IsNullOrWhiteSpace(requestHash) || requestHash.Length != 64 || !requestHash.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("İstek özeti 64 onaltılık karakter olmalıdır.", nameof(requestHash));
        }
    }

    private static void RequireCommand(string commandName)
    {
        if (!LogisticsCommands.IsKnown(commandName))
        {
            throw new ArgumentException("Bilinmeyen komut adı.", nameof(commandName));
        }
    }

    /// <summary>ADR-011 §2: the attempt identity is the uppercase SHA-256 of the trimmed key; the raw key is never stored.</summary>
    public static string IdempotencyKeyHash(string idempotencyKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey.Trim())));

    private static LogisticsStoreException Refused(string code, string message, Exception? inner = null) => new LogisticsStoreException(code, message, inner);

    private static decimal RequireFits(decimal value) =>
        value < ColumnBound && Quantity.SignificantDecimals(value) <= Quantity.KilogramScale
            ? value
            : throw Refused(LogisticsStoreCodes.ValueDoesNotFit, "Miktar decimal(18,3) sütununa sığmıyor; hiçbir şey kaydedilmedi.");

    /// <summary>The opaque, company-bound list cursor (the Sales shape): base64url of company | createdAtUtc ticks | id.</summary>
    internal static class ListCursor
    {
        public static string Encode(Guid companyId, DateTimeOffset createdAtUtc, Guid id)
        {
            var bytes = new byte[16 + 8 + 16];
            companyId.TryWriteBytes(bytes.AsSpan(0, 16));
            BitConverter.TryWriteBytes(bytes.AsSpan(16, 8), createdAtUtc.UtcTicks);
            id.TryWriteBytes(bytes.AsSpan(24, 16));
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static (DateTimeOffset CreatedAtUtc, Guid Id) Decode(string cursor, Guid companyId)
        {
            byte[] bytes;
            try
            {
                var padded = cursor.Replace('-', '+').Replace('_', '/');
                bytes = Convert.FromBase64String(padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '='));
            }
            catch (FormatException exception)
            {
                throw Refused(LogisticsStoreCodes.InvalidCursor, "Liste imleci çözülemedi.", exception);
            }

            if (bytes.Length != 40 || new Guid(bytes.AsSpan(0, 16)) != companyId)
            {
                throw Refused(LogisticsStoreCodes.InvalidCursor, "Liste imleci bu şirketin değil ya da bozuk.");
            }

            return (new DateTimeOffset(BitConverter.ToInt64(bytes, 16), TimeSpan.Zero), new Guid(bytes.AsSpan(24, 16)));
        }
    }
}

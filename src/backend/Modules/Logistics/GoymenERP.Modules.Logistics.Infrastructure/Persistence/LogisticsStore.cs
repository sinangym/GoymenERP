using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Domain.Quantities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

/// <summary>
/// The Logistics store of the server-resolved company (LOG-4a; the Sales SALES-P2 shape). One call is
/// one save; a stored instruction is rebuilt through the domain in recording order (lines, issue, then
/// every share as the command that recorded it), so a corrupt row is refused, not repaired, and the
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
        Func<LoadingInstruction> build, CancellationToken cancellationToken = default,
        IReadOnlyList<LogisticsOrderLineAggregateLimit>? aggregateLimits = null)
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

        var limits = aggregateLimits ?? [];
        await using IDbContextTransaction? aggregateTransaction = limits.Count > 0 && dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        await RequireAggregateLimitsAsync(companyId, instruction, limits, cancellationToken);

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
            "{}", Snapshot(instruction, limits), "Yükleme talimatı açıldı");
        await SaveAsync(cancellationToken);
        if (aggregateTransaction is not null)
        {
            await aggregateTransaction.CommitAsync(cancellationToken);
        }
        return new LogisticsStoreResult(new StoredLoadingInstruction(instruction, effectiveOn), LogisticsWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    private async Task RequireAggregateLimitsAsync(Guid companyId, LoadingInstruction instruction,
        IReadOnlyList<LogisticsOrderLineAggregateLimit> limits, CancellationToken cancellationToken)
    {
        if (limits.Count == 0)
        {
            return;
        }

        if (limits.Select(limit => limit.InstructionLineId).Distinct().Count() != limits.Count)
        {
            throw new ArgumentException("Aynı talimat satırı için iki aggregate limit verilemez.", nameof(limits));
        }

        foreach (var group in limits.GroupBy(limit => new { limit.CompanyId, limit.OrderId, limit.OrderLineId, limit.UnitId }))
        {
            var snapshots = group.Select(limit => new
            {
                limit.SalesOrderVersion, limit.MaximumAggregateQuantity, limit.MaximumAggregateQuantityText,
                limit.SourceOwner, limit.SourceId, limit.SourceVersion, limit.EffectiveOn,
            }).Distinct().ToArray();
            if (group.Key.CompanyId != companyId || snapshots.Length != 1 || snapshots[0].MaximumAggregateQuantity <= 0m
                || string.IsNullOrWhiteSpace(snapshots[0].MaximumAggregateQuantityText)
                || string.IsNullOrWhiteSpace(snapshots[0].SourceOwner) || string.IsNullOrWhiteSpace(snapshots[0].SourceId)
                || string.IsNullOrWhiteSpace(snapshots[0].SourceVersion))
            {
                throw new ArgumentException("Aggregate limit snapshot'ı eksik veya kendiyle çelişiyor.", nameof(limits));
            }

            var lineIds = group.Select(limit => limit.InstructionLineId).ToHashSet();
            var newLines = instruction.Lines.Where(line => lineIds.Contains(line.Id)).ToArray();
            if (newLines.Length != lineIds.Count || newLines.Any(line => line.Source.OrderId != group.Key.OrderId
                || line.Source.LineId != group.Key.OrderLineId || line.UnitId != group.Key.UnitId))
            {
                throw new ArgumentException("Aggregate limit talimat satırının Sales bağıyla uyuşmuyor.", nameof(limits));
            }

            var foreignUnit = await dbContext.Lines.AsNoTracking().AnyAsync(row => row.SourceOrderId == group.Key.OrderId
                && row.SourceOrderLineId == group.Key.OrderLineId && row.UnitId != group.Key.UnitId, cancellationToken);
            if (foreignUnit)
            {
                throw new InvalidOperationException("Aynı Sales satırı iki farklı Logistics biriminde aggregate edilemez.");
            }

            // Serializable + IX_LoadingInstructionLines_SourceOrderLine holds the key range while this instruction is inserted.
            var current = await dbContext.Lines.AsNoTracking()
                .Where(row => row.SourceOrderId == group.Key.OrderId && row.SourceOrderLineId == group.Key.OrderLineId
                    && row.UnitId == group.Key.UnitId)
                .SumAsync(row => (decimal?)row.Instructed, cancellationToken) ?? 0m;
            var requested = newLines.Sum(line => line.InstructedQuantity.Value);
            if (current + requested > snapshots[0].MaximumAggregateQuantity)
            {
                throw Refused(LogisticsStoreCodes.AggregateLimitExceeded,
                    "Sales satırının tüm yükleme talimatları toplamı owner-resolved maksimumu aşıyor.");
            }
        }
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
            throw new ArgumentException("Yalnız yayımlama, pay ve kantar tartımı komutları bir talimatı değiştirir.", nameof(commandName));
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
        var instruction = Restore(row, lineRows, shareRows, weighingRow);
        var versionBefore = instruction.Version;
        var before = Snapshot(instruction);
        var sharesBefore = instruction.Shares.Count;
        var weighingBefore = instruction.Weighing;
        var now = timeProvider.GetUtcNow();
        // The domain decides; a refusal leaves the tracked rows untouched (nothing below has run).
        mutate(instruction, now);
        var weighingAdded = weighingBefore is null && instruction.Weighing is not null;
        if (instruction.Version != versionBefore + 1 || instruction.Id != row.Id || instruction.CompanyId != companyId
            || instruction.Shares.Count > sharesBefore + 1 || instruction.Lines.Count != lineRows.Count
            || (weighingBefore is not null && !ReferenceEquals(weighingBefore, instruction.Weighing))
            || (weighingAdded && instruction.Shares.Count != sharesBefore))
        {
            throw new InvalidOperationException("Bir komut tam olarak bir sürüm ilerletir ve en fazla bir pay ya da bir kantar tartımı ekler.");
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

        dbContext.Attempts.Add(Attempt(companyId, commandName, keyHash, requestHash, actorId, envelope, instruction.Id, now));
        Audit(envelope, companyId, instruction.Id, audit.EventType, audit.Operation, versionBefore, instruction.Version, before, Snapshot(instruction), audit.Reason);
        await SaveAsync(cancellationToken);
        return new LogisticsStoreResult(new StoredLoadingInstruction(instruction, row.EffectiveOn), LogisticsWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    public async Task<StoredLoadingInstruction?> FindInstructionAsync(Guid instructionId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var row = await dbContext.Instructions.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == instructionId, cancellationToken);
        return row is null ? null : (await RestoreAsync([row], cancellationToken)).Single();
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
        return [.. rows.Select(row => new StoredLoadingInstruction(
            Restore(row, [.. lines.Where(line => line.InstructionId == row.Id).OrderBy(line => line.LineNo)],
                [.. shares.Where(share => share.InstructionId == row.Id).OrderBy(share => share.ShareNo)],
                weighings.SingleOrDefault(weighing => weighing.InstructionId == row.Id)),
            row.EffectiveOn))];
    }

    /// <summary>
    /// Rebuilds the instruction through the domain in recording order; the stored version and totals must be the ones the rebuild
    /// reaches. The weighbridge record (AT-053) is replayed after the shares it was recorded after, so the domain recalculates its
    /// net, loaded snapshot and difference; the stored ones must be the same.
    /// </summary>
    private static LoadingInstruction Restore(LoadingInstructionRow row, IReadOnlyList<LoadingInstructionLineRow> lines, IReadOnlyList<LoadingShareRow> shares,
        VehicleWeighingRow? weighing)
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

        for (var index = 0; index <= shares.Count; index++)
        {
            if (weighing is not null && weighing.SharesBefore == index)
            {
                RestoreWeighing(instruction, weighing);
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

    private static string Snapshot(LoadingInstruction instruction) =>
        JsonSerializer.Serialize(new
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
        }, Json);

    private static string Snapshot(LoadingInstruction instruction, IReadOnlyList<LogisticsOrderLineAggregateLimit> aggregateLimits)
    {
            if (aggregateLimits.Count == 0)
            {
                return Snapshot(instruction);
            }

            return JsonSerializer.Serialize(new
            {
                status = instruction.Status.ToString(),
                version = instruction.Version,
                lines = instruction.Lines.Select(line => new
                {
                    lineId = line.Id, instructed = line.InstructedQuantity.Value, loaded = line.LoadedQuantity.Value,
                    shipped = line.ShippedQuantity.Value, delivered = line.DeliveredQuantity.Value, blocked = line.IsBlocked,
                }),
                shares = instruction.Shares.Count,
                aggregateLimits = aggregateLimits.Select(limit => new
                {
                    instructionLineId = limit.InstructionLineId, companyId = limit.CompanyId, orderId = limit.OrderId,
                    orderLineId = limit.OrderLineId, unitId = limit.UnitId, salesOrderVersion = limit.SalesOrderVersion,
                    maximumAggregateQuantity = limit.MaximumAggregateQuantityText, sourceOwner = limit.SourceOwner,
                    sourceId = limit.SourceId, sourceVersion = limit.SourceVersion, effectiveOn = limit.EffectiveOn,
                }),
                // AT-053: the weighbridge facts in the audit trail; no tolerance verdict exists to record (OPEN-004).
                weighing = instruction.Weighing is { } weighing
                    ? new
                    {
                        weighingId = weighing.WeighingId, direction = weighing.Direction.ToString(), first = weighing.FirstWeighing.Value,
                        second = weighing.SecondWeighing.Value, net = weighing.Net.Value, loaded = weighing.LoadedAtWeighing.Value,
                        difference = weighing.DifferenceKg, recordedBy = weighing.RecordedBy,
                    }
                    : null,
            }, Json);
    }

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
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 1205 })
        {
            dbContext.ChangeTracker.Clear();
            throw Refused(LogisticsStoreCodes.ConcurrentChange, "Aggregate yazma yarışı kaybedildi; hiçbir şey kaydedilmedi.", exception);
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

using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.GoodsReceipts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>
/// ALC-S16 (F-027; KLN-D41, KLN-D43, KLN-D45): the bulk goods receipt records of the server-resolved company. One call is one save:
/// the batch change, its attempt and its audit event commit together, inside the host transaction when the caller opened one (the
/// posting path) and alone otherwise (a pending submission or a rejection). Rows are read back strictly: an unknown status, outcome or
/// code source is refused, never repaired. The PurchasingStore attempt/refusal shape is mirrored on the batch's own attempt table.
/// </summary>
public sealed class PurchaseGoodsReceiptBatchStore(PurchasingDbContext dbContext, ICompanyContext companyContext, IAuditEventCollector auditEvents,
    TimeProvider timeProvider) : IPurchaseGoodsReceiptBatchStore
{
    private const string BatchEntityType = "Purchasing.GoodsReceiptBatch";
    private const int ActorMaximumLength = 128;
    private const int KeyMaximumLength = 200;
    private const int CorrelationMaximumLength = 128;
    private const int ProductVersionMaximumLength = 64;
    private const int MaximumTake = 1_000;
    private const int AttemptOutcomeRecorded = 1;
    private const int AttemptOutcomeRefused = 2;
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    // decimal(18,3): 15 integer digits and 3 decimals; a value outside is refused before the save, never a provider exception.
    private const decimal ColumnBound = 1_000_000_000_000_000m;
    private const int KgScale = 3;

    // ---- reads ----------------------------------------------------------------------------------

    public async Task<PurchaseGoodsReceiptBatchReplay?> FindReplayAsync(PurchasingWriteEnvelope envelope, string requestHash, string commandName,
        CancellationToken cancellationToken = default)
    {
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireCommand(commandName);
        RequireResolvedCompany();
        var attempt = await FindAttemptAsync(commandName, PurchasingStore.IdempotencyKeyHash(envelope.IdempotencyKey), cancellationToken);
        if (attempt is null)
        {
            return null;
        }

        RequireSameContent(attempt, envelope.ActorId.Trim(), requestHash);
        if (attempt.Outcome == AttemptOutcomeRecorded)
        {
            return attempt.BatchId is { } batchId
                ? new PurchaseGoodsReceiptBatchReplay(batchId, null, attempt.CorrelationId)
                : throw new InvalidOperationException("Kayıtlı deneme bir toplu mal kabul taşımıyor.");
        }

        var refusal = attempt.RefusalJson is null ? null : JsonSerializer.Deserialize<PurchasingTerminalRefusal>(attempt.RefusalJson, Json);
        if (refusal is null || refusal.Status is < 400 or > 599 || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null
            || !PurchasingPhysicalStockRefusalDetailV1.IsValid(refusal.Status, refusal.Code, refusal.Detail))
        {
            throw new InvalidOperationException("Kayıtlı terminal ret okunamadı.");
        }

        return new PurchaseGoodsReceiptBatchReplay(null, refusal, attempt.CorrelationId);
    }

    public async Task<StoredPurchaseGoodsReceiptBatch?> FindAsync(Guid orderId, Guid batchId, CancellationToken cancellationToken = default)
    {
        var companyId = RequireResolvedCompany();
        var row = await dbContext.GoodsReceiptBatches.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.BatchId == batchId && candidate.OrderId == orderId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var restored = await RestoreAsync([row], companyId, cancellationToken);
        return restored[0];
    }

    public Task<bool> BatchIdExistsAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        // Existence is scoped to the caller's company. A cross-company primary-key collision is left to the unique
        // constraint and the normal concurrent-change path; a probe must never disclose another company's record.
        return dbContext.GoodsReceiptBatches.AsNoTracking().AnyAsync(row => row.BatchId == batchId, cancellationToken);
    }

    public async Task<IReadOnlyList<StoredPurchaseGoodsReceiptBatch>> ListForOrderAsync(Guid orderId, int take, CancellationToken cancellationToken = default)
    {
        RequireTake(take);
        var companyId = RequireResolvedCompany();
        var rows = await dbContext.GoodsReceiptBatches.AsNoTracking()
            .Where(row => row.OrderId == orderId)
            .OrderByDescending(row => row.PreparedAtUtc).ThenByDescending(row => row.BatchId)
            .Take(take)
            .ToListAsync(cancellationToken);
        return rows.Count == 0 ? [] : await RestoreAsync(rows, companyId, cancellationToken);
    }

    public async Task<IReadOnlySet<string>> TakenLabelCodesAsync(IReadOnlyCollection<string> codes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(codes);
        RequireResolvedCompany();
        if (codes.Count == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var wanted = codes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        // The column's default collation is the uniqueness rule (case-insensitive); the answer set is compared the same way.
        var taken = await dbContext.GoodsReceiptBatchLabels.AsNoTracking()
            .Where(row => !row.Released && wanted.Contains(row.LabelCode))
            .Select(row => row.LabelCode)
            .ToListAsync(cancellationToken);
        return new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlySet<string>> TakenLabelCodesStartingWithAsync(string prefix, int take, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        RequireTake(take);
        RequireResolvedCompany();
        var taken = await dbContext.GoodsReceiptBatchLabels.AsNoTracking()
            .Where(row => !row.Released && row.LabelCode.StartsWith(prefix))
            .OrderBy(row => row.LabelCode)
            .Select(row => row.LabelCode)
            .Take(take)
            .ToListAsync(cancellationToken);
        return new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<long> NextGeneratedNumberAsync(CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        // Released numbers are never reused (a printed label may still carry one); a race on the same number loses on the code's
        // filtered unique index and re-reads.
        var largest = await dbContext.GoodsReceiptBatchLabels.AsNoTracking()
            .Where(row => row.GeneratedNumber != null)
            .MaxAsync(row => row.GeneratedNumber, cancellationToken);
        return largest is { } value ? checked(value + 1) : 1;
    }

    public async Task<IReadOnlyList<PurchaseGoodsReceiptLabelMatchV1>> SearchLabelsAsync(string text, int take, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        RequireTake(take);
        RequireResolvedCompany();
        var trimmed = text.Trim();
        // KLN-D41: our code and the supplier's original number are both searchable; a prefix keeps the company index usable.
        var rows = await (
                from label in dbContext.GoodsReceiptBatchLabels.AsNoTracking()
                join batch in dbContext.GoodsReceiptBatches.AsNoTracking()
                    on new { label.CompanyId, label.BatchId } equals new { batch.CompanyId, batch.BatchId }
                where label.LabelCode.StartsWith(trimmed) || (label.SupplierLabelNumber != null && label.SupplierLabelNumber.StartsWith(trimmed))
                orderby batch.OccurredAtUtc descending, label.BatchId descending, label.LineNo
                select new
                {
                    batch.BatchId,
                    batch.OrderId,
                    batch.LineId,
                    batch.Status,
                    batch.OccurredAtUtc,
                    label.LabelId,
                    label.LabelCode,
                    label.SupplierLabelNumber,
                    label.MeasuredQuantityKg,
                })
            .Take(take)
            .ToListAsync(cancellationToken);
        return rows.Select(row => new PurchaseGoodsReceiptLabelMatchV1(row.BatchId, row.OrderId, row.LineId, StatusOf(row.Status).ToString(),
                row.LabelId, row.LabelCode, row.SupplierLabelNumber, PurchaseAcceptanceParticipantContractV1.FormatQuantity(row.MeasuredQuantityKg), row.OccurredAtUtc))
            .ToArray();
    }

    // ---- writes ---------------------------------------------------------------------------------

    public async Task<StoredPurchaseGoodsReceiptBatch> SaveNewAsync(PurchaseGoodsReceiptBatchDraft draft, PurchasingWriteEnvelope envelope, string requestHash,
        string commandName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireCommand(commandName);
        var companyId = RequireResolvedCompany();
        RequireDraft(draft);
        var keyHash = PurchasingStore.IdempotencyKeyHash(envelope.IdempotencyKey);
        if (await FindAttemptAsync(commandName, keyHash, cancellationToken) is not null)
        {
            throw Refused(PurchasingStoreCodes.ConcurrentChange, "Bu anahtar zaten bir deneme taşıyor; tekrar onu alır.");
        }

        var now = timeProvider.GetUtcNow();
        var actorId = envelope.ActorId.Trim();
        var row = new PurchaseGoodsReceiptBatchRow
        {
            CompanyId = companyId,
            BatchId = draft.BatchId,
            OrderId = draft.PurchaseOrderId,
            LineId = draft.PurchaseOrderLineId,
            ProductId = draft.ProductId,
            ProductVersion = draft.ProductVersion,
            UnitId = draft.UnitId,
            LocationId = draft.LocationId,
            OccurredAtUtc = draft.OccurredAtUtc,
            Status = (int)draft.Status,
            Version = 1,
            ExpectedQuantityKg = RequireFits(draft.ExpectedQuantityKg),
            ReceivedQuantityKg = RequireFits(draft.ReceivedQuantityKg),
            VarianceOutcome = (int)draft.VarianceOutcome,
            TolerancePercent = draft.TolerancePercent,
            VarianceAcknowledged = draft.VarianceAcknowledged,
            KilogramsPerUnit = draft.KilogramsPerUnit,
            PreparedBy = actorId,
            PreparedAtUtc = now,
            CorrelationId = envelope.CorrelationId.Trim(),
        };
        dbContext.GoodsReceiptBatches.Add(row);
        var labels = draft.Labels.Select(label => new PurchaseGoodsReceiptBatchLabelRow
        {
            CompanyId = companyId,
            BatchId = draft.BatchId,
            LineNo = label.LineNo,
            AcceptanceId = label.AcceptanceId,
            LotId = label.LotId,
            LabelId = label.LabelId,
            MovementId = label.MovementId,
            LabelCode = label.LabelCode,
            CodeSource = (int)label.CodeSource,
            GeneratedNumber = label.GeneratedNumber,
            SupplierLabelNumber = label.SupplierLabelNumber,
            MeasuredQuantityKg = RequireFits(label.MeasuredQuantityKg),
            PieceCount = label.PieceCount,
            HeatNumber = label.HeatNumber,
            CoilNumber = label.CoilNumber,
            Released = false,
        }).ToArray();
        dbContext.GoodsReceiptBatchLabels.AddRange(labels);
        dbContext.GoodsReceiptBatchAttempts.Add(Attempt(companyId, commandName, keyHash, requestHash, actorId, envelope, draft.BatchId, now));
        var stored = new StoredPurchaseGoodsReceiptBatch(companyId, draft, 1, actorId, now, null, null, null);
        var reason = draft.Status == PurchaseGoodsReceiptBatchStatus.Posted ? "Toplu mal kabul kaydedildi" : "Toplu mal kabul onaya gönderildi";
        Audit(envelope, companyId, draft.BatchId, "purchasing.goods-receipt-batch.submitted.v1", "Create", null, 1, "{}", Snapshot(stored), reason);
        await SaveAsync(cancellationToken);
        return stored;
    }

    public async Task<StoredPurchaseGoodsReceiptBatch> SaveDecisionAsync(Guid orderId, PurchaseGoodsReceiptBatch decided, long expectedVersion,
        PurchasingWriteEnvelope envelope, string requestHash, string commandName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decided);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireCommand(commandName);
        var companyId = RequireResolvedCompany();
        if (decided.Status is not (PurchaseGoodsReceiptBatchStatus.Posted or PurchaseGoodsReceiptBatchStatus.Rejected)
            || string.IsNullOrWhiteSpace(decided.DecidedBy) || decided.Version != expectedVersion + 1
            || (decided.Status == PurchaseGoodsReceiptBatchStatus.Rejected) == string.IsNullOrWhiteSpace(decided.Reason))
        {
            throw new ArgumentException("Karar bir onay veya gerekçeli ret ve bir sonraki sürümdür.", nameof(decided));
        }

        var actorId = envelope.ActorId.Trim();
        if (!string.Equals(decided.DecidedBy, actorId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Kararı veren yazmanın aktörüdür.", nameof(decided));
        }

        var keyHash = PurchasingStore.IdempotencyKeyHash(envelope.IdempotencyKey);
        if (await FindAttemptAsync(commandName, keyHash, cancellationToken) is not null)
        {
            throw Refused(PurchasingStoreCodes.ConcurrentChange, "Bu anahtar zaten bir deneme taşıyor; tekrar onu alır.");
        }

        var row = await dbContext.GoodsReceiptBatches
            .SingleOrDefaultAsync(candidate => candidate.BatchId == decided.Id && candidate.OrderId == orderId, cancellationToken);
        if (row is null || row.Version != expectedVersion || row.Status != (int)PurchaseGoodsReceiptBatchStatus.PendingApproval)
        {
            dbContext.ChangeTracker.Clear();
            throw Refused(PurchasingStoreCodes.ConcurrentChange, "Toplu mal kabul başka bir yazmayla değişti; hiçbir şey kaydedilmedi.");
        }

        var labels = await dbContext.GoodsReceiptBatchLabels
            .Where(label => label.BatchId == row.BatchId)
            .OrderBy(label => label.LineNo)
            .ToListAsync(cancellationToken);
        var before = Restore(row, companyId, labels);
        var now = timeProvider.GetUtcNow();
        row.Status = (int)decided.Status;
        row.DecidedBy = actorId;
        row.DecidedAtUtc = now;
        row.DecisionReason = decided.Reason;
        row.Version = decided.Version;
        if (decided.Status == PurchaseGoodsReceiptBatchStatus.Rejected)
        {
            // KLN-D41: a rejected receipt received no stock, so its label codes are free again; the rows stay as the record.
            foreach (var label in labels)
            {
                label.Released = true;
            }
        }

        dbContext.GoodsReceiptBatchAttempts.Add(Attempt(companyId, commandName, keyHash, requestHash, actorId, envelope, row.BatchId, now));
        var after = before with
        {
            Batch = before.Batch with { Status = decided.Status },
            Version = decided.Version,
            DecidedBy = actorId,
            DecidedAtUtc = now,
            DecisionReason = decided.Reason,
        };
        var approved = decided.Status == PurchaseGoodsReceiptBatchStatus.Posted;
        Audit(envelope, companyId, row.BatchId,
            approved ? "purchasing.goods-receipt-batch.approved.v1" : "purchasing.goods-receipt-batch.rejected.v1",
            approved ? "Approve" : "Reject", expectedVersion, decided.Version, Snapshot(before), Snapshot(after),
            approved ? "Toplu mal kabul farkı onaylandı" : "Toplu mal kabul farkı reddedildi");
        await SaveAsync(cancellationToken);
        return after;
    }

    public async Task RecordRefusalAsync(PurchasingWriteEnvelope envelope, string requestHash, string commandName, PurchasingTerminalRefusal refusal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireCommand(commandName);
        var companyId = RequireResolvedCompany();
        if (refusal.Status is < 400 or > 599 || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null
            || !PurchasingPhysicalStockRefusalDetailV1.IsValid(refusal.Status, refusal.Code, refusal.Detail)
            || refusal.Errors.Any(error => error is null || string.IsNullOrWhiteSpace(error.Field) || string.IsNullOrWhiteSpace(error.Code)))
        {
            throw new ArgumentException("Terminal ret bir durum, bir kod ve tam alan hataları taşır.", nameof(refusal));
        }

        var keyHash = PurchasingStore.IdempotencyKeyHash(envelope.IdempotencyKey);
        if (await FindAttemptAsync(commandName, keyHash, cancellationToken) is not null)
        {
            throw Refused(PurchasingStoreCodes.ConcurrentChange, "Bu anahtar zaten bir deneme taşıyor; tekrar onu alır.");
        }

        var row = Attempt(companyId, commandName, keyHash, requestHash, envelope.ActorId.Trim(), envelope, null, timeProvider.GetUtcNow());
        row.Outcome = AttemptOutcomeRefused;
        row.RefusalJson = JsonSerializer.Serialize(refusal, Json);
        dbContext.GoodsReceiptBatchAttempts.Add(row);
        await SaveAsync(cancellationToken);
    }

    // ---- restore --------------------------------------------------------------------------------

    private async Task<IReadOnlyList<StoredPurchaseGoodsReceiptBatch>> RestoreAsync(IReadOnlyList<PurchaseGoodsReceiptBatchRow> rows, Guid companyId,
        CancellationToken cancellationToken)
    {
        var ids = rows.Select(row => row.BatchId).ToArray();
        var labels = await dbContext.GoodsReceiptBatchLabels.AsNoTracking()
            .Where(label => ids.Contains(label.BatchId))
            .ToListAsync(cancellationToken);
        return rows.Select(row => Restore(row, companyId, labels.Where(label => label.BatchId == row.BatchId))).ToArray();
    }

    private static StoredPurchaseGoodsReceiptBatch Restore(PurchaseGoodsReceiptBatchRow row, Guid companyId, IEnumerable<PurchaseGoodsReceiptBatchLabelRow> labelRows)
    {
        if (row.CompanyId != companyId)
        {
            throw new InvalidOperationException("Toplu mal kabul başka bir şirkete ait.");
        }

        var status = StatusOf(row.Status);
        var outcome = Enum.IsDefined(typeof(GoodsReceiptKgVarianceOutcome), row.VarianceOutcome)
            ? (GoodsReceiptKgVarianceOutcome)row.VarianceOutcome
            : throw new InvalidOperationException("Kayıtlı kg farkı sonucu tanınmıyor.");
        var labels = labelRows.OrderBy(label => label.LineNo).Select(label => new PurchaseGoodsReceiptBatchLabelRecord(
                label.LineNo, label.AcceptanceId, label.LotId, label.LabelId, label.MovementId, label.LabelCode,
                Enum.IsDefined(typeof(GoodsReceiptLabelCodeSource), label.CodeSource)
                    ? (GoodsReceiptLabelCodeSource)label.CodeSource
                    : throw new InvalidOperationException("Kayıtlı etiket kodu kaynağı tanınmıyor."),
                label.GeneratedNumber, label.SupplierLabelNumber, label.MeasuredQuantityKg, label.HeatNumber, label.CoilNumber,
                label.PieceCount))
            .ToArray();
        if (labels.Length == 0 || labels.Select((label, index) => label.LineNo == index + 1).Any(ordered => !ordered)
            || labels.Sum(label => label.MeasuredQuantityKg) != row.ReceivedQuantityKg
            || !PieceFactsAgree(row.KilogramsPerUnit, row.ExpectedQuantityKg, labels))
        {
            // The stored total is the Σ of its labels, numbered 1..n; anything else is a corrupt record, refused.
            throw new InvalidOperationException("Toplu mal kabul etiketleri kayıtlı toplamla uyuşmuyor.");
        }

        var draft = new PurchaseGoodsReceiptBatchDraft(row.BatchId, row.OrderId, row.LineId, row.ProductId, row.ProductVersion, row.UnitId,
            row.LocationId, row.OccurredAtUtc, status, row.ExpectedQuantityKg, row.ReceivedQuantityKg, outcome, row.TolerancePercent,
            row.VarianceAcknowledged, labels, row.KilogramsPerUnit);
        return new StoredPurchaseGoodsReceiptBatch(companyId, draft, row.Version, row.PreparedBy, row.PreparedAtUtc, row.DecidedBy, row.DecidedAtUtc,
            row.DecisionReason);
    }

    private static PurchaseGoodsReceiptBatchStatus StatusOf(int value) =>
        Enum.IsDefined(typeof(PurchaseGoodsReceiptBatchStatus), value)
            ? (PurchaseGoodsReceiptBatchStatus)value
            : throw new InvalidOperationException("Kayıtlı toplu mal kabul durumu tanınmıyor.");

    // ---- guards ---------------------------------------------------------------------------------

    private static void RequireDraft(PurchaseGoodsReceiptBatchDraft draft)
    {
        if (draft.BatchId == Guid.Empty || draft.PurchaseOrderId == Guid.Empty || draft.PurchaseOrderLineId == Guid.Empty || draft.ProductId == Guid.Empty
            || draft.UnitId == Guid.Empty || draft.LocationId == Guid.Empty || string.IsNullOrWhiteSpace(draft.ProductVersion)
            || draft.Status is not (PurchaseGoodsReceiptBatchStatus.PendingApproval or PurchaseGoodsReceiptBatchStatus.Posted)
            || !Enum.IsDefined(draft.VarianceOutcome)
            || draft.TolerancePercent is { } tolerance && !GoodsReceiptKgVariance.IsValidTolerancePercent(tolerance) || draft.Labels is null || draft.Labels.Count == 0
            || draft.Labels.Select((label, index) => label.LineNo == index + 1).Any(ordered => !ordered)
            || draft.Labels.Sum(label => label.MeasuredQuantityKg) != draft.ReceivedQuantityKg
            || !PieceFactsAgree(draft.KilogramsPerUnit, draft.ExpectedQuantityKg, draft.Labels)
            || draft.IsPieceBatch && draft.Status != PurchaseGoodsReceiptBatchStatus.Posted)
        {
            throw new ArgumentException("Yeni toplu mal kabul kimlikli, 1..n numaralı etiketli ve etiket toplamı kadardır.", nameof(draft));
        }

        if (draft.ProductVersion.Length > ProductVersionMaximumLength
            || draft.Labels.Any(label => label.LabelCode.Length > GoodsReceiptLabelCodes.MaximumCodeLength))
        {
            throw Refused(PurchasingStoreCodes.ValueDoesNotFit, "Ürün sürümü veya etiket kodu sütununa sığmıyor.");
        }
    }

    /// <summary>
    /// AK-3b: a kilogram batch has no piece fact at all; a piece batch has a positive kg per piece, a positive whole piece count on
    /// every label and an expected kg equal to the theoretical kg of its pieces (Σ pieces × kg per piece, URN-D9).
    /// </summary>
    private static bool PieceFactsAgree(decimal? kilogramsPerUnit, decimal expectedQuantityKg,
        IReadOnlyCollection<PurchaseGoodsReceiptBatchLabelRecord> labels)
    {
        if (kilogramsPerUnit is not { } perUnit) return labels.All(label => label.PieceCount is null);
        if (perUnit <= 0m || labels.Any(label => label.PieceCount is not > 0)) return false;
        var pieces = labels.Sum(label => (long)label.PieceCount!.Value);
        return PurchasePieceKilograms.TheoreticalKilograms(pieces, perUnit) == expectedQuantityKg;
    }

    private static decimal RequireFits(decimal value) =>
        value > -ColumnBound && value < ColumnBound && decimal.Round(value, KgScale) == value
            ? value
            : throw Refused(PurchasingStoreCodes.ValueDoesNotFit, "Kg değeri decimal(18,3) sütununa sığmıyor.");

    private static void RequireTake(int take)
    {
        if (take is < 1 or > MaximumTake)
        {
            throw new ArgumentOutOfRangeException(nameof(take), $"Okuma boyutu 1–{MaximumTake} olmalıdır.");
        }
    }

    private Task<PurchaseGoodsReceiptBatchAttemptRow?> FindAttemptAsync(string commandName, string keyHash, CancellationToken cancellationToken) =>
        dbContext.GoodsReceiptBatchAttempts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == commandName && row.IdempotencyKeyHash == keyHash, cancellationToken);

    private static void RequireSameContent(PurchaseGoodsReceiptBatchAttemptRow attempt, string actorId, string requestHash)
    {
        // ADR-011 §3: another content or another actor under the same key is one 409 that discloses nothing.
        if (!string.Equals(attempt.ActorId, actorId, StringComparison.Ordinal) || !string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw Refused(PurchasingStoreCodes.SameKeyPayloadMismatch, "Aynı Idempotency-Key başka bir istekle geldi; ilk cevap bu isteğe verilmez.");
        }
    }

    private static PurchaseGoodsReceiptBatchAttemptRow Attempt(Guid companyId, string commandName, string keyHash, string requestHash, string actorId,
        PurchasingWriteEnvelope envelope, Guid? batchId, DateTimeOffset now) => new PurchaseGoodsReceiptBatchAttemptRow
    {
        CompanyId = companyId,
        CommandName = commandName,
        IdempotencyKeyHash = keyHash,
        RequestHash = requestHash,
        ActorId = actorId,
        Outcome = AttemptOutcomeRecorded,
        BatchId = batchId,
        CorrelationId = envelope.CorrelationId.Trim(),
        RecordedAtUtc = now,
    };

    private void Audit(PurchasingWriteEnvelope envelope, Guid companyId, Guid batchId, string eventType, string operation, long? versionBefore,
        long versionAfter, string before, string after, string reason) =>
        auditEvents.Enqueue(new AuditEventDraft(companyId, eventType, BatchEntityType, batchId.ToString("D"), operation, versionBefore, versionAfter,
            AuditActorTypes.User, envelope.ActorId, envelope.PermissionCode, envelope.RoleCodes, before, after, reason, ApprovalContextJson: null,
            AuditAiInvolvement.None, AiContextJson: null, envelope.SourceChannel, envelope.ClientIpAddress, envelope.UserAgent, envelope.CorrelationId,
            envelope.CausationId, envelope.IdempotencyKey));

    private static string Snapshot(StoredPurchaseGoodsReceiptBatch stored) => JsonSerializer.Serialize(new
    {
        stored.Batch.BatchId,
        OrderId = stored.Batch.PurchaseOrderId,
        LineId = stored.Batch.PurchaseOrderLineId,
        Status = stored.Batch.Status.ToString(),
        stored.Version,
        stored.Batch.ExpectedQuantityKg,
        stored.Batch.ReceivedQuantityKg,
        VarianceOutcome = stored.Batch.VarianceOutcome.ToString(),
        stored.Batch.TolerancePercent,
        stored.Batch.VarianceAcknowledged,
        stored.Batch.KilogramsPerUnit,
        stored.Batch.LocationId,
        stored.PreparedBy,
        stored.DecidedBy,
        stored.DecisionReason,
        Labels = stored.Batch.Labels.Select(label => new
        {
            label.LineNo,
            label.LabelId,
            label.LabelCode,
            CodeSource = label.CodeSource.ToString(),
            label.SupplierLabelNumber,
            label.MeasuredQuantityKg,
            label.PieceCount,
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
            throw Refused(PurchasingStoreCodes.ConcurrentChange, "Kayıt başka bir yazmayla değişti; hiçbir şey kaydedilmedi.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // A unique rule lost a race: the attempt key, the batch id or a held label code (KLN-D41). The loser re-reads.
            dbContext.ChangeTracker.Clear();
            throw Refused(PurchasingStoreCodes.ConcurrentChange, "Kalıcı tekillik kuralı başka bir yazmanın lehine sonuçlandı; hiçbir şey kaydedilmedi.", exception);
        }
    }

    private Guid RequireResolvedCompany() =>
        companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
            ? companyContext.CompanyId
            : throw Refused(PurchasingStoreCodes.CompanyContextMismatch, "Aktif şirket bağlamı olmadan satınalma kaydı işlenmez.");

    private static void RequireEnvelope(PurchasingWriteEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (string.IsNullOrWhiteSpace(envelope.ActorId) || string.IsNullOrWhiteSpace(envelope.PermissionCode) || string.IsNullOrWhiteSpace(envelope.CorrelationId)
            || string.IsNullOrWhiteSpace(envelope.SourceChannel) || envelope.RoleCodes is null)
        {
            throw new ArgumentException("Satınalma yazması aktör, izin, correlation, idempotency ve kanal bilgisi ister.", nameof(envelope));
        }

        if (string.IsNullOrWhiteSpace(envelope.IdempotencyKey) || !string.Equals(envelope.IdempotencyKey, envelope.IdempotencyKey.Trim(), StringComparison.Ordinal)
            || envelope.IdempotencyKey.Length > KeyMaximumLength)
        {
            throw new ArgumentException("Idempotency-Key kanonik ve en fazla 200 karakter olmalıdır.", nameof(envelope));
        }

        if (envelope.ActorId.Trim().Length > ActorMaximumLength || envelope.CorrelationId.Trim().Length > CorrelationMaximumLength)
        {
            throw Refused(PurchasingStoreCodes.ValueDoesNotFit, "Aktör veya correlation kimliği sütununa sığmıyor.");
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
        if (commandName is not (PurchaseGoodsReceiptBatchCommandsV1.SubmitV1 or PurchaseGoodsReceiptBatchCommandsV1.ApproveV1
            or PurchaseGoodsReceiptBatchCommandsV1.RejectV1))
        {
            throw new ArgumentException("Bilinmeyen toplu mal kabul komutu.", nameof(commandName));
        }
    }

    private static PurchasingStoreException Refused(string code, string message, Exception? inner = null) => new PurchasingStoreException(code, message, inner);
}

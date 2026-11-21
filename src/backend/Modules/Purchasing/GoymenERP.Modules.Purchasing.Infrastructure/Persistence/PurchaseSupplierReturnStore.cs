using System.Text.Json;
using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Returns;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>
/// ALC-S5 (B-19; SAT-D306, SAT-D392, SAT-D393, SAT-D396): the supplier return records of the server-resolved company. One call is one
/// save: the return change, its attempt and its audit event commit together, inside the host transaction when the caller opened one
/// (a return that leaves stock) and alone otherwise (a pending cost exception or its rejection). Rows are read back strictly: an
/// unknown status, cost basis or source block is refused, never repaired. The PurchasingStore attempt/refusal shape is mirrored on
/// the return's own attempt table.
/// </summary>
public sealed class PurchaseSupplierReturnStore(PurchasingDbContext dbContext, ICompanyContext companyContext, IAuditEventCollector auditEvents,
    TimeProvider timeProvider) : IPurchaseSupplierReturnStore
{
    private const string ReturnEntityType = "Purchasing.SupplierReturn";
    private const int ActorMaximumLength = 128;
    private const int KeyMaximumLength = 200;
    private const int CorrelationMaximumLength = 128;
    private const int MaximumTake = 1_000;
    private const int AttemptOutcomeRecorded = 1;
    private const int AttemptOutcomeRefused = 2;
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    // decimal(18,3) kg and decimal(18,4) unit cost: a value outside is refused before the save, never a provider exception.
    private const decimal KgColumnBound = 1_000_000_000_000_000m;
    private const int KgScale = 3;
    private const decimal CostColumnBound = 100_000_000_000_000m;
    private const int CostScale = 4;

    // ---- reads ----------------------------------------------------------------------------------

    public async Task<PurchaseSupplierReturnReplay?> FindReplayAsync(PurchasingWriteEnvelope envelope, string requestHash, string commandName,
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
            if (attempt.ResultJson is { } snapshot)
            {
                StoredPurchaseSupplierReturn? original;
                try { original = JsonSerializer.Deserialize<StoredPurchaseSupplierReturn>(snapshot, Json); }
                catch (JsonException exception) { throw new InvalidOperationException("Kayıtlı iade yanıtı okunamadı.", exception); }
                if (original is null || original.Return is null || original.CompanyId != attempt.CompanyId || original.Return.ReturnId != attempt.ReturnId
                    || original.Version < 1 || original.Return.PurchaseOrderId == Guid.Empty || original.Return.ReturnId == Guid.Empty
                    || original.Return.ReturnLineId == Guid.Empty || original.Return.PurchaseOrderLineId == Guid.Empty
                    || original.Return.GoodsAcceptanceId == Guid.Empty || original.Return.InventoryMovementId == Guid.Empty
                    || !Enum.IsDefined(original.Return.Status) || !Enum.IsDefined(original.Return.CostBasis)
                    || !PurchaseSupplierReturnJsonV1.SourceBlocks.Contains(original.Return.SourceBlock, StringComparer.Ordinal)
                    || original.Return.QuantityKg <= 0 || original.Return.UnitCostTryPerKg <= 0
                    || string.IsNullOrWhiteSpace(original.RequestedBy))
                    throw new InvalidOperationException("Kayıtlı iade yanıtı deneme kimlikleriyle uyuşmuyor.");
                ValidateAllocation(original.Return);
                return new PurchaseSupplierReturnReplay(original.Return.ReturnId, null, attempt.CorrelationId, original);
            }
            // Older attempts did not persist an original response; their explicit legacy fallback stays identifiable.
            return attempt.ReturnId is { } returnId
                ? new PurchaseSupplierReturnReplay(returnId, null, attempt.CorrelationId)
                : throw new InvalidOperationException("Kayıtlı deneme bir tedarikçi iadesi taşımıyor.");
        }

        if (attempt.Outcome != AttemptOutcomeRefused)
            throw new InvalidOperationException("Kayıtlı iade denemesi durumu tanınmıyor.");
        var refusal = attempt.RefusalJson is null ? null : JsonSerializer.Deserialize<PurchasingTerminalRefusal>(attempt.RefusalJson, Json);
        if (refusal is null || refusal.Status is < 400 or > 599 || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null
            || !PurchasingPhysicalStockRefusalDetailV1.IsValid(refusal.Status, refusal.Code, refusal.Detail))
        {
            throw new InvalidOperationException("Kayıtlı terminal ret okunamadı.");
        }

        return new PurchaseSupplierReturnReplay(null, refusal, attempt.CorrelationId);
    }

    public async Task<StoredPurchaseSupplierReturn?> FindAsync(Guid orderId, Guid returnId, CancellationToken cancellationToken = default)
    {
        var companyId = RequireResolvedCompany();
        var row = await dbContext.SupplierReturns.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.ReturnId == returnId && candidate.OrderId == orderId, cancellationToken);
        return row is null ? null : Restore(row, companyId);
    }

    public Task<bool> ReturnIdExistsAsync(Guid returnId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        // Existence is scoped to the caller's company. A cross-company primary-key collision is handled by the
        // unique constraint and the normal concurrent-change path, without disclosing the other company's row.
        return dbContext.SupplierReturns.AsNoTracking().AnyAsync(row => row.ReturnId == returnId, cancellationToken);
    }

    public async Task<IReadOnlyList<StoredPurchaseSupplierReturn>> ListForOrderAsync(Guid orderId, int take, CancellationToken cancellationToken = default)
    {
        RequireTake(take);
        var companyId = RequireResolvedCompany();
        var rows = await dbContext.SupplierReturns.AsNoTracking()
            .Where(row => row.OrderId == orderId)
            .OrderByDescending(row => row.RequestedAtUtc).ThenByDescending(row => row.ReturnId)
            .Take(take)
            .ToListAsync(cancellationToken);
        return rows.Select(row => Restore(row, companyId)).ToArray();
    }

    public async Task<Guid?> FindAcceptanceLineAsync(Guid orderId, Guid goodsAcceptanceId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        // Purchasing's own acceptance share (the receipt counted on the order line); the label itself is Inventory's and is
        // resolved by Inventory when the exit is staged.
        var lines = await dbContext.Acceptances.AsNoTracking()
            .Where(row => row.AcceptanceId == goodsAcceptanceId && row.OrderId == orderId)
            .Select(row => row.LineId)
            .Take(2)
            .ToListAsync(cancellationToken);
        return lines.Count == 1 ? lines[0] : null;
    }

    // ---- writes ---------------------------------------------------------------------------------

    public async Task<StoredPurchaseSupplierReturn> SaveNewAsync(PurchaseSupplierReturnDraft draft, PurchasingWriteEnvelope envelope, string requestHash,
        string commandName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireCommand(commandName);
        RequireDraft(draft);
        var companyId = RequireResolvedCompany();
        var keyHash = PurchasingStore.IdempotencyKeyHash(envelope.IdempotencyKey);
        if (await FindAttemptAsync(commandName, keyHash, cancellationToken) is not null)
        {
            throw Refused(PurchasingStoreCodes.ConcurrentChange, "Bu anahtar zaten bir deneme taşıyor; tekrar onu alır.");
        }

        var actorId = envelope.ActorId.Trim();
        var now = timeProvider.GetUtcNow();
        var reason = draft.Reason.Trim();
        var row = new PurchaseSupplierReturnRow
        {
            CompanyId = companyId,
            ReturnId = draft.ReturnId,
            ReturnLineId = draft.ReturnLineId,
            OrderId = draft.PurchaseOrderId,
            LineId = draft.PurchaseOrderLineId,
            GoodsAcceptanceId = draft.GoodsAcceptanceId,
            MovementId = draft.InventoryMovementId,
            Status = (int)draft.Status,
            Version = 1,
            SourceBlock = draft.SourceBlock,
            QuantityKg = RequireKgFits(draft.QuantityKg),
            CostBasis = (int)draft.CostBasis,
            UnitCostTryPerKg = RequireCostFits(draft.UnitCostTryPerKg),
            ReplacementExpected = draft.ReplacementExpected,
            ExpectedPurchaseOrderVersion = draft.ExpectedPurchaseOrderVersion,
            UndoQuantity = draft.UndoQuantity,
            CostAllocationJson = draft.CostAllocation is null ? null : JsonSerializer.Serialize(
                new CostAllocationSnapshot(draft.CostAllocation, draft.CostAllocationSources!), Json),
            Reason = reason,
            OccurredAtUtc = draft.OccurredAtUtc,
            RequestedBy = actorId,
            RequestedAtUtc = now,
            CorrelationId = envelope.CorrelationId.Trim(),
        };
        dbContext.SupplierReturns.Add(row);
        var stored = new StoredPurchaseSupplierReturn(companyId, draft with { Reason = reason }, 1, actorId, now, null, null, null);
        var creationAttempt = Attempt(companyId, commandName, keyHash, requestHash, actorId, envelope, draft.ReturnId, now);
        creationAttempt.ResultJson = JsonSerializer.Serialize(stored, Json);
        dbContext.SupplierReturnAttempts.Add(creationAttempt);
        var auditReason = draft.Status == PurchaseSupplierReturnStatus.Recorded
            ? "Tedarikçi iadesi fatura birim maliyetiyle kaydedildi"
            : "Tedarikçi iadesi maliyet istisnası onayına gönderildi";
        Audit(envelope, companyId, draft.ReturnId, "purchasing.supplier-return.created.v1", "Create", null, 1, "{}", Snapshot(stored), auditReason);
        await SaveAsync(cancellationToken);
        return stored;
    }

    public async Task<StoredPurchaseSupplierReturn> SaveDecisionAsync(Guid orderId, PurchaseSupplierReturn decided, long expectedVersion,
        PurchasingWriteEnvelope envelope, string requestHash, string commandName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decided);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireCommand(commandName);
        var companyId = RequireResolvedCompany();
        if (decided.Status is not (PurchaseSupplierReturnStatus.Recorded or PurchaseSupplierReturnStatus.Rejected)
            || string.IsNullOrWhiteSpace(decided.DecidedBy) || decided.Version != expectedVersion + 1
            || (decided.Status == PurchaseSupplierReturnStatus.Rejected) == string.IsNullOrWhiteSpace(decided.Reason))
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

        var row = await dbContext.SupplierReturns
            .SingleOrDefaultAsync(candidate => candidate.ReturnId == decided.Id && candidate.OrderId == orderId, cancellationToken);
        if (row is null || row.Version != expectedVersion || row.Status != (int)PurchaseSupplierReturnStatus.PendingCostException)
        {
            dbContext.ChangeTracker.Clear();
            throw Refused(PurchasingStoreCodes.ConcurrentChange, "Tedarikçi iadesi başka bir yazmayla değişti; hiçbir şey kaydedilmedi.");
        }

        var before = Restore(row, companyId);
        var now = timeProvider.GetUtcNow();
        row.Status = (int)decided.Status;
        row.DecidedBy = actorId;
        row.DecidedAtUtc = now;
        row.DecisionReason = decided.Reason;
        row.Version = decided.Version;
        var after = before with
        {
            Return = before.Return with { Status = decided.Status },
            Version = decided.Version,
            DecidedBy = actorId,
            DecidedAtUtc = now,
            DecisionReason = decided.Reason,
        };
        var decisionAttempt = Attempt(companyId, commandName, keyHash, requestHash, actorId, envelope, row.ReturnId, now);
        decisionAttempt.ResultJson = JsonSerializer.Serialize(after, Json);
        dbContext.SupplierReturnAttempts.Add(decisionAttempt);
        var approved = decided.Status == PurchaseSupplierReturnStatus.Recorded;
        Audit(envelope, companyId, row.ReturnId,
            approved ? "purchasing.supplier-return.cost-exception-approved.v1" : "purchasing.supplier-return.cost-exception-rejected.v1",
            approved ? "Approve" : "Reject", expectedVersion, decided.Version, Snapshot(before), Snapshot(after),
            approved ? "Tedarikçi iadesi maliyet istisnası onaylandı" : "Tedarikçi iadesi maliyet istisnası reddedildi");
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
        dbContext.SupplierReturnAttempts.Add(row);
        await SaveAsync(cancellationToken);
    }

    // ---- restore --------------------------------------------------------------------------------

    private static StoredPurchaseSupplierReturn Restore(PurchaseSupplierReturnRow row, Guid companyId)
    {
        if (row.CompanyId != companyId)
        {
            throw new InvalidOperationException("Tedarikçi iadesi başka bir şirkete ait.");
        }

        var status = Enum.IsDefined(typeof(PurchaseSupplierReturnStatus), row.Status)
            ? (PurchaseSupplierReturnStatus)row.Status
            : throw new InvalidOperationException("Kayıtlı tedarikçi iadesi durumu tanınmıyor.");
        var basis = Enum.IsDefined(typeof(PurchaseSupplierReturnCostBasis), row.CostBasis)
            ? (PurchaseSupplierReturnCostBasis)row.CostBasis
            : throw new InvalidOperationException("Kayıtlı tedarikçi iadesi maliyet dayanağı tanınmıyor.");
        if (!PurchaseSupplierReturnJsonV1.SourceBlocks.Contains(row.SourceBlock, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Kayıtlı tedarikçi iadesi kaynak bloğu tanınmıyor.");
        }

        var allocation = row.CostAllocationJson is null ? null : JsonSerializer.Deserialize<CostAllocationSnapshot>(row.CostAllocationJson, Json)
            ?? throw new InvalidOperationException("Kayıtlı iade maliyet kaynak seçimi okunamadı.");
        var draft = new PurchaseSupplierReturnDraft(row.ReturnId, row.ReturnLineId, row.OrderId, row.LineId, row.GoodsAcceptanceId, row.MovementId,
            status, row.SourceBlock, row.QuantityKg, basis, row.UnitCostTryPerKg, row.ReplacementExpected, row.Reason, row.OccurredAtUtc,
            row.ExpectedPurchaseOrderVersion, row.UndoQuantity,
            allocation?.Selection, allocation?.InvoiceSources);
        ValidateAllocation(draft);
        return new StoredPurchaseSupplierReturn(companyId, draft, row.Version, row.RequestedBy, row.RequestedAtUtc, row.DecidedBy, row.DecidedAtUtc,
            row.DecisionReason);
    }

    // ---- guards ---------------------------------------------------------------------------------

    private static void RequireDraft(PurchaseSupplierReturnDraft draft)
    {
        ValidateAllocation(draft);
        var coherent = (draft.Status == PurchaseSupplierReturnStatus.Recorded && draft.CostBasis == PurchaseSupplierReturnCostBasis.InvoiceUnitCost)
            || (draft.Status == PurchaseSupplierReturnStatus.PendingCostException
                && draft.CostBasis == PurchaseSupplierReturnCostBasis.ManualCostWithException);
        if (draft.ReturnId == Guid.Empty || draft.ReturnLineId == Guid.Empty || draft.PurchaseOrderId == Guid.Empty
            || draft.PurchaseOrderLineId == Guid.Empty || draft.GoodsAcceptanceId == Guid.Empty || draft.InventoryMovementId == Guid.Empty
            || !coherent || (draft.ReplacementExpected && (draft.ExpectedPurchaseOrderVersion is null || draft.UndoQuantity is null))
            || (draft.ExpectedPurchaseOrderVersion is null) != (draft.UndoQuantity is null)
            || draft.ExpectedPurchaseOrderVersion is <= 0 || draft.UndoQuantity is <= 0
            || (draft.UndoQuantity is { } undo && (undo >= KgColumnBound || decimal.Round(undo, KgScale) != undo))
            || !PurchaseSupplierReturnJsonV1.SourceBlocks.Contains(draft.SourceBlock, StringComparer.Ordinal)
            || draft.QuantityKg <= 0 || draft.UnitCostTryPerKg <= 0 || string.IsNullOrWhiteSpace(draft.Reason))
        {
            throw new ArgumentException("Yeni tedarikçi iadesi kimlikli, pozitif kg ve maliyetli, tutarlı durum ve dayanaklıdır.", nameof(draft));
        }

        if (draft.Reason.Trim().Length > PurchaseSupplierReturn.MaximumReasonLength)
        {
            throw Refused(PurchasingStoreCodes.ValueDoesNotFit, "İade gerekçesi sütununa sığmıyor.");
        }
    }

    private static void ValidateAllocation(PurchaseSupplierReturnDraft draft)
    {
        if ((draft.CostAllocation is null) != (draft.CostAllocationSources is null))
            throw new JsonException("Source allocation and invoice labels must be retained together.");
        if (draft.CostAllocation is { } allocation)
        {
            if (draft.ExpectedPurchaseOrderVersion is null) throw new JsonException("Source allocation requires a purchase-unit adjustment.");
            PurchaseSupplierReturnCostAllocationContractV1.Validate(allocation,
                draft.QuantityKg.ToString("0.###", CultureInfo.InvariantCulture), draft.UndoQuantity?.ToString("0.###", CultureInfo.InvariantCulture));
            var labels = draft.CostAllocationSources!;
            if (labels.Count != allocation.InvoicedSources.Count || labels.Any(row => row is null || row.SourceEventId == Guid.Empty
                || row.InvoiceId == Guid.Empty || row.InvoiceLineId == Guid.Empty || string.IsNullOrWhiteSpace(row.InvoiceNumber)
                || row.InvoiceNumber.Length > 128 || row.InvoiceLineNo < 1)
                || labels.Select(row => row.SourceEventId).Distinct().Count() != labels.Count
                || allocation.InvoicedSources.Any(row => !labels.Any(label => label.SourceEventId == row.SourceEventId)))
                throw new JsonException("Stored source labels do not identify the selected invoice sources.");
        }
    }

    private sealed record CostAllocationSnapshot(PurchaseSupplierReturnCostAllocationV1 Selection,
        IReadOnlyList<PurchaseSupplierReturnInvoiceSourceLabelV1> InvoiceSources);

    private static decimal RequireKgFits(decimal value) =>
        value > -KgColumnBound && value < KgColumnBound && decimal.Round(value, KgScale) == value
            ? value
            : throw Refused(PurchasingStoreCodes.ValueDoesNotFit, "Kg değeri decimal(18,3) sütununa sığmıyor.");

    private static decimal RequireCostFits(decimal value) =>
        value > -CostColumnBound && value < CostColumnBound && decimal.Round(value, CostScale) == value
            ? value
            : throw Refused(PurchasingStoreCodes.ValueDoesNotFit, "Birim maliyet decimal(18,4) sütununa sığmıyor.");

    private static void RequireTake(int take)
    {
        if (take is < 1 or > MaximumTake)
        {
            throw new ArgumentOutOfRangeException(nameof(take), $"Okuma boyutu 1–{MaximumTake} olmalıdır.");
        }
    }

    private Task<PurchaseSupplierReturnAttemptRow?> FindAttemptAsync(string commandName, string keyHash, CancellationToken cancellationToken) =>
        dbContext.SupplierReturnAttempts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == commandName && row.IdempotencyKeyHash == keyHash, cancellationToken);

    private static void RequireSameContent(PurchaseSupplierReturnAttemptRow attempt, string actorId, string requestHash)
    {
        // ADR-011 §3: another content or another actor under the same key is one 409 that discloses nothing.
        if (!string.Equals(attempt.ActorId, actorId, StringComparison.Ordinal) || !string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw Refused(PurchasingStoreCodes.SameKeyPayloadMismatch, "Aynı Idempotency-Key başka bir istekle geldi; ilk cevap bu isteğe verilmez.");
        }
    }

    private static PurchaseSupplierReturnAttemptRow Attempt(Guid companyId, string commandName, string keyHash, string requestHash, string actorId,
        PurchasingWriteEnvelope envelope, Guid? returnId, DateTimeOffset now) => new PurchaseSupplierReturnAttemptRow
    {
        CompanyId = companyId,
        CommandName = commandName,
        IdempotencyKeyHash = keyHash,
        RequestHash = requestHash,
        ActorId = actorId,
        Outcome = AttemptOutcomeRecorded,
        ReturnId = returnId,
        CorrelationId = envelope.CorrelationId.Trim(),
        RecordedAtUtc = now,
    };

    private void Audit(PurchasingWriteEnvelope envelope, Guid companyId, Guid returnId, string eventType, string operation, long? versionBefore,
        long versionAfter, string before, string after, string reason) =>
        auditEvents.Enqueue(new AuditEventDraft(companyId, eventType, ReturnEntityType, returnId.ToString("D"), operation, versionBefore, versionAfter,
            AuditActorTypes.User, envelope.ActorId, envelope.PermissionCode, envelope.RoleCodes, before, after, reason, ApprovalContextJson: null,
            AuditAiInvolvement.None, AiContextJson: null, envelope.SourceChannel, envelope.ClientIpAddress, envelope.UserAgent, envelope.CorrelationId,
            envelope.CausationId, envelope.IdempotencyKey));

    private static string Snapshot(StoredPurchaseSupplierReturn stored) => JsonSerializer.Serialize(new
    {
        stored.Return.ReturnId,
        stored.Return.ReturnLineId,
        OrderId = stored.Return.PurchaseOrderId,
        LineId = stored.Return.PurchaseOrderLineId,
        stored.Return.GoodsAcceptanceId,
        MovementId = stored.Return.InventoryMovementId,
        Status = stored.Return.Status.ToString(),
        stored.Version,
        stored.Return.SourceBlock,
        stored.Return.QuantityKg,
        CostBasis = stored.Return.CostBasis.ToString(),
        stored.Return.UnitCostTryPerKg,
        stored.Return.ReplacementExpected,
        stored.Return.ExpectedPurchaseOrderVersion,
        stored.Return.UndoQuantity,
        stored.Return.CostAllocation,
        stored.Return.CostAllocationSources,
        stored.Return.Reason,
        stored.RequestedBy,
        stored.DecidedBy,
        stored.DecisionReason,
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
            // A unique rule lost a race: the attempt key, the return id or the inventory movement id. The loser re-reads.
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
        if (commandName is not (PurchaseSupplierReturnCommandsV1.CreateV1 or PurchaseSupplierReturnCommandsV1.ApproveCostExceptionV1
            or PurchaseSupplierReturnCommandsV1.RejectCostExceptionV1))
        {
            throw new ArgumentException("Bilinmeyen tedarikçi iadesi komutu.", nameof(commandName));
        }
    }

    private static PurchasingStoreException Refused(string code, string message, Exception? inner = null) => new PurchasingStoreException(code, message, inner);
}

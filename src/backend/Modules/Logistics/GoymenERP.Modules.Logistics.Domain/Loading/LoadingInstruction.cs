using GoymenERP.Modules.Logistics.Domain.Quantities;

namespace GoymenERP.Modules.Logistics.Domain.Loading;

public enum LoadingInstructionStatus
{
    Draft = 1,
    Issued = 2,
}

/// <summary>How a loaded quantity was established (§7.4): read per package, or a vehicle total spread over packages.</summary>
public enum MeasurementKind
{
    /// <summary>Measured on the package or label itself.</summary>
    Measured = 1,

    /// <summary>Allocated to the package from a vehicle weighing; not an individual measurement.</summary>
    AllocatedFromVehicleWeight = 2,
}

/// <summary>
/// A loading instruction for one company towards one customer (#275 §7.4, §5). The customer is
/// named twice on purpose: the Party (who) and the CompanyPartyAccount (the commercial
/// relationship of that Party in THIS company, which holds its active state) — a Party alone is
/// not a customer of a company. Both are identities only; that the account belongs to the Party
/// and is active is Cari's check at integration (dependency_wait).
/// <para>
/// An instruction is NOT a definite shipment: it says what should be loaded. What actually gets
/// loaded, shipped and delivered are separate shares per line, each capped by the one before
/// it, so a loaded sub-share is never subtracted twice (§13: 6.000 allocated, 4.000 loaded,
/// 3.800 shipped → 200 loaded-not-shipped stays visible). A delivery names its source shipment
/// (§7.4: shortage, damage, rejection and return are tied to the shipment they belong to). The
/// physical stock exit and the allocation consumption of a definite shipment belong to
/// Inventory (T11/T12) and are one transaction with the shipped share; this record books
/// nothing physical.
/// </para>
/// <para>
/// This aggregate is the single authority for every share bound; each refusal carries a
/// <see cref="LogisticsRuleCodes"/> code.
/// </para>
/// </summary>
public sealed class LoadingInstruction
{
    private readonly List<LoadingInstructionLine> lines = [];
    private readonly List<LoadingShare> shares = [];
    private readonly List<DispatchNoteDraft> dispatchNotes = [];

    private LoadingInstruction(Guid id, Guid companyId, Guid customerPartyId, Guid companyAccountId, DateTimeOffset now)
    {
        Id = Identity.Require(id, nameof(id));
        CompanyId = Identity.Require(companyId, nameof(companyId));
        CustomerPartyId = Identity.Require(customerPartyId, nameof(customerPartyId));
        CompanyAccountId = Identity.Require(companyAccountId, nameof(companyAccountId));
        CreatedAtUtc = now.ToUniversalTime();
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; }
    public Guid CompanyId { get; }
    public Guid CustomerPartyId { get; }

    /// <summary>The customer's CompanyPartyAccount in this company (Cari identity only).</summary>
    public Guid CompanyAccountId { get; }

    public LoadingInstructionStatus Status { get; private set; } = LoadingInstructionStatus.Draft;
    public long Version { get; private set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Read-only views; the lists behind them never leave this record.</summary>
    public IReadOnlyList<LoadingInstructionLine> Lines => lines.AsReadOnly();

    /// <summary>Every recorded load, shipment and delivery share, in order.</summary>
    public IReadOnlyList<LoadingShare> Shares => shares.AsReadOnly();

    /// <summary>The instruction's manual weighbridge record (AT-053), or null while none is recorded.</summary>
    public VehicleWeighing? Weighing { get; private set; }

    /// <summary>The dispatch notes prepared from this instruction's shipments (AT-055), in creation order; a read-only view.</summary>
    public IReadOnlyList<DispatchNoteDraft> DispatchNotes => dispatchNotes.AsReadOnly();

    /// <summary>
    /// How many dispatch-note steps (create, approve, cancel, e-İrsaliye number) this instruction has taken; each note records
    /// the step of each of its transitions, so the rebuild replays them in their order.
    /// </summary>
    public int DispatchSteps { get; private set; }

    public static LoadingInstruction Create(Guid id, Guid companyId, Guid customerPartyId, Guid companyAccountId, DateTimeOffset now) =>
        new(id, companyId, customerPartyId, companyAccountId, now);

    /// <summary>
    /// What should be loaded for one product, from the sales order line it serves (its identity
    /// binding: order, line, revision — §5 "her olayın kaynak kimliği vardır"; LOG-4 D4: only an
    /// APPROVED line is a need, the Sales owner answers that at command time), in a Settings unit (identity only; the unit's
    /// profile and the quantity's scale are verified by the Application layer), optionally
    /// drawing from one Inventory allocation. Loading is a sub-share of the allocation, never a
    /// second reservation.
    /// </summary>
    public LoadingInstructionLine AddLine(long expectedVersion, Guid lineId, SourceOrderLineRef source, Guid productId, Guid unitId,
        Guid? allocationId, Quantity instructed, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireStatus(LoadingInstructionStatus.Draft, LogisticsRuleCodes.InstructionNotDraft, "Yalnız taslak talimata satır eklenebilir.");
        Identity.Require(lineId, nameof(lineId));
        SourceOrderLineRef.Require(source);
        Identity.Require(productId, nameof(productId));
        Identity.Require(unitId, nameof(unitId));
        if (allocationId == Guid.Empty)
        {
            throw new ArgumentException("Kimlik boş olamaz.", nameof(allocationId));
        }

        if (instructed.IsZero)
        {
            throw new ArgumentOutOfRangeException(nameof(instructed), "Talimat miktarı sıfırdan büyük olmalıdır.");
        }

        if (lines.Any(line => line.Id == lineId))
        {
            throw new LogisticsRuleException(LogisticsRuleCodes.DuplicateLineId, "Aynı talimat satırı kimliği ikinci kez eklenemez.");
        }

        var line = new LoadingInstructionLine(lineId, source, productId, unitId, allocationId, instructed);
        lines.Add(line);
        Touch(now);
        return line;
    }

    public void Issue(long expectedVersion, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireStatus(LoadingInstructionStatus.Draft, LogisticsRuleCodes.InstructionNotDraft, "Yalnız taslak talimat yayımlanabilir.");
        if (lines.Count == 0)
        {
            throw new LogisticsRuleException(LogisticsRuleCodes.InstructionWithoutLines, "Satırsız talimat yayımlanamaz.");
        }

        Status = LoadingInstructionStatus.Issued;
        Touch(now);
    }

    /// <summary>A quality or ownership block on a line (its source is Inventory/Quality): loads and shipments are refused while it stands.</summary>
    public void BlockLine(long expectedVersion, Guid lineId, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        FindLine(lineId).SetBlocked(true);
        Touch(now);
    }

    public void UnblockLine(long expectedVersion, Guid lineId, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        FindLine(lineId).SetBlocked(false);
        Touch(now);
    }

    /// <summary>
    /// Records what was actually loaded against a line, with how it was measured. Never more than
    /// what remains to load; a blocked line loads nothing; the same load id is one load.
    /// </summary>
    public LoadingShare RecordLoad(long expectedVersion, Guid loadId, Guid lineId, Quantity loaded,
        MeasurementKind measurement, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireStatus(LoadingInstructionStatus.Issued, LogisticsRuleCodes.InstructionNotIssued, "Yükleme yalnız yayımlanmış talimata kaydedilir.");
        if (!Enum.IsDefined(measurement))
        {
            throw new LogisticsRuleException(LogisticsRuleCodes.MeasurementRequired, "Ölçüm niteliği belirtilmelidir.");
        }

        var line = FindLine(lineId);
        RequireNotBlocked(line, "Bloklu satıra yükleme kaydedilemez.");
        RequirePositiveShare(loadId, loaded, line.RemainingToLoad, LogisticsRuleCodes.LoadExceedsInstructed,
            "Yüklenen pay talimatın yüklenmemiş kalanını aşamaz.");
        line.AddLoaded(loaded);
        return Add(new LoadingShare(loadId, line.Id, LoadingShareKind.Loaded, loaded, measurement, null, now.ToUniversalTime()), now);
    }

    /// <summary>
    /// Marks part of the loaded share as definitely shipped. Shipped is taken from LOADED only,
    /// never from the instruction again, so nothing is decremented twice. A blocked line ships
    /// nothing, even when it was loaded before the block (§7.4 "kalite bloku … engellenir", Ö3
    /// "sevk serbestliği yok"). The stock exit and the allocation consumption that go with a
    /// definite shipment are Inventory's (T11/T12) and belong in the same transaction as this share.
    /// </summary>
    public LoadingShare MarkShipped(long expectedVersion, Guid shipmentId, Guid lineId, Quantity shipped, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireStatus(LoadingInstructionStatus.Issued, LogisticsRuleCodes.InstructionNotIssued, "Sevk yalnız yayımlanmış talimatta kaydedilir.");
        var line = FindLine(lineId);
        RequireNotBlocked(line, "Bloklu satırdan sevk kaydedilemez.");
        RequirePositiveShare(shipmentId, shipped, line.LoadedNotShipped, LogisticsRuleCodes.ShipmentExceedsLoaded,
            "Sevk edilen pay yüklenmiş ama sevk edilmemiş payı aşamaz.");
        line.AddShipped(shipped);
        return Add(new LoadingShare(shipmentId, line.Id, LoadingShareKind.Shipped, shipped, null, null, now.ToUniversalTime()), now);
    }

    /// <summary>
    /// Marks part of a definite shipment as delivered. The delivery names its source shipment and
    /// is bounded by that shipment's undelivered remainder (§7.4). A block on the line does NOT
    /// stop this record (Business #66 5730092974, LOG-3): a block acts forward — it refuses new
    /// loads, shipments and use — but a shipment recorded BEFORE the block is a physical fact, and
    /// its delivery may still be completed, only up to that shipment's undelivered remainder. This
    /// record creates no new load, shipment or stock exit and lifts no block; a delivery without a
    /// real shipment, on another line's shipment, or above the remainder is refused whole with no
    /// version effect; the same delivery id again is one delivery. The line-level cap is a second
    /// guard that reports <see cref="LogisticsRuleCodes.InternalInvariant"/> (ORG #291 P3-1):
    /// it cannot be reached, because every shipment's remainder sums to the line's.
    /// </summary>
    public LoadingShare MarkDelivered(long expectedVersion, Guid deliveryId, Guid lineId, Guid shipmentId,
        Quantity delivered, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireStatus(LoadingInstructionStatus.Issued, LogisticsRuleCodes.InstructionNotIssued, "Teslim yalnız yayımlanmış talimatta kaydedilir.");
        var line = FindLine(lineId);
        var shipment = FindShipment(shipmentId);
        if (shipment.LineId != line.Id)
        {
            throw new LogisticsRuleException(LogisticsRuleCodes.ShipmentOfAnotherLine, "Teslim, başka satırın sevkine bağlanamaz.");
        }

        RequirePositiveShare(deliveryId, delivered, UndeliveredOf(shipment), LogisticsRuleCodes.DeliveryExceedsShipment,
            "Teslim edilen pay kaynak sevkin teslim edilmemiş kalanını aşamaz.");
        if (delivered.IsGreaterThan(line.ShippedNotDelivered))
        {
            // Unreachable by construction: the per-shipment cap above already bounds this. Kept
            // as a second guard, so it reports itself as one (LOG-3, ORG #291 P3-1).
            throw new LogisticsRuleException(LogisticsRuleCodes.InternalInvariant,
                "Teslim edilen pay sevk edilmiş ama teslim edilmemiş payı aşamaz (iç değişmez).");
        }

        line.AddDelivered(delivered);
        return Add(new LoadingShare(deliveryId, line.Id, LoadingShareKind.Delivered, delivered, null, shipment.ShareId,
            now.ToUniversalTime()), now);
    }

    /// <summary>
    /// Records the vehicle's manual weighbridge record (AT-053; OPEN-003: entered by hand). The backend
    /// calculates the net from the two weighings and the declared direction (WGT-001, refused when they
    /// contradict it) and keeps, as facts, the kilograms loaded on the instruction at this moment and the
    /// signed difference net − loaded (WGT-002). The difference is never judged: its tolerance is OPEN-004
    /// (DEC-STK-009), so no share is refused or adjusted because of it. One record per instruction: how a
    /// wrong entry is corrected is not decided, so a second record is refused (fail closed) rather than
    /// replacing the first. Nothing physical is written; Inventory is not touched.
    /// </summary>
    public VehicleWeighing RecordWeighing(long expectedVersion, Guid weighingId, WeighingDirection direction, Quantity firstWeighing,
        Quantity secondWeighing, string recordedBy, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireStatus(LoadingInstructionStatus.Issued, LogisticsRuleCodes.InstructionNotIssued, "Kantar tartımı yalnız yayımlanmış talimata kaydedilir.");
        Identity.Require(weighingId, nameof(weighingId));
        if (string.IsNullOrWhiteSpace(recordedBy))
        {
            throw new ArgumentException("Tartımı kaydeden kullanıcı belirtilmelidir.", nameof(recordedBy));
        }

        if (Weighing is not null)
        {
            throw new LogisticsRuleException(LogisticsRuleCodes.WeighingAlreadyRecorded,
                "Bu talimatın kantar tartımı zaten kayıtlı; düzeltme kuralı karara bağlanmadığı için ikinci tartım kaydedilmez.");
        }

        if (lines.Any(line => line.InstructedQuantity.Unit != QuantityUnit.Kilogram))
        {
            throw new LogisticsRuleException(LogisticsRuleCodes.WeighingUnitUnsupported, "Kantar farkı yalnız kilogram satırlarıyla karşılaştırılır.");
        }

        var net = WeighbridgeCalculator.Net(direction, firstWeighing, secondWeighing);
        // Re-read through Of so equal kilograms print equally (3.0 → 3), as every entered quantity does.
        var loaded = Quantity.Of(lines.Aggregate(Quantity.Zero(QuantityUnit.Kilogram), (sum, line) => sum.Add(line.LoadedQuantity)).Value,
            QuantityUnit.Kilogram);
        Weighing = new VehicleWeighing(weighingId, direction, firstWeighing, secondWeighing, net, loaded,
            WeighbridgeCalculator.Difference(net, loaded), shares.Count, recordedBy.Trim(), now.ToUniversalTime());
        Touch(now);
        return Weighing;
    }

    /// <summary>
    /// Prepares a dispatch-note (irsaliye) DRAFT from definite shipments of this instruction (AT-055; master v3 §7.8, L-08).
    /// Every named shipment must be a recorded shipment of this instruction, named once, and not on a non-cancelled dispatch
    /// note — a shipment is on at most one (AT-055: "aynı sevkiyat gerçekleşmesi iki kez irsaliyeye bağlanamaz"); a cancelled
    /// note released its shipments (user decision 2026-09-30: correction = cancel + new draft). Each line carries the
    /// shipment's shipped quantity only, in recording order. A block on the line does not stop the draft: the shipment already
    /// happened (LOG-3, a block acts forward), and a note books nothing physical. A note id is never reused, not even after
    /// its note was cancelled.
    /// </summary>
    public DispatchNoteDraft CreateDispatchNoteDraft(long expectedVersion, Guid dispatchNoteId, IReadOnlyCollection<Guid> shipmentIds,
        string createdBy, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireStatus(LoadingInstructionStatus.Issued, LogisticsRuleCodes.InstructionNotIssued, "İrsaliye taslağı yalnız yayımlanmış talimatın sevklerinden hazırlanır.");
        Identity.Require(dispatchNoteId, nameof(dispatchNoteId));
        ArgumentNullException.ThrowIfNull(shipmentIds);
        RequireActor(createdBy, "İrsaliye taslağını hazırlayan kullanıcı belirtilmelidir.", nameof(createdBy));
        if (shipmentIds.Count == 0 || shipmentIds.Any(id => id == Guid.Empty) || shipmentIds.Distinct().Count() != shipmentIds.Count)
        {
            throw new ArgumentException("İrsaliye taslağı en az bir sevki, her birini bir kez adlandırır.", nameof(shipmentIds));
        }

        if (dispatchNotes.Any(note => note.DispatchNoteId == dispatchNoteId))
        {
            throw new LogisticsRuleException(LogisticsRuleCodes.DuplicateDispatchNoteId, "Aynı irsaliye kimliği ikinci kez kullanılamaz.");
        }

        var linked = dispatchNotes.Where(note => note.HoldsShipments).SelectMany(note => note.Lines).Select(line => line.ShipmentId).ToHashSet();
        var chosen = new List<LoadingShare>(shipmentIds.Count);
        foreach (var shipmentId in shipmentIds)
        {
            var shipment = FindShipment(shipmentId);
            if (linked.Contains(shipmentId))
            {
                throw new LogisticsRuleException(LogisticsRuleCodes.ShipmentAlreadyDispatched,
                    "Bu sevk gerçekleşmesi zaten iptal edilmemiş bir irsaliyeye bağlı; ikinci kez bağlanamaz.");
            }

            chosen.Add(shipment);
        }

        var lines = chosen.OrderBy(shipment => IndexOfShare(shipment.ShareId))
            .Select((shipment, index) => new DispatchNoteLine(index + 1, shipment.ShareId, shipment.LineId, shipment.Quantity))
            .ToArray();
        var draft = new DispatchNoteDraft(dispatchNoteId, DispatchNoteStatus.Draft, Array.AsReadOnly(lines), shares.Count, createdBy.Trim(),
            now.ToUniversalTime(), DispatchSteps + 1);
        dispatchNotes.Add(draft);
        DispatchSteps++;
        Touch(now);
        return draft;
    }

    /// <summary>
    /// Approves (finalises) a DRAFT dispatch note (user decision 2026-09-30). The note becomes <see cref="DispatchNoteStatus.Approved"/>;
    /// its system number is assigned right after, in the same save, by <see cref="AssignDispatchNoteNumber"/> from the company's
    /// gap-free series (the series lives outside this aggregate). An approved note is never deleted, only cancelled. Who may
    /// approve is the permission's (no self-approval restriction is documented; ASSUMPTION-AT055-APPROVER).
    /// </summary>
    public DispatchNoteDraft ApproveDispatchNote(long expectedVersion, Guid dispatchNoteId, string approvedBy, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireStatus(LoadingInstructionStatus.Issued, LogisticsRuleCodes.InstructionNotIssued, "İrsaliye yalnız yayımlanmış talimatta onaylanır.");
        RequireActor(approvedBy, "İrsaliyeyi onaylayan kullanıcı belirtilmelidir.", nameof(approvedBy));
        var (index, note) = FindDispatchNote(dispatchNoteId);
        if (note.Status != DispatchNoteStatus.Draft)
        {
            throw new LogisticsRuleException(LogisticsRuleCodes.DispatchNoteState, "Yalnız taslak irsaliye onaylanır.");
        }

        var approved = note with
        {
            Status = DispatchNoteStatus.Approved,
            ApprovedBy = approvedBy.Trim(),
            ApprovedAtUtc = now.ToUniversalTime(),
            ApprovedStep = DispatchSteps + 1,
        };
        dispatchNotes[index] = approved;
        DispatchSteps++;
        Touch(now);
        return approved;
    }

    /// <summary>
    /// Gives the note approved by the LAST step its system number (once; no version step of its own — it is part of the
    /// approval's save). The number must not be on another note of this instruction; the company-wide uniqueness and the
    /// gap-free sequence are the series owner's (the store, in the same transaction) and SQL Server's unique key.
    /// </summary>
    public DispatchNoteDraft AssignDispatchNoteNumber(Guid dispatchNoteId, DispatchNoteNumber number)
    {
        ArgumentNullException.ThrowIfNull(number);
        var (index, note) = FindDispatchNote(dispatchNoteId);
        if (note.Status != DispatchNoteStatus.Approved || note.Number is not null || note.ApprovedStep != DispatchSteps
            || dispatchNotes.Any(other => other.Number == number))
        {
            throw new LogisticsRuleException(LogisticsRuleCodes.InternalInvariant, "İç tutarsızlık: numara yalnız az önce onaylanan irsaliyeye bir kez verilir.");
        }

        var numbered = note with { Number = number };
        dispatchNotes[index] = numbered;
        return numbered;
    }

    /// <summary>
    /// Cancels a draft or an approved dispatch note with a reason (user decision 2026-09-30): nothing is deleted — the note, its
    /// lines and its number stay visible — but its shipments are released, so they can be put on a new note. A cancelled note
    /// cannot be cancelled again or changed. Cancelling an official e-İrsaliye at GİB is outside this system (DEC-INT-001).
    /// </summary>
    public DispatchNoteDraft CancelDispatchNote(long expectedVersion, Guid dispatchNoteId, string cancelledBy, string reason, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireStatus(LoadingInstructionStatus.Issued, LogisticsRuleCodes.InstructionNotIssued, "İrsaliye yalnız yayımlanmış talimatta iptal edilir.");
        RequireActor(cancelledBy, "İrsaliyeyi iptal eden kullanıcı belirtilmelidir.", nameof(cancelledBy));
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > DispatchNoteCancelReasonMaximumLength)
        {
            throw new ArgumentException("İptal gerekçesi boş olamaz ve en fazla 500 karakterdir.", nameof(reason));
        }

        var (index, note) = FindDispatchNote(dispatchNoteId);
        if (note.Status == DispatchNoteStatus.Cancelled || (note.Status == DispatchNoteStatus.Approved && note.Number is null))
        {
            throw new LogisticsRuleException(LogisticsRuleCodes.DispatchNoteState, "İptal edilmiş irsaliye yeniden iptal edilemez.");
        }

        var cancelled = note with
        {
            Status = DispatchNoteStatus.Cancelled,
            CancelledBy = cancelledBy.Trim(),
            CancelledAtUtc = now.ToUniversalTime(),
            CancelReason = reason.Trim(),
            CancelledStep = DispatchSteps + 1,
        };
        dispatchNotes[index] = cancelled;
        DispatchSteps++;
        Touch(now);
        return cancelled;
    }

    /// <summary>
    /// Records the official e-İrsaliye number of an APPROVED note, once (entered by the user now; a later integration may write
    /// it the same way). It is kept apart from the system number and must be unique per company when present: this aggregate
    /// refuses a number already on one of its notes (cancelled ones too); the store checks the company and SQL Server holds it
    /// as a filtered unique key. Format: the GİB document number — three letters/digits and thirteen digits (year + sequence).
    /// </summary>
    public DispatchNoteDraft SetEDispatchNumber(long expectedVersion, Guid dispatchNoteId, string eDispatchNumber, string setBy, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireStatus(LoadingInstructionStatus.Issued, LogisticsRuleCodes.InstructionNotIssued, "e-İrsaliye numarası yalnız yayımlanmış talimatta kaydedilir.");
        RequireActor(setBy, "e-İrsaliye numarasını kaydeden kullanıcı belirtilmelidir.", nameof(setBy));
        if (!IsEDispatchNumber(eDispatchNumber))
        {
            throw new ArgumentException("e-İrsaliye numarası üç harf/rakam ve on üç rakamdır (ör. ABC2026000000001).", nameof(eDispatchNumber));
        }

        var (index, note) = FindDispatchNote(dispatchNoteId);
        if (note.Status != DispatchNoteStatus.Approved || note.Number is null || note.EDispatchNumber is not null)
        {
            throw new LogisticsRuleException(LogisticsRuleCodes.DispatchNoteState,
                "e-İrsaliye numarası yalnız onaylanmış ve numarası henüz kaydedilmemiş irsaliyeye bir kez kaydedilir.");
        }

        if (dispatchNotes.Any(other => string.Equals(other.EDispatchNumber, eDispatchNumber, StringComparison.Ordinal)))
        {
            throw new LogisticsRuleException(LogisticsRuleCodes.EDispatchNumberInUse, "Bu e-İrsaliye numarası başka bir irsaliyede kayıtlı.");
        }

        var recorded = note with
        {
            EDispatchNumber = eDispatchNumber,
            EDispatchNumberSetBy = setBy.Trim(),
            EDispatchNumberSetAtUtc = now.ToUniversalTime(),
            EDispatchNumberStep = DispatchSteps + 1,
        };
        dispatchNotes[index] = recorded;
        DispatchSteps++;
        Touch(now);
        return recorded;
    }

    /// <summary>The longest cancellation reason kept (the column's width).</summary>
    public const int DispatchNoteCancelReasonMaximumLength = 500;

    /// <summary>The GİB e-document number shape: 3 upper-case letters/digits, then 13 digits (4-digit year + 9-digit sequence).</summary>
    public static bool IsEDispatchNumber(string? value) =>
        value is { Length: 16 } && value.Take(3).All(character => character is (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
        && value.Skip(3).All(char.IsAsciiDigit);

    /// <summary>The non-cancelled dispatch note a shipment is on (AT-055), or null while it is on none.</summary>
    public Guid? DispatchNoteOf(Guid shipmentId) =>
        dispatchNotes.FirstOrDefault(note => note.HoldsShipments && note.Lines.Any(line => line.ShipmentId == shipmentId))?.DispatchNoteId;

    private (int Index, DispatchNoteDraft Note) FindDispatchNote(Guid dispatchNoteId)
    {
        var index = dispatchNotes.FindIndex(note => note.DispatchNoteId == dispatchNoteId);
        return index < 0
            ? throw new LogisticsRuleException(LogisticsRuleCodes.DispatchNoteNotFound, "İrsaliye bu talimatta bulunamadı.")
            : (index, dispatchNotes[index]);
    }

    private static void RequireActor(string actor, string message, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(actor))
        {
            throw new ArgumentException(message, parameterName);
        }
    }

    private int IndexOfShare(Guid shareId)
    {
        for (var index = 0; index < shares.Count; index++)
        {
            if (shares[index].ShareId == shareId)
            {
                return index;
            }
        }

        throw new LogisticsRuleException(LogisticsRuleCodes.InternalInvariant, "İç tutarsızlık: sevk pay listesinde yok.");
    }

    /// <summary>What a definite shipment still has undelivered: its quantity minus the deliveries that name it.</summary>
    public Quantity UndeliveredOf(Guid shipmentId) => UndeliveredOf(FindShipment(shipmentId));

    private Quantity UndeliveredOf(LoadingShare shipment) =>
        shares.Where(share => share.Kind == LoadingShareKind.Delivered && share.SourceShipmentId == shipment.ShareId)
            .Aggregate(shipment.Quantity, (left, delivery) => left.Subtract(delivery.Quantity));

    private LoadingShare FindShipment(Guid shipmentId) =>
        shares.FirstOrDefault(share => share.ShareId == shipmentId && share.Kind == LoadingShareKind.Shipped)
        ?? throw new LogisticsRuleException(LogisticsRuleCodes.ShipmentNotFound, "Kaynak sevk kaydı bulunamadı.");

    private void RequirePositiveShare(Guid shareId, Quantity quantity, Quantity available, string tooMuchCode, string tooMuch)
    {
        Identity.Require(shareId, nameof(shareId));
        if (shares.Any(share => share.ShareId == shareId))
        {
            throw new LogisticsRuleException(LogisticsRuleCodes.DuplicateShareId, "Aynı pay kaydı ikinci kez işlenemez.");
        }

        if (quantity.IsZero)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Pay miktarı sıfırdan büyük olmalıdır.");
        }

        if (quantity.IsGreaterThan(available))
        {
            throw new LogisticsRuleException(tooMuchCode, tooMuch);
        }
    }

    private static void RequireNotBlocked(LoadingInstructionLine line, string message)
    {
        if (line.IsBlocked)
        {
            throw new LogisticsRuleException(LogisticsRuleCodes.LineBlocked, message);
        }
    }

    private LoadingShare Add(LoadingShare share, DateTimeOffset now)
    {
        shares.Add(share);
        Touch(now);
        return share;
    }

    private LoadingInstructionLine FindLine(Guid lineId) =>
        lines.FirstOrDefault(line => line.Id == lineId)
        ?? throw new LogisticsRuleException(LogisticsRuleCodes.LineNotFound, "Talimat satırı bulunamadı.");

    private void RequireStatus(LoadingInstructionStatus required, string code, string message)
    {
        if (Status != required)
        {
            throw new LogisticsRuleException(code, message);
        }
    }

    private void RequireVersion(long expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new LogisticsVersionConflictException(expectedVersion, Version);
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAtUtc = now.ToUniversalTime();
        Version = checked(Version + 1);
    }
}

public enum LoadingShareKind
{
    Loaded = 1,
    Shipped = 2,
    Delivered = 3,
}

/// <summary>
/// One recorded share. Measurement is set for loads only; SourceShipmentId for deliveries only
/// (§7.4: shortage, damage, rejection and return are tied to the source shipment). Not a
/// document, not a stock movement.
/// </summary>
public sealed record LoadingShare(Guid ShareId, Guid LineId, LoadingShareKind Kind, Quantity Quantity,
    MeasurementKind? Measurement, Guid? SourceShipmentId, DateTimeOffset RecordedAtUtc);

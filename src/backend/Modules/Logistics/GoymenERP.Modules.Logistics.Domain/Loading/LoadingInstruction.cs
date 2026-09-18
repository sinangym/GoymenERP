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

    public static LoadingInstruction Create(Guid id, Guid companyId, Guid customerPartyId, Guid companyAccountId, DateTimeOffset now) =>
        new(id, companyId, customerPartyId, companyAccountId, now);

    /// <summary>
    /// What should be loaded for one product, from the sales order line it serves (identity only,
    /// §5 "her olayın kaynak kimliği vardır"), in a Settings unit (identity only; the unit's
    /// profile and the quantity's scale are verified by the Application layer), optionally
    /// drawing from one Inventory allocation. Loading is a sub-share of the allocation, never a
    /// second reservation.
    /// </summary>
    public LoadingInstructionLine AddLine(long expectedVersion, Guid lineId, Guid sourceOrderLineId, Guid productId, Guid unitId,
        Guid? allocationId, Quantity instructed, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireStatus(LoadingInstructionStatus.Draft, LogisticsRuleCodes.InstructionNotDraft, "Yalnız taslak talimata satır eklenebilir.");
        Identity.Require(lineId, nameof(lineId));
        Identity.Require(sourceOrderLineId, nameof(sourceOrderLineId));
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

        var line = new LoadingInstructionLine(lineId, sourceOrderLineId, productId, unitId, allocationId, instructed);
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
    /// is bounded by that shipment's undelivered remainder first (§7.4), then by the line as a
    /// whole. A block on the line does NOT stop this record: the goods have already left, and
    /// §7.4 ties shortage, damage, rejection and return to the source shipment as facts about it.
    /// Whether a later block should also flag the delivery is an open question for Business
    /// (README); until then the record is not refused.
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
            throw new LogisticsRuleException(LogisticsRuleCodes.DeliveryExceedsLine,
                "Teslim edilen pay sevk edilmiş ama teslim edilmemiş payı aşamaz.");
        }

        line.AddDelivered(delivered);
        return Add(new LoadingShare(deliveryId, line.Id, LoadingShareKind.Delivered, delivered, null, shipment.ShareId,
            now.ToUniversalTime()), now);
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

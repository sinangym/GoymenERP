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
/// A loading instruction for one company towards one customer Party (#275 §7.4). An
/// instruction is NOT a definite shipment: it says what should be loaded. What actually gets
/// loaded, shipped and delivered are separate shares per line, each capped by the one before
/// it, so a loaded sub-share is never subtracted twice (§13: 6.000 allocated, 4.000 loaded,
/// 3.800 shipped → 200 loaded-not-shipped stays visible). The physical stock exit and the
/// allocation consumption of a definite shipment belong to Inventory (T11/T12); this record
/// books nothing physical. Vehicle, driver and weighing tickets are later slices.
/// </summary>
public sealed class LoadingInstruction
{
    private readonly List<LoadingInstructionLine> lines = [];
    private readonly List<LoadingShare> shares = [];

    private LoadingInstruction(Guid id, Guid companyId, Guid customerPartyId, DateTimeOffset now)
    {
        Id = Identity.Require(id, nameof(id));
        CompanyId = Identity.Require(companyId, nameof(companyId));
        CustomerPartyId = Identity.Require(customerPartyId, nameof(customerPartyId));
        CreatedAtUtc = now.ToUniversalTime();
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; }
    public Guid CompanyId { get; }
    public Guid CustomerPartyId { get; }
    public LoadingInstructionStatus Status { get; private set; } = LoadingInstructionStatus.Draft;
    public long Version { get; private set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public IReadOnlyList<LoadingInstructionLine> Lines => lines.AsReadOnly();

    /// <summary>Every recorded load, shipment and delivery share, in order.</summary>
    public IReadOnlyList<LoadingShare> Shares => shares.AsReadOnly();

    public static LoadingInstruction Create(Guid id, Guid companyId, Guid customerPartyId, DateTimeOffset now) =>
        new(id, companyId, customerPartyId, now);

    /// <summary>
    /// What should be loaded for one product, optionally drawing from one Inventory allocation
    /// (identity only). The allocation is the source; loading is a sub-share of it, never a
    /// second reservation.
    /// </summary>
    public LoadingInstructionLine AddLine(long expectedVersion, Guid lineId, Guid productId, Guid? allocationId,
        Quantity instructed, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireStatus(LoadingInstructionStatus.Draft, "Yalnız taslak talimata satır eklenebilir.");
        Identity.Require(lineId, nameof(lineId));
        Identity.Require(productId, nameof(productId));
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
            throw new InvalidOperationException("Aynı talimat satırı kimliği ikinci kez eklenemez.");
        }

        var line = new LoadingInstructionLine(lineId, productId, allocationId, instructed);
        lines.Add(line);
        Touch(now);
        return line;
    }

    public void Issue(long expectedVersion, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireStatus(LoadingInstructionStatus.Draft, "Yalnız taslak talimat yayımlanabilir.");
        if (lines.Count == 0)
        {
            throw new InvalidOperationException("Satırsız talimat yayımlanamaz.");
        }

        Status = LoadingInstructionStatus.Issued;
        Touch(now);
    }

    /// <summary>A quality or ownership block on a line (its source is Inventory/Quality): loads are refused while it stands.</summary>
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
        RequireStatus(LoadingInstructionStatus.Issued, "Yükleme yalnız yayımlanmış talimata kaydedilir.");
        if (!Enum.IsDefined(measurement))
        {
            throw new ArgumentException("Ölçüm niteliği belirtilmelidir.", nameof(measurement));
        }

        var line = FindLine(lineId);
        if (line.IsBlocked)
        {
            throw new InvalidOperationException("Bloklu satıra yükleme kaydedilemez.");
        }

        RequirePositiveShare(loadId, loaded, line.RemainingToLoad, "Yüklenen pay talimatın yüklenmemiş kalanını aşamaz.");
        line.AddLoaded(loaded);
        return Add(new LoadingShare(loadId, line.Id, LoadingShareKind.Loaded, loaded, measurement, now.ToUniversalTime()), now);
    }

    /// <summary>
    /// Marks part of the loaded share as definitely shipped. Shipped is taken from LOADED only,
    /// never from the instruction again, so nothing is decremented twice. A blocked line ships
    /// nothing, even when it was loaded before the block (§7.4 "kalite bloku … engellenir", Ö3
    /// "sevk serbestliği yok"): the block matters most at the moment goods would leave. The stock
    /// exit and the allocation consumption that go with a definite shipment are Inventory's
    /// (T11/T12) and belong in the same transaction as this share.
    /// </summary>
    public LoadingShare MarkShipped(long expectedVersion, Guid shipmentId, Guid lineId, Quantity shipped, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireStatus(LoadingInstructionStatus.Issued, "Sevk yalnız yayımlanmış talimatta kaydedilir.");
        var line = FindLine(lineId);
        if (line.IsBlocked)
        {
            throw new InvalidOperationException("Bloklu satırdan sevk kaydedilemez.");
        }

        RequirePositiveShare(shipmentId, shipped, line.LoadedNotShipped, "Sevk edilen pay yüklenmiş ama sevk edilmemiş payı aşamaz.");
        line.AddShipped(shipped);
        return Add(new LoadingShare(shipmentId, line.Id, LoadingShareKind.Shipped, shipped, null, now.ToUniversalTime()), now);
    }

    /// <summary>
    /// Marks part of the shipped share as delivered; delivery never exceeds what was shipped. A
    /// block on the line does NOT stop this record: the goods have already left, and §7.4 ties
    /// shortage, damage, rejection and return to the source shipment as facts about it, so a
    /// delivery record is that kind of fact. Whether a later block should also flag the delivery
    /// is an open question for Business (README); until then the record is not refused.
    /// </summary>
    public LoadingShare MarkDelivered(long expectedVersion, Guid deliveryId, Guid lineId, Quantity delivered, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireStatus(LoadingInstructionStatus.Issued, "Teslim yalnız yayımlanmış talimatta kaydedilir.");
        var line = FindLine(lineId);
        RequirePositiveShare(deliveryId, delivered, line.ShippedNotDelivered, "Teslim edilen pay sevk edilmiş ama teslim edilmemiş payı aşamaz.");
        line.AddDelivered(delivered);
        return Add(new LoadingShare(deliveryId, line.Id, LoadingShareKind.Delivered, delivered, null, now.ToUniversalTime()), now);
    }

    private void RequirePositiveShare(Guid shareId, Quantity quantity, Quantity available, string tooMuch)
    {
        Identity.Require(shareId, nameof(shareId));
        if (shares.Any(share => share.ShareId == shareId))
        {
            throw new InvalidOperationException("Aynı pay kaydı ikinci kez işlenemez.");
        }

        if (quantity.IsZero)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Pay miktarı sıfırdan büyük olmalıdır.");
        }

        if (quantity.IsGreaterThan(available))
        {
            throw new InvalidOperationException(tooMuch);
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
        ?? throw new InvalidOperationException("Talimat satırı bulunamadı.");

    private void RequireStatus(LoadingInstructionStatus required, string message)
    {
        if (Status != required)
        {
            throw new InvalidOperationException(message);
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

/// <summary>One recorded share. Measurement is set for loads only. Not a document, not a stock movement.</summary>
public sealed record LoadingShare(Guid ShareId, Guid LineId, LoadingShareKind Kind, Quantity Quantity,
    MeasurementKind? Measurement, DateTimeOffset RecordedAtUtc);

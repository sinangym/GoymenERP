using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Domain.Transfers;

public enum InventoryTransferStatus
{
    Planned = 1,
    InTransit = 2,
    PartiallyAccepted = 3,
    Accepted = 4,

    /// <summary>KLN-D20: the dispatch was reversed by a separate movement before any acceptance; terminal.</summary>
    DispatchCancelled = 5,

    /// <summary>
    /// KLN-D23: every line is terminal and at least one line was written off as an approved transit loss (the rest
    /// were received). A transfer whose every line was received stays <see cref="Accepted"/>; terminal.
    /// </summary>
    Closed = 6,
}

public enum InventoryTransferLineStatus
{
    Planned = 1,
    InTransit = 2,
    Accepted = 3,
    DispatchCancelled = 4,

    /// <summary>KLN-D23 (c): the pending label was written off as an approved transit loss; terminal.</summary>
    WrittenOff = 5,
}

/// <summary>An outgoing or incoming delivery-document identity. Its kind is an owner token, not a closed policy list.</summary>
public sealed record InventoryTransferDocumentReference
{
    public InventoryTransferDocumentReference(string kind, Guid id, Guid? lineId = null)
    {
        var normalized = kind?.Trim();
        Kind = string.IsNullOrWhiteSpace(normalized) || normalized.Length > 64 || normalized.Any(char.IsControl)
            ? throw Invalid("Belge türü 1-64 karakter olmalıdır.")
            : normalized;
        Id = id == Guid.Empty ? throw Invalid("Belge kimliği zorunludur.") : id;
        LineId = lineId == Guid.Empty ? throw Invalid("Belge satırı verildiyse boş olamaz.") : lineId;
    }

    public string Kind { get; }
    public Guid Id { get; }
    public Guid? LineId { get; }

    private static InventoryRuleException Invalid(string message) =>
        new(InventoryRuleCodes.InvalidTransfer, message);
}

public sealed record InventoryTransferPlanLine(
    Guid LineId,
    Guid LabelId,
    long ExpectedLabelVersion,
    Kilograms PlannedQuantity);

/// <summary>
/// The exact stored buckets of one planned label at dispatch. Supplying every bucket makes a dispatch a whole-label
/// operation while preserving Free/Blocked state losslessly. Active-allocation checks remain the writer's SQL race gate.
/// </summary>
public sealed record InventoryTransferDispatchLine(
    Guid LineId,
    long LabelVersion,
    IReadOnlyList<StockBalance> Buckets);

public sealed record InventoryTransferAcceptedLineState(
    Guid LineId,
    DateTimeOffset AcceptedAtUtc,
    InventoryTransferDocumentReference IncomingDocument);

/// <summary>KLN-D23 (c): an approved transit-loss decision of one still pending line.</summary>
public sealed record InventoryTransferWrittenOffLineState(Guid LineId, DateTimeOffset WrittenOffAtUtc);

public sealed record InventoryTransferBucketEffect(StockPosition Position, Kilograms Quantity);

public sealed record InventoryTransferLineEffect(
    Guid LineId,
    Guid LabelId,
    Kilograms PlannedQuantity,
    Kilograms ActualQuantity,
    decimal DifferenceKg,
    IReadOnlyList<InventoryTransferBucketEffect> Buckets);

public sealed record InventoryTransferDispatchEffect(
    Guid TransferId,
    long TransferVersion,
    InventoryTransferDocumentReference OutgoingDocument,
    DateTimeOffset DispatchedAtUtc,
    IReadOnlyList<InventoryTransferLineEffect> Lines)
{
    public StockMovement Movement(Guid movementId, Guid companyId) => new(
        movementId,
        companyId,
        StockMovementKind.TransferDispatch,
        DispatchedAtUtc,
        new StockSourceReference(InventoryTransfer.SourceKind, TransferId),
        Lines.SelectMany(line => line.Buckets)
            .Select(bucket => new StockMovementLine(bucket.Position, StockDirection.Out, bucket.Quantity)));
}

public sealed record InventoryTransferAcceptanceEffect(
    Guid TransferId,
    long TransferVersion,
    InventoryTransferDocumentReference IncomingDocument,
    DateTimeOffset AcceptedAtUtc,
    InventoryTransferLineEffect Line)
{
    public StockMovement Movement(Guid movementId, Guid companyId) => new(
        movementId,
        companyId,
        StockMovementKind.TransferAccept,
        AcceptedAtUtc,
        new StockSourceReference(InventoryTransfer.SourceKind, TransferId, Line.LineId),
        Line.Buckets.Select(bucket => new StockMovementLine(bucket.Position, StockDirection.In, bucket.Quantity)));
}

/// <summary>
/// KLN-D21: the automatic fire of a receipt shortage. The receiver's entered kilograms are final; the dispatched
/// label is first accepted whole at the destination and this separate Out line then removes the missing kilograms
/// from that destination bucket. No tolerance and no extra approval apply (an application reading, revocable).
/// </summary>
public sealed record InventoryTransferReceiptShortageEffect(
    Guid TransferId,
    Guid LineId,
    DateTimeOffset ReceivedAtUtc,
    InventoryTransferBucketEffect Bucket)
{
    public StockMovement Movement(Guid movementId, Guid companyId) => new(
        movementId,
        companyId,
        StockMovementKind.TransferReceiptShortage,
        ReceivedAtUtc,
        new StockSourceReference(InventoryTransfer.SourceKind, TransferId, LineId),
        [new StockMovementLine(Bucket.Position, StockDirection.Out, Bucket.Quantity)]);
}

/// <summary>
/// B-22c: one received line. <see cref="Acceptance"/> always moves the whole dispatched label to the destination;
/// a shortage adds <see cref="Shortage"/>; an overage is only reported here, because KLN-D24 keeps the excess out of
/// stock until a different person approves it.
/// </summary>
public sealed record InventoryTransferReceiptEffect(
    InventoryTransferAcceptanceEffect Acceptance,
    Kilograms DispatchedQuantity,
    Kilograms ReceivedQuantity,
    Kilograms ShortageQuantity,
    Kilograms OverageQuantity,
    InventoryTransferReceiptShortageEffect? Shortage);

/// <summary>KLN-D24: the approved receipt excess, one In line on the received destination bucket.</summary>
public sealed record InventoryTransferReceiptOverageEffect(
    Guid TransferId,
    Guid LineId,
    DateTimeOffset ApprovedAtUtc,
    InventoryTransferBucketEffect Bucket)
{
    public StockMovement Movement(Guid movementId, Guid companyId) => new(
        movementId,
        companyId,
        StockMovementKind.TransferReceiptOverage,
        ApprovedAtUtc,
        new StockSourceReference(InventoryTransfer.SourceKind, TransferId, LineId),
        [new StockMovementLine(Bucket.Position, StockDirection.In, Bucket.Quantity)]);
}

/// <summary>
/// KLN-D23 (c): the approved transit loss of one pending line. The dispatched kilograms already left the source at
/// dispatch, so the loss changes no balance: every dispatched bucket is recorded as an In and an Out of the same
/// kilograms on its exact source position, which keeps the written-off quantity a movement fact of its own.
/// </summary>
public sealed record InventoryTransferTransitLossEffect(
    Guid TransferId,
    long TransferVersion,
    DateTimeOffset WrittenOffAtUtc,
    InventoryTransferLineEffect Line)
{
    public StockMovement Movement(Guid movementId, Guid companyId) => new(
        movementId,
        companyId,
        StockMovementKind.TransferTransitLoss,
        WrittenOffAtUtc,
        new StockSourceReference(InventoryTransfer.SourceKind, TransferId, Line.LineId),
        Line.Buckets.SelectMany(bucket => new[]
        {
            new StockMovementLine(bucket.Position, StockDirection.In, bucket.Quantity),
            new StockMovementLine(bucket.Position, StockDirection.Out, bucket.Quantity),
        }));
}

/// <summary>
/// KLN-D20: the reverse of a whole dispatch. The dispatch row and its movement stay; this separate inbound movement
/// puts every dispatched bucket back at its exact source position (same block, owner, custodian and location). Cost is
/// not touched here: KLN-D64 (2), while transfers do not post to the cost ledger the cancellation does not either. When
/// transfer cost posting starts, the reverse re-enters the source at the unit cost used at dispatch, never at the source's
/// average at cancel time (KLN-D64 (1), <see cref="Costing.AverageCostCalculator.ApplyTransferDispatchCancel"/>).
/// </summary>
public sealed record InventoryTransferDispatchCancellationEffect(
    Guid TransferId,
    long TransferVersion,
    DateTimeOffset CancelledAtUtc,
    IReadOnlyList<InventoryTransferLineEffect> Lines)
{
    public StockMovement Movement(Guid movementId, Guid companyId) => new(
        movementId,
        companyId,
        StockMovementKind.TransferDispatchCancellation,
        CancelledAtUtc,
        new StockSourceReference(InventoryTransfer.SourceKind, TransferId),
        Lines.SelectMany(line => line.Buckets)
            .Select(bucket => new StockMovementLine(bucket.Position, StockDirection.In, bucket.Quantity)));
}

/// <summary>
/// DEC-STK-008 / INV-R20: a same-company two-leg transfer. Planning changes no stock. Dispatch derives the actual
/// shipped quantity from every stored bucket of every named label and moves those whole labels into transit. Acceptance
/// moves one whole dispatched label to the target context; accepting a subset of lines is the supported partial-accept
/// grain. B-22c receipt adds the receiver's weighed kilograms: a shortage is a separate automatic fire (KLN-D21), an
/// excess waits for a maker-checker approval (KLN-D24) and a pending label may be written off as an approved transit
/// loss (KLN-D23 (c)). Loss, damage and missing acceptance remain separate events.
/// </summary>
public sealed class InventoryTransfer
{
    public const string SourceKind = "inventory.transfer";

    private readonly Line[] lines;

    public InventoryTransfer(
        Guid id,
        Guid companyId,
        Guid sourceLocationId,
        StockCustodian sourceCustodian,
        Guid destinationLocationId,
        StockCustodian destinationCustodian,
        DateTimeOffset plannedAtUtc,
        IEnumerable<InventoryTransferPlanLine> lines)
    {
        Id = RequireId(id, "Transfer");
        CompanyId = RequireId(companyId, "Şirket");
        SourceLocationId = RequireId(sourceLocationId, "Kaynak konum");
        DestinationLocationId = RequireId(destinationLocationId, "Hedef konum");
        SourceCustodian = sourceCustodian.Id == Guid.Empty ? throw Invalid("Kaynak gözeten zorunludur.") : sourceCustodian;
        DestinationCustodian = destinationCustodian.Id == Guid.Empty ? throw Invalid("Hedef gözeten zorunludur.") : destinationCustodian;
        if (SourceLocationId == DestinationLocationId && SourceCustodian == DestinationCustodian)
        {
            throw Invalid("Transfer hedefi kaynak fiziksel yerinden farklı olmalıdır.");
        }

        RequireUtc(plannedAtUtc, "Plan zamanı");
        PlannedAtUtc = plannedAtUtc;
        ArgumentNullException.ThrowIfNull(lines);
        this.lines = [.. lines.Select(CreateLine)];
        if (this.lines.Length == 0 || this.lines.Select(line => line.LineId).Distinct().Count() != this.lines.Length
            || this.lines.Select(line => line.LabelId).Distinct().Count() != this.lines.Length)
        {
            throw Invalid("Transfer en az bir benzersiz satır ve etiket içermelidir.");
        }
    }

    public Guid Id { get; }
    public Guid CompanyId { get; }
    public Guid SourceLocationId { get; }
    public StockCustodian SourceCustodian { get; }
    public Guid DestinationLocationId { get; }
    public StockCustodian DestinationCustodian { get; }
    public DateTimeOffset PlannedAtUtc { get; }
    public DateTimeOffset? DispatchedAtUtc { get; private set; }
    public DateTimeOffset? DispatchCancelledAtUtc { get; private set; }
    public InventoryTransferDocumentReference? OutgoingDocument { get; private set; }
    public long Version { get; private set; } = 1;
    public InventoryTransferStatus Status { get; private set; } = InventoryTransferStatus.Planned;
    public IReadOnlyList<InventoryTransferLineSnapshot> Lines =>
        [.. lines.Select(line => line.Snapshot())];

    public static InventoryTransfer Restore(
        Guid id,
        Guid companyId,
        Guid sourceLocationId,
        StockCustodian sourceCustodian,
        Guid destinationLocationId,
        StockCustodian destinationCustodian,
        DateTimeOffset plannedAtUtc,
        IEnumerable<InventoryTransferPlanLine> plannedLines,
        long storedVersion,
        InventoryTransferStatus storedStatus,
        DateTimeOffset? dispatchedAtUtc,
        InventoryTransferDocumentReference? outgoingDocument,
        IEnumerable<InventoryTransferDispatchLine>? dispatchedLines,
        IEnumerable<InventoryTransferAcceptedLineState>? acceptedLines,
        DateTimeOffset? dispatchCancelledAtUtc = null,
        IEnumerable<InventoryTransferWrittenOffLineState>? writtenOffLines = null)
    {
        var transfer = new InventoryTransfer(id, companyId, sourceLocationId, sourceCustodian,
            destinationLocationId, destinationCustodian, plannedAtUtc, plannedLines);
        var accepted = acceptedLines?.OrderBy(line => line.AcceptedAtUtc).ThenBy(line => line.LineId).ToArray() ?? [];
        var writtenOff = writtenOffLines?.ToArray() ?? [];
        if (storedStatus == InventoryTransferStatus.Planned)
        {
            if (storedVersion != 1 || dispatchedAtUtc is not null || outgoingDocument is not null
                || dispatchedLines is not null || accepted.Length != 0 || writtenOff.Length != 0)
            {
                throw Invalid("Kayıtlı planlanmış transfer durumu tutarsız.");
            }

            return transfer;
        }

        if (dispatchedAtUtc is null || outgoingDocument is null || dispatchedLines is null)
        {
            throw Invalid("Kayıtlı yoldaki transferin çıkış bilgisi eksik.");
        }

        transfer.Dispatch(1, dispatchedAtUtc.Value, outgoingDocument, dispatchedLines);
        if (dispatchCancelledAtUtc is { } cancelledAtUtc)
        {
            if (accepted.Length != 0 || writtenOff.Length != 0)
            {
                throw Invalid("İptal edilmiş transfer çıkışında kabul veya kayıp kaydı olamaz.");
            }

            transfer.CancelDispatch(transfer.Version, cancelledAtUtc);
        }

        // Acceptances and write-offs are replayed in their recorded order; a line in both is refused by the second.
        var events = accepted
            .Select(acceptance => (At: acceptance.AcceptedAtUtc, acceptance.LineId,
                Acceptance: (InventoryTransferAcceptedLineState?)acceptance,
                WriteOff: (InventoryTransferWrittenOffLineState?)null))
            .Concat(writtenOff.Select(writeOff => (At: writeOff.WrittenOffAtUtc, writeOff.LineId,
                Acceptance: (InventoryTransferAcceptedLineState?)null, WriteOff: (InventoryTransferWrittenOffLineState?)writeOff)))
            .OrderBy(item => item.At)
            .ThenBy(item => item.LineId)
            .ToArray();
        foreach (var item in events)
        {
            if (item.Acceptance is { } acceptance)
            {
                transfer.AcceptLine(transfer.Version, acceptance.LineId, acceptance.AcceptedAtUtc,
                    acceptance.IncomingDocument);
            }
            else if (item.WriteOff is { } writeOff)
            {
                transfer.WriteOffLine(transfer.Version, writeOff.LineId, writeOff.WrittenOffAtUtc);
            }
        }

        if (transfer.Version != storedVersion || transfer.Status != storedStatus)
        {
            throw Invalid("Kayıtlı transfer sürümü veya durumu olaylarıyla uyuşmuyor.");
        }

        return transfer;
    }

    public InventoryTransferDispatchEffect Dispatch(
        long expectedVersion,
        DateTimeOffset dispatchedAtUtc,
        InventoryTransferDocumentReference outgoingDocument,
        IEnumerable<InventoryTransferDispatchLine> actualLines)
    {
        RequireVersion(expectedVersion);
        RequireUtc(dispatchedAtUtc, "Çıkış zamanı");
        if (dispatchedAtUtc < PlannedAtUtc)
        {
            throw Invalid("Çıkış zamanı transfer planından önce olamaz.");
        }
        ArgumentNullException.ThrowIfNull(outgoingDocument);
        ArgumentNullException.ThrowIfNull(actualLines);
        if (Status != InventoryTransferStatus.Planned)
        {
            throw Invalid("Yalnız planlanmış transfer yola çıkarılabilir.");
        }

        var supplied = actualLines.ToArray();
        if (supplied.Length != lines.Length || supplied.Select(line => line.LineId).Distinct().Count() != supplied.Length)
        {
            throw Invalid("Çıkış bütün transfer satırlarını tam bir kez içermelidir.");
        }

        var effects = new List<InventoryTransferLineEffect>(lines.Length);
        foreach (var line in lines)
        {
            var actual = supplied.SingleOrDefault(candidate => candidate.LineId == line.LineId)
                ?? throw Invalid("Çıkış bütün transfer satırlarını içermelidir.");
            effects.Add(ValidateDispatch(line, actual));
        }

        foreach (var effect in effects)
        {
            var line = lines.Single(candidate => candidate.LineId == effect.LineId);
            line.Dispatch(effect);
        }

        Version = checked(Version + 1);
        Status = InventoryTransferStatus.InTransit;
        DispatchedAtUtc = dispatchedAtUtc;
        OutgoingDocument = outgoingDocument;
        return new(Id, Version, outgoingDocument, dispatchedAtUtc, effects.ToArray());
    }

    public InventoryTransferAcceptanceEffect AcceptLine(
        long expectedVersion,
        Guid lineId,
        DateTimeOffset acceptedAtUtc,
        InventoryTransferDocumentReference incomingDocument)
    {
        RequireVersion(expectedVersion);
        RequireUtc(acceptedAtUtc, "Kabul zamanı");
        ArgumentNullException.ThrowIfNull(incomingDocument);
        if (Status is not (InventoryTransferStatus.InTransit or InventoryTransferStatus.PartiallyAccepted))
        {
            throw Invalid("Yalnız yoldaki transfer satırı kabul edilebilir.");
        }

        if (DispatchedAtUtc is null || acceptedAtUtc < DispatchedAtUtc)
        {
            throw Invalid("Kabul zamanı transfer çıkışından önce olamaz.");
        }

        var line = lines.SingleOrDefault(candidate => candidate.LineId == lineId)
            ?? throw Invalid("Transfer satırı bulunamadı.");
        if (line.Status != InventoryTransferLineStatus.InTransit || line.Effect is null)
        {
            throw Invalid("Transfer satırı yolda değil veya daha önce kabul edildi.");
        }

        var targetBuckets = line.Effect.Buckets
            .Select(bucket => new InventoryTransferBucketEffect(
                bucket.Position.Relocate(DestinationCustodian, DestinationLocationId), bucket.Quantity))
            .ToArray();
        line.Accept(acceptedAtUtc, incomingDocument);
        Version = checked(Version + 1);
        Status = Settle();
        var effect = line.Effect with { Buckets = targetBuckets };
        return new(Id, Version, incomingDocument, acceptedAtUtc, effect);
    }

    /// <summary>
    /// B-22c receipt of one in-transit line with the receiver's weighed kilograms (KLN-D21: the entered kg is final).
    /// The whole dispatched label is accepted at the destination exactly as <see cref="AcceptLine"/> does. A shortage
    /// becomes a separate automatic fire movement; an overage is only reported (KLN-D24 approval comes later). A
    /// KLN-D91: a label dispatched in more than one block/lot bucket is received only with its full dispatched kilograms;
    /// a difference is refused and never split over the buckets.
    /// </summary>
    public InventoryTransferReceiptEffect ReceiveLine(
        long expectedVersion,
        Guid lineId,
        DateTimeOffset receivedAtUtc,
        InventoryTransferDocumentReference incomingDocument,
        Kilograms receivedQuantity)
    {
        if (receivedQuantity.Value <= 0m)
        {
            throw Invalid("Teslim alınan miktar sıfırdan büyük olmalıdır.");
        }

        var candidate = lines.SingleOrDefault(item => item.LineId == lineId);
        if (candidate?.Effect is { } dispatched && dispatched.Buckets.Count != 1
            && receivedQuantity != dispatched.ActualQuantity)
        {
            throw Invalid("Birden çok blok veya partiyle sevk edilen etiket yalnız tam miktarla teslim alınır.");
        }

        var acceptance = AcceptLine(expectedVersion, lineId, receivedAtUtc, incomingDocument);
        var dispatchedQuantity = acceptance.Line.ActualQuantity;
        var shortage = dispatchedQuantity > receivedQuantity
            ? Kilograms.Of(dispatchedQuantity.Value - receivedQuantity.Value)
            : Kilograms.Zero;
        var overage = receivedQuantity > dispatchedQuantity
            ? Kilograms.Of(receivedQuantity.Value - dispatchedQuantity.Value)
            : Kilograms.Zero;
        var shortageEffect = shortage.Value > 0m
            ? new InventoryTransferReceiptShortageEffect(Id, lineId, receivedAtUtc,
                new InventoryTransferBucketEffect(acceptance.Line.Buckets[0].Position, shortage))
            : null;
        return new(acceptance, dispatchedQuantity, receivedQuantity, shortage, overage, shortageEffect);
    }

    /// <summary>
    /// KLN-D24: the stock effect of an approved receipt excess on an already received single-bucket line. It changes
    /// no transfer state; the maker-checker rule and the excess quantity are the writer's stored facts.
    /// </summary>
    public InventoryTransferReceiptOverageEffect ReceiptOverage(
        Guid lineId,
        Kilograms overageQuantity,
        DateTimeOffset approvedAtUtc)
    {
        RequireUtc(approvedAtUtc, "Onay zamanı");
        if (overageQuantity.Value <= 0m)
        {
            throw Invalid("Fazla teslim miktarı sıfırdan büyük olmalıdır.");
        }

        var line = lines.SingleOrDefault(candidate => candidate.LineId == lineId)
            ?? throw Invalid("Transfer satırı bulunamadı.");
        if (line.Status != InventoryTransferLineStatus.Accepted || line.Effect is null
            || line.AcceptedAtUtc is not { } acceptedAtUtc)
        {
            throw Invalid("Fazla teslim yalnız teslim alınmış satırda onaylanabilir.");
        }

        if (line.Effect.Buckets.Count != 1)
        {
            // KLN-D91: a multi-block label is received only in full, so it never carries an excess to approve.
            throw Invalid("Birden çok blok veya partiyle sevk edilen etiket yalnız tam miktarla teslim alınır.");
        }

        if (approvedAtUtc < acceptedAtUtc)
        {
            throw Invalid("Onay zamanı teslim almadan önce olamaz.");
        }

        var position = line.Effect.Buckets[0].Position.Relocate(DestinationCustodian, DestinationLocationId);
        return new(Id, lineId, approvedAtUtc, new InventoryTransferBucketEffect(position, overageQuantity));
    }

    /// <summary>
    /// KLN-D23 (c): writes one pending (in-transit) line off as an approved transit loss. The maker-checker approval is
    /// the writer's stored fact; this method guards the transfer state. A transfer closes only when no pending line is
    /// left; <see cref="InventoryTransferStatus.Closed"/> marks one that closed with at least one written-off line.
    /// </summary>
    public InventoryTransferTransitLossEffect WriteOffLine(
        long expectedVersion,
        Guid lineId,
        DateTimeOffset writtenOffAtUtc)
    {
        RequireVersion(expectedVersion);
        RequireUtc(writtenOffAtUtc, "Kayıp zamanı");
        if (Status is not (InventoryTransferStatus.InTransit or InventoryTransferStatus.PartiallyAccepted))
        {
            throw Invalid("Yalnız yoldaki transfer satırı kayıp olarak kapatılabilir.");
        }

        if (DispatchedAtUtc is null || writtenOffAtUtc < DispatchedAtUtc)
        {
            throw Invalid("Kayıp zamanı transfer çıkışından önce olamaz.");
        }

        var line = lines.SingleOrDefault(candidate => candidate.LineId == lineId)
            ?? throw Invalid("Transfer satırı bulunamadı.");
        if (line.Status != InventoryTransferLineStatus.InTransit || line.Effect is null)
        {
            throw Invalid("Transfer satırı yolda değil veya daha önce kapatıldı.");
        }

        line.WriteOff(writtenOffAtUtc);
        Version = checked(Version + 1);
        Status = Settle();
        return new(Id, Version, writtenOffAtUtc, line.Effect);
    }

    private InventoryTransferStatus Settle()
    {
        if (lines.All(line => line.Status is InventoryTransferLineStatus.Accepted
                or InventoryTransferLineStatus.WrittenOff))
        {
            return lines.Any(line => line.Status == InventoryTransferLineStatus.WrittenOff)
                ? InventoryTransferStatus.Closed
                : InventoryTransferStatus.Accepted;
        }

        return lines.Any(line => line.Status == InventoryTransferLineStatus.Accepted)
            ? InventoryTransferStatus.PartiallyAccepted
            : InventoryTransferStatus.InTransit;
    }

    /// <summary>
    /// KLN-D20: reverses the whole dispatch. Only an in-transit transfer whose every line is still in transit can be
    /// cancelled; an accepted or partially accepted transfer never can. The cancellation window (KLN-D25/D27) is decided
    /// by <see cref="InventoryTransferCancelWindow"/> before this is called; this method only guards the stock state.
    /// </summary>
    public InventoryTransferDispatchCancellationEffect CancelDispatch(long expectedVersion, DateTimeOffset cancelledAtUtc)
    {
        RequireVersion(expectedVersion);
        RequireUtc(cancelledAtUtc, "İptal zamanı");
        if (Status != InventoryTransferStatus.InTransit
            || lines.Any(line => line.Status != InventoryTransferLineStatus.InTransit || line.Effect is null))
        {
            throw Invalid("Yalnız hiçbir satırı kabul edilmemiş yoldaki transferin çıkışı iptal edilebilir.");
        }

        if (DispatchedAtUtc is null || cancelledAtUtc < DispatchedAtUtc)
        {
            throw Invalid("İptal zamanı transfer çıkışından önce olamaz.");
        }

        var effects = lines.Select(line => line.Effect!).ToArray();
        foreach (var line in lines)
        {
            line.CancelDispatch();
        }

        Version = checked(Version + 1);
        Status = InventoryTransferStatus.DispatchCancelled;
        DispatchCancelledAtUtc = cancelledAtUtc;
        return new(Id, Version, cancelledAtUtc, effects);
    }

    private InventoryTransferLineEffect ValidateDispatch(Line line, InventoryTransferDispatchLine actual)
    {
        if (actual.LabelVersion != line.ExpectedLabelVersion || actual.LabelVersion < 1)
        {
            throw Invalid("Etiket sürümü planla tam eşleşmelidir.");
        }

        if (actual.Buckets is null || actual.Buckets.Count == 0)
        {
            throw Invalid("Etiketin çıkışta pozitif stok bakiyesi olmalıdır.");
        }

        var effects = new List<InventoryTransferBucketEffect>(actual.Buckets.Count);
        foreach (var bucket in actual.Buckets)
        {
            if (bucket is null || bucket.Position is null || bucket.Quantity.Value <= 0m
                || bucket.Position.CompanyId != CompanyId || bucket.Position.LabelId != line.LabelId
                || bucket.Position.LocationId != SourceLocationId || bucket.Position.Custodian != SourceCustodian)
            {
                throw Invalid("Etiketin bütün bakiyeleri planlanan kaynak şirket/konum/gözeten bağlamında olmalıdır.");
            }

            effects.Add(new(bucket.Position, bucket.Quantity));
        }

        if (effects.Select(effect => effect.Position.Block).Distinct().Count() != effects.Count
            || effects.Select(effect => new
            {
                effect.Position.CompanyId,
                effect.Position.Owner,
                effect.Position.Custodian,
                effect.Position.LocationId,
                effect.Position.ProductId,
                effect.Position.LabelId,
            }).Distinct().Count() != 1)
        {
            throw Invalid("Bir etiketin çıkış bakiyeleri tek fiziksel parçayı ve benzersiz blok kovalarını göstermelidir.");
        }

        var actualQuantity = effects.Aggregate(Kilograms.Zero, (sum, effect) => sum + effect.Quantity);
        return new(line.LineId, line.LabelId, line.PlannedQuantity, actualQuantity,
            actualQuantity.Value - line.PlannedQuantity.Value, effects.ToArray());
    }

    private void RequireVersion(long expectedVersion)
    {
        if (expectedVersion < 1 || expectedVersion != Version)
        {
            throw Invalid("Transfer sürümü güncel değil.");
        }
    }

    private static Line CreateLine(InventoryTransferPlanLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.LineId == Guid.Empty || line.LabelId == Guid.Empty || line.ExpectedLabelVersion < 1
            || line.PlannedQuantity.Value <= 0m)
        {
            throw Invalid("Transfer satırı kimlik, etiket sürümü ve pozitif plan miktarı ister.");
        }

        return new(line.LineId, line.LabelId, line.ExpectedLabelVersion, line.PlannedQuantity);
    }

    private static Guid RequireId(Guid id, string what) =>
        id == Guid.Empty ? throw Invalid($"{what} kimliği zorunludur.") : id;

    private static void RequireUtc(DateTimeOffset value, string what)
    {
        if (value.Offset != TimeSpan.Zero) throw Invalid($"{what} UTC olmalıdır.");
    }

    private static InventoryRuleException Invalid(string message) =>
        new(InventoryRuleCodes.InvalidTransfer, message);

    private sealed class Line(Guid lineId, Guid labelId, long expectedLabelVersion, Kilograms plannedQuantity)
    {
        public Guid LineId { get; } = lineId;
        public Guid LabelId { get; } = labelId;
        public long ExpectedLabelVersion { get; } = expectedLabelVersion;
        public Kilograms PlannedQuantity { get; } = plannedQuantity;
        public InventoryTransferLineStatus Status { get; private set; } = InventoryTransferLineStatus.Planned;
        public InventoryTransferLineEffect? Effect { get; private set; }

        public void Dispatch(InventoryTransferLineEffect effect)
        {
            Effect = effect;
            Status = InventoryTransferLineStatus.InTransit;
        }

        public DateTimeOffset? AcceptedAtUtc { get; private set; }
        public InventoryTransferDocumentReference? IncomingDocument { get; private set; }

        public void Accept(DateTimeOffset acceptedAtUtc, InventoryTransferDocumentReference incomingDocument)
        {
            AcceptedAtUtc = acceptedAtUtc;
            IncomingDocument = incomingDocument;
            Status = InventoryTransferLineStatus.Accepted;
        }

        public void CancelDispatch() => Status = InventoryTransferLineStatus.DispatchCancelled;

        public DateTimeOffset? WrittenOffAtUtc { get; private set; }

        public void WriteOff(DateTimeOffset writtenOffAtUtc)
        {
            WrittenOffAtUtc = writtenOffAtUtc;
            Status = InventoryTransferLineStatus.WrittenOff;
        }

        public InventoryTransferLineSnapshot Snapshot() => new(LineId, LabelId, ExpectedLabelVersion,
            PlannedQuantity, Effect?.ActualQuantity, Effect?.DifferenceKg, Status, AcceptedAtUtc, IncomingDocument);
    }
}

public sealed record InventoryTransferLineSnapshot(
    Guid LineId,
    Guid LabelId,
    long ExpectedLabelVersion,
    Kilograms PlannedQuantity,
    Kilograms? ActualQuantity,
    decimal? DifferenceKg,
    InventoryTransferLineStatus Status,
    DateTimeOffset? AcceptedAtUtc,
    InventoryTransferDocumentReference? IncomingDocument);

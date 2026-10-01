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
}

public enum InventoryTransferLineStatus
{
    Planned = 1,
    InTransit = 2,
    Accepted = 3,
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
/// DEC-STK-008 / INV-R20: a same-company two-leg transfer. Planning changes no stock. Dispatch derives the actual
/// shipped quantity from every stored bucket of every named label and moves those whole labels into transit. Acceptance
/// moves one whole dispatched label to the target context; accepting a subset of lines is the supported partial-accept
/// grain. Accepting only some kilograms of one physical label is deliberately absent: it needs the existing split
/// workflow first. Loss, damage and missing acceptance remain separate events.
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
        IEnumerable<InventoryTransferAcceptedLineState>? acceptedLines)
    {
        var transfer = new InventoryTransfer(id, companyId, sourceLocationId, sourceCustodian,
            destinationLocationId, destinationCustodian, plannedAtUtc, plannedLines);
        var accepted = acceptedLines?.OrderBy(line => line.AcceptedAtUtc).ThenBy(line => line.LineId).ToArray() ?? [];
        if (storedStatus == InventoryTransferStatus.Planned)
        {
            if (storedVersion != 1 || dispatchedAtUtc is not null || outgoingDocument is not null
                || dispatchedLines is not null || accepted.Length != 0)
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
        foreach (var acceptance in accepted)
        {
            transfer.AcceptLine(transfer.Version, acceptance.LineId, acceptance.AcceptedAtUtc,
                acceptance.IncomingDocument);
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
        Status = lines.All(candidate => candidate.Status == InventoryTransferLineStatus.Accepted)
            ? InventoryTransferStatus.Accepted
            : InventoryTransferStatus.PartiallyAccepted;
        var effect = line.Effect with { Buckets = targetBuckets };
        return new(Id, Version, incomingDocument, acceptedAtUtc, effect);
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

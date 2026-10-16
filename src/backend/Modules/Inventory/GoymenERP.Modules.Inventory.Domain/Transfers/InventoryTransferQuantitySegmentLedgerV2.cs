using GoymenERP.Modules.Inventory.Domain.Quantities;

namespace GoymenERP.Modules.Inventory.Domain.Transfers;

/// <summary>
/// The logistical disposition of a dispatched quantity. These values do not state physical location and do not
/// create, split or relocate a material label.
/// </summary>
public enum InventoryTransferQuantitySegmentStateV2
{
    InTransit = 1,
    Accepted = 2,
    Discrepancy = 3,
}

/// <summary>A separately recorded transfer difference; it is never treated as accepted quantity.</summary>
public enum InventoryTransferDiscrepancyKindV2
{
    Shortage = 1,
    Damage = 2,
}

/// <summary>
/// One real dispatched source share. A share refers to an existing physical label and its exact version; it is not a
/// second label. Multiple source shares may fund one transfer line, while a physical label may occur only once.
/// </summary>
public sealed record InventoryTransferSourceShareV2
{
    public InventoryTransferSourceShareV2(
        Guid sourceShareId,
        Guid labelId,
        long labelVersion,
        Kilograms dispatchedQuantity)
    {
        SourceShareId = RequireId(sourceShareId, "Kaynak payı");
        LabelId = RequireId(labelId, "Kaynak etiketi");
        LabelVersion = labelVersion > 0 ? labelVersion : throw Invalid("Kaynak etiket sürümü pozitif olmalıdır.");
        DispatchedQuantity = dispatchedQuantity.Value > 0m
            ? dispatchedQuantity
            : throw Invalid("Sevk edilen kaynak payı pozitif olmalıdır.");
    }

    public Guid SourceShareId { get; }
    public Guid LabelId { get; }
    public long LabelVersion { get; }
    public Kilograms DispatchedQuantity { get; }

    private static Guid RequireId(Guid id, string name) =>
        id != Guid.Empty ? id : throw Invalid($"{name} kimliği zorunludur.");

    private static InventoryRuleException Invalid(string message) =>
        new(InventoryRuleCodes.InvalidTransfer, message);
}

/// <summary>The exact part of one dispatched source share carried by a logical quantity segment.</summary>
public sealed record InventoryTransferSegmentSourceAllocationV2
{
    public InventoryTransferSegmentSourceAllocationV2(Guid sourceShareId, Kilograms quantity)
    {
        SourceShareId = sourceShareId != Guid.Empty
            ? sourceShareId
            : throw Invalid("Segment kaynak payı kimliği zorunludur.");
        Quantity = quantity.Value > 0m
            ? quantity
            : throw Invalid("Segment kaynak miktarı pozitif olmalıdır.");
    }

    public Guid SourceShareId { get; }
    public Kilograms Quantity { get; }

    private static InventoryRuleException Invalid(string message) =>
        new(InventoryRuleCodes.InvalidTransfer, message);
}

/// <summary>
/// A stable, logical partition of dispatched quantity. Segment identity is not a material-label identity. A parent
/// link records logical repartition lineage only; physical split and label genealogy remain separate Inventory facts.
/// </summary>
public sealed record InventoryTransferQuantitySegmentV2
{
    public InventoryTransferQuantitySegmentV2(
        Guid segmentId,
        InventoryTransferQuantitySegmentStateV2 state,
        Kilograms quantity,
        IEnumerable<InventoryTransferSegmentSourceAllocationV2> sourceAllocations,
        Guid? parentSegmentId = null,
        InventoryTransferDiscrepancyKindV2? discrepancyKind = null)
    {
        SegmentId = segmentId != Guid.Empty ? segmentId : throw Invalid("Segment kimliği zorunludur.");
        State = Enum.IsDefined(state) ? state : throw Invalid("Segment durumu tanımsız.");
        Quantity = quantity.Value > 0m ? quantity : throw Invalid("Segment miktarı pozitif olmalıdır.");
        if (parentSegmentId == Guid.Empty || parentSegmentId == segmentId)
        {
            throw Invalid("Segment üst kimliği boş veya kendi kimliği olamaz.");
        }

        ParentSegmentId = parentSegmentId;
        DiscrepancyKind = state == InventoryTransferQuantitySegmentStateV2.Discrepancy
            ? discrepancyKind is not null && Enum.IsDefined(discrepancyKind.Value)
                ? discrepancyKind
                : throw Invalid("Fark segmenti eksik veya tanımsız fark türü içeremez.")
            : discrepancyKind is null
                ? null
                : throw Invalid("Yoldaki veya kabul edilen segment fark türü içeremez.");

        ArgumentNullException.ThrowIfNull(sourceAllocations);
        var allocations = sourceAllocations.ToArray();
        if (allocations.Length == 0 || allocations.Any(static allocation => allocation is null)
            || allocations.Select(allocation => allocation.SourceShareId).Distinct().Count() != allocations.Length)
        {
            throw Invalid("Segment benzersiz ve en az bir kaynak payı dağılımı içermelidir.");
        }

        var allocated = allocations.Sum(allocation => allocation.Quantity.Value);
        if (allocated != quantity.Value)
        {
            throw Invalid("Segment kaynak payları segment miktarına tam eşit olmalıdır.");
        }

        SourceAllocations = Array.AsReadOnly(allocations);
    }

    public Guid SegmentId { get; }
    public InventoryTransferQuantitySegmentStateV2 State { get; }
    public Kilograms Quantity { get; }
    public IReadOnlyList<InventoryTransferSegmentSourceAllocationV2> SourceAllocations { get; }
    public Guid? ParentSegmentId { get; }
    public InventoryTransferDiscrepancyKindV2? DiscrepancyKind { get; }

    private static InventoryRuleException Invalid(string message) =>
        new(InventoryRuleCodes.InvalidTransfer, message);
}

/// <summary>An exactly conserved partition of the source shares dispatched for one transfer line.</summary>
public sealed class InventoryTransferQuantityPartitionV2
{
    public InventoryTransferQuantityPartitionV2(
        IEnumerable<InventoryTransferSourceShareV2> sourceShares,
        IEnumerable<InventoryTransferQuantitySegmentV2> segments)
    {
        ArgumentNullException.ThrowIfNull(sourceShares);
        ArgumentNullException.ThrowIfNull(segments);
        var sources = sourceShares.ToArray();
        var partition = segments.ToArray();
        if (sources.Length == 0 || sources.Any(static source => source is null)
            || sources.Select(source => source.SourceShareId).Distinct().Count() != sources.Length
            || sources.Select(source => source.LabelId).Distinct().Count() != sources.Length)
        {
            throw Invalid("Transfer satırı benzersiz ve en az bir gerçek kaynak payı içermelidir.");
        }

        if (partition.Length == 0 || partition.Any(static segment => segment is null)
            || partition.Select(segment => segment.SegmentId).Distinct().Count() != partition.Length)
        {
            throw Invalid("Transfer satırı benzersiz ve en az bir miktar segmenti içermelidir.");
        }

        var sourceById = sources.ToDictionary(source => source.SourceShareId);
        foreach (var segment in partition)
        {
            if (segment.SourceAllocations.Any(allocation => !sourceById.ContainsKey(allocation.SourceShareId)))
            {
                throw Invalid("Segment yalnız transfer satırındaki gerçek kaynak paylarını kullanabilir.");
            }
        }

        foreach (var source in sources)
        {
            var allocated = partition
                .SelectMany(segment => segment.SourceAllocations)
                .Where(allocation => allocation.SourceShareId == source.SourceShareId)
                .Sum(allocation => allocation.Quantity.Value);
            if (allocated != source.DispatchedQuantity.Value)
            {
                throw Invalid("Her kaynak payının sevk miktarı segmentlere tam ve yalnız bir kez dağıtılmalıdır.");
            }
        }

        SourceShares = Array.AsReadOnly(sources);
        Segments = Array.AsReadOnly(partition);
        DispatchedQuantity = Kilograms.Of(sources.Sum(source => source.DispatchedQuantity.Value));
        InTransitQuantity = Sum(InventoryTransferQuantitySegmentStateV2.InTransit);
        AcceptedQuantity = Sum(InventoryTransferQuantitySegmentStateV2.Accepted);
        DiscrepancyQuantity = Sum(InventoryTransferQuantitySegmentStateV2.Discrepancy);
        if (InTransitQuantity.Value + AcceptedQuantity.Value + DiscrepancyQuantity.Value
            != DispatchedQuantity.Value)
        {
            throw Invalid("Yoldaki, kabul edilen ve fark miktarlarının toplamı sevk miktarına eşit olmalıdır.");
        }
    }

    public IReadOnlyList<InventoryTransferSourceShareV2> SourceShares { get; }
    public IReadOnlyList<InventoryTransferQuantitySegmentV2> Segments { get; }
    public Kilograms DispatchedQuantity { get; }
    public Kilograms InTransitQuantity { get; }
    public Kilograms AcceptedQuantity { get; }
    public Kilograms DiscrepancyQuantity { get; }

    private Kilograms Sum(InventoryTransferQuantitySegmentStateV2 state) =>
        Kilograms.Of(Segments.Where(segment => segment.State == state).Sum(segment => segment.Quantity.Value));

    private static InventoryRuleException Invalid(string message) =>
        new(InventoryRuleCodes.InvalidTransfer, message);
}

/// <summary>
/// One proposed append-only revision. The expected version is a concurrency token. A reversal names the prior
/// revision it corrects and supplies a complete replacement partition; it never deletes prior evidence.
/// </summary>
public sealed record InventoryTransferQuantityRevisionV2
{
    public InventoryTransferQuantityRevisionV2(
        Guid revisionId,
        long expectedVersion,
        InventoryTransferQuantityPartitionV2 partition,
        Guid? reversesRevisionId = null,
        string? reason = null)
    {
        RevisionId = revisionId != Guid.Empty ? revisionId : throw Invalid("Revizyon kimliği zorunludur.");
        ExpectedVersion = expectedVersion > 0 ? expectedVersion : throw Invalid("Beklenen sürüm pozitif olmalıdır.");
        Partition = partition ?? throw new ArgumentNullException(nameof(partition));
        if (reversesRevisionId == Guid.Empty || reversesRevisionId == revisionId)
        {
            throw Invalid("Terslenen revizyon kimliği boş veya yeni revizyonun kendisi olamaz.");
        }

        ReversesRevisionId = reversesRevisionId;
        var normalizedReason = reason?.Trim();
        if (reversesRevisionId is not null && string.IsNullOrWhiteSpace(normalizedReason))
        {
            throw Invalid("Ters/düzeltme revizyonu gerekçe içermelidir.");
        }

        Reason = string.IsNullOrWhiteSpace(normalizedReason) ? null : normalizedReason;
    }

    public Guid RevisionId { get; }
    public long ExpectedVersion { get; }
    public InventoryTransferQuantityPartitionV2 Partition { get; }
    public Guid? ReversesRevisionId { get; }
    public string? Reason { get; }

    private static InventoryRuleException Invalid(string message) =>
        new(InventoryRuleCodes.InvalidTransfer, message);
}

public sealed record InventoryTransferQuantityRevisionSnapshotV2(
    Guid RevisionId,
    long Version,
    Guid? ReversesRevisionId,
    string? Reason,
    InventoryTransferQuantityPartitionV2 Partition);

/// <summary>
/// Pure line-level contract for D-TRANSFER-GRAIN=B. Runtime persistence and stock movements are intentionally absent.
/// The accepted whole-label V1 transfer remains valid; this ledger defines the later segmented-quantity boundary.
/// </summary>
public sealed class InventoryTransferQuantitySegmentLedgerV2
{
    private readonly List<InventoryTransferQuantityRevisionSnapshotV2> revisions;

    private InventoryTransferQuantitySegmentLedgerV2(
        Guid transferId,
        Guid lineId,
        Guid initialRevisionId,
        InventoryTransferQuantityPartitionV2 initialPartition)
    {
        TransferId = RequireId(transferId, "Transfer");
        LineId = RequireId(lineId, "Transfer satırı");
        if (initialRevisionId == Guid.Empty)
        {
            throw Invalid("İlk revizyon kimliği zorunludur.");
        }

        if (initialPartition.Segments.Any(segment =>
                segment.State != InventoryTransferQuantitySegmentStateV2.InTransit
                || segment.ParentSegmentId is not null))
        {
            throw Invalid("İlk sevk bölümü yalnız üst bağı olmayan yoldaki segmentleri içerebilir.");
        }

        revisions = [new(initialRevisionId, 1, null, null, initialPartition)];
    }

    public Guid TransferId { get; }
    public Guid LineId { get; }
    public long Version => revisions[^1].Version;
    public InventoryTransferQuantityPartitionV2 Current => revisions[^1].Partition;
    public IReadOnlyList<InventoryTransferQuantityRevisionSnapshotV2> Revisions => revisions.AsReadOnly();

    public static InventoryTransferQuantitySegmentLedgerV2 Create(
        Guid transferId,
        Guid lineId,
        Guid initialRevisionId,
        InventoryTransferQuantityPartitionV2 initialPartition) =>
        new(transferId, lineId, initialRevisionId,
            initialPartition ?? throw new ArgumentNullException(nameof(initialPartition)));

    public InventoryTransferQuantityRevisionSnapshotV2 Apply(InventoryTransferQuantityRevisionV2 revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        if (revision.ExpectedVersion != Version)
        {
            throw Invalid("Transfer miktar segment sürümü güncel değil.");
        }

        if (revisions.Any(existing => existing.RevisionId == revision.RevisionId))
        {
            throw Invalid("Revizyon kimliği daha önce kullanıldı.");
        }

        RequireSameSourceShares(revision.Partition);
        RequireStableSegmentIdentities(revision.Partition);
        if (revision.ReversesRevisionId is null)
        {
            RequireForwardRepartition(revision.Partition);
        }
        else
        {
            RequireLinkedReversal(revision);
        }

        var nextVersion = checked(Version + 1);
        var snapshot = new InventoryTransferQuantityRevisionSnapshotV2(
            revision.RevisionId,
            nextVersion,
            revision.ReversesRevisionId,
            revision.Reason,
            revision.Partition);
        revisions.Add(snapshot);
        return snapshot;
    }

    private void RequireSameSourceShares(InventoryTransferQuantityPartitionV2 proposed)
    {
        var currentSources = Current.SourceShares.OrderBy(source => source.SourceShareId).ToArray();
        var proposedSources = proposed.SourceShares.OrderBy(source => source.SourceShareId).ToArray();
        if (!currentSources.SequenceEqual(proposedSources))
        {
            throw Invalid("Sevk edilmiş gerçek kaynak payları revizyonda değiştirilemez.");
        }
    }

    private void RequireStableSegmentIdentities(InventoryTransferQuantityPartitionV2 proposed)
    {
        var historical = revisions
            .SelectMany(revision => revision.Partition.Segments)
            .GroupBy(segment => segment.SegmentId)
            .ToDictionary(group => group.Key, group => group.First());
        foreach (var segment in proposed.Segments)
        {
            if (historical.TryGetValue(segment.SegmentId, out var existing) && !SameSegment(existing, segment))
            {
                throw Invalid("Kullanılmış segment kimliği farklı miktar veya anlamla yeniden yazılamaz.");
            }
        }
    }

    private void RequireForwardRepartition(InventoryTransferQuantityPartitionV2 proposed)
    {
        var currentById = Current.Segments.ToDictionary(segment => segment.SegmentId);
        var proposedById = proposed.Segments.ToDictionary(segment => segment.SegmentId);

        foreach (var terminal in Current.Segments.Where(segment =>
                     segment.State is InventoryTransferQuantitySegmentStateV2.Accepted
                         or InventoryTransferQuantitySegmentStateV2.Discrepancy))
        {
            if (!proposedById.TryGetValue(terminal.SegmentId, out var preserved) || !SameSegment(terminal, preserved))
            {
                throw Invalid("Kabul veya fark segmenti yalnız bağlı ters/düzeltme revizyonuyla değiştirilebilir.");
            }
        }

        var removedPending = Current.Segments
            .Where(segment => segment.State == InventoryTransferQuantitySegmentStateV2.InTransit
                              && !proposedById.ContainsKey(segment.SegmentId))
            .ToArray();
        var removedIds = removedPending.Select(segment => segment.SegmentId).ToHashSet();
        var historicalIds = revisions.SelectMany(item => item.Partition.Segments)
            .Select(segment => segment.SegmentId).ToHashSet();
        var newSegments = proposed.Segments.Where(segment => !historicalIds.Contains(segment.SegmentId)).ToArray();
        if (newSegments.Any(segment => segment.ParentSegmentId is null
                                       || !removedIds.Contains(segment.ParentSegmentId.Value)))
        {
            throw Invalid("Yeni segment yalnız bu revizyonda kaldırılan yoldaki segmentin bağlı çocuğu olabilir.");
        }

        foreach (var removed in removedPending)
        {
            var children = newSegments.Where(segment => segment.ParentSegmentId == removed.SegmentId).ToArray();
            if (children.Length == 0 || !SameAllocations(removed.SourceAllocations,
                    children.SelectMany(segment => segment.SourceAllocations)))
            {
                throw Invalid("Yoldaki segmentin çocukları her gerçek kaynak payını tam korumalıdır.");
            }
        }

        foreach (var retained in Current.Segments.Where(segment => proposedById.ContainsKey(segment.SegmentId)))
        {
            if (!SameSegment(retained, proposedById[retained.SegmentId]))
            {
                throw Invalid("Mevcut segment kimliği aynı gerçeklerle korunmalıdır.");
            }
        }

        if (currentById.Count == proposedById.Count && removedPending.Length == 0 && newSegments.Length == 0)
        {
            throw Invalid("Değişiklik içermeyen miktar segment revizyonu kaydedilemez.");
        }
    }

    private void RequireLinkedReversal(InventoryTransferQuantityRevisionV2 revision)
    {
        if (!revisions.Any(existing => existing.RevisionId == revision.ReversesRevisionId))
        {
            throw Invalid("Ters/düzeltme yalnız kayıtlı bir revizyona bağlanabilir.");
        }

        var historicalSegments = revisions.SelectMany(item => item.Partition.Segments).ToArray();
        var historicalIds = historicalSegments.Select(segment => segment.SegmentId).ToHashSet();
        foreach (var segment in revision.Partition.Segments.Where(segment => !historicalIds.Contains(segment.SegmentId)))
        {
            if (segment.ParentSegmentId is null
                || historicalSegments.All(parent => parent.SegmentId != segment.ParentSegmentId.Value))
            {
                throw Invalid("Yeni düzeltme segmenti kayıtlı bir segment köküne bağlanmalıdır.");
            }
        }
    }

    private static bool SameSegment(
        InventoryTransferQuantitySegmentV2 left,
        InventoryTransferQuantitySegmentV2 right) =>
        left.SegmentId == right.SegmentId
        && left.State == right.State
        && left.Quantity == right.Quantity
        && left.ParentSegmentId == right.ParentSegmentId
        && left.DiscrepancyKind == right.DiscrepancyKind
        && SameAllocations(left.SourceAllocations, right.SourceAllocations);

    private static bool SameAllocations(
        IEnumerable<InventoryTransferSegmentSourceAllocationV2> left,
        IEnumerable<InventoryTransferSegmentSourceAllocationV2> right) =>
        left.GroupBy(allocation => allocation.SourceShareId)
            .ToDictionary(group => group.Key, group => group.Sum(allocation => allocation.Quantity.Value))
            .OrderBy(pair => pair.Key)
            .SequenceEqual(right.GroupBy(allocation => allocation.SourceShareId)
                .ToDictionary(group => group.Key, group => group.Sum(allocation => allocation.Quantity.Value))
                .OrderBy(pair => pair.Key));

    private static Guid RequireId(Guid id, string name) =>
        id != Guid.Empty ? id : throw Invalid($"{name} kimliği zorunludur.");

    private static InventoryRuleException Invalid(string message) =>
        new(InventoryRuleCodes.InvalidTransfer, message);
}

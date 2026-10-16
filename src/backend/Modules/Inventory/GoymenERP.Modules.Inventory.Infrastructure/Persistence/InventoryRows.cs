namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>A row of the server-resolved company (ADR-002): its company is set once and never changes.</summary>
internal interface IInventoryTenantRow
{
    Guid CompanyId { get; }
}

/// <summary>
/// A stored <see cref="Domain.Lots.MaterialLot"/> (DEC-PRD-003). Append-only in this slice: nothing
/// updates it, so its version stays 1; the version and row version are there for a later, decided
/// completion of its facts (Ö3). Every label of the company references it by (company, lot).
/// </summary>
internal sealed class LotRow : IInventoryTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ProductId { get; set; }
    public string? HeatNumber { get; set; }
    public string? CoilNumber { get; set; }
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}

/// <summary>
/// A stored <see cref="Domain.Labels.MaterialLabel"/>: its identity facts, and a version bumped by
/// every write that touches the label (its stock, prints or attachments). Two writes that read the
/// same version cannot both commit: that is the persistent race guard of one physical piece.
/// </summary>
internal sealed class LabelRow : IInventoryTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public Guid ProductId { get; set; }
    public string? ProductVersion { get; set; }
    public Guid LotId { get; set; }
    public string? HeatNumber { get; set; }
    public string? CoilNumber { get; set; }
    public int? Classification { get; set; }
    public string RequiredDocumentsJson { get; set; } = "[]";
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>One print of a label, in its sequence. Append-only.</summary>
internal sealed class LabelPrintRow : IInventoryTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid LabelId { get; set; }
    public int Sequence { get; set; }
    public DateTimeOffset PrintedAtUtc { get; set; }
    public string PrintedBy { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

/// <summary>One document attached to a label, in its sequence. Append-only.</summary>
internal sealed class LabelDocumentRow : IInventoryTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid LabelId { get; set; }
    public int Sequence { get; set; }
    public string Kind { get; set; } = string.Empty;
    public DateTimeOffset AttachedAtUtc { get; set; }
    public string AttachedBy { get; set; } = string.Empty;
}

/// <summary>One immutable same-company split or merge event (DEC-LBL-002/003).</summary>
internal sealed class LabelTransformationRow : IInventoryTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public int Kind { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>A source-label edge captured before its whole balance is consumed.</summary>
internal sealed class LabelTransformationInputRow : IInventoryTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid TransformationId { get; set; }
    public int Sequence { get; set; }
    public Guid LabelId { get; set; }
    public long LabelVersion { get; set; }
    public decimal QuantityKg { get; set; }
}

/// <summary>An output-label edge captured when its new physical identity is created.</summary>
internal sealed class LabelTransformationOutputRow : IInventoryTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid TransformationId { get; set; }
    public int Sequence { get; set; }
    public Guid LabelId { get; set; }
    public long LabelVersion { get; set; }
    public decimal QuantityKg { get; set; }
}

/// <summary>
/// A stored <see cref="Domain.Movements.StockMovement"/>. Append-only: a wrong movement is corrected
/// by a new linked movement (T13), never by editing this one. <see cref="PayloadHash"/> is the
/// canonical content, so the same id offered again is either the same movement (a replay) or a
/// conflict.
/// </summary>
internal sealed class MovementRow : IInventoryTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public int Kind { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public Guid SourceId { get; set; }
    public Guid? SourceLineId { get; set; }
    public int SourceGrain { get; set; }
    public int CostStatus { get; set; }
    public decimal? CostAmount { get; set; }
    public Guid? CostCurrencyId { get; set; }
    public string PayloadHash { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// The grain one source is answered at for one movement kind: whole or line by line (#293 ORG
/// P3-1). Written with the source's first movement, never changed; every movement references it by
/// (company, kind, source kind, source, grain), so SQL itself refuses a whole movement of a source
/// answered by lines and the other way round. Append-only.
/// </summary>
internal sealed class SourceGrainRow : IInventoryTenantRow
{
    public Guid CompanyId { get; set; }
    public int Kind { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public Guid SourceId { get; set; }
    public int Grain { get; set; }
}

/// <summary>One line of a stored movement: a position and a kilogram amount. Append-only.</summary>
internal sealed class MovementLineRow : IInventoryTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid MovementId { get; set; }
    public int LineNumber { get; set; }
    public int Direction { get; set; }
    public int OwnerKind { get; set; }
    public Guid OwnerId { get; set; }
    public int CustodianKind { get; set; }
    public Guid CustodianId { get; set; }
    public Guid LocationId { get; set; }
    public Guid LabelId { get; set; }
    public Guid ProductId { get; set; }
    public int Block { get; set; }
    public decimal QuantityKg { get; set; }
}

/// <summary>
/// The current quantity of one physical position (§5's four facts + label, product and block).
/// Only its quantity ever changes, and only together with the movement that changes it.
/// </summary>
internal sealed class BalanceRow : IInventoryTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public int OwnerKind { get; set; }
    public Guid OwnerId { get; set; }
    public int CustodianKind { get; set; }
    public Guid CustodianId { get; set; }
    public Guid LocationId { get; set; }
    public Guid LabelId { get; set; }
    public Guid ProductId { get; set; }
    public int Block { get; set; }
    public decimal QuantityKg { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// One physical-stock allocation. Its immutable position identifies the exact free balance the
/// allocation protects. Only remaining kg, status and version change when a definite shipment
/// consumes it; allocation creation/release policies are outside this predecessor.
/// </summary>
internal sealed class AllocationRow : IInventoryTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public int OwnerKind { get; set; }
    public Guid OwnerId { get; set; }
    public int CustodianKind { get; set; }
    public Guid CustodianId { get; set; }
    public Guid LocationId { get; set; }
    public Guid LabelId { get; set; }
    public int Block { get; set; }
    public string? DemandKind { get; set; }
    public Guid? DemandId { get; set; }
    public Guid? DemandLineId { get; set; }
    public Guid? DemandProductId { get; set; }
    public string? DemandProductVersion { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset? ReservedUntilUtc { get; set; }
    public decimal AllocatedKg { get; set; }
    public decimal RemainingKg { get; set; }
    public int Status { get; set; }
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? ReleasedAtUtc { get; set; }
    public string? ReleaseReason { get; set; }
}

/// <summary>
/// INV-AL-1 change: one append-only history fact of an allocation change. A resize names the same allocation as
/// source and target (its version moved from <see cref="SourceVersion"/> to <see cref="SourceVersion"/> + 1); a move
/// names the released source and the new target allocation (version 1). Kilograms are the remaining kilograms before
/// (source) and after (target). One change per source version: (company, source allocation, source version) is unique.
/// </summary>
internal sealed class AllocationChangeRow : IInventoryTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public int Kind { get; set; }
    public Guid SourceAllocationId { get; set; }
    public long SourceVersion { get; set; }
    public Guid SourceLabelId { get; set; }
    public decimal SourceRemainingKg { get; set; }
    public Guid TargetAllocationId { get; set; }
    public Guid TargetLabelId { get; set; }
    public decimal TargetRemainingKg { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string ChangedBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset ChangedAtUtc { get; set; }
}

/// <summary>
/// Inventory-owned physical hold on one complete real label. The row keeps the exact physical place
/// and kilograms Inventory blocked; a Quality decision may close only a Quality-owned row. Other
/// typed holds remain separate rows, so closing one never erases another owner's restriction.
/// </summary>
internal sealed class InventoryPhysicalHoldRow : IInventoryTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid LabelId { get; set; }
    public long LabelVersionAtPlacement { get; set; }
    public int OwnerKind { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public Guid SourceId { get; set; }
    public int PositionOwnerKind { get; set; }
    public Guid PositionOwnerId { get; set; }
    public int CustodianKind { get; set; }
    public Guid CustodianId { get; set; }
    public Guid LocationId { get; set; }
    public Guid ProductId { get; set; }
    public decimal QuantityKg { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public int Status { get; set; }
    public long Version { get; set; }
    public string PlacementPayloadHash { get; set; } = string.Empty;
    public Guid? BlockMovementId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string CreatedCorrelationId { get; set; } = string.Empty;
    public Guid? ReleaseDispositionId { get; set; }
    public string? ReleasePayloadHash { get; set; }
    public Guid? ReleaseMovementId { get; set; }
    public DateTimeOffset? ReleasedAtUtc { get; set; }
    public string? ReleasedBy { get; set; }
    public string? ReleaseReason { get; set; }
    public string? ReleaseCorrelationId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// One unpublished Inventory-owned event stored in the producer transaction. This slice has no
/// dispatcher and never mutates publication state; the row is append-only.
/// </summary>
internal sealed class InventoryOutboxRow : IInventoryTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public string SourceOperation { get; set; } = string.Empty;
    public string AggregateType { get; set; } = string.Empty;
    public Guid AggregateId { get; set; }
    public long AggregateVersion { get; set; }
    public long Sequence { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string? CausationId { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}

/// <summary>Organization's company, referenced by identity only (the table is Organization's, not migrated here).</summary>
internal sealed class OrganizationCompanyReference
{
    public Guid Id { get; set; }
}

/// <summary>
/// Organization's location, referenced by (company, id) so a row can never point at another
/// company's location (ADR-002 composite key; the table is Organization's, not migrated here).
/// </summary>
internal sealed class OrganizationLocationReference
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
}

/// <summary>
/// A stored opening count (INV-OPEN-1): the count document every opening receipt comes from. Company,
/// location, counted-on date and the physical sheet's reference; no personal name (the recorder is in the
/// audit). <see cref="ContentHash"/> is the canonical content, so the same id again is a replay or a
/// conflict. Append-only.
/// </summary>
internal sealed class OpeningCountRow : IInventoryTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid LocationId { get; set; }
    public DateOnly CountedOn { get; set; }
    public string SheetReference { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// One counted piece of an opening count, in its order. The line id is also the id of its one receipt
/// movement, which the schema requires to exist (a line is never without its posted receipt). Append-only.
/// </summary>
internal sealed class OpeningCountLineRow : IInventoryTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid CountId { get; set; }
    public int LineNumber { get; set; }
    public Guid LineId { get; set; }
    public Guid LabelId { get; set; }
    public Guid LotId { get; set; }
    public decimal QuantityKg { get; set; }
}

/// <summary>
/// One ADR-011 command of the company (INV-OPEN-1 part 2): the key is (company, command, SHA-256 of the
/// trimmed Idempotency-Key, uppercase hex); the actor and the canonical payload hash decide replay versus
/// conflict; <see cref="ResultJson"/> is the answer a replay repeats — a success or an authorized terminal
/// refusal (§7) — with the first <see cref="CorrelationId"/>. Append-only.
/// </summary>
internal sealed class InventoryCommandRow : IInventoryTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public Guid? TargetId { get; set; }
    public string ResultJson { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

/// <summary>
/// ST-05 / AT-037: one physical count of one stocked label (DEC-STK-007). The counted position, the label version
/// and book kg the counter saw, the counted kg and the mandatory reason are kept exactly as recorded; a count
/// changes no stock. <see cref="PayloadHash"/> is the canonical request, so the same id again is a replay or a
/// conflict. The recorder is kept for the fail-closed maker≠checker rule and the audit. Append-only.
/// </summary>
internal sealed class StockCountRow : IInventoryTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid LabelId { get; set; }
    public long LabelVersion { get; set; }
    public int OwnerKind { get; set; }
    public Guid OwnerId { get; set; }
    public int CustodianKind { get; set; }
    public Guid CustodianId { get; set; }
    public Guid LocationId { get; set; }
    public Guid ProductId { get; set; }
    public int Block { get; set; }
    public decimal BookKg { get; set; }
    public decimal CountedKg { get; set; }
    public DateTimeOffset CountedAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// The one decision on a count difference: approved with the adjustment movement it posted (the schema requires
/// that movement), or rejected with none. The key (company, count) allows exactly one decision. Append-only.
/// </summary>
internal sealed class StockCountDecisionRow : IInventoryTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid CountId { get; set; }
    public int Decision { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? MovementId { get; set; }
    public string PayloadHash { get; set; } = string.Empty;
    public DateTimeOffset DecidedAtUtc { get; set; }
    public string DecidedBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// ST-02 scrap write-off: part or all of one stocked label recorded as scrap. The scrapped position, the label
/// version and book kg the recorder saw, the scrap kg and the mandatory reason are kept exactly as recorded; a
/// recorded scrap changes no stock. <see cref="PayloadHash"/> is the canonical request, so the same id again is a
/// replay or a conflict. The recorder is kept for the fail-closed maker≠checker rule and the audit. Append-only.
/// </summary>
internal sealed class StockScrapRow : IInventoryTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid LabelId { get; set; }
    public long LabelVersion { get; set; }
    public int OwnerKind { get; set; }
    public Guid OwnerId { get; set; }
    public int CustodianKind { get; set; }
    public Guid CustodianId { get; set; }
    public Guid LocationId { get; set; }
    public Guid ProductId { get; set; }
    public int Block { get; set; }
    public decimal BookKg { get; set; }
    public decimal ScrapKg { get; set; }
    public DateTimeOffset ScrappedAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// The one decision on a scrap: approved with the scrap movement it posted (the schema requires that movement), or
/// rejected with none. The key (company, scrap) allows exactly one decision. Append-only.
/// </summary>
internal sealed class StockScrapDecisionRow : IInventoryTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid ScrapId { get; set; }
    public int Decision { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? MovementId { get; set; }
    public string PayloadHash { get; set; } = string.Empty;
    public DateTimeOffset DecidedAtUtc { get; set; }
    public string DecidedBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>INV-R20 same-company transfer header. Only lifecycle status/version/update time may advance.</summary>
internal sealed class InventoryTransferRow : IInventoryTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public int Status { get; set; }
    public Guid SourceLocationId { get; set; }
    public int SourceCustodianKind { get; set; }
    public Guid SourceCustodianId { get; set; }
    public Guid DestinationLocationId { get; set; }
    public int DestinationCustodianKind { get; set; }
    public Guid DestinationCustodianId { get; set; }
    public DateTimeOffset PlannedAtUtc { get; set; }
    public string PayloadHash { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>One immutable planned whole-label line.</summary>
internal sealed class InventoryTransferLineRow : IInventoryTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid TransferId { get; set; }
    public Guid LineId { get; set; }
    public int Sequence { get; set; }
    public Guid LabelId { get; set; }
    public long ExpectedLabelVersion { get; set; }
    public decimal PlannedKg { get; set; }
}

/// <summary>The one immutable dispatch of all planned lines, linked to its one physical movement.</summary>
internal sealed class InventoryTransferDispatchRow : IInventoryTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid TransferId { get; set; }
    public Guid MovementId { get; set; }
    public string DocumentKind { get; set; } = string.Empty;
    public Guid DocumentId { get; set; }
    public Guid? DocumentLineId { get; set; }
    public DateTimeOffset DispatchedAtUtc { get; set; }
    public string PayloadHash { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>Actual whole-label kilograms at dispatch and their signed planned difference.</summary>
internal sealed class InventoryTransferDispatchLineRow : IInventoryTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid TransferId { get; set; }
    public Guid LineId { get; set; }
    public long LabelVersionAfterDispatch { get; set; }
    public decimal ActualKg { get; set; }
    public decimal DifferenceKg { get; set; }
}

/// <summary>One lossless Free/Blocked bucket captured by dispatch and reused by acceptance.</summary>
internal sealed class InventoryTransferBucketRow : IInventoryTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid TransferId { get; set; }
    public Guid LineId { get; set; }
    public int Sequence { get; set; }
    public int OwnerKind { get; set; }
    public Guid OwnerId { get; set; }
    public int CustodianKind { get; set; }
    public Guid CustodianId { get; set; }
    public Guid LocationId { get; set; }
    public Guid LabelId { get; set; }
    public Guid ProductId { get; set; }
    public int Block { get; set; }
    public decimal QuantityKg { get; set; }
}

/// <summary>One immutable whole-line target acceptance and its physical movement.</summary>
internal sealed class InventoryTransferAcceptanceRow : IInventoryTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid TransferId { get; set; }
    public Guid LineId { get; set; }
    public Guid MovementId { get; set; }
    public string DocumentKind { get; set; } = string.Empty;
    public Guid DocumentId { get; set; }
    public Guid? DocumentLineId { get; set; }
    public DateTimeOffset AcceptedAtUtc { get; set; }
    public string PayloadHash { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}

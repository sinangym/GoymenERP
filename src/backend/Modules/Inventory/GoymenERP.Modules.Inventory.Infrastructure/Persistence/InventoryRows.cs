namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>A row of the server-resolved company (ADR-002): its company is set once and never changes.</summary>
internal interface IInventoryTenantRow
{
    Guid CompanyId { get; }
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
    public Guid LotId { get; set; }
    public string? HeatNumber { get; set; }
    public string? CoilNumber { get; set; }
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

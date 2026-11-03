namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

internal interface IPurchasingTenantRow
{
    Guid CompanyId { get; }
}

internal sealed class OrganizationCompanyReference
{
    public Guid Id { get; set; }
}

/// <summary>
/// The request header (talep, §7.2); only Status, Version, UpdatedAtUtc, SourcingVersion and the A1-1 decision facts
/// (SubmittedBy, Approved*, Rejected*, RejectionReason) and the A1-1b withdrawal facts (Withdrawn*, WithdrawalReason) move
/// after the first commit. SourcingVersion moves with every
/// order approval that binds to this request, so two approvals against the same request — or an approval and a
/// rejection (No.161 §1) — collide on the row version and the loser measures again. CreatedBy is the preparer and
/// SubmittedBy the submitter: the makers no decision may come from (ALS-D2); they are also the only actors a
/// withdrawal may come from (ALS-D46, ALS-D47).
/// </summary>
internal sealed class PurchaseRequestRow : IPurchasingTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public int Status { get; set; }
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public long SourcingVersion { get; set; }
    public DateOnly EffectiveOn { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string? SubmittedBy { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public string? RejectedBy { get; set; }
    public DateTimeOffset? RejectedAtUtc { get; set; }
    public string? RejectionReason { get; set; }
    public string? WithdrawnBy { get; set; }
    public DateTimeOffset? WithdrawnAtUtc { get; set; }
    public string? WithdrawalReason { get; set; }
}

/// <summary>
/// One request line: the product (with the Product owner's answered version), the Settings unit and the exact quantity.
/// A1-2 (plan API-02): the optional source — SourceKind (1 Stock, 2 Sales, 3 Production) with the source record's opaque
/// identity (SourceDocumentId, SourceDocumentLineId); all three null for a manual line. Written once with the line.
/// </summary>
internal sealed class PurchaseRequestLineRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid RequestId { get; set; }
    public int LineNo { get; set; }
    public Guid LineId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductVersion { get; set; } = string.Empty;
    public Guid UnitId { get; set; }
    public bool UnitIsKilogram { get; set; }
    public decimal Quantity { get; set; }
    public int? SourceKind { get; set; }
    public Guid? SourceDocumentId { get; set; }
    public Guid? SourceDocumentLineId { get; set; }
}

/// <summary>The order header (the commitment towards one supplier); only Status, Version and UpdatedAtUtc move after the first commit.</summary>
internal sealed class PurchaseOrderRow : IPurchasingTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid SupplierPartyId { get; set; }
    public Guid CompanyAccountId { get; set; }
    public Guid? SourceRequestId { get; set; }
    public int Status { get; set; }
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateOnly EffectiveOn { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// One order line with its shares as stored totals (§7.2, §13): Ordered is the first commitment, Technical the
/// part moved into technical lines, Accepted the accepted share; committed = Ordered − Technical and remaining =
/// committed − Accepted are derived. The request binding (SourceRequestLineId) is the share this supplier takes.
/// </summary>
internal sealed class PurchaseOrderLineRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid OrderId { get; set; }
    public int LineNo { get; set; }
    public Guid LineId { get; set; }
    public int Kind { get; set; }
    public Guid? ParentLineId { get; set; }
    public Guid? SourceRequestLineId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductVersion { get; set; } = string.Empty;
    public Guid UnitId { get; set; }
    public bool UnitIsKilogram { get; set; }
    public decimal Ordered { get; set; }
    public decimal Technical { get; set; }
    public decimal Accepted { get; set; }
}

/// <summary>ADR-011 attempt record (the Sales shape): the key hash, the content hash, the actor, the outcome and the result (a request, an order or a refusal).</summary>
internal sealed class PurchaseAttemptRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public int Outcome { get; set; }
    public Guid? RequestId { get; set; }
    public Guid? OrderId { get; set; }
    public string? RefusalJson { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

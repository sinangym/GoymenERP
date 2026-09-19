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
/// The request header (talep, §7.2); only Status, Version, UpdatedAtUtc and SourcingVersion move after the
/// first commit. SourcingVersion moves with every order approval that binds to this request, so two approvals
/// against the same request collide on the row version and the loser measures the Σ-shares rule again.
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
}

/// <summary>One request line: the product (with the Product owner's answered version), the Settings unit and the exact quantity.</summary>
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

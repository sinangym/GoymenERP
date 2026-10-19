namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

internal sealed class PurchasingSubcontractOrderRow : IPurchasingTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid SupplierPartyId { get; set; }
    public string SupplierPartyVersion { get; set; } = string.Empty;
    public Guid CompanyAccountId { get; set; }
    public string CompanyAccountVersion { get; set; } = string.Empty;
    public int Status { get; set; }
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}

internal sealed class PurchasingSubcontractOrderLineRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid OrderId { get; set; }
    public int LineNo { get; set; }
    public Guid LineId { get; set; }
    public Guid ServiceId { get; set; }
    public string ServiceVersion { get; set; } = string.Empty;
    public Guid ServiceUnitId { get; set; }
    public string ServiceQuantity { get; set; } = string.Empty;
    public Guid SourcePhysicalRequestId { get; set; }
    public string SourcePhysicalRequestVersion { get; set; } = string.Empty;
}

internal sealed class PurchasingSubcontractAdmissionRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid OrderId { get; set; }
    public Guid LineId { get; set; }
    public Guid ServiceId { get; set; }
    public string ServiceVersion { get; set; } = string.Empty;
    public string ServiceCode { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string ExpectedOrderVersion { get; set; } = string.Empty;
    public int MaterialOwnerKind { get; set; }
    public Guid MaterialOwnerId { get; set; }
    public Guid ExternalLocationId { get; set; }
    public string ExternalLocationVersion { get; set; } = string.Empty;
    public Guid OperatorPartyId { get; set; }
    public string OperatorPartyVersion { get; set; } = string.Empty;
    public Guid SourcePhysicalRequestId { get; set; }
    public string SourcePhysicalRequestVersion { get; set; } = string.Empty;
    public DateTimeOffset AdmittedAtUtc { get; set; }
}

internal sealed class PurchasingSubcontractAttemptRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public int Outcome { get; set; }
    public Guid? OrderId { get; set; }
    public string? RefusalJson { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

/// <summary>B-04a: one version of a company's posting account mapping. Only its activation facts change.</summary>
internal sealed class AccountingPostingAccountMappingVersionRow : IAccountingTenantRow
{
    public Guid CompanyId { get; set; }
    public long MappingVersion { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public int Status { get; set; }
    public long Version { get; set; }
    public string AccountPlanSourceVersion { get; set; } = string.Empty;
    public string AccountPlanSourceSha256 { get; set; } = string.Empty;
    public Guid PreparedByUserId { get; set; }
    public DateTimeOffset PreparedAtUtc { get; set; }
    public Guid? ActivatedByUserId { get; set; }
    public DateTimeOffset? ActivatedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>B-04a: one immutable row of a mapping version.</summary>
internal sealed class AccountingPostingAccountMappingEntryRow : IAccountingTenantRow
{
    public Guid CompanyId { get; set; }
    public long MappingVersion { get; set; }
    public int LineNumber { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string MappingKey { get; set; } = string.Empty;
    public string? ItemType { get; set; }
    public string? LinePurpose { get; set; }
    public bool? IsExport { get; set; }
    public Guid AccountId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string? VatRateCode { get; set; }
}

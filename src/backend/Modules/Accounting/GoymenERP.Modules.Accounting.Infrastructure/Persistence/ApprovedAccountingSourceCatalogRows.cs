namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

internal sealed class ApprovedAccountingSourceCatalogRow : IAccountingTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public int Status { get; set; }
    public long Version { get; set; }
    public long? CatalogVersion { get; set; }
    public string TemplateVersion { get; set; } = string.Empty;
    public string AccountPlanSourceVersion { get; set; } = string.Empty;
    public string AccountsSourceSha256 { get; set; } = string.Empty;
    public string PostingMappingsSourceSha256 { get; set; } = string.Empty;
    public string PackageJson { get; set; } = string.Empty;
    public Guid PreparedByUserId { get; set; }
    public DateTimeOffset PreparedAtUtc { get; set; }
    public Guid? ActivatedByUserId { get; set; }
    public DateTimeOffset? ActivatedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

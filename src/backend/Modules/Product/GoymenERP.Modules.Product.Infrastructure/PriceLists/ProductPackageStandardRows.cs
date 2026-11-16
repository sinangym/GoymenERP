namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

/// <summary>
/// B-08 / BS-10 (STK-S12): one version of the company's package standard table — insert-only (the newest is current). An
/// <c>Import</c> version records the mandatory source label and the file's name, SHA-256 and size; an <c>Edit</c> version records
/// the product whose entry was set or removed. Every version holds the whole table (<see cref="ProductPackageStandardLineRow"/>).
/// </summary>
internal sealed class ProductPackageStandardVersionRow : IProductPriceListTenantRow
{
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public Guid VersionId { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public string? SourceLabel { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string? FileSha256 { get; set; }
    public int? FileSizeBytes { get; set; }
    public Guid? EditedProductId { get; set; }
    public int EntryCount { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>One product's pieces per package in one version (a product appears once per version). Insert-only.</summary>
internal sealed class ProductPackageStandardLineRow : IProductPriceListTenantRow
{
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public Guid ProductId { get; set; }
    public int PiecesPerPackage { get; set; }
}

/// <summary>
/// The terminal answer of one package standard command under one key (ADR-011): company, command and the SHA-256 of the key,
/// under a binary collation; the raw key is never stored. Insert-only.
/// </summary>
internal sealed class ProductPackageStandardAttemptRow : IProductPriceListTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string AnswerJson { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

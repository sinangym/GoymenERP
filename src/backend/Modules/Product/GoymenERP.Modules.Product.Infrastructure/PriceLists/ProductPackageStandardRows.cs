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

    /// <summary>SAT-D382 (STK-SC36-c): the customer whose override was set or removed by an <c>Edit</c> version (null otherwise).</summary>
    public Guid? EditedCustomerId { get; set; }

    public int EntryCount { get; set; }

    /// <summary>The customer override entries this version holds (<see cref="ProductPackageStandardCustomerLineRow"/>).</summary>
    public int CustomerEntryCount { get; set; }

    /// <summary>
    /// SAT-D382: true for every version written since approvals exist — it takes effect only through an approval decision
    /// (<see cref="ProductPackageStandardDecisionRow"/>). False only for the versions written before (effective without one).
    /// </summary>
    public bool RequiresApproval { get; set; }

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

/// <summary>
/// SAT-D382 (STK-SC36-c): one customer's override of one product's pieces per package in one version (a customer and product pair
/// appears once per version). The product default stays in <see cref="ProductPackageStandardLineRow"/>. Insert-only.
/// </summary>
internal sealed class ProductPackageStandardCustomerLineRow : IProductPriceListTenantRow
{
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public Guid CustomerId { get; set; }
    public Guid ProductId { get; set; }
    public int PiecesPerPackage { get; set; }
}

/// <summary>
/// SAT-D382 (STK-SC36-c): the single decision on a version that requires approval — Approved (effective from
/// <see cref="EffectiveFrom"/>) or Rejected (never effective). One per version; insert-only (a decision is never changed).
/// </summary>
internal sealed class ProductPackageStandardDecisionRow : IProductPriceListTenantRow
{
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public Guid DecisionId { get; set; }
    public string Decision { get; set; } = string.Empty;
    public DateOnly? EffectiveFrom { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset DecidedAtUtc { get; set; }
    public string DecidedBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// SAT-D383 / SAT-D397 / SAT-D401 (STK-SC36-c): an uploaded package standard file kept with its result report — accepted (it
/// produced <see cref="Version"/>) or refused (row errors). Kept as long as the audit record: insert-only, never updated or deleted
/// (a database trigger refuses both); no retention period or destruction is chosen here.
/// </summary>
internal sealed class ProductPackageStandardImportFileRow : IProductPriceListTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid ImportId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FileSha256 { get; set; } = string.Empty;
    public int FileSizeBytes { get; set; }
    public byte[] Content { get; set; } = [];
    public string Outcome { get; set; } = string.Empty;
    public string? ProblemCode { get; set; }
    public string ReportJson { get; set; } = string.Empty;
    public long? Version { get; set; }
    public string SourceLabel { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}

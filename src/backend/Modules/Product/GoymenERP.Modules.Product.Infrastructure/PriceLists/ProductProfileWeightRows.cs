namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

/// <summary>
/// URN-D13/D14 (STK-S37R): one import of the company's kg/m table — a new, insert-only version (the newest is current). It
/// records the mandatory source label, the reason, the file's name, SHA-256 and size, and the vault document the file is kept as
/// (linked to <see cref="ImportId"/>). The rows read from the file are stored separately (<see cref="ProductProfileWeightLineRow"/>).
/// </summary>
internal sealed class ProductProfileWeightImportRow : IProductPriceListTenantRow
{
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public Guid ImportId { get; set; }
    public string SourceLabel { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FileSha256 { get; set; } = string.Empty;
    public int FileSizeBytes { get; set; }
    public Guid DocumentId { get; set; }
    public int RowCount { get; set; }
    public DateTimeOffset ImportedAtUtc { get; set; }
    public string ImportedBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// One kg/m row of an import: a profile (width, height, wall; no diameter) or a round tube (outer diameter, wall; no width or
/// height). A measure pair appears once per import (the database holds it). Insert-only.
/// </summary>
internal sealed class ProductProfileWeightLineRow : IProductPriceListTenantRow
{
    public Guid CompanyId { get; set; }
    public long ImportVersion { get; set; }
    public int LineNo { get; set; }
    public string SectionKind { get; set; } = string.Empty;
    public decimal? WidthMm { get; set; }
    public decimal? HeightMm { get; set; }
    public decimal? OuterDiameterMm { get; set; }
    public decimal WallThicknessMm { get; set; }
    public decimal KgPerMetre { get; set; }
}

/// <summary>
/// The terminal answer of one kg/m table command under one key (ADR-011): company, command and the SHA-256 of the key, under a
/// binary collation; the raw key is never stored. Insert-only.
/// </summary>
internal sealed class ProductProfileWeightAttemptRow : IProductPriceListTenantRow
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

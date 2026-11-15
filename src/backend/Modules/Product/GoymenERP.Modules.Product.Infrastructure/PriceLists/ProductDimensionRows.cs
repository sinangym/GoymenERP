namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

/// <summary>
/// BS-9: the "Ebat/kalınlık" row of a product on the SHARED product card — one row for every company, keyed by the product
/// alone (no company scope, like the global card it extends and like the item type of SAT-D149). The section kind says which
/// measures are present (the database checks it); only the measures, the version and the update facts move.
/// <c>UpdatedInCompanyId</c> is the company in which the last change was made (the audit event of each change is written in
/// that company). The history is the audit trail.
/// </summary>
internal sealed class ProductCardDimensionRow
{
    public Guid ProductId { get; set; }
    public string SectionKind { get; set; } = string.Empty;
    public decimal? ThicknessMm { get; set; }
    public decimal? WidthMm { get; set; }
    public decimal? LengthMm { get; set; }
    public decimal? HeightMm { get; set; }
    public decimal? OuterDiameterMm { get; set; }
    public decimal? WallThicknessMm { get; set; }
    public decimal? KgPerMetre { get; set; }

    /// <summary>KLN-D48: the structural profile series (<c>ProductProfileSeriesV1</c>); the kg/m is the company table's.</summary>
    public string? ProfileSeries { get; set; }

    /// <summary>KLN-D48: the structural profile size designation ("200").</summary>
    public string? ProfileSize { get; set; }

    /// <summary>KLN-D48: the mesh spacing of welded mesh (hasır çelik), millimetres.</summary>
    public decimal? MeshSpacingMm { get; set; }

    /// <summary>KLN-D48: the surface (paint / colour) of a painted ERW kind.</summary>
    public string? SurfaceFinish { get; set; }

    public long Version { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public Guid UpdatedInCompanyId { get; set; }
}

/// <summary>
/// The terminal answer of one dimension command under one key (ADR-011): company, command and the SHA-256 of the key, under a
/// binary collation; the raw key is never stored. Insert-only.
/// </summary>
internal sealed class ProductDimensionAttemptRow : IProductPriceListTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public Guid ProductId { get; set; }
    public int StatusCode { get; set; }
    public string AnswerJson { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

/// <summary>
/// KLN-D54: one imported version of the company's structural profile weight table — insert-only (the whole table is imported
/// again as the next version; the highest version is current). The source label names the standard or catalogue and its edition.
/// </summary>
internal sealed class ProductProfileWeightTableRow : IProductPriceListTenantRow
{
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public string SourceLabel { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int RowCount { get; set; }
    public DateTimeOffset ImportedAtUtc { get; set; }
    public string ImportedBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>KLN-D54: one profile of one table version (series, size designation, kg/m). Insert-only.</summary>
internal sealed class ProductProfileWeightRow : IProductPriceListTenantRow
{
    public Guid CompanyId { get; set; }
    public long TableVersion { get; set; }
    public string ProfileSeries { get; set; } = string.Empty;
    public string ProfileSize { get; set; } = string.Empty;
    public decimal KgPerMetre { get; set; }
}

/// <summary>The terminal answer of one weight table import under one key (ADR-011); the raw key is never stored. Insert-only.</summary>
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

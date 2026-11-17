namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>BS-10 / URT-D25: explicit source-backed membership, never inferred from a name, tax type or steel taxonomy.</summary>
public static class ProductPaintMembershipPermissionsV1
{
    public const string Manage = "product.paint-memberships.manage";
}

public enum ProductPaintMembershipReadOutcomeV1 { Available = 1, NoSource = 2, NotFound = 3, Inactive = 4, Unavailable = 5 }
public enum ProductPaintMembershipCatalogOutcomeV1 { Available = 1, NoSource = 2, Unavailable = 3 }
public sealed record ProductPaintMembershipEntryV1(Guid ProductId, string ProductVersion, string MembershipVersion,
    string ProductCode, string ProductName, string SourceVersion);
public sealed record ProductPaintMembershipReadV1(ProductPaintMembershipReadOutcomeV1 Outcome, Guid CompanyId,
    Guid ProductId, ProductPaintMembershipEntryV1? Entry, string SourceVersion);
public sealed record ProductPaintMembershipCatalogV1(ProductPaintMembershipCatalogOutcomeV1 Outcome,
    Guid CompanyId, string Version, IReadOnlyList<ProductPaintMembershipEntryV1> Entries, string SourceVersion);

/// <summary>Authenticated company membership only; the consuming Manufacturing operation owns its permission fence.
/// Supplier selection uses the actual Cari owner. Optional color is a Settings PaintColor reference, not a new Product master.
/// ProductVersion and MembershipVersion must be snapshotted and revalidated by the consumer; no rate/formula is returned.</summary>
public interface IProductPaintMembershipReaderV1
{
    Task<ProductPaintMembershipReadV1> GetAsync(Guid productId, CancellationToken cancellationToken = default);
    Task<ProductPaintMembershipCatalogV1> ListAsync(CancellationToken cancellationToken = default);
}

public static class ProductPaintMembershipLimitsV1
{
    public const int MaximumFileBytes = 2_097_152;
    public const int MaximumRows = 5000;
    public const int MaximumReasonLength = 4000;
    public const int MaximumSourceLabelLength = 200;
    public const string DefaultProductCodeColumn = "Ürün kodu";
}
public sealed record ValidateProductPaintMembershipImportV1(string? FileName, string? ContentBase64, string? ProductCodeColumn);
public sealed record ProductPaintMembershipImportErrorV1(int? RowNumber, string Field, string Code, string Message);
public sealed record ProductPaintMembershipImportRowV1(int RowNumber, Guid ProductId, string ProductVersion,
    string MembershipVersion, string ProductCode, string ProductName, bool AlreadyMember);
public sealed record ProductPaintMembershipImportPreviewV1(Guid CompanyId, string Version, string FileSha256,
    string ProductCodeColumn, string PreviewToken, int SourceRowCount, IReadOnlyList<ProductPaintMembershipImportRowV1> Rows,
    IReadOnlyList<ProductPaintMembershipImportErrorV1> Errors)
{
    public bool Ready => SourceRowCount > 0 && Rows.Count == SourceRowCount && Errors.Count == 0;
}
/// <summary>The server parses and validates the same bytes again, then binds the preview to the current actor/company,
/// Product versions and table version. Only the explicitly imported product rows become members; absent rows stay untouched.</summary>
public sealed record ApplyProductPaintMembershipImportV1(string? ExpectedVersion, string? FileName, string? ContentBase64,
    string? ProductCodeColumn, string? FileSha256, string? PreviewToken, string? SourceLabel, string? Reason);
public sealed record SetProductPaintMembershipV1(string? ExpectedVersion, string? ExpectedProductVersion,
    string? ExpectedMembershipVersion, bool IsMember, string? Reason);
public sealed record ProductPaintMembershipCommandMetadataV1(Guid CompanyId, string ActorId, string CorrelationId,
    string? CausationId, string IdempotencyKey);
public sealed record ProductPaintMembershipMutationV1(Guid CompanyId, string Version,
    IReadOnlyList<ProductPaintMembershipEntryV1> ChangedEntries, bool IsReplay, string OriginalCorrelationId);
public sealed class ProductPaintMembershipException(int statusCode, string code, bool isReplay = false,
    string? originalCorrelationId = null) : Exception(code)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public bool IsReplay { get; } = isReplay;
    public string? OriginalCorrelationId { get; } = originalCorrelationId;
}
public interface IProductPaintMembershipServiceV1
{
    Task<ProductPaintMembershipCatalogV1> GetCurrentAsync(CancellationToken cancellationToken = default);
    Task<ProductPaintMembershipImportPreviewV1> ValidateImportAsync(ValidateProductPaintMembershipImportV1 request,
        CancellationToken cancellationToken = default);
    Task<ProductPaintMembershipMutationV1> ApplyImportAsync(ApplyProductPaintMembershipImportV1 request,
        ProductPaintMembershipCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
    Task<ProductPaintMembershipMutationV1> SetAsync(Guid productId, SetProductPaintMembershipV1 request,
        ProductPaintMembershipCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}

public static class ProductPaintMembershipCodesV1
{
    public const string InvalidRequest = "product_paint_membership_request_invalid";
    public const string InvalidFile = "product_paint_membership_file_invalid";
    public const string PreviewMismatch = "product_paint_membership_preview_mismatch";
    public const string VersionConflict = "product_paint_membership_version_conflict";
    public const string IdempotencyConflict = "product_paint_membership_idempotency_conflict";
    public const string NotFound = "product_paint_membership_product_not_found";
    public const string Inactive = "product_paint_membership_product_inactive";
    public const string NoSource = "product_paint_membership_no_source";
    public const string Unavailable = "product_paint_membership_source_unavailable";
}

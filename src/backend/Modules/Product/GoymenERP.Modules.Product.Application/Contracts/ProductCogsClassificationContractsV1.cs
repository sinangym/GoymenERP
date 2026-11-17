using System.Text.Json.Serialization;
namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>SAT-D419/D422: explicit card classification. Purchase/production/stock events and tax item types never infer it.</summary>
public enum ProductCogsClassificationKindV1 { TradeGoods = 1, Manufactured = 2 }
public enum ProductCogsClassificationReadOutcomeV1 { Available = 1, NotSet = 2, NotFound = 3, Unavailable = 4 }
public static class ProductCogsClassificationContractsV1
{
    public const string SourceVersion = "product.cogs-classification.v1";
    public const string Manage = "product.cogs-classifications.manage";
    public const string CommandName = "product.card.cogs-classification.set.v1";
    public const int MaximumReasonLength = 1000;
}
public sealed record ProductCogsClassificationSnapshotV1(Guid ProductId, string ProductVersion,
    string ClassificationVersion, ProductCogsClassificationKindV1? Classification, Guid? ChangedInCompanyId, string SourceVersion);
public sealed record ProductCogsClassificationReadV1(ProductCogsClassificationReadOutcomeV1 Outcome, Guid CompanyId,
    Guid ProductId, ProductCogsClassificationSnapshotV1? Snapshot, string SourceVersion);
/// <summary>Narrow operational owner read, authenticated server-resolved company only. Its consuming invoice/cost command fences
/// its own permission. NotSet is never silently converted to TradeGoods/Manufactured or to an account; Finance owns account mapping.</summary>
public interface IProductCogsClassificationReadPortV1
{
    Task<ProductCogsClassificationReadV1> GetAsync(Guid productId, CancellationToken cancellationToken = default);
}
public sealed record ProductCogsClassificationViewV1(Guid CompanyId, Guid ProductId, string ProductVersion,
    string ClassificationVersion, ProductCogsClassificationKindV1? Classification, Guid? ChangedInCompanyId,
    DateTimeOffset? UpdatedAtUtc, string SourceVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetProductCogsClassificationV1([property: JsonRequired] string ExpectedProductVersion,
    [property: JsonRequired] string ExpectedClassificationVersion, [property: JsonRequired] ProductCogsClassificationKindV1 Classification,
    [property: JsonRequired] string Reason);
public sealed record ProductCogsClassificationCommandMetadataV1(Guid CompanyId, string ActorId, string CorrelationId,
    string? CausationId, string IdempotencyKey, string SourceChannel, string? ClientIpAddress = null, string? UserAgent = null);
public sealed record ProductCogsClassificationMutationV1(ProductCogsClassificationViewV1 Classification,
    bool IsReplay, string OriginalCorrelationId);
public interface IProductCogsClassificationServiceV1
{
    /// <summary>The existing explicit global Product Read operation, never a company permission.</summary>
    Task<ProductCogsClassificationViewV1> GetAsync(Guid productId, CancellationToken cancellationToken = default);
    /// <summary>Fresh explicit global SetCogsClassification authority before lookup/read/write/replay and commit.</summary>
    Task<ProductCogsClassificationMutationV1> SetAsync(Guid productId, SetProductCogsClassificationV1 request,
        ProductCogsClassificationCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}
public sealed class ProductCogsClassificationException(int statusCode, string code, bool isReplay = false,
    string? originalCorrelationId = null) : Exception(code)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public bool IsReplay { get; } = isReplay;
    public string? OriginalCorrelationId { get; } = originalCorrelationId;
}
public static class ProductCogsClassificationCodesV1
{
    public const string InvalidRequest = "product_cogs_classification_request_invalid";
    public const string NotFound = "product_cogs_classification_product_not_found";
    public const string VersionConflict = "product_cogs_classification_version_conflict";
    public const string IdempotencyConflict = "product_cogs_classification_idempotency_conflict";
    public const string Unavailable = "product_cogs_classification_source_unavailable";
}

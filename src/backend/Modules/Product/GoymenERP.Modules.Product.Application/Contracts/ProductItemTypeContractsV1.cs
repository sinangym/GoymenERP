namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// SAT-D134 (Sinan, 2026-10-31, <c>docs/decisions/SINAN_KARARLARI_2026-10-31_SATIS_EK19.md</c>): the product card holds NO tax
/// class and NO tax exception — only an ITEM TYPE: demir / hurda / hizmet. The tax comes from the Settings default per item type
/// (SAT-D139) and may be changed on the order line (SAT-D93). Product-owned and held ONCE on the SHARED product card (SAT-D149,
/// <c>docs/decisions/SINAN_KARARLARI_2026-11-01_SATIS_EK20.md</c>): the same product has the same item type in every company.
/// </summary>
public static class ProductItemTypesV1
{
    /// <summary>Demir.</summary>
    public const string Steel = "Steel";

    /// <summary>Hurda.</summary>
    public const string Scrap = "Scrap";

    /// <summary>Hizmet.</summary>
    public const string Service = "Service";

    public static IReadOnlyList<string> All { get; } = [Steel, Scrap, Service];

    public static bool IsKnown(string? itemType) => itemType is Steel or Scrap or Service;
}

public static class ProductItemTypePermissionsV1
{
    /// <summary>
    /// ONE company-wide name for reading and setting a product's item type. SAT-D149: a holder in ANY company changes the shared
    /// value (each change audited in the acting company). Registered, NOT granted here: SAT-D145 names Müdür and Admin; the grant
    /// is a separate PR (human gate). Not Critical.
    /// </summary>
    public const string Manage = "product.item-type.manage";
}

public static class ProductItemTypeProblemCodesV1
{
    public const string RequestInvalid = "product_item_type_request_invalid";
    public const string CommandMetadataInvalid = "product_item_type_command_metadata_invalid";
    public const string ReasonRequired = "product_item_type_reason_required";
    public const string ReasonTooLong = "product_item_type_reason_too_long";
    public const string IdempotencyConflict = "product_item_type_idempotency_conflict";
    public const string VersionConflict = "product_item_type_version_conflict";
    public const string ProviderUnavailable = "product_item_type_provider_unavailable";
}

public static class ProductItemTypeInputLimitsV1
{
    public const int ReasonMaximumLength = 4000;
}

/// <summary>
/// Set the item type of a product on the shared card, acting in the server-resolved company (SAT-D149). <c>ItemType</c> is one
/// of <see cref="ProductItemTypesV1"/>; <c>ExpectedVersion</c> is the shared value's version as decimal text ("0" = none yet).
/// </summary>
public sealed record SetProductItemTypeRequestV1(string? ExpectedVersion, string? ItemType, string? Reason);

/// <summary>
/// The shared card's item type ("0" and null while none is stored), read in <c>CompanyId</c> (the server-resolved company);
/// <c>UpdatedInCompanyId</c> is the company in which the last change was made (SAT-D149: the value is the same everywhere).
/// </summary>
public sealed record ProductItemTypeViewV1(
    Guid CompanyId,
    Guid ProductId,
    string Version,
    string? ItemType,
    DateTimeOffset? UpdatedAtUtc,
    string? UpdatedBy,
    Guid? UpdatedInCompanyId);

/// <summary>Server-derived command metadata; company and actor never come from a payload.</summary>
public sealed record ProductItemTypeCommandMetadataV1(
    Guid CompanyId,
    string ActorId,
    string CorrelationId,
    string? CausationId,
    string SourceChannel,
    string? ClientIpAddress,
    string? UserAgent,
    string IdempotencyKey);

/// <summary>A command's terminal answer (stored for replay); <c>Code</c> is null for a success and the code-less 404.</summary>
public sealed record ProductItemTypeAnswerV1(int StatusCode, string? Code, string Title, string Detail);

public sealed record ProductItemTypeResultV1(ProductItemTypeAnswerV1 Answer, bool IsReplay, string OriginalCorrelationId);

/// <summary>A failure of the item type read or write: 400 / 404 / 503 (the code names it).</summary>
public sealed class ProductItemTypeException(int statusCode, string? code) : Exception(code ?? statusCode.ToString(
    System.Globalization.CultureInfo.InvariantCulture))
{
    public int StatusCode { get; } = statusCode;

    public string? Code { get; } = code;
}

/// <summary>
/// The item type use cases. Both are authorized first on <see cref="ProductItemTypePermissionsV1.Manage"/> company-wide and
/// re-measured before the answer / the save. One person decides: a mandatory reason, Idempotency-Key, the expected version and
/// one audit event per real change.
/// </summary>
public interface IProductItemTypeServiceV1
{
    Task<ProductItemTypeViewV1> GetAsync(Guid productId, CancellationToken cancellationToken = default);

    Task<ProductItemTypeResultV1> SetAsync(Guid productId, SetProductItemTypeRequestV1 request,
        ProductItemTypeCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}

public enum ProductItemTypeOutcomeV1
{
    Unavailable = 0,
    Denied = 1,
    NotFound = 2,
    NotSet = 3,
    Found = 4,
}

public sealed record ProductItemTypeReadV1(ProductItemTypeOutcomeV1 Outcome, string? ItemType, long? Version);

/// <summary>
/// The read port consumers use (the host's Settings line tax adapter; Sales via Settings): the shared card's item type of a
/// product, read by an actor of the server-resolved company. Access only (authenticated member of the company); no permission is measured — the answer is
/// reference data and the consumer fences its own operation. Never throws for a missing card or item type.
/// </summary>
public interface IProductItemTypeReaderV1
{
    Task<ProductItemTypeReadV1> GetAsync(Guid productId, CancellationToken cancellationToken = default);
}

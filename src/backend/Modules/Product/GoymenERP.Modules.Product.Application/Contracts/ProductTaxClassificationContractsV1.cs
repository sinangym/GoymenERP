namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// MOD-03 (SAT-D73/D74/D82, <c>docs/decisions/SINAN_KARARLARI_2026-10-28_SATIS_EK12.md</c>): the product's VAT class and
/// withholding (tevkifat) class — Product-owned, COMPANY-scoped (like the price lists), referring to the company's Settings
/// tax categories by id. The rates are never on the card: they are the Settings tax master's dated records (SAT-D82).
/// </summary>
public static class ProductTaxClassificationPermissionsV1
{
    /// <summary>
    /// ONE company-wide name for reading and setting a product's tax classes. Registered, NOT granted: who receives it
    /// (Muhasebe/Finans, Admin, the product owner) is a human grant gate (NEEDS-SINAN). Not Critical.
    /// </summary>
    public const string Manage = "product.tax-classification.manage";
}

public static class ProductTaxClassificationProblemCodesV1
{
    public const string RequestInvalid = "product_tax_classification_request_invalid";
    public const string CommandMetadataInvalid = "product_tax_classification_command_metadata_invalid";
    public const string ReasonRequired = "product_tax_classification_reason_required";
    public const string ReasonTooLong = "product_tax_classification_reason_too_long";
    public const string IdempotencyConflict = "product_tax_classification_idempotency_conflict";
    public const string VersionConflict = "product_tax_classification_version_conflict";
    public const string VatClassInvalid = "product_tax_classification_vat_class_invalid";
    public const string WithholdingClassInvalid = "product_tax_classification_withholding_class_invalid";
    public const string ProviderUnavailable = "product_tax_classification_provider_unavailable";
}

public static class ProductTaxClassificationInputLimitsV1
{
    public const int ReasonMaximumLength = 4000;
}

/// <summary>
/// Set the product's classes in the server-resolved company. <c>VatClassId</c> is required (a Settings VAT class of the
/// company); <c>WithholdingClassId</c> null means the product bears no withholding (SAT-D74), otherwise a Settings
/// withholding class of the company. <c>ExpectedVersion</c> is the classification's version as decimal text ("0" = none yet).
/// </summary>
public sealed record SetProductTaxClassificationRequestV1(
    string? ExpectedVersion,
    Guid? VatClassId,
    Guid? WithholdingClassId,
    string? Reason);

/// <summary>The classification ("0" and no classes while none is stored).</summary>
public sealed record ProductTaxClassificationViewV1(
    Guid CompanyId,
    Guid ProductId,
    string Version,
    Guid? VatClassId,
    Guid? WithholdingClassId,
    DateTimeOffset? UpdatedAtUtc,
    string? UpdatedBy);

/// <summary>Server-derived command metadata; company and actor never come from a payload.</summary>
public sealed record ProductTaxClassificationCommandMetadataV1(
    Guid CompanyId,
    string ActorId,
    string CorrelationId,
    string? CausationId,
    string SourceChannel,
    string? ClientIpAddress,
    string? UserAgent,
    string IdempotencyKey);

/// <summary>A command's terminal answer (stored for replay); <c>Code</c> is null for a success and the code-less 404.</summary>
public sealed record ProductTaxClassificationAnswerV1(int StatusCode, string? Code, string Title, string Detail);

public sealed record ProductTaxClassificationResultV1(ProductTaxClassificationAnswerV1 Answer, bool IsReplay,
    string OriginalCorrelationId);

/// <summary>A failure of the classification read: 403 / 404 / 503 (the code names the 503).</summary>
public sealed class ProductTaxClassificationException(int statusCode, string? code) : Exception(code ?? statusCode.ToString(
    System.Globalization.CultureInfo.InvariantCulture))
{
    public int StatusCode { get; } = statusCode;

    public string? Code { get; } = code;
}

/// <summary>
/// The classification use cases. Both are authorized first on <see cref="ProductTaxClassificationPermissionsV1.Manage"/>
/// company-wide and re-measured before the answer / the save. One person decides: a mandatory reason, Idempotency-Key, the
/// expected version and one audit event per real change.
/// </summary>
public interface IProductTaxClassificationServiceV1
{
    Task<ProductTaxClassificationViewV1> GetAsync(Guid productId, CancellationToken cancellationToken = default);

    Task<ProductTaxClassificationResultV1> SetAsync(Guid productId, SetProductTaxClassificationRequestV1 request,
        ProductTaxClassificationCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}

public enum ProductTaxClassificationOutcomeV1
{
    Unavailable = 0,
    Denied = 1,
    NotFound = 2,
    NotClassified = 3,
    Found = 4,
}

public sealed record ProductTaxClassificationReadV1(
    ProductTaxClassificationOutcomeV1 Outcome,
    Guid? VatClassId,
    Guid? WithholdingClassId,
    long? Version);

/// <summary>
/// The read port consumers use (the host's Settings line tax adapter; Sales via Settings): the product's classes in the
/// server-resolved company. Access only (authenticated member of the company); no permission is measured — the answer is
/// reference data and the consumer fences its own operation. Never throws for a missing card or classification.
/// </summary>
public interface IProductTaxClassificationReaderV1
{
    Task<ProductTaxClassificationReadV1> GetAsync(Guid productId, CancellationToken cancellationToken = default);
}

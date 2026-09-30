using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// AT-018 / AT-021 price lists (U-05 "Fiyatlar": list, version and validity), Product-owned and company-scoped.
/// Money values travel as exact decimal TEXT (never a JS number): up to 15 integer digits and 4 decimals, a dot as the
/// separator. Dates are ISO <c>yyyy-MM-dd</c>. The concurrency <c>version</c> is canonical positive decimal text.
/// </summary>
public static class ProductPriceListApiV1
{
    public const string ListPath = "/api/v1/price-lists";
    public const string DetailPath = "/api/v1/price-lists/{priceListId:guid}";
    public const string DraftRevisionPath = "/api/v1/price-lists/{priceListId:guid}/draft-revision";
    public const string VersionsPath = "/api/v1/price-lists/{priceListId:guid}/versions";
    public const string PublicationPath = "/api/v1/price-lists/{priceListId:guid}/publication";
    public const string ProductOptionsPath = "/api/v1/price-lists/product-options";

    public const string ListOperationId = "GetPriceListsV1";
    public const string CreateOperationId = "CreatePriceListV1";
    public const string DetailOperationId = "GetPriceListV1";
    public const string ReviseDraftOperationId = "RevisePriceListDraftV1";
    public const string StartVersionOperationId = "StartPriceListVersionV1";
    public const string PublishOperationId = "PublishPriceListVersionV1";
    public const string ProductOptionsOperationId = "GetPriceListProductOptionsV1";
}

/// <summary>The three company-wide names this surface uses (registered centrally; granted by a human).</summary>
public static class ProductPriceListPermissionsV1
{
    public const string Read = "product.price-lists.read";
    public const string Write = "product.price-lists.write";
    /// <summary>Its own name: preparing a list does not confer publishing it, and publishing confers no edit.</summary>
    public const string Publish = "product.price-lists.publish";
}

/// <summary>The versioned command names the attempt record is keyed by (ADR-011 §2).</summary>
public static class ProductPriceListCommandsV1
{
    public const string CreateV1 = "product.price-list.create.v1";
    public const string ReviseDraftV1 = "product.price-list.revise-draft.v1";
    public const string StartVersionV1 = "product.price-list.start-version.v1";
    public const string PublishV1 = "product.price-list.publish.v1";

    public static bool IsMutation(string commandName) => commandName is ReviseDraftV1 or StartVersionV1 or PublishV1;

    public static bool IsKnown(string commandName) => commandName == CreateV1 || IsMutation(commandName);
}

public static class ProductPriceListCodesV1
{
    public const string Unauthenticated = "authentication_required";
    public const string Forbidden = "product_price_list_forbidden";
    public const string PermissionUnregistered = "product_price_list_permission_unregistered";
    public const string ValidationFailed = "validation_failed";
    public const string NotFound = "not_found";
    public const string VersionConflict = "version_conflict";
    public const string StateConflict = "product_price_list_state_conflict";
    public const string DuplicateName = "product_price_list_duplicate_name";
    public const string IdempotencyConflict = "idempotency_conflict";
    public const string ConcurrentChange = "concurrent_change";
    public const string InvalidCursor = "invalid_cursor";
    public const string Unavailable = "product_price_list_unavailable";

    // field codes
    public const string ProductInvalid = "product_invalid";
    public const string ProductNotActive = "product_not_active";
    public const string ProductNotSellable = "product_not_sellable";
    public const string PriceUnitUnsupported = "price_unit_unsupported";
    public const string InvalidDecimal = "invalid_decimal";
    public const string InvalidDate = "invalid_date";
    public const string Required = "required";
}

public static class ProductPriceListHttpCodesV1
{
    public static IReadOnlySet<string> Status400Codes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        ProductPriceListCodesV1.ValidationFailed, ProductPriceListCodesV1.InvalidCursor,
    };

    public static IReadOnlySet<string> Status404Codes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        ProductPriceListCodesV1.NotFound,
    };

    public static IReadOnlySet<string> Status409Codes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        ProductPriceListCodesV1.VersionConflict, ProductPriceListCodesV1.StateConflict, ProductPriceListCodesV1.DuplicateName,
        ProductPriceListCodesV1.IdempotencyConflict, ProductPriceListCodesV1.ConcurrentChange,
    };

    public static IReadOnlySet<string> Status422Codes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        ProductPriceListCodesV1.ValidationFailed,
    };

    public static IReadOnlySet<string> Status503Codes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        ProductPriceListCodesV1.PermissionUnregistered, ProductPriceListCodesV1.Unavailable,
    };
}

public sealed record ProductPriceListFieldErrorV1(string Field, string Code);

/// <summary>An owner answer with an HTTP status, a closed code and the field errors it names.</summary>
public sealed class ProductPriceListException(int status, string code, IReadOnlyList<ProductPriceListFieldErrorV1>? errors = null,
    Exception? innerException = null) : Exception(code, innerException)
{
    public int Status { get; } = status;

    public string Code { get; } = code;

    public IReadOnlyList<ProductPriceListFieldErrorV1> Errors { get; } = errors ?? [];
}

// ---- requests -------------------------------------------------------------------------------------

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PriceListLineRequestV1(
    [property: JsonRequired] Guid LineId,
    [property: JsonRequired] Guid ProductId,
    [property: JsonRequired] Guid PriceUnitId,
    [property: JsonRequired] string UnitPrice,
    string? FloorUnitPrice = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreatePriceListRequestV1(
    [property: JsonRequired] Guid PriceListId,
    [property: JsonRequired] Guid VersionId,
    [property: JsonRequired] string Name,
    [property: JsonRequired] string CurrencyCode,
    [property: JsonRequired] string Channel,
    [property: JsonRequired] string VatBasis,
    [property: JsonRequired] string ValidFrom,
    string? ValidThrough,
    [property: JsonRequired] IReadOnlyList<PriceListLineRequestV1> Lines);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RevisePriceListDraftRequestV1(
    [property: JsonRequired] string ExpectedVersion,
    [property: JsonRequired] string Name,
    [property: JsonRequired] string ValidFrom,
    string? ValidThrough,
    [property: JsonRequired] IReadOnlyList<PriceListLineRequestV1> Lines);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StartPriceListVersionRequestV1(
    [property: JsonRequired] string ExpectedVersion,
    [property: JsonRequired] Guid VersionId,
    [property: JsonRequired] string ValidFrom,
    string? ValidThrough);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PublishPriceListVersionRequestV1(
    [property: JsonRequired] string ExpectedVersion,
    [property: JsonRequired] int VersionNo);

// ---- responses ------------------------------------------------------------------------------------

public sealed record PriceListLineViewV1(Guid LineId, int LineNo, Guid ProductId, string? ProductCode, string? ProductName,
    Guid PriceUnitId, string UnitPrice, string? FloorUnitPrice);

/// <summary><c>state</c> is stored (Draft / Published); <c>status</c> is derived for today (Draft / Planned / Active / Ended).</summary>
public sealed record PriceListVersionViewV1(Guid VersionId, int VersionNo, string State, string Status, string ValidFrom,
    string? ValidThrough, DateTimeOffset CreatedAtUtc, string CreatedBy, DateTimeOffset? PublishedAtUtc, string? PublishedBy,
    IReadOnlyList<PriceListLineViewV1> Lines);

public sealed record PriceListViewV1(Guid Id, string Name, string CurrencyCode, string Channel, string VatBasis, string Version,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, int? ActiveVersionNo, int? DraftVersionNo,
    IReadOnlyList<PriceListVersionViewV1> Versions);

public sealed record PriceListSummaryV1(Guid Id, string Name, string CurrencyCode, string Channel, string VatBasis, string Version,
    int? ActiveVersionNo, string? ActiveValidFrom, int? DraftVersionNo, int VersionCount, DateTimeOffset UpdatedAtUtc);

public sealed record PriceListPageV1(IReadOnlyList<PriceListSummaryV1> Items, string? NextCursor);

public sealed record PriceListResultV1(PriceListViewV1 PriceList, bool IsReplay, string OriginalCorrelationId);

/// <summary>One unit a product may be priced in: the card's own stock or sales unit (Settings unit identity, code if known).</summary>
public sealed record PriceListProductUnitOptionV1(Guid UnitId, string Role, string? Code);

public sealed record PriceListProductOptionV1(Guid ProductId, string Code, string Name, IReadOnlyList<PriceListProductUnitOptionV1> Units);

public sealed record PriceListProductOptionsV1(IReadOnlyList<PriceListProductOptionV1> Items, bool HasMore);

/// <summary>The server-side context of one write (ADR-009): who, under which permission, with which key.</summary>
public sealed record ProductPriceListCommandMetadata(
    string ActorId,
    string PermissionCode,
    IReadOnlyCollection<string> RoleCodes,
    string CorrelationId,
    string? CausationId,
    string IdempotencyKey,
    string SourceChannel,
    string? ClientIpAddress = null,
    string? UserAgent = null);

/// <summary>The price-list surface of the server-resolved company. Every call measures its own permission.</summary>
public interface IProductPriceListServiceV1
{
    Task<PriceListPageV1> ListAsync(int? pageSize, string? cursor, CancellationToken cancellationToken = default);

    Task<PriceListViewV1> GetAsync(Guid priceListId, CancellationToken cancellationToken = default);

    Task<PriceListProductOptionsV1> GetProductOptionsAsync(string? query, CancellationToken cancellationToken = default);

    Task<PriceListResultV1> CreateAsync(CreatePriceListRequestV1 request, ProductPriceListCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<PriceListResultV1> ReviseDraftAsync(Guid priceListId, RevisePriceListDraftRequestV1 request, ProductPriceListCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<PriceListResultV1> StartVersionAsync(Guid priceListId, StartPriceListVersionRequestV1 request, ProductPriceListCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<PriceListResultV1> PublishAsync(Guid priceListId, PublishPriceListVersionRequestV1 request, ProductPriceListCommandMetadata metadata,
        CancellationToken cancellationToken = default);
}

/// <summary>Strict text shapes of the wire (the Sales decimal-text pattern).</summary>
public static partial class ProductPriceListWire
{
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);

    [GeneratedRegex("^(0|[1-9][0-9]{0,14})(\\.[0-9]{1,4})?$", RegexOptions.CultureInvariant)]
    private static partial Regex PriceText();

    [GeneratedRegex("^[0-9]{4}-[0-9]{2}-[0-9]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex DateText();

    [GeneratedRegex("^[1-9][0-9]{0,18}$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionText();

    /// <summary>Exact decimal text → value, keeping the written scale ("12.50" stays 12.50); null when the shape is wrong.</summary>
    public static decimal? ParsePrice(string? text) =>
        text is not null && PriceText().IsMatch(text)
        && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    public static DateOnly? ParseDate(string? text) =>
        text is not null && DateText().IsMatch(text)
        && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    public static long? ParseVersion(string? text) =>
        text is not null && VersionText().IsMatch(text) && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    public static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string FormatPrice(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}

// ---- the Sales-facing reference read (the port carries no authority; the consumer fences its own names) --------------

/// <summary>
/// A published list price for one product in one price unit, answered for the server-resolved company: the list,
/// its version and validity, the currency, the VAT basis and channel, the price and the optional floor (AT-021).
/// </summary>
public sealed record ProductPriceReferenceV1(
    Guid PriceListId,
    string PriceListName,
    Guid VersionId,
    int VersionNo,
    DateOnly ValidFrom,
    DateOnly? ValidThrough,
    Guid ProductId,
    Guid PriceUnitId,
    string CurrencyCode,
    string Channel,
    string VatBasis,
    decimal UnitPrice,
    decimal? FloorUnitPrice);

/// <summary>
/// Product's answer to "which list prices apply to this product on this date?" for the Sales order line. Only Sales
/// channel lists of the context company, only the version that prices the date (<c>PriceList.ResolveOn</c>), only the
/// currency asked for; several lists may answer (no scope priority is decided — the user chooses). The port carries no
/// authority: the consumer measures its own permission before asking. No company context → refused before any read.
/// </summary>
public interface IProductPriceReferenceReaderV1
{
    Task<IReadOnlyList<ProductPriceReferenceV1>> SearchAsync(Guid productId, IReadOnlyCollection<Guid> priceUnitIds, string currencyCode,
        DateOnly on, CancellationToken cancellationToken = default);

    /// <summary>The exact reference a line names, if that published version still prices the product in the unit on the date.</summary>
    Task<ProductPriceReferenceV1?> GetAsync(Guid priceListId, int versionNo, Guid productId, Guid priceUnitId, DateOnly on,
        CancellationToken cancellationToken = default);
}

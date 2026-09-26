using System.Text.Json.Serialization;
using GoymenERP.Modules.Inventory.Domain;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// The Inventory permission names this module asks the central catalogue about (INV-OPEN-1 part 2).
/// Registering a name and granting it are not this module's: an unregistered name answers 503, and the
/// grant is a human decision (decision A: one narrow authorized user, #66 5738113623 §2).
/// </summary>
public static class InventoryPermissionManifestV1
{
    /// <summary>Record — and read back — an opening count. Company-wide.</summary>
    public const string OpeningStockRecord = "inventory.opening-stock.record";

    /// <summary>Create and explicitly release a physical stock allocation. Company-wide.</summary>
    public const string PhysicalAllocationWrite = "inventory.physical-allocation.write";

    /// <summary>
    /// Record one same-company label split or merge (R19, DEC-LBL-002/003). Company-wide. The literal is the
    /// owner's technical mapping, confirmed by the coordinator (session decision 2026-09-27; technical names are
    /// not Sinan's to pick, #66/5808295853 and #66/5811361336). Its only grant is a disposable R19 browser test
    /// actor in the per-run database: no real user and no seeded role receives it, and the real-user mapping
    /// stays with the persona card #66/5802952566. A separate Inventory-owned name follows #66/5806047097 by
    /// ANALOGY only: that ruling decided the STK/T11 physical-allocation command, not split/merge authority.
    /// Registration and classification (company-wide, not Critical) are the central writer's.
    /// </summary>
    public const string LabelTransformationRecord = "inventory.label-transformation.record";
}

/// <summary>The opening count as the client sends it: one physical count sheet of one location.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OpeningCountRequestV1(
    [property: JsonRequired] Guid CountId,
    [property: JsonRequired] Guid LocationId,
    [property: JsonRequired] DateOnly CountedOn,
    [property: JsonRequired] string SheetReference,
    [property: JsonRequired] IReadOnlyList<OpeningCountLineRequestV1> Lines);

/// <summary>
/// One counted piece: its new label (id + printed code), its lot (heat/coil null = unknown), the product
/// card version the counter read (Product's stock eligibility is asked for exactly that version) and the
/// counted kilograms as a lossless decimal string.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OpeningCountLineRequestV1(
    [property: JsonRequired] Guid LineId,
    [property: JsonRequired] Guid LabelId,
    [property: JsonRequired] string LabelCode,
    [property: JsonRequired] Guid LotId,
    string? HeatNumber,
    string? CoilNumber,
    [property: JsonRequired] Guid ProductId,
    [property: JsonRequired] string ProductVersion,
    [property: JsonRequired] string QuantityKg);

/// <summary>A stored opening count as read back.</summary>
public sealed record OpeningCountViewV1(
    Guid CountId,
    Guid CompanyId,
    Guid LocationId,
    DateOnly CountedOn,
    string SheetReference,
    IReadOnlyList<OpeningCountLineViewV1> Lines);

public sealed record OpeningCountLineViewV1(
    Guid LineId,
    Guid LabelId,
    string LabelCode,
    Guid LotId,
    string? HeatNumber,
    string? CoilNumber,
    Guid ProductId,
    string QuantityKg);

/// <summary>A successful record: the count, whether it was a replay, and the correlation of its first recording.</summary>
public sealed record OpeningCountResultV1(OpeningCountViewV1 Count, bool IsReplay, string OriginalCorrelationId);

/// <summary>One refused field.</summary>
public sealed record InventoryFieldIssueV1(string Field, string Code);

/// <summary>
/// A refused opening-count command: <see cref="Status"/> is the HTTP status the host answers with,
/// <see cref="Code"/> the stable problem code, <see cref="Issues"/> the per-field (per-line) reasons.
/// Nothing was written.
/// </summary>
public sealed class InventoryOpeningServiceException(int status, string code, IReadOnlyList<InventoryFieldIssueV1>? issues = null,
    bool isReplay = false, string? originalCorrelationId = null) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public IReadOnlyList<InventoryFieldIssueV1> Issues { get; } = issues ?? [];
    public bool IsReplay { get; } = isReplay;
    public string? OriginalCorrelationId { get; } = originalCorrelationId;
}

/// <summary>The problem codes of the opening-count routes beside the store's and the domain's own codes.</summary>
public static class InventoryOpeningCodesV1
{
    public const string Unauthenticated = "unauthenticated";
    public const string Forbidden = "forbidden";
    public const string ValidationFailed = "validation_failed";
    public const string IdempotencyConflict = "idempotency_conflict";
    public const string PermissionUnregistered = "inventory_permission_unregistered";
    public const string LocationNotFound = "inventory_location_not_found";
    public const string LocationInactive = "inventory_location_inactive";
    public const string LocationExternal = "inventory_location_external";
    public const string LocationUnavailable = "inventory_location_unavailable";
    public const string ProductIneligible = "inventory_product_ineligible";
    public const string ProductEligibilityUnavailable = "inventory_product_eligibility_unavailable";
    public const string NotFound = "not_found";
    public const string ProductVersionInvalid = "product_version_invalid";
    public const string QuantityInvalid = "quantity_invalid";
    public const string IdempotencyKeyInvalid = "idempotency_key_invalid";

    /// <summary>
    /// The CLOSED problem codes of the record route, per HTTP status (the OpenAPI lists exactly these). The
    /// service answers nothing outside them: an unmapped store or domain code is an internal fault (500), never
    /// a guessed 4xx. 401/403/404/503 are answered by the host as platform problems; their codes are listed for
    /// the logs and the tests.
    /// </summary>
    public static IReadOnlySet<string> Status400Codes { get; } = Set(ValidationFailed);

    public static IReadOnlySet<string> Status409Codes { get; } = Set(IdempotencyConflict, InventoryStoreCodes.ConcurrentChange,
        InventoryStoreCodes.OpeningConflict, InventoryStoreCodes.LabelConflict, InventoryStoreCodes.LabelCodeTaken,
        InventoryStoreCodes.LotConflict, InventoryRuleCodes.LabelAlreadyInStock, InventoryStoreCodes.DuplicateSource,
        InventoryStoreCodes.MovementIdConflict);

    public static IReadOnlySet<string> Status422Codes { get; } = Set(LocationNotFound, LocationInactive, LocationExternal,
        ProductIneligible);

    public static IReadOnlySet<string> Status503Codes { get; } = Set(PermissionUnregistered, LocationUnavailable,
        ProductEligibilityUnavailable, InventoryScaleCodes.ProfileUnusable, "settings_provider_unavailable",
        "settings_unit_profile_not_found", "settings_profile_invalid", "settings_access_denied", "settings_decimal_invalid",
        "settings_decimal_scale_exceeded");

    /// <summary>
    /// The codes of a 400's <c>errors[]</c>. The per-line Product codes of a 422 (<c>lines[i].*</c>) are
    /// Product's own and not listed here.
    /// </summary>
    public static IReadOnlySet<string> IssueCodes { get; } = Set(IdempotencyKeyInvalid, InventoryStoreCodes.ValueDoesNotFit,
        InventoryRuleCodes.InvalidOpeningCount, ProductVersionInvalid, QuantityInvalid, InventoryRuleCodes.QuantityNotPositive,
        InventoryRuleCodes.KilogramPrecisionExceeded, InventoryScaleCodes.ScaleExceeded, InventoryRuleCodes.LabelCompanyMismatch,
        InventoryRuleCodes.MissingReference, InventoryRuleCodes.InvalidLabelOperation);

    private static IReadOnlySet<string> Set(params string[] codes) => codes.ToHashSet(StringComparer.Ordinal);
}

/// <summary>
/// What Organization says about one location of the company, read by the host's adapter through Organization's
/// company-scoped module read — never a user-authorized read, so a caller's missing Organization grant cannot look
/// like a missing location. Only <see cref="Active"/> is opened; every other answer is refused with its own code.
/// Inactive is the location's OWN state (no site cascade is decided anywhere). When several apply, the adapter
/// answers the first of <see cref="NotFound"/>, <see cref="External"/>, <see cref="Inactive"/>: what the place is
/// before its state, since reactivating an external location would not open it (T07 = A: the company's own location).
/// </summary>
public enum InventoryLocationStatus
{
    NotFound = 0,
    Active = 1,
    Inactive = 2,

    /// <summary>
    /// An external location (a supplier's or fason party's yard; Organization's <c>IsExternal</c>,
    /// SINAN_KARARLARI_2026-08-31 §3): its custodian is another party, so an opening — whose owner and custodian
    /// are the company itself — is not recorded there (DEC-STK-001).
    /// </summary>
    External = 3,
}

/// <summary>
/// The location port of the opening command: the current state of one location of the company, as
/// Organization holds it now. An implementation that cannot answer throws; it never guesses "active".
/// </summary>
public interface IInventoryLocationStatusPort
{
    Task<InventoryLocationStatus> ReadAsync(Guid companyId, Guid locationId, CancellationToken cancellationToken);
}

/// <summary>Product's stock answer for one counted product at one card version and date.</summary>
public sealed record InventoryProductStockAnswer(bool IsEligible, IReadOnlyList<InventoryFieldIssueV1> Issues);

/// <summary>
/// The product port of the opening command: whether a product, at the card version the counter read, may
/// be stocked on the counted-on date (Product's <c>IProductStockEligibilityV1</c> through the host's
/// adapter). Unavailable owners throw <see cref="InventoryOpeningServiceException"/> 503; a refused
/// product is an answer, never an exception.
/// </summary>
public interface IInventoryProductStockPort
{
    Task<InventoryProductStockAnswer> EvaluateAsync(Guid productId, string productVersion, DateOnly effectiveOn,
        CancellationToken cancellationToken);
}

/// <summary>The opening-count command surface (INV-OPEN-1 part 2).</summary>
public interface IInventoryOpeningCommandService
{
    /// <summary>Records an opening count under ADR-011 (Idempotency-Key). Throws <see cref="InventoryOpeningServiceException"/> on refusal.</summary>
    Task<OpeningCountResultV1> RecordAsync(OpeningCountRequestV1 request, InventoryCommandMetadata metadata,
        CancellationToken cancellationToken);

    /// <summary>A stored count of the company, or 404 <see cref="InventoryOpeningCodesV1.NotFound"/>.</summary>
    Task<OpeningCountViewV1> ReadAsync(Guid countId, CancellationToken cancellationToken);
}

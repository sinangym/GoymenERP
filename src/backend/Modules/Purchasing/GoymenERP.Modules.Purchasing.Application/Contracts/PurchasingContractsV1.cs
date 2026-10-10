using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

// ---- requests (strict wire shapes; PurchasingJson validates the shape, the service the meaning) ----

/// <summary>
/// POST /api/v1/purchasing/requests: the demand side (talep, §7.2), a Draft with lines. Every line names
/// the product (with the version the client saw, for the Product owner's answer), the Settings unit and the
/// exact quantity text (Ö2). <paramref name="EffectiveOn"/> is the business date the unit profile and the
/// product eligibility are read for.
/// </summary>
public sealed record CreatePurchaseRequestRequest(DateOnly EffectiveOn, IReadOnlyList<PurchaseRequestLineRequest> Lines);

public sealed record PurchaseRequestLineRequest(Guid LineId, Guid ProductId, string ExpectedProductVersion, Guid UnitId, string Quantity);

/// <summary>POST …/requests/{id}/submission: Draft → Submitted at the expected version.</summary>
public sealed record SubmitPurchaseRequestRequest(string ExpectedVersion);

/// <summary>
/// POST /api/v1/purchasing/orders: a commitment towards ONE supplier (the Cari party + its company account
/// with the Supplier role), a Draft with general lines. With <paramref name="SourceRequestId"/> the order is
/// opened for one SUBMITTED request of this company and every line MAY bind to one of that request's lines
/// (<see cref="PurchaseOrderLineRequest.SourceRequestLineId"/>) at the share this supplier takes
/// (multi-sourcing, Sinan #66 5738113623 §1); a bound line keeps the request line's product and unit.
/// Without a source request no line may bind. No price (T28).
/// </summary>
public sealed record CreatePurchaseOrderRequest(
    Guid SupplierPartyId,
    Guid CompanyAccountId,
    Guid? SourceRequestId,
    DateOnly EffectiveOn,
    IReadOnlyList<PurchaseOrderLineRequest> Lines);

public sealed record PurchaseOrderLineRequest(Guid LineId, Guid ProductId, string ExpectedProductVersion, Guid UnitId, string Quantity,
    Guid? SourceRequestLineId);

/// <summary>POST …/orders/{id}/approval: Draft → Approved at the expected version; the Σ-shares rule is measured in the same save.</summary>
public sealed record ApprovePurchaseOrderRequest(string ExpectedVersion);

// ---- views --------------------------------------------------------------------------------------

public sealed record PurchaseRequestSummaryView(
    Guid Id, Guid CompanyId, string Status, string Version, DateOnly EffectiveOn,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, int LineCount);

public sealed record PurchaseRequestLineView(Guid LineId, int LineNo, Guid ProductId, string ProductVersion, Guid UnitId, string Quantity);

public sealed record PurchaseRequestDetailView(PurchaseRequestSummaryView Request, IReadOnlyList<PurchaseRequestLineView> Lines);

public sealed record PurchaseRequestResponse(PurchaseRequestDetailView Detail, bool IsReplay, string OriginalCorrelationId);

public sealed record PurchaseRequestListResponse(IReadOnlyList<PurchaseRequestSummaryView> Items, string? NextCursor);

/// <param name="Status">An exact status name ("Draft" | "Submitted") to narrow the list to, or null.</param>
public sealed record PurchaseRequestListQuery(int PageSize = 50, string? Cursor = null, string? Status = null);

public sealed record PurchaseOrderSummaryView(
    Guid Id, Guid CompanyId, string Status, string Version, Guid SupplierPartyId, Guid CompanyAccountId, Guid? SourceRequestId,
    DateOnly EffectiveOn, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, int LineCount)
{
    /// <summary>
    /// Present only on authorized order list/detail reads. Commands keep their existing response and do
    /// not synthesize a Cari fallback when the record-bound owner source is unavailable.
    /// </summary>
    public PurchasingSupplierDisplayV1? SupplierDisplay { get; init; }
}

/// <summary>The line's shares as exact text (§7.2, §13): ordered, moved into technical lines, committed (= ordered − technical), accepted, remaining (= committed − accepted, the expected supply).</summary>
public sealed record PurchaseOrderLineView(
    Guid LineId, int LineNo, string Kind, Guid? ParentLineId, Guid? SourceRequestLineId,
    Guid ProductId, string ProductVersion, Guid UnitId,
    string Ordered, string Technical, string Committed, string Accepted, string Remaining);

/// <summary>One immutable goods-acceptance share in the persisted order sequence; quantity is exact text in the order line's unit.</summary>
public sealed record PurchaseOrderAcceptanceHistoryView(
    Guid AcceptanceId, Guid LineId, string Quantity, DateTimeOffset RecordedAtUtc);

public sealed record PurchaseOrderDetailView(PurchaseOrderSummaryView Order, IReadOnlyList<PurchaseOrderLineView> Lines)
{
    public IReadOnlyList<PurchaseOrderAcceptanceHistoryView> Acceptances { get; init; } = [];
}

public sealed record PurchaseOrderResponse(PurchaseOrderDetailView Detail, bool IsReplay, string OriginalCorrelationId);

public sealed record PurchaseOrderListResponse(IReadOnlyList<PurchaseOrderSummaryView> Items, string? NextCursor);

/// <param name="Status">An exact status name ("Draft" | "Approved") to narrow the list to, or null.</param>
public sealed record PurchaseOrderListQuery(int PageSize = 50, string? Cursor = null, string? Status = null);

public sealed record PurchasingFieldError(string Field, string Code);

// ---- problems -----------------------------------------------------------------------------------

/// <summary>The problem codes the purchasing routes answer beside the domain's rule codes (PUR-4a contract).</summary>
public static class PurchasingProblemCodesV1
{
    /// <summary>400: the Idempotency-Key header is missing, repeated, blank, non-canonical, over 128 characters or malformed.</summary>
    public const string CommandMetadataInvalid = "command_metadata_invalid";

    public const string InvalidCursor = "invalid_cursor";

    /// <summary>409: the supplier account is not the party's, not this company's, not active, or has no Supplier role (the Sales/Logistics pattern).</summary>
    public const string AccountState = "account_state";

    /// <summary>409 (fail-closed): the source request is not SUBMITTED, unknown in this company, or a bound line does not match one of its lines; field errors say which.</summary>
    public const string RequestState = "purchasing_request_state";

    /// <summary>409, terminal (§7): the expected version is stale.</summary>
    public const string VersionConflict = "version_conflict";

    /// <summary>409, terminal (§7): the order is not in the state the command needs (an Approved order cannot be approved again).</summary>
    public const string OrderState = "purchasing_order_state";

    /// <summary>409, terminal (§7): the request is not in the state the command needs (a Submitted request cannot be submitted again).</summary>
    public const string RequestNotDraft = "purchasing_request_not_draft";

    /// <summary>409, terminal (§7; Sinan 5738113623 §1): approving this order would take the approved shares of a request line over its need.</summary>
    public const string RequestLineOverSourced = "purchasing_request_line_over_sourced";

    public const string SameKeyPayloadMismatch = "same_key_payload_mismatch";
    public const string ConcurrentChange = "concurrent_change";

    /// <summary>422: the unit is not an approved kilogram profile for this company on the date, or the quantity's scale exceeds it (Ö2).</summary>
    public const string UnsupportedUnit = "unsupported_unit";

    /// <summary>422: the Product owner refused this selection (not purchasable, lifecycle, unit binding); field errors carry the owner's codes.</summary>
    public const string UnsupportedProduct = "unsupported_product";

    /// <summary>503 (never on the wire): the Cari account owner cannot answer.</summary>
    public const string CounterpartyProviderUnavailable = "counterparty_provider_unavailable";

    /// <summary>503 (never on the wire): the Product owner has not published purchase eligibility, or cannot answer (fail-closed).</summary>
    public const string ProductEligibilityUnavailable = "product_eligibility_unavailable";

    /// <summary>503 (never on the wire): an owner answered a malformed value.</summary>
    public const string ReferenceProviderInvalid = "reference_provider_invalid";

    /// <summary>503 (never on the wire): a Purchasing permission name is not in the central catalogue yet.</summary>
    public const string PermissionUnregistered = "permission_unregistered";
}

public sealed class PurchasingServiceException(int status, string code,
    IReadOnlyList<PurchasingFieldError>? errors = null, bool isReplay = false, string? originalCorrelationId = null)
    : Exception($"{status} {code}")
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public IReadOnlyList<PurchasingFieldError> Errors { get; } = errors ?? [];
    public bool IsReplay { get; } = isReplay;
    public string? OriginalCorrelationId { get; } = originalCorrelationId;
}

/// <summary>The permission names the service measures; null = not bound → 503 setup problem, never a grant.</summary>
public sealed record PurchasingServiceBindings(
    string? CreatePermission = PurchasingPermissionManifestV1.OrdersCreate,
    string? ReadPermission = PurchasingPermissionManifestV1.OrdersRead,
    string? ApprovePermission = PurchasingPermissionManifestV1.OrdersApprove,
    string? GoodsReceiptPermission = PurchasingPermissionManifestV1.GoodsReceiptsRecord);

/// <summary>Constructed by the server, never bound from the request body.</summary>
public sealed record PurchasingCommandMetadata(string IdempotencyKey, string CorrelationId, string? CausationId, DateTimeOffset RequestedAtUtc);

public static class PurchasingApiV1
{
    public const string RequestsPath = "/api/v1/purchasing/requests";
    public const string RequestDetailPath = "/api/v1/purchasing/requests/{id}";
    public const string RequestSubmissionPath = "/api/v1/purchasing/requests/{id}/submission";
    public const string OrdersPath = "/api/v1/purchasing/orders";
    public const string OrderDetailPath = "/api/v1/purchasing/orders/{id}";
    public const string OrderApprovalPath = "/api/v1/purchasing/orders/{id}/approval";
    public const string ProductCandidatesPath = "/api/v1/purchasing/references/products";
    public const string IdempotencyHeader = "Idempotency-Key";

    /// <summary>The first commit and its replay both answer 201 (ADR-011 §4; the Sales #321 decision), told apart by the replay headers.</summary>
    public const int CreatedStatus = 201;

    /// <summary>A transition answers the record after it (200); the same key again answers 200 + replay headers.</summary>
    public const int MutationStatus = 200;
}

/// <summary>Shape validation only; the owners' scales, permissions and references are the service's.</summary>
public static class PurchasingJson
{
    private const int MaximumLines = 500;
    private const int MaximumProductVersionLength = 64;
    private static readonly Regex DecimalText = new("\\A[0-9]+(?:\\.[0-9]+)?\\z", RegexOptions.CultureInvariant);
    private static readonly Regex VersionText = new("\\A[1-9][0-9]*\\z", RegexOptions.CultureInvariant);

    public static JsonSerializerOptions Options { get; } = BuildOptions();

    private static JsonSerializerOptions BuildOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            NumberHandling = JsonNumberHandling.Strict,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true,
            RespectNullableAnnotations = true,
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    public static CreatePurchaseRequestRequest ReadCreateRequest(string json)
    {
        var request = JsonSerializer.Deserialize<CreatePurchaseRequestRequest>(json, Options)
            ?? throw new JsonException("Request body is required.");
        if (request.Lines is null || request.Lines.Count is < 1 or > MaximumLines)
        {
            throw new JsonException("Invalid request fields.");
        }

        var ids = new HashSet<Guid>();
        foreach (var line in request.Lines)
        {
            if (line is null || line.LineId == Guid.Empty || !ids.Add(line.LineId) || line.ProductId == Guid.Empty || line.UnitId == Guid.Empty)
            {
                throw new JsonException("Invalid line fields.");
            }

            RequireProductVersionShape(line.ExpectedProductVersion);
            RequireDecimalShape(line.Quantity);
        }

        return request;
    }

    public static SubmitPurchaseRequestRequest ReadSubmit(string json)
    {
        var request = JsonSerializer.Deserialize<SubmitPurchaseRequestRequest>(json, Options)
            ?? throw new JsonException("Request body is required.");
        RequireVersionShape(request.ExpectedVersion);
        return request;
    }

    public static CreatePurchaseOrderRequest ReadCreateOrder(string json)
    {
        var request = JsonSerializer.Deserialize<CreatePurchaseOrderRequest>(json, Options)
            ?? throw new JsonException("Request body is required.");
        if (request.SupplierPartyId == Guid.Empty || request.CompanyAccountId == Guid.Empty || request.SourceRequestId == Guid.Empty
            || request.Lines is null || request.Lines.Count is < 1 or > MaximumLines)
        {
            throw new JsonException("Invalid order fields.");
        }

        var ids = new HashSet<Guid>();
        var sourceLines = new HashSet<Guid>();
        foreach (var line in request.Lines)
        {
            if (line is null || line.LineId == Guid.Empty || !ids.Add(line.LineId) || line.ProductId == Guid.Empty || line.UnitId == Guid.Empty
                || line.SourceRequestLineId == Guid.Empty || (line.SourceRequestLineId is { } source && !sourceLines.Add(source))
                || (line.SourceRequestLineId is not null && request.SourceRequestId is null))
            {
                throw new JsonException("Invalid line fields.");
            }

            RequireProductVersionShape(line.ExpectedProductVersion);
            RequireDecimalShape(line.Quantity);
        }

        return request;
    }

    public static ApprovePurchaseOrderRequest ReadApprove(string json)
    {
        var request = JsonSerializer.Deserialize<ApprovePurchaseOrderRequest>(json, Options)
            ?? throw new JsonException("Request body is required.");
        RequireVersionShape(request.ExpectedVersion);
        return request;
    }

    private static void RequireVersionShape(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 19 || !VersionText.IsMatch(text))
        {
            throw new JsonException("Invalid expected version.");
        }
    }

    private static void RequireProductVersionShape(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumProductVersionLength
            || !string.Equals(text, text.Trim(), StringComparison.Ordinal))
        {
            throw new JsonException("Invalid product version.");
        }
    }

    private static void RequireDecimalShape(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 128 || !DecimalText.IsMatch(text) || !text.Any(c => c is >= '1' and <= '9'))
        {
            throw new JsonException("Invalid decimal text.");
        }
    }
}

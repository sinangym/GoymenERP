using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GoymenERP.Modules.Purchasing.Domain.Requests;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

// ---- requests (strict wire shapes; PurchasingJson validates the shape, the service the meaning) ----

/// <summary>
/// POST /api/v1/purchasing/requests: the demand side (talep, §7.2), a Draft with lines. Every line names
/// the product (with the version the client saw, for the Product owner's answer), the Settings unit and the
/// exact quantity text (Ö2). <paramref name="EffectiveOn"/> is the business date the unit profile and the
/// product eligibility are read for.
/// </summary>
public sealed record CreatePurchaseRequestRequest(DateOnly EffectiveOn, IReadOnlyList<PurchaseRequestLineRequest> Lines);

/// <summary>
/// One request line. A1-2 (plan API-02): <paramref name="Source"/> is optional; absent or null is a manual line. It is never
/// written when null, so the content hash of a manual line — and every ADR-011 attempt recorded before A1-2 — is unchanged.
/// </summary>
public sealed record PurchaseRequestLineRequest(Guid LineId, Guid ProductId, string ExpectedProductVersion, Guid UnitId, string Quantity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PurchaseRequestLineSourceRequest? Source = null);

/// <summary>
/// A1-2 (plan API-02; Üretim A-2, Satış S-A1): the line's source — <paramref name="Kind"/> is exactly <c>Stock</c>,
/// <c>Sales</c> or <c>Production</c> (a manual line sends no source; <c>Manual</c> is not accepted here, so a line has one
/// spelling for it), <paramref name="DocumentId"/> the source record (sales order, production plan, stock need) and
/// <paramref name="DocumentLineId"/> its line, both non-empty. The identities are opaque: Purchasing stores them as given and
/// does not read the source module (no port exists yet; validation is the producing module's follow-up).
/// </summary>
public sealed record PurchaseRequestLineSourceRequest(string Kind, Guid DocumentId, Guid DocumentLineId);

/// <summary>POST …/requests/{id}/submission: Draft → Submitted at the expected version.</summary>
public sealed record SubmitPurchaseRequestRequest(string ExpectedVersion);

/// <summary>A1-1 (ALS-D2): POST …/requests/{id}/approval — Submitted → Approved at the expected version, by someone other than its preparer and submitter.</summary>
public sealed record ApprovePurchaseRequestRequest(string ExpectedVersion);

/// <summary>
/// A1-1 (No.161 §1): POST …/requests/{id}/rejection — Submitted | Approved → Rejected at the expected version, with the
/// reason (required, at most 4000 characters, never cut), by someone other than its makers, refused while an APPROVED
/// order is bound to the request.
/// </summary>
public sealed record RejectPurchaseRequestRequest(string ExpectedVersion, string Reason);

/// <summary>
/// A1-1b (ALS-D46, ALS-D47): POST …/requests/{id}/withdrawal — Submitted → Withdrawn at the expected version, by the
/// request's preparer (creator) or submitter only, with the reason (required, at most 4000 characters, never cut), refused while an APPROVED order is
/// bound to the request (No.161 §1).
/// </summary>
public sealed record WithdrawPurchaseRequestRequest(string ExpectedVersion, string Reason);

/// <summary>
/// POST /api/v1/purchasing/orders: a commitment towards ONE supplier (the Cari party + its company account
/// with the Supplier role), a Draft with general lines. With <paramref name="SourceRequestId"/> the order is
/// opened for one APPROVED request of this company (A1-1, ALS-D2/ALS-D16) and every line MAY bind to one of that request's lines
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

/// <summary>
/// The request header. A1-2 (plan API-02): <see cref="SourceKinds"/> names the distinct sources of its lines in a fixed order
/// (<c>Manual</c>, <c>Stock</c>, <c>Sales</c>, <c>Production</c>), so the request pool shows where each need came from
/// (ALS-D16) without reading the lines; a request whose lines are all manual answers <c>["Manual"]</c>.
/// </summary>
public sealed record PurchaseRequestSummaryView(
    Guid Id, Guid CompanyId, string Status, string Version, DateOnly EffectiveOn,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, int LineCount)
{
    public IReadOnlyList<string> SourceKinds { get; init; } = [];
}

/// <summary>A request line; A1-2: <see cref="Source"/> is the line's source as recorded, or null for a manual line.</summary>
public sealed record PurchaseRequestLineView(Guid LineId, int LineNo, Guid ProductId, string ProductVersion, Guid UnitId, string Quantity)
{
    public PurchaseRequestLineSourceView? Source { get; init; }
}

/// <summary>A1-2 (plan API-02): a line's recorded source — the kind (<c>Stock</c> | <c>Sales</c> | <c>Production</c>) and the opaque source identity.</summary>
public sealed record PurchaseRequestLineSourceView(string Kind, Guid DocumentId, Guid DocumentLineId);

/// <summary>A1-1: one decision on a request — who decided, when, and (a rejection or a withdrawal, A1-1b) why.</summary>
public sealed record PurchaseRequestDecisionView(string DecidedBy, DateTimeOffset DecidedAtUtc, string? Reason);

/// <summary>
/// The request with its lines; <see cref="Approval"/> and <see cref="Rejection"/> (A1-1) and <see cref="Withdrawal"/>
/// (A1-1b) are the recorded decisions, null until made. An approval stays visible after a later rejection.
/// <see cref="PreparedBy"/> and <see cref="SubmittedBy"/> (A1-1b) are the recorded makers — the only actors who may
/// withdraw (ALS-D47) — or null when not recorded (the submitter of a Draft, or of a submission recorded without its actor).
/// </summary>
public sealed record PurchaseRequestDetailView(PurchaseRequestSummaryView Request, IReadOnlyList<PurchaseRequestLineView> Lines)
{
    public PurchaseRequestDecisionView? Approval { get; init; }

    public PurchaseRequestDecisionView? Rejection { get; init; }

    public PurchaseRequestDecisionView? Withdrawal { get; init; }

    public string? PreparedBy { get; init; }

    public string? SubmittedBy { get; init; }
}

public sealed record PurchaseRequestResponse(PurchaseRequestDetailView Detail, bool IsReplay, string OriginalCorrelationId);

public sealed record PurchaseRequestListResponse(IReadOnlyList<PurchaseRequestSummaryView> Items, string? NextCursor);

/// <param name="Status">An exact status name ("Draft" | "Submitted" | "Approved" | "Rejected" | "Withdrawn") to narrow the list to, or null.</param>
public sealed record PurchaseRequestListQuery(int PageSize = 50, string? Cursor = null, string? Status = null);

public sealed record PurchaseOrderSummaryView(
    Guid Id, Guid CompanyId, string Status, string Version, Guid SupplierPartyId, Guid CompanyAccountId, Guid? SourceRequestId,
    DateOnly EffectiveOn, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, int LineCount);

/// <summary>
/// Authorized list/detail projection. Unlike command responses, every returned order has an exact,
/// record-bound current Cari supplier display and never falls back to the raw Party id alone.
/// </summary>
public sealed record PurchaseOrderReadSummaryViewV1(
    Guid Id, Guid CompanyId, string Status, string Version, Guid SupplierPartyId, Guid CompanyAccountId, Guid? SourceRequestId,
    DateOnly EffectiveOn, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, int LineCount,
    PurchasingSupplierDisplayV1 SupplierDisplay);

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

public sealed record PurchaseOrderReadDetailViewV1(
    PurchaseOrderReadSummaryViewV1 Order,
    IReadOnlyList<PurchaseOrderLineView> Lines)
{
    public IReadOnlyList<PurchaseOrderAcceptanceHistoryView> Acceptances { get; init; } = [];
}

public sealed record PurchaseOrderResponse(PurchaseOrderDetailView Detail, bool IsReplay, string OriginalCorrelationId);

public sealed record PurchaseOrderListResponse(IReadOnlyList<PurchaseOrderSummaryView> Items, string? NextCursor);

public sealed record PurchaseOrderReadListResponseV1(
    IReadOnlyList<PurchaseOrderReadSummaryViewV1> Items,
    string? NextCursor);

/// <param name="Status">An exact status name ("Draft" | "Approved") to narrow the list to, or null.</param>
/// <param name="SupplierPartyId">The exact supplier Party identity to narrow the list to, or null.</param>
public sealed record PurchaseOrderListQuery(
    int PageSize = 50,
    string? Cursor = null,
    string? Status = null,
    Guid? SupplierPartyId = null);

/// <summary>
/// A1-2 (plan API-02): the request-line source vocabulary on the wire. <see cref="Manual"/> appears only in a summary's
/// <see cref="PurchaseRequestSummaryView.SourceKinds"/>; a line says "manual" by carrying no source.
/// </summary>
public static class PurchaseRequestSourceKindsV1
{
    public const string Manual = "Manual";
    public const string Stock = "Stock";
    public const string Sales = "Sales";
    public const string Production = "Production";

    /// <summary>The exact (ordinal) name of a source kind; <see cref="Manual"/>, a different case or anything else is refused.</summary>
    public static bool TryParse(string? text, out PurchaseRequestSourceKind kind)
    {
        switch (text)
        {
            case Stock:
                kind = PurchaseRequestSourceKind.Stock;
                return true;
            case Sales:
                kind = PurchaseRequestSourceKind.Sales;
                return true;
            case Production:
                kind = PurchaseRequestSourceKind.Production;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    public static string Name(PurchaseRequestSourceKind kind) => kind switch
    {
        PurchaseRequestSourceKind.Stock => Stock,
        PurchaseRequestSourceKind.Sales => Sales,
        PurchaseRequestSourceKind.Production => Production,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>The distinct kinds of the given line sources in the fixed order Manual, Stock, Sales, Production.</summary>
    public static IReadOnlyList<string> Distinct(IEnumerable<PurchaseRequestLineSource?> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var present = sources.Select(source => source is null ? 0 : (int)source.Kind).ToHashSet();
        return [.. new[] { (0, Manual), (1, Stock), (2, Sales), (3, Production) }.Where(item => present.Contains(item.Item1)).Select(item => item.Item2)];
    }
}

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

    /// <summary>
    /// 409 (fail-closed): the source request is not APPROVED (A1-1; field <c>sourceRequestId</c> = <c>not_approved</c>),
    /// unknown in this company, or a bound line does not match one of its lines; field errors say which. At an order's
    /// approval it is terminal (§7) when the source request is no longer APPROVED.
    /// </summary>
    public const string RequestState = "purchasing_request_state";

    /// <summary>
    /// 409, terminal (§7; A1-1): the request is not in the state the decision needs (approve: Submitted; reject: Submitted
    /// or Approved; withdraw, A1-1b: Submitted).
    /// </summary>
    public const string RequestDecisionState = "purchasing_request_decision_state";

    /// <summary>409, terminal (§7; ALS-D2): the actor prepared or submitted this request and cannot decide it (hazırlayan ≠ onaylayan).</summary>
    public const string RequestSameActor = "purchasing_request_same_actor";

    /// <summary>
    /// 409, terminal (§7; A1-1 fail-closed): the request's preparer or submitter is not recorded, so no one may decide it.
    /// A1-1b (ALS-D47): a withdrawal is refused with this code only when NEITHER maker is recorded; with one recorded, only
    /// that one may withdraw and anyone else is <see cref="RequestNotPreparer"/>.
    /// </summary>
    public const string RequestMakerUnknown = "purchasing_request_maker_unknown";

    /// <summary>409, terminal (§7; No.161 §1): an APPROVED order is bound to the request; the order is cancelled first (rejection and A1-1b withdrawal).</summary>
    public const string RequestHasApprovedOrder = "purchasing_request_has_approved_order";

    /// <summary>422: the rejection reason is missing or blank.</summary>
    public const string RequestRejectionReasonRequired = "purchasing_request_rejection_reason_required";

    /// <summary>422: the rejection reason is longer than 4000 characters (refused, never cut).</summary>
    public const string RequestRejectionReasonTooLong = "purchasing_request_rejection_reason_too_long";

    /// <summary>
    /// 409, terminal (§7; A1-1b, ALS-D46/ALS-D47): the actor is neither the request's recorded preparer (creator) nor its
    /// recorded submitter; only they withdraw it.
    /// </summary>
    public const string RequestNotPreparer = "purchasing_request_not_preparer";

    /// <summary>422 (A1-1b): the withdrawal reason is missing or blank.</summary>
    public const string RequestWithdrawalReasonRequired = "purchasing_request_withdrawal_reason_required";

    /// <summary>422 (A1-1b): the withdrawal reason is longer than 4000 characters (refused, never cut).</summary>
    public const string RequestWithdrawalReasonTooLong = "purchasing_request_withdrawal_reason_too_long";

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
    string? GoodsReceiptPermission = PurchasingPermissionManifestV1.GoodsReceiptsRecord,
    string? RequestApprovePermission = PurchasingPermissionManifestV1.RequestsApprove,
    string? RequestWithdrawPermission = PurchasingPermissionManifestV1.RequestsWithdraw);

/// <summary>Constructed by the server, never bound from the request body.</summary>
public sealed record PurchasingCommandMetadata(string IdempotencyKey, string CorrelationId, string? CausationId, DateTimeOffset RequestedAtUtc);

public static class PurchasingApiV1
{
    public const string RequestsPath = "/api/v1/purchasing/requests";
    public const string RequestDetailPath = "/api/v1/purchasing/requests/{id}";
    public const string RequestSubmissionPath = "/api/v1/purchasing/requests/{id}/submission";
    public const string RequestApprovalPath = "/api/v1/purchasing/requests/{id}/approval";
    public const string RequestRejectionPath = "/api/v1/purchasing/requests/{id}/rejection";
    public const string RequestWithdrawalPath = "/api/v1/purchasing/requests/{id}/withdrawal";
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
            if (line.Source is { } source
                && (!PurchaseRequestSourceKindsV1.TryParse(source.Kind, out _) || source.DocumentId == Guid.Empty || source.DocumentLineId == Guid.Empty))
            {
                throw new JsonException("Invalid line source.");
            }
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

    public static ApprovePurchaseRequestRequest ReadApproveRequest(string json)
    {
        var request = JsonSerializer.Deserialize<ApprovePurchaseRequestRequest>(json, Options)
            ?? throw new JsonException("Request body is required.");
        RequireVersionShape(request.ExpectedVersion);
        return request;
    }

    /// <summary>The reason is a required string (shape); whether it is blank or too long is the domain's 422.</summary>
    public static RejectPurchaseRequestRequest ReadRejectRequest(string json)
    {
        var request = JsonSerializer.Deserialize<RejectPurchaseRequestRequest>(json, Options)
            ?? throw new JsonException("Request body is required.");
        RequireVersionShape(request.ExpectedVersion);
        if (request.Reason is null)
        {
            throw new JsonException("Invalid reason.");
        }

        return request;
    }

    /// <summary>A1-1b: the reason is a required string (shape); whether it is blank or too long is the domain's 422.</summary>
    public static WithdrawPurchaseRequestRequest ReadWithdrawRequest(string json)
    {
        var request = JsonSerializer.Deserialize<WithdrawPurchaseRequestRequest>(json, Options)
            ?? throw new JsonException("Request body is required.");
        RequireVersionShape(request.ExpectedVersion);
        if (request.Reason is null)
        {
            throw new JsonException("Invalid reason.");
        }

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

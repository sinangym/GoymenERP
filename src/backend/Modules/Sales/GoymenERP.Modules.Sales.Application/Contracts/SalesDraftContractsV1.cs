using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GoymenERP.Modules.Sales.Application.Contracts;

public sealed record CreateSalesOrderDraftRequest(
    string Kind, Guid PartyId, Guid CompanyAccountId, DateOnly BusinessDate,
    Guid CurrencyId, IReadOnlyList<SalesOrderDraftLineRequest> Lines,
    DateOnly? RequestedDeliveryDate = null, string? Note = null,
    SalesDraftOwnerReferenceV1? DeliveryAddress = null,
    SalesDraftOwnerReferenceV1? Contact = null,
    SalesDraftOwnerReferenceV1? Responsible = null,
    SalesDraftOwnerReferenceV1? PaymentTerms = null,
    SalesDraftOwnerReferenceV1? SourceStrategy = null,
    SalesDraftOwnerReferenceV1? Package = null);

public sealed record SalesOrderDraftLineRequest(
    Guid LineId, Guid ProductId, string ExpectedProductVersion, Guid OrderUnitId,
    string Quantity, string? UnitPrice = null, Guid? PriceUnitId = null,
    DateOnly? RequestedDeliveryDate = null, string? Note = null,
    IReadOnlyList<SalesDraftDeliverySliceV1>? RequestedDeliverySlices = null,
    string? DiscountPercent = null);

// A supplied owner reference must be authorized and snapshotted by that owner
// at command time. This wire reference is not an eligibility result.
public sealed record SalesDraftOwnerReferenceV1(Guid Id, string ExpectedVersion);
public sealed record SalesDraftDeliverySliceV1(
    Guid SliceId, Guid OrderUnitId, string Quantity, DateOnly RequestedDeliveryDate);

/// <summary>
/// Q9: the server-derived commercial stage of the SAME record. A current customer acceptance promotes
/// a new record from Quote to Order; legacy records which do not require acceptance are already Order.
/// This is not the lifecycle or internal-approval axis and is never persisted independently.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SalesCommercialStageV1>))]
public enum SalesCommercialStageV1 { Quote, Order }

// SALES-A1 (contract delta): `approvalState` is the internal approval axis — "Preparing",
// "PendingApproval", "Approved", "Rejected" or (D2) "Withdrawn" — beside the lifecycle `state` ("Draft", or since
// DEC-SAL-005 "Cancelled" for an approved order cancelled after its impact preview), the
// commercial `revision` and the concurrency `version`. Four separate facts, never merged.
public sealed record SalesOrderDraftSummaryView(
    Guid Id, Guid CompanyId, string DocumentNo, string Kind, string State,
    string Version, string Revision, Guid PartyId, Guid CompanyAccountId,
    Guid CurrencyId, DateOnly BusinessDate, DateTimeOffset CreatedAtUtc, int LineCount,
    string ApprovalState,
    SalesCommercialStageV1? CommercialStage = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SalesOrderDraftProductSnapshotViewV1>? ProductSnapshots = null);

/// <summary>
/// Product presentation already accepted from the Product owner and stored on the order line.
/// The list projects each distinct product/version/order-unit snapshot once; it performs no master
/// read, price calculation or current-name claim.
/// </summary>
public sealed record SalesOrderDraftProductSnapshotViewV1(
    Guid ProductId, string ProductVersion, string ProductCode, string ProductName,
    Guid OrderUnitId, string OrderUnitCode);

/// <summary>SALES-A1: the approval facts of the detail; every member null until it happened (ADR-009 audit facts, shown, never derived).</summary>
public sealed record SalesDraftApprovalV1(
    string? SubmittedBy, DateTimeOffset? SubmittedAtUtc,
    string? DecidedBy, DateTimeOffset? DecidedAtUtc,
    string? RejectionReason);

public sealed record SalesDraftCustomerAcceptanceV1(
    bool Required,
    string? AcceptedRevision,
    string? AcceptedBy,
    DateTimeOffset? AcceptedAtUtc,
    bool IsCurrentRevisionAccepted);

/// <summary>
/// SALES-A1: the body of a state command (submission / approval / rejection): the expected
/// concurrency version as text (the same shape as every version on this wire) and, for a
/// rejection only, the optional reason. Strict: no other member, no missing version.
/// </summary>
public sealed record SalesDraftStateCommandRequest(string ExpectedVersion, string? Reason = null);

/// <summary>
/// K3 Head B: one line's commercial fields in a commercial revision — the client line id and the three fields (all nullable, as on the create).
/// AT-018 / AT-021 (user decision 2026-09-30, contract delta, all optional): <c>priceReference</c> names the Product price list
/// version the line is priced from (the server re-reads its price; the client never sends it), <c>discountAmount</c> is the
/// discount as an amount (instead of <c>discountPercent</c>), and <c>surchargePercent</c> / <c>surchargeAmount</c> the maturity
/// surcharge (vade farkı). The unit price stays the line's own: the reference's price by default, or the person's overwrite.
/// </summary>
public sealed record SalesDraftLineCommercialV1(Guid LineId, string? UnitPrice = null, Guid? PriceUnitId = null, string? DiscountPercent = null,
    SalesDraftPriceReferenceRequestV1? PriceReference = null, string? DiscountAmount = null, string? SurchargePercent = null,
    string? SurchargeAmount = null);

/// <summary>AT-018: the Product price list version a line is priced from (list id + version number); the price is the owner's.</summary>
public sealed record SalesDraftPriceReferenceRequestV1(Guid PriceListId, int VersionNo);

/// <summary>AT-018: a list price as the Product owner answers it, copied onto the line at the revision (exact text).</summary>
public sealed record SalesDraftPriceReferenceV1(Guid PriceListId, string PriceListName, int VersionNo, DateOnly ValidFrom,
    DateOnly? ValidThrough, string CurrencyCode, Guid PriceUnitId, string UnitPrice, string? FloorUnitPrice, string VatBasis,
    string LowerQuantity, string? UpperQuantity);

/// <summary>
/// AT-018 / AT-021 / master §10.2: one line's price as the line carries it — the reference list price, the line's own unit
/// price and whether it is the reference's ("Reference") or overwritten ("Manual"), the discount and the maturity surcharge
/// as given, and the net from the one calculator (exact, never rounded; <c>null</c> without a price).
/// </summary>
public sealed record SalesDraftLinePricingV1(Guid LineId, int LineNo, string? UnitPrice, Guid? PriceUnitId, string? PriceSource,
    SalesDraftPriceReferenceV1? PriceReference, string? DiscountPercent, string? DiscountAmount, string? SurchargePercent,
    string? SurchargeAmount, string? NetUnitPrice, SalesDraftPriceFloorExceptionV1? FloorException);

public sealed record SalesDraftPriceFloorExceptionV1(string RequestedBy, DateTimeOffset RequestedAtUtc,
    string? ApprovedBy, DateTimeOffset? ApprovedAtUtc, string? Reason);

public sealed record SalesDraftPriceFloorExceptionApprovalLineV1(Guid LineId, string Reason);

/// <summary>Every current below-floor line exactly once; each approval carries its stored reason.</summary>
public sealed record SalesDraftPriceFloorExceptionApprovalRequest(string ExpectedVersion,
    IReadOnlyList<SalesDraftPriceFloorExceptionApprovalLineV1> Lines);

/// <summary>GET …/{id}/line-pricing (price read): every line's pricing at the draft's current version.</summary>
public sealed record SalesOrderDraftLinePricingResponse(string Version, IReadOnlyList<SalesDraftLinePricingV1> Lines);

/// <summary>
/// GET …/{id}/lines/{lineId}/price-references (price write): the Product owner's list prices for the line's product in its
/// stock / sales / order unit, in the draft's currency, on the draft's business date. Several lists may answer (no scope
/// priority is decided): the person chooses one.
/// </summary>
public sealed record SalesOrderDraftPriceReferencesResponse(Guid LineId, string CurrencyCode, DateOnly On,
    IReadOnlyList<SalesDraftPriceReferenceV1> Items);

/// <summary>
/// K3 Head B (Sinan 5748258299): POST …/{id}/commercial-revision — the expected concurrency version and
/// EVERY line of the draft exactly once with its new commercial fields. Strict: no other member.
/// </summary>
public sealed record SalesDraftCommercialRevisionRequest(string ExpectedVersion, IReadOnlyList<SalesDraftLineCommercialV1> Lines);

/// <summary>DEC-SAL-002: one line's new ordered quantity in a quantity revision — the client line id and the exact decimal text (the create's shape).</summary>
public sealed record SalesDraftLineQuantityV1(Guid LineId, string Quantity);

/// <summary>
/// DEC-SAL-002 / AT-023: POST …/{id}/quantity-revision — the expected concurrency version, the reason (required; why the order
/// changes) and EVERY line of the draft exactly once with its new ordered quantity in the line's own order unit. Strict: every
/// member present, no other member. The unit, product, price and delivery plan do not change here.
/// </summary>
public sealed record SalesDraftQuantityRevisionRequest(string ExpectedVersion, string Reason, IReadOnlyList<SalesDraftLineQuantityV1> Lines);

/// <summary>DEC-SAL-003: one existing slice's new requested delivery date, addressed by its client slice id.</summary>
public sealed record SalesDraftSliceScheduleV1(Guid SliceId, DateOnly RequestedDeliveryDate);

/// <summary>
/// DEC-SAL-003: one line's new delivery dates — the line's own date (explicit null = none) and a date for EVERY slice the
/// line already carries (an empty list for a line without slices). Dates only: no quantity, unit or new slice.
/// </summary>
public sealed record SalesDraftLineScheduleV1(Guid LineId, DateOnly? RequestedDeliveryDate, IReadOnlyList<SalesDraftSliceScheduleV1> Slices);

/// <summary>
/// DEC-SAL-003: POST …/{id}/delivery-schedule — the expected concurrency version, the header's requested delivery date
/// (explicit null = none) and EVERY line exactly once with its dates. Strict: every member present, no other member.
/// </summary>
public sealed record SalesDraftDeliveryScheduleRequest(
    string ExpectedVersion, DateOnly? RequestedDeliveryDate, IReadOnlyList<SalesDraftLineScheduleV1> Lines);

/// <summary>A slice as it stood in one delivery schedule: identity, (unchanged) quantity text and date.</summary>
public sealed record SalesDraftSliceScheduleViewV1(Guid SliceId, string Quantity, DateOnly RequestedDeliveryDate);

/// <summary>A line's dates in one delivery schedule.</summary>
public sealed record SalesDraftLineScheduleViewV1(Guid LineId, int LineNo, DateOnly? RequestedDeliveryDate,
    IReadOnlyList<SalesDraftSliceScheduleViewV1> Slices);

/// <summary>
/// DEC-SAL-003 ("auditlidir"): one delivery schedule of the draft. Schedule 0 is the draft as created (<c>changedBy</c> and
/// <c>changedAtUtc</c> null: nobody changed it); every later one is a recorded change with its actor and time.
/// </summary>
public sealed record SalesDraftDeliveryScheduleV1(int ScheduleNo, string? ChangedBy, DateTimeOffset? ChangedAtUtc,
    DateOnly? RequestedDeliveryDate, IReadOnlyList<SalesDraftLineScheduleViewV1> Lines);

/// <summary>GET …/{id}/delivery-schedules: schedule 0 then every recorded change, oldest first; under the draft read.</summary>
public sealed record SalesOrderDraftDeliverySchedulesResponse(IReadOnlyList<SalesDraftDeliveryScheduleV1> Items);

/// <summary>
/// K3 P2 (Sinan 5748258299): whether the caller may see a line's commercial fields. NEVER null, and that is the
/// load-bearing part: an absent price is indistinguishable from a price of nothing and from a price the caller is
/// not allowed to see. <see cref="Withheld"/> says "you may not see this" (the price texts are null BECAUSE of it);
/// <see cref="Visible"/> with null texts says "there is none" — an unpriced line. The same argument that made
/// <c>canOpenLocationDetail</c> a required field (#377): an absent answer cannot be told apart from a negative one.
/// Decided by the narrow <c>sales.orders.price.read</c> name — the draft read alone never discloses.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SalesPriceDisclosure>))]
public enum SalesPriceDisclosure { Visible, Withheld }

/// <summary>
/// A line of an archived approved revision, as it stood: identity, quantity text and the commercial fields, verbatim —
/// or, for a caller without the price read, the WITHHELD marker with the three commercial fields null.
/// </summary>
public sealed record SalesDraftRevisionLineV1(
    Guid LineId, int LineNo, Guid ProductId, string ProductVersion, Guid OrderUnitId, string Quantity,
    string? UnitPrice, Guid? PriceUnitId, string? DiscountPercent, SalesPriceDisclosure PriceDisclosure);

/// <summary>
/// An approved revision the draft left behind when its price or discount was changed (K3: the previous
/// approved snapshot is never deleted): who submitted and who approved it, when it was archived and by
/// whom, the money profile it was approved under, and its lines. DEC-SAL-002 (contract delta): <c>reason</c> is why it was
/// superseded — the quantity revision's required reason; null when a price revision (which has no reason member) archived it.
/// </summary>
public sealed record SalesDraftRevisionV1(
    string Revision, string? SubmittedBy, DateTimeOffset? SubmittedAtUtc, string? ApprovedBy, DateTimeOffset? ApprovedAtUtc,
    DateTimeOffset ArchivedAtUtc, string ArchivedBy, Guid MoneyProfileId, string MoneyProfileVersion, int MoneyScale,
    IReadOnlyList<SalesDraftRevisionLineV1> Lines, string? Reason);

/// <summary>GET …/{id}/revisions: the archived approved revisions, oldest first; empty for a draft never revised after an approval.</summary>
public sealed record SalesOrderDraftRevisionsResponse(IReadOnlyList<SalesDraftRevisionV1> Items);

public sealed record SalesOrderDraftLineDetail(
    Guid LineId, int LineNo, Guid ProductId, string ProductVersion,
    string ProductCode, string ProductName, Guid OrderUnitId, string Quantity,
    string? UnitPrice, Guid? PriceUnitId, DateOnly? RequestedDeliveryDate, string? Note,
    IReadOnlyList<SalesDraftDeliverySliceV1>? RequestedDeliverySlices,
    string? DiscountPercent,
    SalesPriceDisclosure PriceDisclosure,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? OrderUnitCode = null);

/// <summary>
/// D2 (Sinan 5749046401): <c>IsOwnDraft</c> is the RELATIONSHIP, computed on the server for the caller — "the caller is
/// this draft's stored creator" — never the creator's identity (which does not travel: the list is the module's widest
/// surface and no screen displays "created by"). The same shape as P2's disclosure marker: computed at response time
/// under the caller's scope, so a replay is the replaying actor's own answer (ADR-011 §3 keeps that the same actor).
/// The screen mirrors it; the server refuses regardless.
/// </summary>
public sealed record SalesOrderDraftDetailView(
    SalesOrderDraftSummaryView Draft, DateOnly? RequestedDeliveryDate, string? Note,
    IReadOnlyList<SalesOrderDraftLineDetail> Lines,
    SalesDraftOwnerReferenceV1? DeliveryAddress,
    SalesDraftOwnerReferenceV1? Contact,
    SalesDraftOwnerReferenceV1? Responsible,
    SalesDraftOwnerReferenceV1? PaymentTerms,
    SalesDraftOwnerReferenceV1? SourceStrategy,
    SalesDraftOwnerReferenceV1? Package,
    SalesDraftApprovalV1 Approval,
    SalesDraftCustomerAcceptanceV1 CustomerAcceptance,
    bool IsOwnDraft,
    bool IsOwnDecisionRestricted);

public sealed record SalesOrderDraftResponse(
    SalesOrderDraftDetailView Detail, bool IsReplay, string OriginalCorrelationId);

public sealed record SalesOrderDraftListResponse(
    IReadOnlyList<SalesOrderDraftSummaryView> Items, string? NextCursor);
/// <param name="ApprovalState">SALES-A1: an exact approval state name to narrow the list to, or null for every draft.</param>
public sealed record SalesOrderDraftListQuery(int PageSize = 50, string? Cursor = null, string? ApprovalState = null);
public sealed record SalesDraftFieldError(string Field, string Code);
public sealed record SalesDraftProblem(int Status, string Title, string? Code = null,
    string? CorrelationId = null, IReadOnlyList<SalesDraftFieldError>? Errors = null);

public static class SalesDraftApiV1
{
    public const string DraftPath = "/api/v1/sales/drafts";
    public const string DraftDetailPath = "/api/v1/sales/drafts/{id}";
    public const string IdempotencyHeader = "Idempotency-Key";
    public const string Rev0 = "Rev.0";
    /// <summary>
    /// The first commit and its replay both answer 201 (ADR-011 §4: the persisted result is replayed;
    /// the platform's Counterparty and Manufacturing commands do the same). A replay is told by the
    /// <c>Idempotency-Replayed</c> header and <c>isReplay</c> in the body — a #257 delta (the contract
    /// said 200 for the replay), decided with the coordinator on #321.
    /// </summary>
    public const int CreatedStatus = 201;

    // SALES-A1: the internal-approval commands. Each answers the draft after the transition with 200
    // (the command's result IS the detail view; the Counterparty / Manufacturing pattern), and the
    // same key again answers 200 with `Idempotency-Replayed` and the first correlation (ADR-011 §4).
    public const string SubmissionPath = "/api/v1/sales/drafts/{id}/submission";
    public const string ApprovalPath = "/api/v1/sales/drafts/{id}/approval";
    public const string RejectionPath = "/api/v1/sales/drafts/{id}/rejection";
    public const string CustomerAcceptancePath = "/api/v1/sales/drafts/{id}/customer-acceptance";

    /// <summary>D2: POST …/{id}/withdrawal — the preparer takes back their own not-yet-approved draft (own state, own trail, no deletion).</summary>
    public const string WithdrawalPath = "/api/v1/sales/drafts/{id}/withdrawal";
    public const int StateCommandStatus = 200;

    // K3 Head B: the commercial revision (200 with the detail after the change; same-key replay 200 +
    // `Idempotency-Replayed`) and the read of the archived approved revisions (200; a draft never revised
    // after an approval answers an empty list). The detail wire itself is unchanged: `revision` reads
    // "Rev.N", and the history lives on its own route so the S-04 decoder (exact members) keeps working.
    public const string CommercialRevisionPath = "/api/v1/sales/drafts/{id}/commercial-revision";
    public const string RevisionsPath = "/api/v1/sales/drafts/{id}/revisions";

    // AT-018 / AT-021: the line pricing read (price read) and the line's list-price references (price write). The detail
    // wire is unchanged: pricing lives on its own route so the S-04 decoder (exact members) keeps working.
    public const string LinePricingPath = "/api/v1/sales/drafts/{id}/line-pricing";
    public const string PriceReferencesPath = "/api/v1/sales/drafts/{id}/lines/{lineId}/price-references";

    // DEC-SAL-002 / AT-023: the quantity revision (200 with the detail after the change; same-key replay 200 +
    // `Idempotency-Replayed`). The detail wire is unchanged (the line's `quantity` carries the new text); the superseded
    // approved revision, with its reason, is read on the revisions route.
    public const string QuantityRevisionPath = "/api/v1/sales/drafts/{id}/quantity-revision";

    // DEC-SAL-003: the delivery-schedule update (200 with the detail after the change; same-key replay 200 +
    // `Idempotency-Replayed`) and the read of the schedule history. The detail wire is unchanged: it carries the
    // CURRENT dates; the history lives on its own route so the S-04 decoder (exact members) keeps working.
    public const string DeliverySchedulePath = "/api/v1/sales/drafts/{id}/delivery-schedule";
    public const string DeliverySchedulesPath = "/api/v1/sales/drafts/{id}/delivery-schedules";

    // DEC-SAL-005: Logistics-owned loading-instruction facts per Sales line, read under the draft-read authority.
    public const string LoadingProgressPath = "/api/v1/sales/drafts/{id}/loading-progress";

    // DEC-SAL-005 / AT-007: the impact preview (GET, the cancel permission), the cancel command (POST, 200 with the detail
    // after the change; same-key replay 200 + `Idempotency-Replayed`) and the recorded cancellation (GET, the draft read).
    // The detail wire is unchanged apart from the lifecycle `state` ("Cancelled"): the facts live on their own route so the
    // S-04 decoder (exact members) keeps working.
    public const string CancellationImpactPath = "/api/v1/sales/drafts/{id}/cancellation-impact";
    public const string CancellationPath = "/api/v1/sales/drafts/{id}/cancellation";

    // Partial delivery (user decision 2026-09-30): the remainder change of a partly delivered line — requested (POST, the
    // quantity-write name), approved or rejected by an approver (POST, the approve name; 200 with the detail after the step,
    // same-key replay 200 + `Idempotency-Replayed`), and the lines' remainder facts with every recorded change (GET, the draft
    // read). The detail wire is unchanged: the line's `quantity` carries the approved quantity.
    public const string RemainderChangesPath = "/api/v1/sales/drafts/{id}/remainder-changes";
    public const string RemainderApprovalPath = "/api/v1/sales/drafts/{id}/remainder-changes/{changeNo}/approval";
    public const string RemainderRejectionPath = "/api/v1/sales/drafts/{id}/remainder-changes/{changeNo}/rejection";

    /// <summary>The wire text of a revision number (<c>Rev.0</c>, <c>Rev.1</c>, …).</summary>
    public static string RevisionText(int revision) => $"Rev.{revision.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}

// Shape validation only. Owner profile scale, current permissions and references
// must be checked by the future command implementation before any write.
public static class SalesDraftJson
{
    public static JsonSerializerOptions Options { get; } = BuildOptions();

    private static JsonSerializerOptions BuildOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            NumberHandling = JsonNumberHandling.Strict,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true,
            RespectNullableAnnotations = true
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    public static CreateSalesOrderDraftRequest ReadCreate(string json)
    {
        var request = JsonSerializer.Deserialize<CreateSalesOrderDraftRequest>(json, Options)
            ?? throw new JsonException("Request body is required.");
        if (request.Kind != "Order" || request.PartyId == Guid.Empty
            || request.CompanyAccountId == Guid.Empty || request.CurrencyId == Guid.Empty
            || request.Lines.Count is < 1 or > 500 || request.Note?.Length > 4000)
            throw new JsonException("Invalid draft fields.");
        var ids = new HashSet<Guid>();
        foreach (var reference in new[] { request.DeliveryAddress, request.Contact,
            request.Responsible, request.PaymentTerms, request.SourceStrategy, request.Package })
        {
            if (reference is not null && (reference.Id == Guid.Empty
                || string.IsNullOrWhiteSpace(reference.ExpectedVersion)
                || reference.ExpectedVersion.Length > 128))
                throw new JsonException("Invalid owner reference.");
        }
        foreach (var line in request.Lines)
        {
            if (line is null || line.LineId == Guid.Empty || !ids.Add(line.LineId)
                || line.ProductId == Guid.Empty || line.OrderUnitId == Guid.Empty
                || string.IsNullOrWhiteSpace(line.ExpectedProductVersion)
                || line.ExpectedProductVersion.Length > 128 || line.Note?.Length > 4000
                || line.PriceUnitId == Guid.Empty
                || (line.UnitPrice is null) != (line.PriceUnitId is null))
                throw new JsonException("Invalid line fields.");
            RequireDecimalShape(line.Quantity, positive: true);
            if (line.UnitPrice is not null) RequireDecimalShape(line.UnitPrice, positive: false);
            if (line.DiscountPercent is not null) RequireDecimalShape(line.DiscountPercent, positive: false);
            SalesDraftDeliveryPlanV1.GetUnscheduledQuantity(line);
        }
        return request;
    }

    /// <summary>
    /// K3 Head B: the strict body of a commercial revision — the expected version as on every state
    /// command, and 1..500 lines each with a client line id given once; the commercial shapes are the
    /// create's (price↔price-unit pairing, plain decimal text). The domain decides the rest (every current
    /// line named, scale, unit source, range, no-change).
    /// </summary>
    public static SalesDraftCommercialRevisionRequest ReadCommercialRevision(string json)
    {
        var request = JsonSerializer.Deserialize<SalesDraftCommercialRevisionRequest>(json, Options)
            ?? throw new JsonException("Request body is required.");
        if (string.IsNullOrEmpty(request.ExpectedVersion) || request.ExpectedVersion.Length > 19
            || !Regex.IsMatch(request.ExpectedVersion, "\\A[1-9][0-9]*\\z"))
            throw new JsonException("Invalid expected version.");
        if (request.Lines is null || request.Lines.Count is < 1 or > 500)
            throw new JsonException("Invalid revision lines.");
        var ids = new HashSet<Guid>();
        foreach (var line in request.Lines)
        {
            if (line is null || line.LineId == Guid.Empty || !ids.Add(line.LineId) || line.PriceUnitId == Guid.Empty
                || (line.UnitPrice is null) != (line.PriceUnitId is null))
                throw new JsonException("Invalid line fields.");
            if (line.UnitPrice is not null) RequireDecimalShape(line.UnitPrice, positive: false);
            if (line.DiscountPercent is not null) RequireDecimalShape(line.DiscountPercent, positive: false);
            // AT-018: the reference names a list version (ids only), the amounts and the surcharge percent are plain decimal text.
            if (line.PriceReference is { } reference && (reference.PriceListId == Guid.Empty || reference.VersionNo < 1))
                throw new JsonException("Invalid price reference.");
            if (line.DiscountAmount is not null) RequireDecimalShape(line.DiscountAmount, positive: false);
            if (line.SurchargePercent is not null) RequireDecimalShape(line.SurchargePercent, positive: false);
            if (line.SurchargeAmount is not null) RequireDecimalShape(line.SurchargeAmount, positive: false);
        }

        return request;
    }

    /// <summary>
    /// DEC-SAL-002: the strict body of a quantity revision — the expected version as on every state command, the reason
    /// present (at most 4000; the domain requires it non-blank) and 1..500 lines each named once with a positive plain decimal
    /// quantity text (the create's shape). The domain decides the rest (every current line named, Ö2 in the line's unit, the
    /// delivery plan still fitting, no-change).
    /// </summary>
    public static SalesDraftQuantityRevisionRequest ReadQuantityRevision(string json)
    {
        var request = JsonSerializer.Deserialize<SalesDraftQuantityRevisionRequest>(json, Options)
            ?? throw new JsonException("Request body is required.");
        if (string.IsNullOrEmpty(request.ExpectedVersion) || request.ExpectedVersion.Length > 19
            || !Regex.IsMatch(request.ExpectedVersion, "\\A[1-9][0-9]*\\z"))
            throw new JsonException("Invalid expected version.");
        if (request.Reason is null || request.Reason.Length > 4000)
            throw new JsonException("Invalid reason.");
        if (request.Lines is null || request.Lines.Count is < 1 or > 500)
            throw new JsonException("Invalid revision lines.");
        var ids = new HashSet<Guid>();
        foreach (var line in request.Lines)
        {
            if (line is null || line.LineId == Guid.Empty || !ids.Add(line.LineId))
                throw new JsonException("Invalid line fields.");
            RequireDecimalShape(line.Quantity, positive: true);
        }

        return request;
    }

    /// <summary>
    /// DEC-SAL-003: the strict body of a delivery-schedule update — the expected version as on every state command, the
    /// header date present (null allowed), and 1..500 lines each named once, each with its slices named once (at most
    /// 500). The domain decides the rest (every current line and exactly its current slices, the no-change refusal).
    /// </summary>
    public static SalesDraftDeliveryScheduleRequest ReadDeliverySchedule(string json)
    {
        var request = JsonSerializer.Deserialize<SalesDraftDeliveryScheduleRequest>(json, Options)
            ?? throw new JsonException("Request body is required.");
        if (string.IsNullOrEmpty(request.ExpectedVersion) || request.ExpectedVersion.Length > 19
            || !Regex.IsMatch(request.ExpectedVersion, "\\A[1-9][0-9]*\\z"))
            throw new JsonException("Invalid expected version.");
        if (request.Lines is null || request.Lines.Count is < 1 or > 500)
            throw new JsonException("Invalid schedule lines.");
        var ids = new HashSet<Guid>();
        foreach (var line in request.Lines)
        {
            if (line is null || line.LineId == Guid.Empty || !ids.Add(line.LineId) || line.Slices is null || line.Slices.Count > 500)
                throw new JsonException("Invalid schedule line.");
            var sliceIds = new HashSet<Guid>();
            foreach (var slice in line.Slices)
            {
                if (slice is null || slice.SliceId == Guid.Empty || !sliceIds.Add(slice.SliceId))
                    throw new JsonException("Invalid schedule slice.");
            }
        }

        return request;
    }

    /// <summary>
    /// SALES-A1: the strict body of a state command. <c>expectedVersion</c> is required, positive
    /// integer text (as every version on this wire, at most 19 digits); <c>reason</c> is accepted
    /// only where <paramref name="allowReason"/> (the rejection) and is otherwise an unknown member.
    /// The reason's length is the domain's rule (422), not a shape rule.
    /// </summary>
    /// <summary>
    /// DEC-SAL-005: the strict cancel body — the expected version (canonical positive integer text), the reason (present, at
    /// most 4000; the domain requires it non-blank) and the preview token (non-blank, at most 128). Anything else is a shape
    /// error.
    /// </summary>
    public static SalesDraftCancelRequest ReadCancel(string json)
    {
        var request = JsonSerializer.Deserialize<SalesDraftCancelRequest>(json, Options)
            ?? throw new JsonException("Request body is required.");
        if (string.IsNullOrEmpty(request.ExpectedVersion) || request.ExpectedVersion.Length > 19
            || !Regex.IsMatch(request.ExpectedVersion, "\\A[1-9][0-9]*\\z"))
            throw new JsonException("Invalid expected version.");
        if (request.Reason is null || request.Reason.Length > 4000)
            throw new JsonException("Invalid reason.");
        if (string.IsNullOrWhiteSpace(request.ImpactToken) || request.ImpactToken.Length > 128)
            throw new JsonException("Invalid impact token.");
        return request;
    }

    public static SalesDraftStateCommandRequest ReadStateCommand(string json, bool allowReason)
    {
        var request = JsonSerializer.Deserialize<SalesDraftStateCommandRequest>(json, Options)
            ?? throw new JsonException("Request body is required.");
        if (string.IsNullOrEmpty(request.ExpectedVersion) || request.ExpectedVersion.Length > 19
            || !Regex.IsMatch(request.ExpectedVersion, "\\A[1-9][0-9]*\\z"))
            throw new JsonException("Invalid expected version.");
        if (!allowReason && request.Reason is not null)
            throw new JsonException("A reason is only given with a rejection.");
        return request;
    }

    // No decimal conversion or default scale. Settings ExactPositiveDecimalV1
    // validates quantity against the actual owner-approved scale at command time.
    internal static void RequireDecimalShape(string text, bool positive)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 128
            || !Regex.IsMatch(text, "\\A[0-9]+(?:\\.[0-9]+)?\\z")
            || (positive && !text.Any(c => c is >= '1' and <= '9')))
            throw new JsonException("Invalid decimal text.");
    }
}

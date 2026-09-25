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

// SALES-A1 (contract delta): `approvalState` is the internal approval axis — "Preparing",
// "PendingApproval", "Approved", "Rejected" or (D2) "Withdrawn" — beside the lifecycle `state` ("Draft"), the
// commercial `revision` and the concurrency `version`. Four separate facts, never merged.
public sealed record SalesOrderDraftSummaryView(
    Guid Id, Guid CompanyId, string DocumentNo, string Kind, string State,
    string Version, string Revision, Guid PartyId, Guid CompanyAccountId,
    Guid CurrencyId, DateOnly BusinessDate, DateTimeOffset CreatedAtUtc, int LineCount,
    string ApprovalState);

/// <summary>SALES-A1: the approval facts of the detail; every member null until it happened (ADR-009 audit facts, shown, never derived).</summary>
public sealed record SalesDraftApprovalV1(
    string? SubmittedBy, DateTimeOffset? SubmittedAtUtc,
    string? DecidedBy, DateTimeOffset? DecidedAtUtc,
    string? RejectionReason);

/// <summary>
/// SALES-A1: the body of a state command (submission / approval / rejection): the expected
/// concurrency version as text (the same shape as every version on this wire) and, for a
/// rejection only, the optional reason. Strict: no other member, no missing version.
/// </summary>
public sealed record SalesDraftStateCommandRequest(string ExpectedVersion, string? Reason = null);

/// <summary>K3 Head B: one line's commercial fields in a commercial revision — the client line id and the three fields (all nullable, as on the create).</summary>
public sealed record SalesDraftLineCommercialV1(Guid LineId, string? UnitPrice = null, Guid? PriceUnitId = null, string? DiscountPercent = null);

/// <summary>
/// K3 Head B (Sinan 5748258299): POST …/{id}/commercial-revision — the expected concurrency version and
/// EVERY line of the draft exactly once with its new commercial fields. Strict: no other member.
/// </summary>
public sealed record SalesDraftCommercialRevisionRequest(string ExpectedVersion, IReadOnlyList<SalesDraftLineCommercialV1> Lines);

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
/// whom, the money profile it was approved under, and its lines.
/// </summary>
public sealed record SalesDraftRevisionV1(
    string Revision, string? SubmittedBy, DateTimeOffset? SubmittedAtUtc, string? ApprovedBy, DateTimeOffset? ApprovedAtUtc,
    DateTimeOffset ArchivedAtUtc, string ArchivedBy, Guid MoneyProfileId, string MoneyProfileVersion, int MoneyScale,
    IReadOnlyList<SalesDraftRevisionLineV1> Lines);

/// <summary>GET …/{id}/revisions: the archived approved revisions, oldest first; empty for a draft never revised after an approval.</summary>
public sealed record SalesOrderDraftRevisionsResponse(IReadOnlyList<SalesDraftRevisionV1> Items);

public sealed record SalesOrderDraftLineDetail(
    Guid LineId, int LineNo, Guid ProductId, string ProductVersion,
    string ProductCode, string ProductName, Guid OrderUnitId, string Quantity,
    string? UnitPrice, Guid? PriceUnitId, DateOnly? RequestedDeliveryDate, string? Note,
    IReadOnlyList<SalesDraftDeliverySliceV1>? RequestedDeliverySlices,
    string? DiscountPercent,
    SalesPriceDisclosure PriceDisclosure);

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

    /// <summary>D2: POST …/{id}/withdrawal — the preparer takes back their own not-yet-approved draft (own state, own trail, no deletion).</summary>
    public const string WithdrawalPath = "/api/v1/sales/drafts/{id}/withdrawal";
    public const int StateCommandStatus = 200;

    // K3 Head B: the commercial revision (200 with the detail after the change; same-key replay 200 +
    // `Idempotency-Replayed`) and the read of the archived approved revisions (200; a draft never revised
    // after an approval answers an empty list). The detail wire itself is unchanged: `revision` reads
    // "Rev.N", and the history lives on its own route so the S-04 decoder (exact members) keeps working.
    public const string CommercialRevisionPath = "/api/v1/sales/drafts/{id}/commercial-revision";
    public const string RevisionsPath = "/api/v1/sales/drafts/{id}/revisions";

    // DEC-SAL-003: the delivery-schedule update (200 with the detail after the change; same-key replay 200 +
    // `Idempotency-Replayed`) and the read of the schedule history. The detail wire is unchanged: it carries the
    // CURRENT dates; the history lives on its own route so the S-04 decoder (exact members) keeps working.
    public const string DeliverySchedulePath = "/api/v1/sales/drafts/{id}/delivery-schedule";
    public const string DeliverySchedulesPath = "/api/v1/sales/drafts/{id}/delivery-schedules";

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

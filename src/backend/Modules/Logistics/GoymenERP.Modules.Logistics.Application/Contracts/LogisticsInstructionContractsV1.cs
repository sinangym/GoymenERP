using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GoymenERP.Modules.Logistics.Application.Contracts;

// ---- requests (strict wire shapes; LogisticsJson validates the shape, the service the meaning) ----

/// <summary>
/// POST /api/v1/logistics/loading-instructions. The customer is the Cari party + its company account
/// (LOG-2); every line names the APPROVED sales order line it serves (LOG-4 D4: the Sales owner answers
/// the line's product and customer; the client sends no product), the Settings unit and the exact
/// quantity text (Ö2). <paramref name="EffectiveOn"/> is the business date the unit profile is read for.
/// </summary>
public sealed record CreateLoadingInstructionRequest(
    Guid CustomerPartyId,
    Guid CompanyAccountId,
    DateOnly EffectiveOn,
    IReadOnlyList<LoadingInstructionLineRequest> Lines);

public sealed record LoadingInstructionLineRequest(
    Guid LineId,
    Guid SourceOrderId,
    Guid SourceOrderLineId,
    Guid UnitId,
    string Quantity);

/// <summary>POST …/{id}/issue: Draft → Issued at the expected version.</summary>
public sealed record IssueLoadingInstructionRequest(string ExpectedVersion);

/// <summary>
/// POST …/{id}/lines/{lineId}/loads: one loaded share against a line, with how it was measured
/// (<c>Measured</c> | <c>AllocatedFromVehicleWeight</c>). The load id is the client's (one load is one
/// record); the quantity is exact text (Ö2).
/// </summary>
public sealed record RecordLoadRequest(string ExpectedVersion, Guid LoadId, string Quantity, string Measurement);

/// <summary>
/// POST …/{id}/weighings (AT-053): the vehicle's manual weighbridge record for an issued instruction — the two
/// weighings as exact kilogram text in the order they were taken and the direction (<c>TareFirst</c> |
/// <c>GrossFirst</c>) that says which one is the empty vehicle. The weighing id is the client's (one record per
/// snapshot). No net, difference, tolerance, operator or time is sent: the backend calculates and stamps them.
/// </summary>
public sealed record RecordWeighingRequest(string ExpectedVersion, Guid WeighingId, string Direction, string FirstWeighingKg,
    string SecondWeighingKg);

/// <summary>
/// POST …/{id}/dispatch-notes (AT-055): a dispatch-note (irsaliye) DRAFT from definite shipments of an issued instruction — the
/// shipment ids to put on it (each a recorded shipment of this instruction, named once, not on a non-cancelled dispatch note).
/// The draft id is the client's (one note per id, never reused in the company). No quantity, price, number, date or customer is
/// sent: each line is the recorded shipped quantity, the customer is the instruction's, and the backend stamps the preparer and
/// the time.
/// </summary>
public sealed record CreateDispatchNoteRequest(string ExpectedVersion, Guid DispatchNoteId, IReadOnlyList<Guid> ShipmentIds);

/// <summary>
/// POST …/{id}/dispatch-notes/{dispatchNoteId}/approve (AT-055, user decision 2026-09-30): the draft becomes Approved and gets
/// the company's next system number (series + gap-free sequence) in the same save. The instruction version is the precondition.
/// </summary>
public sealed record ApproveDispatchNoteRequest(string ExpectedVersion);

/// <summary>
/// POST …/{id}/dispatch-notes/{dispatchNoteId}/cancel (AT-055, user decision 2026-09-30): a draft or an approved note is
/// cancelled — never deleted — with the reason (trimmed text, 1–500 characters); its shipments are released for a new note.
/// </summary>
public sealed record CancelDispatchNoteRequest(string ExpectedVersion, string Reason);

/// <summary>
/// POST …/{id}/dispatch-notes/{dispatchNoteId}/e-dispatch-number (AT-055, user decision 2026-09-30): the official e-İrsaliye
/// number of an approved note, entered by the user (a later integration writes the same field); the GİB shape
/// (<see cref="LogisticsJson.EDispatchNumberPattern"/>), set once, unique per company, kept apart from the system number.
/// </summary>
public sealed record SetEDispatchNumberRequest(string ExpectedVersion, string EDispatchNumber);

// ---- views --------------------------------------------------------------------------------------

public sealed record LoadingInstructionSummaryView(
    Guid Id, Guid CompanyId, string Status, string Version, Guid CustomerPartyId, Guid CompanyAccountId,
    DateOnly EffectiveOn, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, int LineCount);

/// <summary>The four shares of a line as exact text (§7.4), plus the derived remainders; the source order binding (D4).</summary>
public sealed record LoadingInstructionLineView(
    Guid LineId, int LineNo, Guid SourceOrderId, Guid SourceOrderLineId, int SourceOrderRevision,
    Guid ProductId, Guid UnitId,
    string Instructed, string Loaded, string Shipped, string Delivered,
    string RemainingToLoad, string LoadedNotShipped, string ShippedNotDelivered, bool IsBlocked);

public sealed record LoadingShareView(
    Guid ShareId, Guid LineId, string Kind, string Quantity, string? Measurement, Guid? SourceShipmentId, DateTimeOffset RecordedAtUtc);

/// <summary>
/// The instruction's manual weighbridge record (AT-053) as exact kilogram text: the two weighings as entered, the
/// direction, tare / gross / net (WGT-001, the backend's calculation), the kilograms loaded on the instruction when
/// it was weighed and the signed difference net − loaded (WGT-002; "-" when lighter). Facts only — no tolerance
/// verdict exists (OPEN-004). <paramref name="RecordedBy"/> is the operator's user id.
/// </summary>
public sealed record LoadingInstructionWeighingView(
    Guid WeighingId, string Direction, string FirstWeighingKg, string SecondWeighingKg, string TareKg, string GrossKg, string NetKg,
    string LoadedKgAtWeighing, string DifferenceKg, string RecordedBy, DateTimeOffset RecordedAtUtc);

/// <summary>
/// One line of a dispatch-note draft (AT-055): the definite shipment it documents, that shipment's instruction line and its
/// Sales order binding (D4), the product and unit identities, and the SHIPPED quantity as exact text (Ö2). Price-free.
/// </summary>
public sealed record DispatchNoteLineView(
    int LineNo, Guid ShipmentId, Guid InstructionLineId, Guid SourceOrderId, Guid SourceOrderLineId, Guid ProductId, Guid UnitId, string Quantity);

/// <summary>
/// A dispatch note of the instruction (AT-055). <paramref name="Status"/> is "Draft" | "Approved" | "Cancelled".
/// <paramref name="Number"/> is the system (ERP) number given on approval ("IRS-000001"; null for a draft and for a draft
/// cancelled before approval; a cancelled note keeps it). <paramref name="EDispatchNumber"/> is the official e-İrsaliye number
/// entered on an approved note, apart from the system number. Actors are user ids; nothing is ever deleted.
/// </summary>
public sealed record DispatchNoteView(Guid DispatchNoteId, string Status, string? Number, string CreatedBy, DateTimeOffset CreatedAtUtc,
    string? ApprovedBy, DateTimeOffset? ApprovedAtUtc, string? CancelledBy, DateTimeOffset? CancelledAtUtc, string? CancelReason,
    string? EDispatchNumber, IReadOnlyList<DispatchNoteLineView> Lines);

/// <param name="Weighing">The manual weighbridge record, or null while none is recorded (always present on the wire).</param>
/// <param name="DispatchNotes">The dispatch notes prepared from this instruction's shipments, oldest first, cancelled ones included (always present; empty when none).</param>
public sealed record LoadingInstructionDetailView(
    LoadingInstructionSummaryView Instruction,
    IReadOnlyList<LoadingInstructionLineView> Lines,
    IReadOnlyList<LoadingShareView> Shares,
    LoadingInstructionWeighingView? Weighing,
    IReadOnlyList<DispatchNoteView> DispatchNotes);

/// <summary>
/// AT-055, the Sales order view's link (S-04): the dispatch-note drafts that document shipments of one Sales order, with only
/// that order's lines. Read under the instruction read; creates nothing. <paramref name="HasMore"/> says the list stopped at its
/// cap (never a silent cut).
/// </summary>
public sealed record SalesOrderDispatchNotesView(Guid SalesOrderId, IReadOnlyList<SalesOrderDispatchNoteView> Items, bool HasMore);

/// <summary>One dispatch note on the asked order: its state, its system number and e-İrsaliye number when present, and this order's lines.</summary>
public sealed record SalesOrderDispatchNoteView(Guid DispatchNoteId, Guid InstructionId, string Status, string? Number, string? EDispatchNumber,
    DateTimeOffset CreatedAtUtc, IReadOnlyList<SalesOrderDispatchNoteLineView> Lines);

/// <summary>One line of the draft on the asked order: the order line, the shipment and the shipped quantity (exact text) in its unit.</summary>
public sealed record SalesOrderDispatchNoteLineView(int LineNo, Guid SourceOrderLineId, Guid ShipmentId, Guid UnitId, string Quantity);

public sealed record LoadingInstructionResponse(LoadingInstructionDetailView Detail, bool IsReplay, string OriginalCorrelationId);

public sealed record LoadingInstructionListResponse(IReadOnlyList<LoadingInstructionSummaryView> Items, string? NextCursor);

/// <param name="Status">An exact status name ("Draft" | "Issued") to narrow the list to, or null.</param>
public sealed record LoadingInstructionListQuery(int PageSize = 50, string? Cursor = null, string? Status = null);

public sealed record LogisticsFieldError(string Field, string Code);

/// <summary>
/// LOG-4b: one APPROVED sales order line offered for loading (the price-free picker, Sinan §4 #66 5738113623:
/// the Sevkiyatçı sees orders, never prices — deny-first, so no price, discount, currency or payment member
/// exists here; a contract test pins the member names). Read under the create name; the Sales owner answers.
/// </summary>
/// <param name="CustomerName">The Cari party's presentation name (trade name, else legal name) through the Logistics party-account port — Business 5745076917: a customer is shown by name, never by a technical id.</param>
public sealed record LogisticsApprovedOrderLineView(
    Guid OrderId, string DocumentNo, Guid LineId, int LineNo, Guid CustomerPartyId, Guid CompanyAccountId, string CustomerName,
    Guid ProductId, string ProductVersion, string ProductCode, string ProductName, Guid UnitId, string UnitCode,
    string Quantity, DateOnly? RequestedDeliveryDate, int Revision);

public sealed record LogisticsApprovedOrderLinePage(IReadOnlyList<LogisticsApprovedOrderLineView> Items, string? NextCursor);

public sealed record LogisticsApprovedOrderLinesQuery(int PageSize = 50, string? Cursor = null);

// ---- problems -----------------------------------------------------------------------------------

/// <summary>The problem codes the loading-instruction routes answer beside the domain's rule codes (LOG-4a contract).</summary>
public static class LogisticsProblemCodesV1
{
    /// <summary>400: the Idempotency-Key header is missing, repeated, blank, non-canonical, over 128 characters or malformed.</summary>
    public const string CommandMetadataInvalid = "command_metadata_invalid";

    public const string InvalidCursor = "invalid_cursor";

    /// <summary>409: the customer account is not the party's, not this company's, or not active (the Sales pattern).</summary>
    public const string AccountState = "account_state";

    /// <summary>409 (D4, fail-closed): the sales order line is not APPROVED, not this customer's, or unknown in this company; field errors say which.</summary>
    public const string OrderLineState = "order_line_state";

    /// <summary>409, terminal (§7): the expected version is stale.</summary>
    public const string VersionConflict = "version_conflict";

    /// <summary>409, terminal (§7): the instruction is not in the state the command needs (a Draft cannot take a load; an Issued one cannot be issued again).</summary>
    public const string InstructionState = "logistics_instruction_state";

    /// <summary>409, terminal (§7, D5): the loaded share would exceed the instructed remainder (§13 cap).</summary>
    public const string LoadExceedsInstructed = "logistics_load_exceeds_instructed";

    /// <summary>409: all loading instructions of the Sales line would exceed its owner-resolved OPEN-004 maximum.</summary>
    public const string OrderLineAggregateLimitExceeded = "logistics_order_line_aggregate_limit_exceeded";

    /// <summary>409, terminal (§7, D2): the line is blocked; no new load.</summary>
    public const string LineBlocked = "logistics_line_blocked";

    /// <summary>409, terminal (§7): the same load id was already recorded on this instruction under another key.</summary>
    public const string DuplicateShare = "logistics_duplicate_share_id";

    /// <summary>409, terminal (§7; AT-053): the instruction already has its weighbridge record; the correction rule is undecided, so a second is refused.</summary>
    public const string WeighingAlreadyRecorded = "logistics_weighing_already_recorded";

    /// <summary>422 (AT-053): the two weighings contradict the declared direction (the gross must be heavier than the tare).</summary>
    public const string WeighingDirectionInconsistent = "logistics_weighing_direction_inconsistent";

    /// <summary>409, terminal (§7; AT-055): a named shipment is already on a non-cancelled dispatch note; a shipment is on at most one.</summary>
    public const string ShipmentAlreadyDispatched = "logistics_shipment_already_dispatched";

    /// <summary>409, terminal (§7): the note id is already a dispatch note of this company — of this or another instruction, even a cancelled one.</summary>
    public const string DuplicateDispatchNote = "logistics_duplicate_dispatch_note_id";

    /// <summary>
    /// 409, terminal (§7; AT-055 lifecycle): the dispatch note is not in the state the command needs — approve needs a Draft,
    /// cancel a Draft or Approved note, the e-İrsaliye number an Approved note without one.
    /// </summary>
    public const string DispatchNoteState = "logistics_dispatch_note_state";

    /// <summary>409, terminal (§7): the e-İrsaliye number is already on another dispatch note of this company.</summary>
    public const string EDispatchNumberInUse = "logistics_e_dispatch_number_in_use";

    /// <summary>422 (AT-055): a named id is not a recorded definite shipment of this instruction.</summary>
    public const string ShipmentNotFound = "logistics_shipment_not_found";

    public const string SameKeyPayloadMismatch = "same_key_payload_mismatch";
    public const string ConcurrentChange = "concurrent_change";

    /// <summary>422: the unit is not an approved kilogram profile for this company on the date, or the quantity's scale exceeds it (Ö2).</summary>
    public const string UnsupportedUnit = "unsupported_unit";

    /// <summary>503 (never on the wire): the Cari account owner cannot answer.</summary>
    public const string CounterpartyProviderUnavailable = "counterparty_provider_unavailable";

    /// <summary>503 (never on the wire): the Sales line reference owner cannot answer.</summary>
    public const string SalesProviderUnavailable = "sales_provider_unavailable";

    /// <summary>503 (never on the wire): the Purchasing open-line owner cannot answer (AT-025, the purchase section).</summary>
    public const string PurchasingProviderUnavailable = "purchasing_provider_unavailable";

    /// <summary>503 (never on the wire): an owner answered a malformed value.</summary>
    public const string ReferenceProviderInvalid = "reference_provider_invalid";

    /// <summary>503 (never on the wire): a Logistics permission name is not in the central catalogue yet.</summary>
    public const string PermissionUnregistered = "permission_unregistered";
}

public sealed class LogisticsInstructionServiceException(int status, string code,
    IReadOnlyList<LogisticsFieldError>? errors = null, bool isReplay = false, string? originalCorrelationId = null)
    : Exception($"{status} {code}")
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public IReadOnlyList<LogisticsFieldError> Errors { get; } = errors ?? [];
    public bool IsReplay { get; } = isReplay;
    public string? OriginalCorrelationId { get; } = originalCorrelationId;
}

/// <summary>The permission names the service measures (D6); null = not bound → 503 setup problem, never a grant.</summary>
public sealed record LogisticsInstructionServiceBindings(
    string? CreatePermission = LogisticsPermissionManifestV1.InstructionsCreate,
    string? ReadPermission = LogisticsPermissionManifestV1.InstructionsRead,
    string? RecordPermission = LogisticsPermissionManifestV1.LoadsRecord,
    string? ShipmentPermission = LogisticsPermissionManifestV1.ShipmentsRecord,
    string? DeliveryPermission = LogisticsPermissionManifestV1.DeliveriesRecord,
    string? WeighingPermission = LogisticsPermissionManifestV1.WeighingsRecord,
    string? DispatchNotePermission = LogisticsPermissionManifestV1.DispatchNotesCreate,
    string? DispatchNoteApprovePermission = LogisticsPermissionManifestV1.DispatchNotesApprove,
    string? DispatchNoteCancelPermission = LogisticsPermissionManifestV1.DispatchNotesCancel);

/// <summary>Constructed by the server, never bound from the request body.</summary>
public sealed record LogisticsCommandMetadata(string IdempotencyKey, string CorrelationId, string? CausationId, DateTimeOffset RequestedAtUtc);

public static class LogisticsApiV1
{
    public const string InstructionsPath = "/api/v1/logistics/loading-instructions";
    public const string InstructionDetailPath = "/api/v1/logistics/loading-instructions/{id}";
    public const string IssuePath = "/api/v1/logistics/loading-instructions/{id}/issue";
    public const string LoadsPath = "/api/v1/logistics/loading-instructions/{id}/lines/{lineId}/loads";

    /// <summary>AT-053: the instruction's manual weighbridge record (one per instruction).</summary>
    public const string WeighingsPath = "/api/v1/logistics/loading-instructions/{id}/weighings";

    /// <summary>AT-055: a dispatch-note draft from the instruction's definite shipments.</summary>
    public const string DispatchNotesPath = "/api/v1/logistics/loading-instructions/{id}/dispatch-notes";

    /// <summary>AT-055: approve a draft (the system number is assigned).</summary>
    public const string ApproveDispatchNotePath = "/api/v1/logistics/loading-instructions/{id}/dispatch-notes/{dispatchNoteId}/approve";

    /// <summary>AT-055: cancel a draft or an approved note (its shipments are released).</summary>
    public const string CancelDispatchNotePath = "/api/v1/logistics/loading-instructions/{id}/dispatch-notes/{dispatchNoteId}/cancel";

    /// <summary>AT-055: record the official e-İrsaliye number of an approved note.</summary>
    public const string EDispatchNumberPath = "/api/v1/logistics/loading-instructions/{id}/dispatch-notes/{dispatchNoteId}/e-dispatch-number";

    /// <summary>AT-055: the dispatch notes of one Sales order (query <c>salesOrderId</c>), for the Sales order view's link.</summary>
    public const string SalesOrderDispatchNotesPath = "/api/v1/logistics/dispatch-notes";

    /// <summary>LOG-4b: the price-free approved-order-lines read for the picker (the create name).</summary>
    public const string ApprovedOrderLinesPath = "/api/v1/logistics/loading-instructions/approved-order-lines";
    public const string IdempotencyHeader = "Idempotency-Key";

    /// <summary>The first commit and its replay both answer 201 (ADR-011 §4; the Sales #321 decision), told apart by the replay headers.</summary>
    public const int CreatedStatus = 201;

    /// <summary>A mutation answers the instruction after the transition (200); the same key again answers 200 + replay headers.</summary>
    public const int MutationStatus = 200;
}

/// <summary>Shape validation only; the owners' scales, permissions and references are the service's.</summary>
public static class LogisticsJson
{
    private const int MaximumLines = 500;
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

    public static CreateLoadingInstructionRequest ReadCreate(string json)
    {
        var request = JsonSerializer.Deserialize<CreateLoadingInstructionRequest>(json, Options)
            ?? throw new JsonException("Request body is required.");
        if (request.CustomerPartyId == Guid.Empty || request.CompanyAccountId == Guid.Empty
            || request.Lines is null || request.Lines.Count is < 1 or > MaximumLines)
        {
            throw new JsonException("Invalid instruction fields.");
        }

        var ids = new HashSet<Guid>();
        foreach (var line in request.Lines)
        {
            if (line is null || line.LineId == Guid.Empty || !ids.Add(line.LineId) || line.SourceOrderId == Guid.Empty
                || line.SourceOrderLineId == Guid.Empty || line.UnitId == Guid.Empty)
            {
                throw new JsonException("Invalid line fields.");
            }

            RequireDecimalShape(line.Quantity);
        }

        return request;
    }

    public static IssueLoadingInstructionRequest ReadIssue(string json)
    {
        var request = JsonSerializer.Deserialize<IssueLoadingInstructionRequest>(json, Options)
            ?? throw new JsonException("Request body is required.");
        RequireVersionShape(request.ExpectedVersion);
        return request;
    }

    public static RecordLoadRequest ReadRecordLoad(string json)
    {
        var request = JsonSerializer.Deserialize<RecordLoadRequest>(json, Options)
            ?? throw new JsonException("Request body is required.");
        RequireVersionShape(request.ExpectedVersion);
        if (request.LoadId == Guid.Empty || request.Measurement is not ("Measured" or "AllocatedFromVehicleWeight"))
        {
            throw new JsonException("Invalid load fields.");
        }

        RequireDecimalShape(request.Quantity);
        return request;
    }

    public static RecordWeighingRequest ReadRecordWeighing(string json)
    {
        var request = JsonSerializer.Deserialize<RecordWeighingRequest>(json, Options)
            ?? throw new JsonException("Request body is required.");
        RequireVersionShape(request.ExpectedVersion);
        if (request.WeighingId == Guid.Empty || request.Direction is not ("TareFirst" or "GrossFirst"))
        {
            throw new JsonException("Invalid weighing fields.");
        }

        RequireDecimalShape(request.FirstWeighingKg);
        RequireDecimalShape(request.SecondWeighingKg);
        return request;
    }

    public static CreateDispatchNoteRequest ReadCreateDispatchNote(string json)
    {
        var request = JsonSerializer.Deserialize<CreateDispatchNoteRequest>(json, Options)
            ?? throw new JsonException("Request body is required.");
        RequireVersionShape(request.ExpectedVersion);
        if (request.DispatchNoteId == Guid.Empty || request.ShipmentIds is null || request.ShipmentIds.Count is < 1 or > MaximumLines
            || request.ShipmentIds.Any(id => id == Guid.Empty) || request.ShipmentIds.Distinct().Count() != request.ShipmentIds.Count)
        {
            throw new JsonException("Invalid dispatch-note fields.");
        }

        return request;
    }

    public static ApproveDispatchNoteRequest ReadApproveDispatchNote(string json)
    {
        var request = JsonSerializer.Deserialize<ApproveDispatchNoteRequest>(json, Options)
            ?? throw new JsonException("Request body is required.");
        RequireVersionShape(request.ExpectedVersion);
        return request;
    }

    public static CancelDispatchNoteRequest ReadCancelDispatchNote(string json)
    {
        var request = JsonSerializer.Deserialize<CancelDispatchNoteRequest>(json, Options)
            ?? throw new JsonException("Request body is required.");
        RequireVersionShape(request.ExpectedVersion);
        // Canonical text only: the client trims; a blank, padded or over-long reason is the caller's shape error.
        if (string.IsNullOrWhiteSpace(request.Reason) || !string.Equals(request.Reason, request.Reason.Trim(), StringComparison.Ordinal)
            || request.Reason.Length > CancelReasonMaximumLength)
        {
            throw new JsonException("Invalid cancellation reason.");
        }

        return request;
    }

    public static SetEDispatchNumberRequest ReadSetEDispatchNumber(string json)
    {
        var request = JsonSerializer.Deserialize<SetEDispatchNumberRequest>(json, Options)
            ?? throw new JsonException("Request body is required.");
        RequireVersionShape(request.ExpectedVersion);
        if (request.EDispatchNumber is null || !EDispatchNumberText.IsMatch(request.EDispatchNumber))
        {
            throw new JsonException("Invalid e-dispatch number.");
        }

        return request;
    }

    /// <summary>The widest cancellation reason (the column; the domain's own cap).</summary>
    public const int CancelReasonMaximumLength = 500;

    /// <summary>The most shipments one dispatch note names (the OpenAPI <c>maxItems</c>).</summary>
    public const int MaximumDispatchNoteShipments = MaximumLines;

    /// <summary>The GİB e-document number: 3 upper-case letters/digits, then 13 digits (4-digit year + 9-digit sequence).</summary>
    public const string EDispatchNumberPattern = "^[A-Z0-9]{3}[0-9]{13}$";

    private static readonly Regex EDispatchNumberText = new("\\A[A-Z0-9]{3}[0-9]{13}\\z", RegexOptions.CultureInvariant);

    private static void RequireVersionShape(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 19 || !VersionText.IsMatch(text))
        {
            throw new JsonException("Invalid expected version.");
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

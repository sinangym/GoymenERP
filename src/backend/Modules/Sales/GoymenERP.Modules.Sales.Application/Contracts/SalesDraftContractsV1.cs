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
// "PendingApproval", "Approved" or "Rejected" — beside the lifecycle `state` ("Draft"), the
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

public sealed record SalesOrderDraftLineDetail(
    Guid LineId, int LineNo, Guid ProductId, string ProductVersion,
    string ProductCode, string ProductName, Guid OrderUnitId, string Quantity,
    string? UnitPrice, Guid? PriceUnitId, DateOnly? RequestedDeliveryDate, string? Note,
    IReadOnlyList<SalesDraftDeliverySliceV1>? RequestedDeliverySlices,
    string? DiscountPercent);

public sealed record SalesOrderDraftDetailView(
    SalesOrderDraftSummaryView Draft, DateOnly? RequestedDeliveryDate, string? Note,
    IReadOnlyList<SalesOrderDraftLineDetail> Lines,
    SalesDraftOwnerReferenceV1? DeliveryAddress,
    SalesDraftOwnerReferenceV1? Contact,
    SalesDraftOwnerReferenceV1? Responsible,
    SalesDraftOwnerReferenceV1? PaymentTerms,
    SalesDraftOwnerReferenceV1? SourceStrategy,
    SalesDraftOwnerReferenceV1? Package,
    SalesDraftApprovalV1 Approval);

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
    public const int StateCommandStatus = 200;
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

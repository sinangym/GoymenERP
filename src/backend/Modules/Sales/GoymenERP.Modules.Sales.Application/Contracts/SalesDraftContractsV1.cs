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

public sealed record SalesOrderDraftSummaryView(
    Guid Id, Guid CompanyId, string DocumentNo, string Kind, string State,
    string Version, string Revision, Guid PartyId, Guid CompanyAccountId,
    Guid CurrencyId, DateOnly BusinessDate, DateTimeOffset CreatedAtUtc, int LineCount);

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
    SalesDraftOwnerReferenceV1? Package);

public sealed record SalesOrderDraftResponse(
    SalesOrderDraftDetailView Detail, bool IsReplay, string OriginalCorrelationId);

public sealed record SalesOrderDraftListResponse(
    IReadOnlyList<SalesOrderDraftSummaryView> Items, string? NextCursor);
public sealed record SalesOrderDraftListQuery(int PageSize = 50, string? Cursor = null);
public sealed record SalesDraftFieldError(string Field, string Code);
public sealed record SalesDraftProblem(int Status, string Title, string? Code = null,
    string? CorrelationId = null, IReadOnlyList<SalesDraftFieldError>? Errors = null);

public static class SalesDraftApiV1
{
    public const string DraftPath = "/api/v1/sales/drafts";
    public const string DraftDetailPath = "/api/v1/sales/drafts/{id}";
    public const string IdempotencyHeader = "Idempotency-Key";
    public const string Rev0 = "Rev.0";
    public const int CreatedStatus = 201;
    public const int ReplayStatus = 200;
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

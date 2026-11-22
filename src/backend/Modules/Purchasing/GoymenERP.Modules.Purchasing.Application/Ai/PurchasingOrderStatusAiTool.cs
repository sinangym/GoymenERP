using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using System.Text.Json;

namespace GoymenERP.Modules.Purchasing.Application.Ai;

/// <summary>
/// WP-A3b READ tool <c>purchasing.order-status.v1</c>: the company's purchase orders in the draft and approved statuses (or
/// one asked status, cancelled included), counted per status, with how many still wait for a price, optionally for one
/// supplier. Read ONLY through the Purchasing owner's order list
/// (<see cref="IPurchasingOrderReadServiceV1.GetOrderListAsync"/>, which measures <c>purchasing.orders.read</c> company-wide
/// around every read); the tool measures the same name for the server company before and after. No price, no amount and
/// no supplier display name are released (identities only). Whether an approved order still waits for goods is the
/// <c>purchasing.pending-receipts.v1</c> tool's answer, not this one's. Over the scan cap the counts are withheld.
/// </summary>
public sealed class PurchasingOrderStatusAiTool(IPurchasingOrderReadServiceV1 orders, IErpAuthorityRevalidationService authority,
    TimeProvider timeProvider) : IAiOwnerReadToolV1
{
    public const string ToolId = "purchasing.order-status.v1";
    public const string ToolVersion = "1";
    public const string StatusField = "status";
    public const string SupplierPartyIdField = "supplierPartyId";
    public const string LimitField = "limit";
    public const string DraftStatus = "Draft";
    public const string ApprovedStatus = "Approved";
    public const string CancelledStatus = "Cancelled";
    public const int DefaultLimit = 20;
    public const int MaximumLimit = 50;
    public const int PageSize = 100;
    public const int ScanLimit = 1000;
    public const string TargetKind = "purchasing.orders";
    private static readonly string[] Fields = [LimitField, StatusField, SupplierPartyIdField];
    private static readonly string[] Statuses = [DraftStatus, ApprovedStatus, CancelledStatus];
    private static readonly string[] Permissions = [PurchasingPermissionManifestV1.OrdersRead];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AiToolDescriptorV1 Descriptor { get; } = new(
        ToolId,
        ToolVersion,
        ownerModule: "Purchasing",
        AiToolRiskClassV1.Read,
        AiDataClassV1.Internal,
        requiredPermissions: Permissions,
        fieldAllowlist: Fields,
        ownerCommandName: null,
        new AiContractRevisionV1(
            InputSchema: "purchasing.order-status.v1/input@1",
            OutputSchema: "purchasing.order-status.v1/output@1",
            FieldPolicy: "purchasing.order-status.v1/fields@1",
            LimitPolicy: "purchasing.order-status.v1/limit-50-scan-1000@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+purchasing.orders.read@1",
            SourceContract: "IPurchasingOrderReadServiceV1.GetOrderListAsync@v1",
            RedactionPolicy: "purchasing.order-status.v1/no-price-no-display@1"));

    public async Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!AiReadArgumentsV1.TryParse(input.ArgumentsJson, Fields, out var arguments, out var invalid))
        {
            return Invalid(invalid);
        }

        if (!arguments.TryChoice(StatusField, Statuses, out var status)) return Invalid(StatusField);
        if (!arguments.TryGuid(SupplierPartyIdField, out var supplierPartyId)) return Invalid(SupplierPartyIdField);
        if (!arguments.TryInt(LimitField, 1, MaximumLimit, DefaultLimit, out var limit)) return Invalid(LimitField);
        var statuses = status is null ? new[] { DraftStatus, ApprovedStatus } : new[] { status };

        if (await AiOwnerReadFenceV1.RequireCompanyWideAsync(authority, Permissions, context.CompanyId, cancellationToken) is { } refused)
        {
            return new AiReadResultV1(refused);
        }

        var rows = new List<PurchaseOrderReadSummaryViewV1>();
        var complete = true;
        try
        {
            foreach (var wanted in statuses)
            {
                string? cursor = null;
                do
                {
                    var page = await orders.GetOrderListAsync(
                        new PurchaseOrderListQuery(PageSize, cursor, wanted, supplierPartyId), cancellationToken);
                    foreach (var item in page.Items)
                    {
                        if (item.CompanyId != context.CompanyId || !string.Equals(item.Status, wanted, StringComparison.Ordinal)
                            || (supplierPartyId is { } supplier && item.SupplierPartyId != supplier))
                        {
                            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
                        }

                        rows.Add(item);
                    }

                    cursor = page.NextCursor;
                    if (cursor is not null && rows.Count >= ScanLimit)
                    {
                        complete = false;
                        break;
                    }
                }
                while (cursor is not null);

                if (!complete)
                {
                    break;
                }
            }
        }
        catch (PurchasingServiceException exception) when (exception.Status is 401 or 403)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Denied);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        if (await AiOwnerReadFenceV1.RequireCompanyWideAsync(authority, Permissions, context.CompanyId, cancellationToken) is { } after)
        {
            return new AiReadResultV1(after);
        }

        if (rows.Select(row => row.Id).Distinct().Count() != rows.Count)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        var ordered = rows.OrderByDescending(row => row.CreatedAtUtc).ThenBy(row => row.Id).ToArray();
        var byStatus = statuses.Select(wanted => new PurchasingStatusCountV1(wanted, ordered.Count(row => row.Status == wanted))).ToArray();
        var listed = ordered.Take(limit).Select(row => new PurchasingOrderItemV1(
            row.Id, row.DocumentNumber, row.Status, row.SupplierPartyId, row.EffectiveOn, row.LineCount,
            row.PricePending == true, row.CreatedAtUtc)).ToArray();
        var content = new PurchasingOrderStatusContentV1(
            statuses,
            supplierPartyId,
            complete,
            complete ? ordered.Length : null,
            complete ? byStatus : null,
            complete ? ordered.Count(row => row.PricePending == true) : null,
            listed.Length,
            listed);
        var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, context.CompanyId, "Alış siparişleri",
            AiSourceVersionV1.Of(ordered.Select(row => $"{row.Id:D}:{row.Version}")),
            timeProvider.GetUtcNow().ToUniversalTime(), AiDataClassV1.Internal);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source);
    }

    private static AiReadResultV1 Invalid(string field) => new(AiToolOutcomeV1.Invalid, invalidFields: [field]);
}

public sealed record PurchasingStatusCountV1(string Status, int Count);

/// <summary>Counts are null when <c>Complete</c> is false.</summary>
public sealed record PurchasingOrderStatusContentV1(
    IReadOnlyList<string> Statuses,
    Guid? SupplierPartyId,
    bool Complete,
    int? OrderCount,
    IReadOnlyList<PurchasingStatusCountV1>? ByStatus,
    int? PricePendingCount,
    int ListedCount,
    IReadOnlyList<PurchasingOrderItemV1> Items);

public sealed record PurchasingOrderItemV1(
    Guid OrderId,
    string? DocumentNumber,
    string Status,
    Guid SupplierPartyId,
    DateOnly EffectiveOn,
    int LineCount,
    bool PricePending,
    DateTimeOffset CreatedAtUtc);

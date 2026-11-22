using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security;
using System.Text.Json;

namespace GoymenERP.Modules.Manufacturing.Application.Ai;

/// <summary>
/// WP-A3b READ tool <c>manufacturing.order-status.v1</c>: the company's production orders in one status (default
/// <c>Open</c>), counted per process with the exact planned input kg total, newest first. Read ONLY through the owner's
/// order list (<see cref="IManufacturingAiReadSourceV1.ListOrdersAsync"/>, company-filtered stored rows); the tool measures
/// <c>manufacturing.orders.read</c> company-wide before and after reading (the list itself relies on the endpoint policy).
/// Over the scan cap the counts and total are withheld and <c>complete</c> is false. Kilogram only; no balance computed.
/// </summary>
public sealed class ManufacturingOrderStatusAiTool(IManufacturingAiReadSourceV1 source, IErpAuthorityRevalidationService authority,
    TimeProvider timeProvider) : IAiOwnerReadToolV1
{
    public const string ToolId = "manufacturing.order-status.v1";
    public const string ToolVersion = "1";
    public const string StatusField = "status";
    public const string LimitField = "limit";
    public const string OpenStatus = "Open";
    public const string ClosedStatus = "Closed";
    public const string CancelledStatus = "Cancelled";
    public const int DefaultLimit = 20;
    public const int MaximumLimit = 50;
    public const int PageSize = 100;
    public const int ScanLimit = 1000;
    public const string TargetKind = "manufacturing.orders";
    private static readonly string[] Fields = [LimitField, StatusField];
    private static readonly string[] Statuses = [OpenStatus, ClosedStatus, CancelledStatus];
    private static readonly string[] Permissions = [ManufacturingPermissionManifestV1.OrdersRead];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AiToolDescriptorV1 Descriptor { get; } = new(
        ToolId,
        ToolVersion,
        ownerModule: "Manufacturing",
        AiToolRiskClassV1.Read,
        AiDataClassV1.Internal,
        requiredPermissions: Permissions,
        fieldAllowlist: Fields,
        ownerCommandName: null,
        new AiContractRevisionV1(
            InputSchema: "manufacturing.order-status.v1/input@1",
            OutputSchema: "manufacturing.order-status.v1/output@1",
            FieldPolicy: "manufacturing.order-status.v1/fields@1",
            LimitPolicy: "manufacturing.order-status.v1/limit-50-scan-1000@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+manufacturing.orders.read@1",
            SourceContract: "ManufacturingCommandService.ListOrdersAsync@v1",
            RedactionPolicy: "manufacturing.order-status.v1/no-cost-no-free-text@1"));

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
        if (!arguments.TryInt(LimitField, 1, MaximumLimit, DefaultLimit, out var limit)) return Invalid(LimitField);
        status ??= OpenStatus;

        if (await AiOwnerReadFenceV1.RequireCompanyWideAsync(authority, Permissions, context.CompanyId, cancellationToken) is { } refused)
        {
            return new AiReadResultV1(refused);
        }

        var rows = new List<OrderSummaryViewV1>();
        var complete = true;
        try
        {
            string? cursor = null;
            do
            {
                var result = await source.ListOrdersAsync(PageSize, cursor, status, cancellationToken);
                if (result.Status == ManufacturingApiStatus.Denied)
                {
                    return new AiReadResultV1(AiToolOutcomeV1.Denied);
                }

                if (result.Status != ManufacturingApiStatus.Ok || result.Resource is not { } page)
                {
                    return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
                }

                foreach (var order in page.Orders)
                {
                    // The owner answered another status than asked: release nothing (fail-closed).
                    if (!string.Equals(order.Status, status, StringComparison.Ordinal))
                    {
                        return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
                    }

                    rows.Add(order);
                }

                cursor = page.NextCursor;
                if (cursor is not null && rows.Count >= ScanLimit)
                {
                    complete = false;
                    break;
                }
            }
            while (cursor is not null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        if (await AiOwnerReadFenceV1.RequireCompanyWideAsync(authority, Permissions, context.CompanyId, cancellationToken) is { } after)
        {
            return new AiReadResultV1(after);
        }

        if (rows.Select(row => row.OrderId).Distinct().Count() != rows.Count
            || !AiExactDecimalV1.TrySum(rows.Select(row => row.PlannedInputKg), out var plannedKg))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        var ordered = rows.OrderByDescending(row => row.CreatedAtUtc).ThenBy(row => row.OrderId).ToArray();
        var byProcess = ordered.GroupBy(row => row.Process, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new ManufacturingProcessCountV1(group.Key, group.Count())).ToArray();
        var listed = ordered.Take(limit).Select(row => new ManufacturingOrderItemV1(
            row.OrderId, row.PlanId, row.WorkCenterId, row.Process, row.Status, row.PlannedRollCount,
            AiExactDecimalV1.Format(Parse(row.PlannedInputKg)), row.CreatedAtUtc, row.ClosedAtUtc)).ToArray();
        var content = new ManufacturingOrderStatusContentV1(
            ManufacturingAiUnitsV1.Kilogram,
            status,
            complete,
            complete ? ordered.Length : null,
            complete ? AiExactDecimalV1.Format(plannedKg) : null,
            complete ? byProcess : null,
            listed.Length,
            listed);
        var sourceInfo = new AiSourceV1(ToolId, ToolVersion, TargetKind, context.CompanyId, "Üretim emirleri",
            AiSourceVersionV1.Of(ordered.Select(row => $"{row.OrderId:D}:{row.Version}")),
            timeProvider.GetUtcNow().ToUniversalTime(), AiDataClassV1.Internal);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), sourceInfo);
    }

    private static decimal Parse(string text) => AiExactDecimalV1.TryParse(text, out var value) ? value : throw new FormatException();

    private static AiReadResultV1 Invalid(string field) => new(AiToolOutcomeV1.Invalid, invalidFields: [field]);
}

public sealed record ManufacturingProcessCountV1(string Process, int Count);

/// <summary>Counts and the total are null when <c>Complete</c> is false.</summary>
public sealed record ManufacturingOrderStatusContentV1(
    string Unit,
    string Status,
    bool Complete,
    int? OrderCount,
    string? PlannedInputKgTotal,
    IReadOnlyList<ManufacturingProcessCountV1>? ByProcess,
    int ListedCount,
    IReadOnlyList<ManufacturingOrderItemV1> Items);

public sealed record ManufacturingOrderItemV1(
    Guid OrderId,
    Guid PlanId,
    Guid WorkCenterId,
    string Process,
    string Status,
    int PlannedRollCount,
    string PlannedInputKg,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ClosedAtUtc);

using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Domain.Labels;
using System.Text.Json;

namespace GoymenERP.Modules.Inventory.Application.Ai;

/// <summary>
/// WP-A3b READ tool <c>inventory.stock-positions.v1</c> (DALGA_PLANI_v2 A.3): the company's physical stock labels, narrowed
/// by label code, product or location, with exact kilogram totals. Read ONLY through the Inventory owner's stock-position
/// read (<see cref="IInventoryStockPositionsV1.SearchAsync"/>), which measures <c>inventory.labels.read</c> and
/// <c>inventory.balances.read</c> company-wide for the request's actor; the tool measures the same names for the server
/// company before and after reading. Stock is kilogram-only (no piece count is reported or derived). No cost, no lot
/// free text, no owner display. Totals are exact sums of the owner's kg texts over every page read; when the scan cap is
/// reached the totals are withheld (null) and <c>complete</c> is false — never an estimate.
/// </summary>
public sealed class InventoryStockPositionsAiTool(IInventoryStockPositionsV1 positions, IErpAuthorityRevalidationService authority,
    TimeProvider timeProvider) : IAiOwnerReadToolV1
{
    public const string ToolId = "inventory.stock-positions.v1";
    public const string ToolVersion = "1";
    public const string LabelCodeField = "labelCode";
    public const string ProductIdField = "productId";
    public const string LocationIdField = "locationId";
    public const string LimitField = "limit";
    public const int DefaultLimit = 20;
    public const int MaximumLimit = 50;
    public const int PageSize = 100;
    public const int ScanLimit = 1000;
    public const string TargetKind = "inventory.stock-positions";
    private static readonly string[] Fields = [LabelCodeField, LimitField, LocationIdField, ProductIdField];
    private static readonly string[] Permissions = [ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AiToolDescriptorV1 Descriptor { get; } = new(
        ToolId,
        ToolVersion,
        ownerModule: "Inventory",
        AiToolRiskClassV1.Read,
        AiDataClassV1.Internal,
        requiredPermissions: Permissions,
        fieldAllowlist: Fields,
        ownerCommandName: null,
        new AiContractRevisionV1(
            InputSchema: "inventory.stock-positions.v1/input@1",
            OutputSchema: "inventory.stock-positions.v1/output@1",
            FieldPolicy: "inventory.stock-positions.v1/fields@1",
            LimitPolicy: "inventory.stock-positions.v1/limit-50-scan-1000@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+inventory.labels.read+inventory.balances.read@1",
            SourceContract: "IInventoryStockPositionsV1.SearchAsync@v1",
            RedactionPolicy: "inventory.stock-positions.v1/no-cost-no-free-text@1"));

    public async Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!AiReadArgumentsV1.TryParse(input.ArgumentsJson, Fields, out var arguments, out var invalid))
        {
            return Invalid(invalid);
        }

        if (!arguments.TryText(LabelCodeField, 1, MaterialLabel.MaximumCodeLength, out var labelCode)) return Invalid(LabelCodeField);
        if (!arguments.TryGuid(ProductIdField, out var productId)) return Invalid(ProductIdField);
        if (!arguments.TryGuid(LocationIdField, out var locationId)) return Invalid(LocationIdField);
        if (!arguments.TryInt(LimitField, 1, MaximumLimit, DefaultLimit, out var limit)) return Invalid(LimitField);

        if (await AiOwnerReadFenceV1.RequireCompanyWideAsync(authority, Permissions, context.CompanyId, cancellationToken) is { } refused)
        {
            return new AiReadResultV1(refused);
        }

        var rows = new List<InventoryStockPositionV1>();
        var complete = true;
        try
        {
            string? cursor = null;
            do
            {
                var page = await positions.SearchAsync(
                    new InventoryStockPositionQueryV1(labelCode, PageSize, cursor, productId, locationId), cancellationToken);
                foreach (var item in page.Items)
                {
                    // The owner answered for another company or outside the asked filter: release nothing.
                    if (item.CompanyId != context.CompanyId || (productId is { } p && item.ProductId != p)
                        || (locationId is { } l && item.LocationId != l))
                    {
                        return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
                    }

                    rows.Add(item);
                }

                cursor = page.SearchWindowFull ? page.NextCursor : null;
                if (page.SearchWindowFull && cursor is null)
                {
                    // The owner says more exists but gave no continuation: the answer cannot be whole.
                    complete = false;
                }

                if (rows.Count >= ScanLimit && cursor is not null)
                {
                    complete = false;
                    break;
                }
            }
            while (cursor is not null);
        }
        catch (InventoryStockPositionException exception) when (exception.StatusCode is 401 or 403)
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

        if (rows.Select(row => row.LabelId).Distinct().Count() != rows.Count
            || !AiExactDecimalV1.TrySum(rows.Select(row => row.PhysicalKg), out var physical)
            || !AiExactDecimalV1.TrySum(rows.Select(row => row.FreePhysicalKg), out var free)
            || !AiExactDecimalV1.TrySum(rows.Select(row => row.BlockedPhysicalKg), out var blocked)
            || !AiExactDecimalV1.TrySum(rows.Select(row => row.ActiveAllocationKg), out var allocated)
            || !AiExactDecimalV1.TrySum(rows.Select(row => row.AvailableKg), out var available))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        var ordered = rows.OrderBy(row => row.LabelCode, StringComparer.Ordinal).ThenBy(row => row.LabelId).ToArray();
        var listed = ordered.Take(limit).Select(row => new InventoryStockPositionItemV1(
            row.LabelId, row.LabelCode, row.ProductId, row.LocationId, row.Classification, row.DocumentStatus,
            AiExactDecimalV1.Format(Parse(row.PhysicalKg)), AiExactDecimalV1.Format(Parse(row.FreePhysicalKg)),
            AiExactDecimalV1.Format(Parse(row.BlockedPhysicalKg)), AiExactDecimalV1.Format(Parse(row.ActiveAllocationKg)),
            AiExactDecimalV1.Format(Parse(row.AvailableKg)))).ToArray();
        var content = new InventoryStockPositionsContentV1(
            InventoryAiUnitsV1.Kilogram,
            complete,
            complete ? ordered.Length : null,
            complete ? AiExactDecimalV1.Format(physical) : null,
            complete ? AiExactDecimalV1.Format(free) : null,
            complete ? AiExactDecimalV1.Format(blocked) : null,
            complete ? AiExactDecimalV1.Format(allocated) : null,
            complete ? AiExactDecimalV1.Format(available) : null,
            listed.Length,
            listed);
        var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, context.CompanyId, "Stok pozisyonları",
            AiSourceVersionV1.Of(ordered.Select(row => $"{row.LabelId:D}:{row.LabelVersion}")),
            timeProvider.GetUtcNow().ToUniversalTime(), AiDataClassV1.Internal);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source);
    }

    private static decimal Parse(string text) => AiExactDecimalV1.TryParse(text, out var value) ? value : throw new FormatException();

    private static AiReadResultV1 Invalid(string field) => new(AiToolOutcomeV1.Invalid, invalidFields: [field]);
}

public static class InventoryAiUnitsV1
{
    /// <summary>Inventory stock is kilogram-only; no piece count is reported or derived by an Inventory AI tool.</summary>
    public const string Kilogram = "kg";
}

/// <summary>Totals are null when <c>Complete</c> is false; every quantity is exact kilogram text.</summary>
public sealed record InventoryStockPositionsContentV1(
    string Unit,
    bool Complete,
    int? LabelCount,
    string? PhysicalKg,
    string? FreePhysicalKg,
    string? BlockedPhysicalKg,
    string? ActiveAllocationKg,
    string? AvailableKg,
    int ListedCount,
    IReadOnlyList<InventoryStockPositionItemV1> Items);

public sealed record InventoryStockPositionItemV1(
    Guid LabelId,
    string LabelCode,
    Guid ProductId,
    Guid LocationId,
    string? Classification,
    string DocumentStatus,
    string PhysicalKg,
    string FreePhysicalKg,
    string BlockedPhysicalKg,
    string ActiveAllocationKg,
    string AvailableKg);

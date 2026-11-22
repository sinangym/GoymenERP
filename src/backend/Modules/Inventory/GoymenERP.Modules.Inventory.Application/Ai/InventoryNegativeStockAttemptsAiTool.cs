using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security;
using System.Text.Json;

namespace GoymenERP.Modules.Inventory.Application.Ai;

/// <summary>
/// WP-A3b READ tool <c>inventory.negative-stock-attempts.v1</c>: physical exits the Inventory owner stopped because stock
/// would go negative (SAT-D328/336..339), open (not yet settled by a stock count) or all. Read ONLY through
/// <see cref="IInventoryNegativeStockAttemptsV1.SearchAsync"/> (company-filtered, measures <c>inventory.labels.read</c> and
/// <c>inventory.balances.read</c> company-wide). The owner window is one bounded page: when it is full the counts and the
/// missing-kg total are withheld and <c>complete</c> is false. The actor who tried and the one who settled are personal
/// data and are not released. Kilogram only.
/// </summary>
public sealed class InventoryNegativeStockAttemptsAiTool(IInventoryNegativeStockAttemptsV1 attempts,
    IErpAuthorityRevalidationService authority, TimeProvider timeProvider) : IAiOwnerReadToolV1
{
    public const string ToolId = "inventory.negative-stock-attempts.v1";
    public const string ToolVersion = "1";
    public const string OpenOnlyField = "openOnly";
    public const string ProductIdField = "productId";
    public const string LocationIdField = "locationId";
    public const string LimitField = "limit";
    public const int DefaultLimit = 20;
    public const int MaximumLimit = 50;
    public const int OwnerWindow = 100;
    public const string TargetKind = "inventory.negative-stock-attempts";
    private static readonly string[] Fields = [LimitField, LocationIdField, OpenOnlyField, ProductIdField];
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
            InputSchema: "inventory.negative-stock-attempts.v1/input@1",
            OutputSchema: "inventory.negative-stock-attempts.v1/output@1",
            FieldPolicy: "inventory.negative-stock-attempts.v1/fields@1",
            LimitPolicy: "inventory.negative-stock-attempts.v1/limit-50-window-100@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+inventory.labels.read+inventory.balances.read@1",
            SourceContract: "IInventoryNegativeStockAttemptsV1.SearchAsync@v1",
            RedactionPolicy: "inventory.negative-stock-attempts.v1/no-actor@1"));

    public async Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!AiReadArgumentsV1.TryParse(input.ArgumentsJson, Fields, out var arguments, out var invalid))
        {
            return Invalid(invalid);
        }

        if (!arguments.TryBool(OpenOnlyField, true, out var openOnly)) return Invalid(OpenOnlyField);
        if (!arguments.TryGuid(ProductIdField, out var productId)) return Invalid(ProductIdField);
        if (!arguments.TryGuid(LocationIdField, out var locationId)) return Invalid(LocationIdField);
        if (!arguments.TryInt(LimitField, 1, MaximumLimit, DefaultLimit, out var limit)) return Invalid(LimitField);

        if (await AiOwnerReadFenceV1.RequireCompanyWideAsync(authority, Permissions, context.CompanyId, cancellationToken) is { } refused)
        {
            return new AiReadResultV1(refused);
        }

        InventoryNegativeStockAttemptPageV1 page;
        try
        {
            page = await attempts.SearchAsync(
                new InventoryNegativeStockAttemptQueryV1(productId, locationId, openOnly, OwnerWindow), cancellationToken);
        }
        catch (InventoryNegativeStockAttemptException exception) when (exception.StatusCode is 401 or 403)
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

        if (page is null || page.CompanyId != context.CompanyId
            || page.Items.Any(item => item.CompanyId != context.CompanyId
                || (openOnly && item.Resolution is not null)
                || (productId is { } p && item.ProductId != p) || (locationId is { } l && item.LocationId != l))
            || page.Items.Select(item => item.AttemptId).Distinct().Count() != page.Items.Count
            || !AiExactDecimalV1.TrySum(page.Items.Select(item => item.MissingKg), out var missing)
            || !page.Items.All(item => AiExactDecimalV1.TryParse(item.RequestedKg, out _) && AiExactDecimalV1.TryParse(item.AvailableKg, out _)))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        var complete = !page.WindowFull;
        var ordered = page.Items.OrderByDescending(item => item.OccurredAtUtc).ThenBy(item => item.AttemptId).ToArray();
        var listed = ordered.Take(limit).Select(item => new InventoryNegativeStockAttemptItemV1(
            item.AttemptId, item.OccurredAtUtc, item.ProductId, item.LocationId, item.LabelId, item.LabelCode,
            item.MovementKind, item.SourceKind, item.SourceId, Format(item.RequestedKg), Format(item.AvailableKg),
            Format(item.MissingKg), item.Resolution is not null)).ToArray();
        var content = new InventoryNegativeStockAttemptsContentV1(
            InventoryAiUnitsV1.Kilogram,
            openOnly,
            complete,
            complete ? ordered.Length : null,
            complete ? ordered.Count(item => item.Resolution is null) : null,
            complete ? AiExactDecimalV1.Format(missing) : null,
            listed.Length,
            listed);
        var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, context.CompanyId, "Negatif stok denemeleri",
            AiSourceVersionV1.Of(ordered.Select(item => $"{item.AttemptId:D}:{(item.Resolution is null ? "open" : item.Resolution.StockCountId.ToString("D"))}")),
            timeProvider.GetUtcNow().ToUniversalTime(), AiDataClassV1.Internal);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source);
    }

    private static string Format(string text) =>
        AiExactDecimalV1.TryParse(text, out var value) ? AiExactDecimalV1.Format(value) : throw new FormatException();

    private static AiReadResultV1 Invalid(string field) => new(AiToolOutcomeV1.Invalid, invalidFields: [field]);
}

/// <summary>Counts and the total are null when <c>Complete</c> is false (the owner window was full).</summary>
public sealed record InventoryNegativeStockAttemptsContentV1(
    string Unit,
    bool OpenOnly,
    bool Complete,
    int? AttemptCount,
    int? OpenCount,
    string? MissingKgTotal,
    int ListedCount,
    IReadOnlyList<InventoryNegativeStockAttemptItemV1> Items);

public sealed record InventoryNegativeStockAttemptItemV1(
    Guid AttemptId,
    DateTimeOffset OccurredAtUtc,
    Guid ProductId,
    Guid LocationId,
    Guid LabelId,
    string LabelCode,
    string MovementKind,
    string SourceKind,
    Guid SourceId,
    string RequestedKg,
    string AvailableKg,
    string MissingKg,
    bool Resolved);

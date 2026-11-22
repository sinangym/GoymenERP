using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Modules.Inventory.Application.Ai;

/// <summary>
/// WP-A3b / AI-D45 READ tool <c>inventory.average-cost.v1</c>: the moving-average purchase cost per stock kilogram of one
/// product at one location (SAT-D262), read ONLY through the Inventory owner's existing cost port
/// (<see cref="IInventoryAverageCostReadPortV1.ReadAsync"/>); the cost is never recomputed here. The port carries no
/// authority, so the tool measures <c>inventory.average-cost.read</c> (the "maliyet görme izni") company-wide for the server
/// company before and after the read.
/// <para>
/// CONFIDENTIAL (AI-D45: "Gizli"): the gateway never routes Confidential content to a cloud model
/// (<see cref="AiGatewayPlacementRulesV1.CloudMaximumDataClass"/>). An unreadable or missing cost is said as such with the
/// owner's code — never a zero, never an estimate.
/// </para>
/// </summary>
public sealed class InventoryAverageCostAiTool(IInventoryAverageCostReadPortV1 costs, IErpAuthorityRevalidationService authority,
    TimeProvider timeProvider) : IAiOwnerReadToolV1
{
    public const string ToolId = "inventory.average-cost.v1";
    public const string ToolVersion = "1";
    public const string ProductIdField = "productId";
    public const string LocationIdField = "locationId";
    public const string TargetKind = "inventory.average-cost";
    private static readonly string[] Fields = [LocationIdField, ProductIdField];
    private static readonly string[] Permissions = [ErpPermissions.InventoryAverageCostRead];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AiToolDescriptorV1 Descriptor { get; } = new(
        ToolId,
        ToolVersion,
        ownerModule: "Inventory",
        AiToolRiskClassV1.Read,
        AiDataClassV1.Confidential,
        requiredPermissions: Permissions,
        fieldAllowlist: Fields,
        ownerCommandName: null,
        new AiContractRevisionV1(
            InputSchema: "inventory.average-cost.v1/input@1",
            OutputSchema: "inventory.average-cost.v1/output@1",
            FieldPolicy: "inventory.average-cost.v1/fields@1",
            LimitPolicy: "inventory.average-cost.v1/one-position@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+inventory.average-cost.read@1",
            SourceContract: "IInventoryAverageCostReadPortV1.ReadAsync@v1",
            RedactionPolicy: "inventory.average-cost.v1/confidential-on-premises-only@1"));

    public async Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!AiReadArgumentsV1.TryParse(input.ArgumentsJson, Fields, out var arguments, out var invalid))
        {
            return Invalid(invalid);
        }

        if (!arguments.TryGuid(ProductIdField, out var productId) || productId is null) return Invalid(ProductIdField);
        if (!arguments.TryGuid(LocationIdField, out var locationId) || locationId is null) return Invalid(LocationIdField);

        if (await AiOwnerReadFenceV1.RequireCompanyWideAsync(authority, Permissions, context.CompanyId, cancellationToken) is { } refused)
        {
            return new AiReadResultV1(refused);
        }

        InventoryAverageCostReadAnswerV1 answer;
        try
        {
            answer = await costs.ReadAsync(locationId.Value, productId.Value, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        if (await AiOwnerReadFenceV1.RequireCompanyWideAsync(authority, Permissions, context.CompanyId, cancellationToken) is { } after)
        {
            return new AiReadResultV1(after);
        }

        // Another company's or another position's answer, or an answer outside the owner's contract: release nothing.
        if (answer is null || answer.CompanyId != context.CompanyId || answer.LocationId != locationId || answer.ProductId != productId
            || answer.ContractVersion != InventoryAverageCostReadContractV1.Version
            || answer.Status switch
            {
                InventoryAverageCostReadStatusV1.Value => answer.AverageCostPerKg is not >= 0m || answer.LedgerVersion is null,
                InventoryAverageCostReadStatusV1.Unreadable => answer.UnreadableCode is null
                                                                || !InventoryAverageCostUnreadableCodesV1.All.Contains(answer.UnreadableCode),
                InventoryAverageCostReadStatusV1.NoSource => answer.AverageCostPerKg is not null,
                _ => true,
            })
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        var hasValue = answer.Status == InventoryAverageCostReadStatusV1.Value;
        var content = new InventoryAverageCostContentV1(
            InventoryAverageCostReadContractV1.CostBasis,
            InventoryAverageCostReadContractV1.CostUnit,
            answer.ProductId,
            answer.LocationId,
            answer.Status.ToString(),
            hasValue,
            hasValue ? AiExactDecimalV1.Format(answer.AverageCostPerKg!.Value) : null,
            answer.UnreadableCode);
        var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, context.CompanyId, "Stok ortalama maliyeti",
            AiSourceVersionV1.Of([
                $"{answer.LocationId:D}:{answer.ProductId:D}:{answer.Status}:" +
                (answer.LedgerVersion?.ToString(CultureInfo.InvariantCulture) ?? "none"),
            ]),
            timeProvider.GetUtcNow().ToUniversalTime(), AiDataClassV1.Confidential);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source);
    }

    private static AiReadResultV1 Invalid(string field) => new(AiToolOutcomeV1.Invalid, invalidFields: [field]);
}

/// <summary>
/// <c>AverageCostPerKg</c> is given only when <c>Complete</c> (status Value); an Unreadable answer names the owner's code and
/// a NoSource answer names nothing. The cost is per stock kilogram in the company's cost currency, as the owner keeps it.
/// </summary>
public sealed record InventoryAverageCostContentV1(
    string CostBasis,
    string CostUnit,
    Guid ProductId,
    Guid LocationId,
    string Status,
    bool Complete,
    string? AverageCostPerKg,
    string? UnreadableCode);

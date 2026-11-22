using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security;
using System.Text.Json;

namespace GoymenERP.Modules.Inventory.Application.Ai;

/// <summary>
/// WP-A3b READ tool <c>inventory.pending-stock-counts.v1</c>: non-zero stock counts (sayım farkları) that still wait for the
/// stock-count decision and that the asking actor could decide (the owner leaves out the actor's own submissions, D-05).
/// Read ONLY through <see cref="IInventoryStockCountPendingApprovalReadServiceV1.ListAsync"/>, which measures
/// <c>inventory.labels.read</c>, <c>inventory.balances.read</c> and <c>inventory.stock-count.approve</c> company-wide before
/// and after every page. Counts and the exact kg difference totals (surplus, shortage, net) come from every page read;
/// over the scan cap they are withheld and <c>complete</c> is false. The count reason (free text) and the submitter are
/// not released. Kilogram only.
/// </summary>
public sealed class InventoryPendingStockCountsAiTool(IInventoryStockCountPendingApprovalReadServiceV1 counts,
    IErpAuthorityRevalidationService authority, TimeProvider timeProvider) : IAiOwnerReadToolV1
{
    public const string ToolId = "inventory.pending-stock-counts.v1";
    public const string ToolVersion = "1";
    public const string LimitField = "limit";
    public const int DefaultLimit = 20;
    public const int MaximumLimit = 50;
    public const int ScanLimit = 1000;
    public const int MaximumPages = 100;
    public const string TargetKind = "inventory.stock-counts.pending";
    private static readonly string[] Fields = [LimitField];
    private static readonly string[] Permissions =
        [ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead, InventoryPermissionManifestV1.StockCountApprove];
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
            InputSchema: "inventory.pending-stock-counts.v1/input@1",
            OutputSchema: "inventory.pending-stock-counts.v1/output@1",
            FieldPolicy: "inventory.pending-stock-counts.v1/fields@1",
            LimitPolicy: "inventory.pending-stock-counts.v1/limit-50-scan-1000@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+inventory.labels.read+inventory.balances.read+inventory.stock-count.approve@1",
            SourceContract: "IInventoryStockCountPendingApprovalReadServiceV1.ListAsync@v1",
            RedactionPolicy: "inventory.pending-stock-counts.v1/no-reason-no-submitter@1"));

    public async Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!AiReadArgumentsV1.TryParse(input.ArgumentsJson, Fields, out var arguments, out var invalid))
        {
            return Invalid(invalid);
        }

        if (!arguments.TryInt(LimitField, 1, MaximumLimit, DefaultLimit, out var limit)) return Invalid(LimitField);

        if (await AiOwnerReadFenceV1.RequireCompanyWideAsync(authority, Permissions, context.CompanyId, cancellationToken) is { } refused)
        {
            return new AiReadResultV1(refused);
        }

        var rows = new List<InventoryStockCountPendingApprovalItemV1>();
        var complete = true;
        try
        {
            string? cursor = null;
            var pages = 0;
            do
            {
                var result = await counts.ListAsync(
                    new InventoryPendingApprovalReadContextV1(context.CompanyId, context.ActorId), cursor, cancellationToken);
                switch (result.Outcome)
                {
                    case InventoryPendingApprovalReadOutcomeV1.Denied:
                        return new AiReadResultV1(AiToolOutcomeV1.Denied);
                    case InventoryPendingApprovalReadOutcomeV1.Found when result.Resource is { } page && page.CompanyId == context.CompanyId:
                        foreach (var item in page.Items)
                        {
                            if (item.CompanyId != context.CompanyId)
                            {
                                return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
                            }

                            rows.Add(item);
                        }

                        cursor = page.NextCursor;
                        break;
                    default:
                        return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
                }

                pages++;
                if (cursor is not null && (rows.Count >= ScanLimit || pages >= MaximumPages))
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

        var differences = new List<decimal>(rows.Count);
        foreach (var row in rows)
        {
            if (!AiExactDecimalV1.TryParse(row.DifferenceKg, out var difference) || difference == 0m
                || !AiExactDecimalV1.TryParse(row.BookKg, out _) || !AiExactDecimalV1.TryParse(row.CountedKg, out _))
            {
                return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
            }

            differences.Add(difference);
        }

        if (rows.Select(row => row.CountId).Distinct().Count() != rows.Count
            || !AiExactDecimalV1.TrySum(differences.Where(value => value > 0m).Select(AiExactDecimalV1.Format), out var surplus)
            || !AiExactDecimalV1.TrySum(differences.Where(value => value < 0m).Select(AiExactDecimalV1.Format), out var shortage))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        var ordered = rows.OrderBy(row => row.SubmittedAtUtc).ThenBy(row => row.CountId).ToArray();
        var listed = ordered.Take(limit).Select(row => new InventoryPendingStockCountItemV1(
            row.CountId, row.LabelId, row.LabelCode, row.ProductId, row.LocationId, row.Block,
            Format(row.BookKg), Format(row.CountedKg), Format(row.DifferenceKg), row.SubmittedAtUtc)).ToArray();
        var content = new InventoryPendingStockCountsContentV1(
            InventoryAiUnitsV1.Kilogram,
            complete,
            complete ? ordered.Length : null,
            complete ? AiExactDecimalV1.Format(surplus) : null,
            complete ? AiExactDecimalV1.Format(shortage) : null,
            complete ? AiExactDecimalV1.Format(surplus + shortage) : null,
            listed.Length,
            listed);
        var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, context.CompanyId, "Onay bekleyen sayım farkları",
            AiSourceVersionV1.Of(ordered.Select(row => $"{row.CountId:D}:{row.LabelVersion}")),
            timeProvider.GetUtcNow().ToUniversalTime(), AiDataClassV1.Internal);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source);
    }

    private static string Format(string text) =>
        AiExactDecimalV1.TryParse(text, out var value) ? AiExactDecimalV1.Format(value) : throw new FormatException();

    private static AiReadResultV1 Invalid(string field) => new(AiToolOutcomeV1.Invalid, invalidFields: [field]);
}

/// <summary>Counts and totals are null when <c>Complete</c> is false. A shortage total is negative kg text.</summary>
public sealed record InventoryPendingStockCountsContentV1(
    string Unit,
    bool Complete,
    int? PendingCount,
    string? SurplusKgTotal,
    string? ShortageKgTotal,
    string? NetDifferenceKg,
    int ListedCount,
    IReadOnlyList<InventoryPendingStockCountItemV1> Items);

public sealed record InventoryPendingStockCountItemV1(
    Guid CountId,
    Guid LabelId,
    string LabelCode,
    Guid ProductId,
    Guid LocationId,
    string Block,
    string BookKg,
    string CountedKg,
    string DifferenceKg,
    DateTimeOffset SubmittedAtUtc);

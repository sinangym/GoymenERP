using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using System.Text.Json;

namespace GoymenERP.Modules.Purchasing.Application.Ai;

/// <summary>
/// WP-A3b READ tool <c>purchasing.pending-receipts.v1</c> (bekleyen mal kabul = açık alış siparişi): APPROVED purchase order
/// lines that still wait for goods receipt, optionally for one supplier or product. KLN-D42
/// (SINAN_KARARLARI_2026-11-14_IZLENEBILIRLIK.md) reads "açık alış siparişi" as exactly this set — the same owner port the
/// shortage warning's supply side reads (<c>InventoryShortageSupplyHostSource</c>): a draft order is not open, a fully
/// received line is not open. Read ONLY through the Purchasing owner's open-line port
/// (<see cref="IPurchaseOpenLineReadPortV1.ListOpenApprovedLinesAsync"/>, AT-025: remaining = committed − accepted −
/// cancelled − closed − returned, the owner's own number, never recomputed here). The port carries no authority, so the
/// tool measures <c>purchasing.orders.read</c> company-wide before and after reading (the LOG-4 D6 shape).
/// <para>
/// Quantities stay in each line's own Settings unit: totals are given PER UNIT and never added across units, and no
/// kg ↔ piece conversion is made (a piece quantity exists only where the order line itself is in pieces). Price-free by the
/// port's construction. Over the scan cap the counts and totals are withheld and <c>complete</c> is false.
/// </para>
/// </summary>
public sealed class PurchasingPendingReceiptsAiTool(IPurchaseOpenLineReadPortV1 lines, IErpAuthorityRevalidationService authority,
    TimeProvider timeProvider) : IAiOwnerReadToolV1
{
    public const string ToolId = "purchasing.pending-receipts.v1";
    public const string ToolVersion = "1";
    public const string SupplierPartyIdField = "supplierPartyId";
    public const string ProductIdField = "productId";
    public const string LimitField = "limit";
    public const int DefaultLimit = 20;
    public const int MaximumLimit = 50;
    public const int PageSize = 100;
    public const int ScanLimit = 1000;
    public const string TargetKind = "purchasing.order-lines.pending-receipt";
    public const string Definition = "KLN-D42";
    private static readonly string[] Fields = [LimitField, ProductIdField, SupplierPartyIdField];
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
            InputSchema: "purchasing.pending-receipts.v1/input@1",
            OutputSchema: "purchasing.pending-receipts.v1/output@2",
            FieldPolicy: "purchasing.pending-receipts.v1/fields@1",
            LimitPolicy: "purchasing.pending-receipts.v1/limit-50-scan-1000@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+purchasing.orders.read@1",
            SourceContract: "IPurchaseOpenLineReadPortV1.ListOpenApprovedLinesAsync@v1",
            RedactionPolicy: "purchasing.pending-receipts.v1/price-free@1"));

    public async Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!AiReadArgumentsV1.TryParse(input.ArgumentsJson, Fields, out var arguments, out var invalid))
        {
            return Invalid(invalid);
        }

        if (!arguments.TryGuid(SupplierPartyIdField, out var supplierPartyId)) return Invalid(SupplierPartyIdField);
        if (!arguments.TryGuid(ProductIdField, out var productId)) return Invalid(ProductIdField);
        if (!arguments.TryInt(LimitField, 1, MaximumLimit, DefaultLimit, out var limit)) return Invalid(LimitField);

        if (await AiOwnerReadFenceV1.RequireCompanyWideAsync(authority, Permissions, context.CompanyId, cancellationToken) is { } refused)
        {
            return new AiReadResultV1(refused);
        }

        var read = new List<PurchaseOpenLineReferenceV1>();
        var complete = true;
        try
        {
            string? cursor = null;
            do
            {
                var page = await lines.ListOpenApprovedLinesAsync(PageSize, cursor, cancellationToken);
                foreach (var line in page.Items)
                {
                    if (line.CompanyId != context.CompanyId)
                    {
                        return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
                    }

                    read.Add(line);
                }

                cursor = page.NextCursor;
                if (cursor is not null && read.Count >= ScanLimit)
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

        if (read.Select(line => line.LineId).Distinct().Count() != read.Count)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        foreach (var line in read)
        {
            // The owner lists only lines with a remaining above zero; anything else is not a trustworthy answer.
            if (!AiExactDecimalV1.TryParse(line.RemainingText, out var remaining) || remaining <= 0m
                || !AiExactDecimalV1.TryParse(line.CommittedText, out _) || !AiExactDecimalV1.TryParse(line.AcceptedText, out _))
            {
                return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
            }
        }

        var matching = read
            .Where(line => (supplierPartyId is not { } supplier || line.SupplierPartyId == supplier)
                           && (productId is not { } product || line.ProductId == product))
            .OrderBy(line => line.OrderEffectiveOn).ThenBy(line => line.OrderId).ThenBy(line => line.LineNo)
            .ToArray();
        var perUnit = new List<PurchasingUnitTotalV1>();
        foreach (var group in matching.GroupBy(line => line.UnitId).OrderBy(group => group.Key))
        {
            if (!AiExactDecimalV1.TrySum(group.Select(line => line.RemainingText), out var remaining))
            {
                return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
            }

            perUnit.Add(new PurchasingUnitTotalV1(group.Key, group.Count(), AiExactDecimalV1.Format(remaining)));
        }

        var listed = matching.Take(limit).Select(line => new PurchasingPendingReceiptItemV1(
            line.OrderId, line.OrderEffectiveOn, line.LineId, line.LineNo, line.Kind, line.SupplierPartyId, line.ProductId,
            line.UnitId, Format(line.CommittedText), Format(line.AcceptedText), Format(line.RemainingText))).ToArray();
        var content = new PurchasingPendingReceiptsContentV1(
            Definition,
            supplierPartyId,
            productId,
            complete,
            complete ? matching.Length : null,
            complete ? matching.Select(line => line.OrderId).Distinct().Count() : null,
            complete ? perUnit : null,
            listed.Length,
            listed);
        var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, context.CompanyId, "Bekleyen mal kabul",
            AiSourceVersionV1.Of(read.OrderBy(line => line.LineId).Select(line => $"{line.LineId:D}:{line.RemainingText}")),
            timeProvider.GetUtcNow().ToUniversalTime(), AiDataClassV1.Internal);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source);
    }

    private static string Format(string text) =>
        AiExactDecimalV1.TryParse(text, out var value) ? AiExactDecimalV1.Format(value) : throw new FormatException();

    private static AiReadResultV1 Invalid(string field) => new(AiToolOutcomeV1.Invalid, invalidFields: [field]);
}

/// <summary>One unit's lines and their exact remaining total; quantities of different units are never added.</summary>
public sealed record PurchasingUnitTotalV1(Guid UnitId, int LineCount, string RemainingTotal);

/// <summary>
/// Counts and per-unit totals are null when <c>Complete</c> is false. <c>Definition</c> names the decision behind "açık alış
/// siparişi" (KLN-D42); <c>OrderCount</c> is the number of open purchase orders in that sense.
/// </summary>
public sealed record PurchasingPendingReceiptsContentV1(
    string Definition,
    Guid? SupplierPartyId,
    Guid? ProductId,
    bool Complete,
    int? LineCount,
    int? OrderCount,
    IReadOnlyList<PurchasingUnitTotalV1>? RemainingByUnit,
    int ListedCount,
    IReadOnlyList<PurchasingPendingReceiptItemV1> Items);

public sealed record PurchasingPendingReceiptItemV1(
    Guid OrderId,
    DateOnly OrderEffectiveOn,
    Guid LineId,
    int LineNo,
    string Kind,
    Guid SupplierPartyId,
    Guid ProductId,
    Guid UnitId,
    string Committed,
    string Accepted,
    string Remaining);

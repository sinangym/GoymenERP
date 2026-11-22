using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GoymenERP.Modules.Purchasing.Domain.Returns;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

// ALC-S5 (B-19; SAT-D306, SAT-D392, SAT-D393, SAT-D396): return to supplier of one received label.
// The return names the original goods acceptance; Inventory resolves the label and leaves stock at the ORIGINAL purchase line's
// unit cost (SAT-D306). SAT-D392: that unit cost is read from the invoice that bills the related order's receipt. SAT-D393: when it
// cannot be read the return carries a manual unit cost and waits for an exception approval (SAT-D396: purchasing.orders.approve,
// audited); without that approval no stock leaves. ALS-D91/D92: explicit purchase-unit undo metadata enables the genuine net
// receipt adjustment. Replacement-expected returns reopen that quantity; definitive returns retain a separate returned share.

/// <summary>
/// A supplier return of one goods acceptance (label). Ids are client-made (idempotent posting). <see cref="SourceBlock"/> is
/// <c>Free</c> or <c>Blocked</c> (B-19: which kilograms of the label leave). <see cref="ManualUnitCostTryPerKg"/> is only allowed
/// when the invoice unit cost cannot be read (SAT-D393).
/// </summary>
public sealed record CreatePurchaseSupplierReturnV1(
    Guid ReturnId, Guid ReturnLineId, Guid GoodsAcceptanceId, Guid InventoryMovementId, string SourceBlock, string QuantityKg,
    string? ManualUnitCostTryPerKg, bool ReplacementExpected, string Reason, DateTimeOffset OccurredAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExpectedPurchaseOrderVersion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? UndoQuantity = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PurchaseSupplierReturnCostAllocationV1? CostAllocation = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? SourceLabelId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PieceCount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<PurchaseSupplierReturnLineagePartRequestV1>? LineageParts = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? SupplierPieceQuantity = null);

/// <summary>An approval or a rejection of a pending cost exception; a rejection needs a reason.</summary>
public sealed record DecidePurchaseSupplierReturnV1(string ExpectedVersion, string? Reason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExpectedPurchaseOrderVersion = null);

public sealed record PurchaseSupplierReturnViewV1(
    Guid CompanyId, Guid ReturnId, Guid ReturnLineId, Guid PurchaseOrderId, Guid PurchaseOrderLineId, Guid GoodsAcceptanceId,
    Guid InventoryMovementId, string Status, string Version, string SourceBlock, string QuantityKg, string CostBasis,
    string? UnitCostTryPerKg, bool ReplacementExpected, string Reason, DateTimeOffset OccurredAtUtc, string RequestedBy,
    DateTimeOffset RequestedAtUtc, string? DecidedBy, DateTimeOffset? DecidedAtUtc, string? DecisionReason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExpectedPurchaseOrderVersion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? UndoQuantity = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PurchaseSupplierReturnCostAllocationV1? CostAllocation = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<PurchaseSupplierReturnInvoiceSourceLabelV1>? CostAllocationSources = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? SourceLabelId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LineageMode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PieceCount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<PurchaseSupplierReturnLineagePartViewV1>? LineageParts = null)
{
    /// <summary>ALS-D70: without a fresh price-read grant the unit cost is null and disclosure is Withheld.</summary>
    public string PriceDisclosure { get; init; } = PurchasingPriceDisclosureV1.Withheld;
}

public sealed record PurchaseSupplierReturnResponseV1(PurchaseSupplierReturnViewV1 Return, bool IsReplay, string OriginalCorrelationId);
public sealed record PurchaseSupplierReturnDetailV1(PurchaseSupplierReturnViewV1 Return);
public sealed record PurchaseSupplierReturnPageV1(IReadOnlyList<PurchaseSupplierReturnViewV1> Items, bool SearchWindowFull);

public interface IPurchaseSupplierReturnServiceV1
{
    Task<PurchaseSupplierReturnResponseV1> CreateSupplierReturnAsync(Guid orderId, CreatePurchaseSupplierReturnV1 request,
        PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<PurchaseSupplierReturnResponseV1> ApproveSupplierReturnCostExceptionAsync(Guid orderId, Guid returnId,
        DecidePurchaseSupplierReturnV1 request, PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<PurchaseSupplierReturnResponseV1> RejectSupplierReturnCostExceptionAsync(Guid orderId, Guid returnId,
        DecidePurchaseSupplierReturnV1 request, PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<PurchaseSupplierReturnDetailV1> GetSupplierReturnAsync(Guid orderId, Guid returnId, CancellationToken cancellationToken = default);
    Task<PurchaseSupplierReturnPageV1> GetSupplierReturnsAsync(Guid orderId, int limit = 50, CancellationToken cancellationToken = default);
    Task<PurchaseSupplierReturnCostSourcesV1> GetSupplierReturnCostSourcesAsync(Guid orderId, Guid goodsAcceptanceId,
        CancellationToken cancellationToken = default) =>
        Task.FromException<PurchaseSupplierReturnCostSourcesV1>(new PurchasingServiceException(503, PurchaseSupplierReturnCodesV1.Unavailable));

    /// <summary>AK-8b: the price-free plan of a return from a source label (mode, per-receipt distribution and open parts).</summary>
    Task<PurchaseSupplierReturnLineagePreviewV1> GetSupplierReturnLineagePreviewAsync(Guid orderId, Guid goodsAcceptanceId,
        Guid sourceLabelId, string quantityKg, int? pieceCount, CancellationToken cancellationToken = default) =>
        Task.FromException<PurchaseSupplierReturnLineagePreviewV1>(new PurchasingServiceException(503, PurchaseSupplierReturnCodesV1.LineageUnavailable));
}

public static class PurchaseSupplierReturnApiV1
{
    public const string ReturnsPath = PurchasingApiV1.OrderDetailPath + "/supplier-returns";
    public const string ReturnDetailPath = ReturnsPath + "/{returnId}";
    public const string CostExceptionApprovalPath = ReturnDetailPath + "/cost-exception/approval";
    public const string CostExceptionRejectionPath = ReturnDetailPath + "/cost-exception/rejection";
    public const string CostSourcesPath = PurchasingApiV1.OrderDetailPath + "/goods-receipts/{acceptanceId}/supplier-return-cost-sources";
    public const string LineagePreviewPath = PurchasingApiV1.OrderDetailPath + "/goods-receipts/{acceptanceId}/supplier-return-lineage-preview";
}

public static class PurchaseSupplierReturnCommandsV1
{
    public const string CreateV1 = "purchasing.supplier-return.create.v1";
    public const string ApproveCostExceptionV1 = "purchasing.supplier-return.cost-exception.approve.v1";
    public const string RejectCostExceptionV1 = "purchasing.supplier-return.cost-exception.reject.v1";
}

public static class PurchaseSupplierReturnCodesV1
{
    /// <summary>
    /// AK-3 (KLN-D119) 422: a supplier return of a piece purchase line names its returned pieces as a whole undo quantity (with the
    /// expected order version); a kg-only legacy return or a fraction of a piece is refused, never converted from kilograms.
    /// </summary>
    public const string PieceCountRequired = "purchasing_supplier_return_piece_count_required";

    /// <summary>The return flow is not wired (no store, no transaction host); 503, nothing recorded.</summary>
    public const string Unavailable = "purchasing_supplier_return_unavailable";

    /// <summary>SAT-D393: the invoice unit cost cannot be read and no manual unit cost was given; nothing leaves stock.</summary>
    public const string CostRequired = "purchasing_supplier_return_cost_required";

    /// <summary>SAT-D392: the invoice unit cost is readable, so a manual cost (the exception path) is not accepted.</summary>
    public const string InvoiceCostAvailable = "purchasing_supplier_return_invoice_cost_available";

    /// <summary>The invoice cost reader answered an unusable value (non-positive or too precise); 503, fail-closed.</summary>
    public const string InvoiceCostInvalid = "purchasing_supplier_return_invoice_cost_invalid";

    /// <summary>A replacement-expected return requires explicit purchase-unit undo quantity and the expected order version.</summary>
    public const string ReplacementUnsupported = "purchasing_supplier_return_replacement_unsupported";

    /// <summary>The return id is already used by another return.</summary>
    public const string ReturnIdTaken = "purchasing_supplier_return_id_taken";

    /// <summary>
    /// AK-8 503: a return from a foreign source label cannot be planned — the golden gate is closed (the earlier boundary), the
    /// lineage port is not wired, or the lineage, a receipt's supplier or a receipt's invoiced/pending history is unreadable.
    /// Nothing is recorded or moved.
    /// </summary>
    public const string LineageUnavailable = "purchasing_supplier_return_lineage_unavailable";

    /// <summary>
    /// AK-8 409 (SAT-D433/SAT-D441): the named goods acceptance cannot carry this return — in a lineage return it is not one of
    /// the label's receipts; in a quantity return the chosen order line is not a priced kilogram line in TL.
    /// </summary>
    public const string LineageReceiptMismatch = "purchasing_supplier_return_lineage_receipt_mismatch";

    /// <summary>
    /// AK-8b 409 (SAT-D433 step 2, SAT-D440, SAT-D442): the request's per-receipt parts are not the label's current distribution
    /// (a receipt is missing or extra, or a selection does not add up to the receipt's kilograms). The preview is read again.
    /// </summary>
    public const string LineagePartsMismatch = "purchasing_supplier_return_lineage_parts_mismatch";

    /// <summary>
    /// AK-8b 409 (K3-B2): a receipt has more than one open part (invoiced and invoice-pending, or several invoices) and the
    /// preparer did not choose between them. The system never assigns a part on the preparer's behalf.
    /// </summary>
    public const string LineageSelectionRequired = "purchasing_supplier_return_lineage_selection_required";

    /// <summary>
    /// AK-8b 409 (TI3, golden I07/I14): a receipt, or the chosen invoiced/pending part of it, has less open quantity than this
    /// return takes from it. Nothing is moved to another receipt or part.
    /// </summary>
    public const string LineageReceiptLimitExceeded = "purchasing_supplier_return_lineage_receipt_limit_exceeded";

    /// <summary>
    /// AK-8b 422 (SAT-D444/SAT-D451): the chosen order line's net unit price after its discount has more than four decimals. The
    /// stock exit carries four and the rounding of such a price is not decided, so the return is refused, never rounded. See
    /// <see cref="PurchaseSupplierReturnQuantityPriceRuleV1"/>, the single rule point.
    /// </summary>
    public const string QuantityPriceUndecided = "purchasing_supplier_return_quantity_price_undecided";

    /// <summary>
    /// AK-8b 409 (SAT-D444/SAT-D393): the moving average of a quantity return is readable, so a manual cost (the exception
    /// path) is not accepted.
    /// </summary>
    public const string QuantityAverageAvailable = "purchasing_supplier_return_quantity_average_available";

    /// <summary>
    /// AK-8b 409 (SAT-D371): on the exception path of a quantity return the manual stock cost differs from the chosen order's unit
    /// price. That is a known difference with no decided posting; it is refused, never recorded.
    /// </summary>
    public const string QuantityManualCostDifference = "purchasing_supplier_return_quantity_manual_cost_difference";

    /// <summary>
    /// SAT-D459 422: in a quantity return from a piece order line the pieces the preparer enters for the document must equal the
    /// pieces that leave the label; a different count, or a label that tracks no pieces, is refused.
    /// </summary>
    public const string QuantityPieceQuantityMismatch = "purchasing_supplier_return_quantity_piece_quantity_mismatch";

    /// <summary>
    /// SAT-D458 422: the chosen order carries a document-level discount. It is neither deducted from the line's net price nor
    /// spread over the lines, so a quantity return from that order stops here.
    /// </summary>
    public const string QuantityDocumentDiscount = "purchasing_supplier_return_quantity_document_discount";
}

/// <summary>
/// SAT-D392: the unit cost (TL per stock kg) of the invoice that bills one goods acceptance. Null means it cannot be read
/// (no invoice recorded, or not a kg-billed line); it is never zero.
/// </summary>
public sealed record PurchaseSupplierReturnInvoiceCostV1(decimal? UnitCostTryPerKg)
{
    public static PurchaseSupplierReturnInvoiceCostV1 Unavailable { get; } = new((decimal?)null);
}

/// <summary>
/// SAT-D392: reads the invoice unit cost of a goods acceptance. Purchasing records no supplier invoice yet, so until the slice that
/// records one registers a reader every return answers <see cref="PurchaseSupplierReturnInvoiceCostV1.Unavailable"/> and takes
/// the SAT-D393 exception path. No order price, receipt-day rate or average cost stands in for the invoice.
/// </summary>
public interface IPurchaseSupplierReturnInvoiceCostReadPortV1
{
    Task<PurchaseSupplierReturnInvoiceCostV1> ReadAsync(Guid companyId, Guid goodsAcceptanceId, CancellationToken cancellationToken);
}

/// <summary>
/// SAT-D396 policy inputs. <see cref="AllowSameActorCostExceptionDecision"/> stays false (the person who entered the return
/// cannot decide its exception) until the same-person approval question is decided; it is a configuration, not a default choice.
/// </summary>
public sealed record PurchaseSupplierReturnPolicyV1(bool AllowSameActorCostExceptionDecision,
    bool LineageGoldenAccepted = PurchaseSupplierReturnLineageGoldenV1.SupplierReturnLineageGoldenAccepted,
    bool QuantityRulesGoldenAccepted = PurchaseSupplierReturnLineageGoldenV1.SupplierReturnQuantityRulesGoldenAccepted)
{
    public static PurchaseSupplierReturnPolicyV1 Default { get; } = new(false);
}

/// <summary>The stock exit one return asks Inventory to stage (SAT-D306). Kg and the unit cost are canonical invariant texts.</summary>
public sealed record PurchaseSupplierReturnExitRequestV1(
    Guid CompanyId, Guid ReturnId, Guid ReturnLineId, Guid GoodsAcceptanceId, Guid InventoryMovementId, string SourceBlock,
    string QuantityKg, string UnitCostTryPerKg, DateTimeOffset OccurredAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PurchaseSupplierReturnCostAllocationV1? CostAllocation = null,
    Guid PurchaseOrderId = default, string? UndoQuantity = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? SourceLabelId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PurchaseSupplierReturnLineageModeV1? LineageMode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PieceCount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<PurchaseSupplierReturnLineagePartV1>? LineageParts = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? ApprovedCostException = null,
    // SAT-D456 (behind the quantity-rules gate): the kuruş-rounded amount the document bills; Inventory measures the difference on it.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? SupplierReturnAmountTry = null);

/// <summary><see cref="UnitCostTryPerKg"/>: the TL unit cost the exit actually left stock at (the moving average of an amount-measured quantity return).</summary>
public sealed record PurchaseSupplierReturnExitResultV1(Guid InventoryMovementId, Guid LabelId, string QuantityKg, bool IsReplay,
    string? UnitCostTryPerKg = null);

public interface IPurchaseSupplierReturnExitV1
{
    Task<PurchaseSupplierReturnExitResultV1> StageExitAsync(PurchaseSupplierReturnExitRequestV1 request, PurchasingWriteEnvelope envelope,
        CancellationToken cancellationToken);
}

/// <summary>
/// The supplier return host owns ONE transaction/connection for the return row and Inventory's exit. beforeCommit runs after the
/// stage, immediately before the sole commit; any throw rolls everything back. Inventory refusals surface as
/// <see cref="PurchasingServiceException"/> with Inventory's own status and code.
/// </summary>
public interface IPurchaseSupplierReturnTransactionV1
{
    Task<T> ExecuteAsync<T>(Guid companyId, Func<IPurchaseSupplierReturnExitV1, CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task> beforeCommit, CancellationToken cancellationToken = default);
}

/// <summary>A new return as the service decided it.</summary>
public sealed record PurchaseSupplierReturnDraft(
    Guid ReturnId, Guid ReturnLineId, Guid PurchaseOrderId, Guid PurchaseOrderLineId, Guid GoodsAcceptanceId, Guid InventoryMovementId,
    PurchaseSupplierReturnStatus Status, string SourceBlock, decimal QuantityKg, PurchaseSupplierReturnCostBasis CostBasis,
    decimal UnitCostTryPerKg, bool ReplacementExpected, string Reason, DateTimeOffset OccurredAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ExpectedPurchaseOrderVersion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? UndoQuantity = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PurchaseSupplierReturnCostAllocationV1? CostAllocation = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<PurchaseSupplierReturnInvoiceSourceLabelV1>? CostAllocationSources = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? SourceLabelId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PurchaseSupplierReturnLineageModeV1? LineageMode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PieceCount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<PurchaseSupplierReturnLineagePartV1>? LineageParts = null);

public sealed record StoredPurchaseSupplierReturn(
    Guid CompanyId, PurchaseSupplierReturnDraft Return, long Version, string RequestedBy, DateTimeOffset RequestedAtUtc,
    string? DecidedBy, DateTimeOffset? DecidedAtUtc, string? DecisionReason);

/// <summary>A same-key attempt already stored: the return it recorded, or its terminal refusal.</summary>
public sealed record PurchaseSupplierReturnReplay(Guid? ReturnId, PurchasingTerminalRefusal? Refusal, string OriginalCorrelationId,
    StoredPurchaseSupplierReturn? Original = null);

/// <summary>
/// The supplier return's own records (return, attempts, audit). Every write is ONE SaveChanges: the return change, its attempt and
/// its audit event commit together, inside the host transaction when one is open. Another company's return is null.
/// </summary>
public interface IPurchaseSupplierReturnStore
{
    Task<PurchaseSupplierReturnReplay?> FindReplayAsync(PurchasingWriteEnvelope envelope, string requestHash, string commandName,
        CancellationToken cancellationToken = default);
    Task<StoredPurchaseSupplierReturn?> FindAsync(Guid orderId, Guid returnId, CancellationToken cancellationToken = default);
    Task<bool> ReturnIdExistsAsync(Guid returnId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredPurchaseSupplierReturn>> ListForOrderAsync(Guid orderId, int take, CancellationToken cancellationToken = default);

    /// <summary>The order line a recorded goods acceptance of <paramref name="orderId"/> counts on, or null.</summary>
    Task<Guid?> FindAcceptanceLineAsync(Guid orderId, Guid goodsAcceptanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// AK-8: the supplier party of the order a recorded goods acceptance counts on (this company), or null when it is unknown or
    /// ambiguous. A store that cannot answer leaves every foreign-label return unplannable (fail closed).
    /// </summary>
    Task<Guid?> FindAcceptanceSupplierAsync(Guid goodsAcceptanceId, CancellationToken cancellationToken = default) =>
        Task.FromResult<Guid?>(null);

    /// <summary>
    /// AK-8b: the order and order line a recorded goods acceptance counts on (this company), or null when unknown or ambiguous.
    /// A lineage return records it for every receipt it takes from; that receipt may belong to another order of the supplier.
    /// </summary>
    Task<PurchaseSupplierReturnAcceptanceOwnerV1?> FindAcceptanceOwnerAsync(Guid goodsAcceptanceId, CancellationToken cancellationToken = default) =>
        Task.FromResult<PurchaseSupplierReturnAcceptanceOwnerV1?>(null);

    Task<StoredPurchaseSupplierReturn> SaveNewAsync(PurchaseSupplierReturnDraft draft, PurchasingWriteEnvelope envelope, string requestHash,
        string commandName, CancellationToken cancellationToken = default);
    Task<StoredPurchaseSupplierReturn> SaveDecisionAsync(Guid orderId, PurchaseSupplierReturn decided, long expectedVersion,
        PurchasingWriteEnvelope envelope, string requestHash, string commandName, CancellationToken cancellationToken = default);
    Task RecordRefusalAsync(PurchasingWriteEnvelope envelope, string requestHash, string commandName, PurchasingTerminalRefusal refusal,
        CancellationToken cancellationToken = default);
}

public static class PurchaseSupplierReturnJsonV1
{
    /// <summary>The source block names Inventory accepts (B-19).</summary>
    public static IReadOnlyList<string> SourceBlocks { get; } = ["Free", "Blocked"];

    private static readonly Regex Quantity = new("\\A(?:0|[1-9][0-9]{0,14})(?:\\.[0-9]{0,2}[1-9])?\\z", RegexOptions.CultureInvariant);
    private static readonly Regex UnitCost = new("\\A(?:0|[1-9][0-9]{0,13})(?:\\.[0-9]{0,3}[1-9])?\\z", RegexOptions.CultureInvariant);

    public static CreatePurchaseSupplierReturnV1 ReadCreate(string json)
    {
        var request = JsonSerializer.Deserialize<CreatePurchaseSupplierReturnV1>(json, PurchasingJson.Options)
            ?? throw new JsonException("Return body is required.");
        ValidateCreate(request);
        return request;
    }

    public static DecidePurchaseSupplierReturnV1 ReadDecision(string json)
    {
        var request = JsonSerializer.Deserialize<DecidePurchaseSupplierReturnV1>(json, PurchasingJson.Options)
            ?? throw new JsonException("Decision body is required.");
        ValidateDecision(request);
        return request;
    }

    public static void ValidateCreate(CreatePurchaseSupplierReturnV1 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ReturnId == Guid.Empty || request.ReturnLineId == Guid.Empty || request.GoodsAcceptanceId == Guid.Empty
            || request.InventoryMovementId == Guid.Empty || request.SourceBlock is null
            || !SourceBlocks.Contains(request.SourceBlock, StringComparer.Ordinal) || !IsPositiveQuantity(request.QuantityKg)
            || request.ManualUnitCostTryPerKg is not null && !IsPositiveUnitCost(request.ManualUnitCostTryPerKg)
            || (request.ExpectedPurchaseOrderVersion is null) != (request.UndoQuantity is null)
            || request.ExpectedPurchaseOrderVersion is not null && !Version(request.ExpectedPurchaseOrderVersion)
            || request.UndoQuantity is not null && !IsPositiveQuantity(request.UndoQuantity)
            || !BoundedReason(request.Reason, required: true)
            || request.OccurredAtUtc == default || request.OccurredAtUtc.Offset != TimeSpan.Zero
            || request.SourceLabelId == Guid.Empty
            || (request.PieceCount is not null || request.LineageParts is not null) && request.SourceLabelId is null
            || request.PieceCount is < 1 || !PurchaseSupplierReturnLineagePartsV1.ValidRequestShape(request)
            || request.SupplierPieceQuantity is not null && (request.SourceLabelId is null || request.SupplierPieceQuantity < 1))
        {
            throw new JsonException("Invalid return shape; identities, a block, a positive exact kg, a reason and UTC time are required.");
        }
        if (request.CostAllocation is { } allocation) PurchaseSupplierReturnCostAllocationContractV1.Validate(allocation, request.QuantityKg, request.UndoQuantity);
    }

    public static void ValidateDecision(DecidePurchaseSupplierReturnV1 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Version(request.ExpectedVersion) || !BoundedReason(request.Reason, required: false)
            || request.ExpectedPurchaseOrderVersion is not null && !Version(request.ExpectedPurchaseOrderVersion))
        {
            throw new JsonException("Invalid decision shape; a canonical version and a bounded reason are required.");
        }
    }

    /// <summary>A non-negative kg with at most three decimals in canonical form.</summary>
    public static bool IsQuantity(string? value) => value is not null && Quantity.IsMatch(value);

    /// <summary>A positive kg with at most three decimals in canonical form (Inventory's "0.###").</summary>
    public static bool IsPositiveQuantity(string? value) => value is not null && Quantity.IsMatch(value) && value != "0";

    /// <summary>A positive TL/kg unit cost with at most four decimals in canonical form (Inventory's "0.####").</summary>
    public static bool IsPositiveUnitCost(string? value) => value is not null && UnitCost.IsMatch(value) && value != "0";

    private static bool BoundedReason(string? value, bool required) => value is null
        ? !required
        : (!required || !string.IsNullOrWhiteSpace(value)) && value.Length <= PurchaseSupplierReturn.MaximumReasonLength
          && !value.Any(IsForbiddenControl);

    private static bool Version(string? value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
        && version >= 1 && version.ToString(CultureInfo.InvariantCulture) == value;

    private static bool IsForbiddenControl(char value) => char.IsControl(value) && value is not ('\n' or '\r' or '\t');
}

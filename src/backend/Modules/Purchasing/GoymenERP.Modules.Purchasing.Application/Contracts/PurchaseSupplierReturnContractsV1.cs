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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? UndoQuantity = null);

/// <summary>An approval or a rejection of a pending cost exception; a rejection needs a reason.</summary>
public sealed record DecidePurchaseSupplierReturnV1(string ExpectedVersion, string? Reason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExpectedPurchaseOrderVersion = null);

public sealed record PurchaseSupplierReturnViewV1(
    Guid CompanyId, Guid ReturnId, Guid ReturnLineId, Guid PurchaseOrderId, Guid PurchaseOrderLineId, Guid GoodsAcceptanceId,
    Guid InventoryMovementId, string Status, string Version, string SourceBlock, string QuantityKg, string CostBasis,
    string UnitCostTryPerKg, bool ReplacementExpected, string Reason, DateTimeOffset OccurredAtUtc, string RequestedBy,
    DateTimeOffset RequestedAtUtc, string? DecidedBy, DateTimeOffset? DecidedAtUtc, string? DecisionReason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExpectedPurchaseOrderVersion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? UndoQuantity = null);

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
}

public static class PurchaseSupplierReturnApiV1
{
    public const string ReturnsPath = PurchasingApiV1.OrderDetailPath + "/supplier-returns";
    public const string ReturnDetailPath = ReturnsPath + "/{returnId}";
    public const string CostExceptionApprovalPath = ReturnDetailPath + "/cost-exception/approval";
    public const string CostExceptionRejectionPath = ReturnDetailPath + "/cost-exception/rejection";
}

public static class PurchaseSupplierReturnCommandsV1
{
    public const string CreateV1 = "purchasing.supplier-return.create.v1";
    public const string ApproveCostExceptionV1 = "purchasing.supplier-return.cost-exception.approve.v1";
    public const string RejectCostExceptionV1 = "purchasing.supplier-return.cost-exception.reject.v1";
}

public static class PurchaseSupplierReturnCodesV1
{
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
public sealed record PurchaseSupplierReturnPolicyV1(bool AllowSameActorCostExceptionDecision)
{
    public static PurchaseSupplierReturnPolicyV1 Default { get; } = new(false);
}

/// <summary>The stock exit one return asks Inventory to stage (SAT-D306). Kg and the unit cost are canonical invariant texts.</summary>
public sealed record PurchaseSupplierReturnExitRequestV1(
    Guid CompanyId, Guid ReturnId, Guid ReturnLineId, Guid GoodsAcceptanceId, Guid InventoryMovementId, string SourceBlock,
    string QuantityKg, string UnitCostTryPerKg, DateTimeOffset OccurredAtUtc);

public sealed record PurchaseSupplierReturnExitResultV1(Guid InventoryMovementId, Guid LabelId, string QuantityKg, bool IsReplay);

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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? UndoQuantity = null);

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
            || request.OccurredAtUtc == default || request.OccurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new JsonException("Invalid return shape; identities, a block, a positive exact kg, a reason and UTC time are required.");
        }
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

using System.Globalization;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// B-19 / SAT-D306: a single-use Inventory participant borrowed by the purchasing host's already-open supplier-return
/// transaction. The host owns commit, rollback, authority and the connection lifetime; Inventory stages only the exit
/// movement, its command, audit and outbox facts.
/// </summary>
public interface IInventorySupplierReturnExitParticipantV1 : IAsyncDisposable
{
    Task<InventorySupplierReturnExitResultV1> StageAsync(
        InventorySupplierReturnExitRequestV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// AK-8b (SAT-D433): stages one exit per receipt of a multi-receipt lineage return under ONE command record. Inventory
    /// re-derives the distribution from the lineage it owns (SAT-D440, SAT-D442) and refuses a request that differs from it.
    /// </summary>
    Task<InventorySupplierReturnLineageExitResultV1> StageLineageAsync(
        InventorySupplierReturnLineageExitRequestV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The return names the original goods acceptance (SAT-D306 (a)); Inventory resolves the label, product, owner and
/// custodian from that receipt movement and the label's current balance, so a caller cannot pick another label. The
/// caller chooses only whether the free or the blocked kilograms leave (B-19). <see cref="OriginalUnitCostTryPerKg"/>
/// is the base-currency unit cost per stock kilogram booked at the original receipt (SAT-D306 (b)); null means that
/// cost is unavailable and is carried as unavailable, never as zero. <c>PieceCount</c> (KLN-D119, AK-2b) is the number of
/// pieces leaving a piece-tracking label: required for such a label and refused on a kg-only label (both 400
/// <see cref="InventorySupplierReturnExitCodesV1.InvalidQuantity"/>); a partial exit leaves at least one piece, a whole
/// exit takes every piece. Purchasing before AK-3/AK-8 omits it and kg-only labels return exactly as before.
/// </summary>
/// <remarks>
/// AK-3 (KLN-D119): <c>PieceCount</c> is the returned piece count of a piece-tracking label (a piece purchase line's undo quantity,
/// derived by the caller); null for a kg-only label, exactly as before. It is derived data and does not enter the payload hash.
/// AK-8 (SAT-D433/SAT-D441/SAT-D445): <c>SourceLabelId</c> names the label that physically leaves when it is not the receipt's own
/// label, and <c>LineageMode</c> says how (see <see cref="InventorySupplierReturnLineageModeV1"/>). Both are null on every earlier
/// request, so the payload hash of an earlier key is unchanged. They are honoured only behind
/// <c>SupplierReturnLineageGoldenV1.SupplierReturnLineageGoldenAccepted</c>; with the gate closed the earlier 503 stays.
/// AK-8b: <c>LineageSelection</c> is the preparer's explicit invoiced/pending choice inside the named receipt (K3-B2) for a
/// foreign-label exit; <c>ApprovedCostException</c> true says the unit cost is a manual cost approved by another person
/// (SAT-D393) because the moving average of a quantity return could not be read. <c>SupplierReturnAmountTry</c> (SAT-D456) is
/// the kuruş-rounded amount the document bills for a quantity return: with it the difference is measured between the
/// kuruş-rounded stock exit amount at the moving average and that amount, and the stock leaves at the average itself.
/// <c>ApprovedStockExitAmountTry</c> (SAT-D460) is the kuruş-rounded stock exit amount whose difference another person approved;
/// the exit proceeds only while the stock still leaves at exactly that amount. All are null on every earlier request.
/// </remarks>
public sealed record InventorySupplierReturnExitRequestV1(
    Guid CompanyId,
    Guid SupplierReturnId,
    Guid SupplierReturnLineId,
    Guid GoodsAcceptanceId,
    Guid InventoryMovementId,
    BlockState SourceBlock,
    string QuantityKg,
    string? OriginalUnitCostTryPerKg,
    DateTimeOffset OccurredAtUtc,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    InventorySupplierReturnCostAllocationV1? CostAllocation = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    InventorySupplierReturnPurchaseSourceV1? PurchaseSource = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? UndoQuantity = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    int? PieceCount = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    Guid? SourceLabelId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    InventorySupplierReturnLineageModeV1? LineageMode = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    InventorySupplierReturnPartSelectionV1? LineageSelection = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    bool? ApprovedCostException = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? SupplierReturnAmountTry = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? ApprovedStockExitAmountTry = null);

/// <summary>The exact Inventory identities staged by one supplier-return exit.</summary>
public sealed record InventorySupplierReturnExitResultV1(
    Guid CompanyId,
    Guid SupplierReturnId,
    Guid SupplierReturnLineId,
    Guid GoodsAcceptanceId,
    Guid InventoryMovementId,
    Guid OriginalReceiptMovementId,
    Guid LabelId,
    string LabelVersion,
    BlockState SourceBlock,
    string QuantityKg,
    string? OriginalUnitCostTryPerKg,
    Guid OutboxMessageId,
    bool IsReplay,
    string OriginalCorrelationId,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    int? PieceCount = null);

public static class InventorySupplierReturnExitCodesV1
{
    public const string InvalidRequest = "inventory_supplier_return_exit_invalid_request";
    public const string InvalidQuantity = "inventory_supplier_return_exit_quantity_invalid";
    public const string InvalidCost = "inventory_supplier_return_exit_cost_invalid";
    public const string InvalidReference = "inventory_supplier_return_exit_invalid_reference";
    public const string ReceiptNotRecorded = "inventory_supplier_return_exit_receipt_not_recorded";
    public const string ReceiptTraceInvalid = "inventory_supplier_return_exit_receipt_trace_invalid";
    public const string ExitExceedsReceipt = "inventory_supplier_return_exit_exceeds_receipt";
    public const string StockNotAvailable = "inventory_supplier_return_exit_stock_not_available";

    /// <summary>
    /// KLN-D93: the exit would take kilograms an active allocation (such as a sales order's) still holds on the label.
    /// The return never releases that allocation itself; it is released first by its own command, then the return is
    /// retried.
    /// </summary>
    public const string StockAllocated = "inventory_supplier_return_exit_stock_allocated";

    /// <summary>
    /// SAT-D410 preserves the SAT-D306 (c) stock-pool refusal: residual value at zero stock, or non-positive remaining
    /// value, requires an unposted difference. An approved manual exception cannot bypass it. SAT-D392's invoice-relative
    /// comparison is separate; merely differing from the moving average does not cause this refusal.
    /// </summary>
    public const string CostDifferenceUnposted = "inventory_supplier_return_exit_cost_difference_unposted";
    public const string CostAllocationRequired = "inventory_supplier_return_exit_cost_allocation_required";

    /// <summary>
    /// AK-8 (SAT-D433): a lineage-mode return names a source label whose lineage is not the named receipt (another root, or a
    /// label of another product or owner). Nothing is moved to another receipt.
    /// </summary>
    public const string LineageMismatch = "inventory_supplier_return_exit_lineage_mismatch";

    /// <summary>
    /// AK-8b (SAT-D441/SAT-D444) 409: a quantity return's stock value (the location/product moving average, or the chosen
    /// receipt's own cost) cannot be read and the request carries no approved manual cost. The SAT-D393 exception path applies;
    /// no value is written without that approval.
    /// </summary>
    public const string QuantityCostUnreadable = "inventory_supplier_return_exit_quantity_cost_unreadable";
    public const string IdempotencyConflict = "inventory_supplier_return_exit_idempotency_conflict";
    public const string ConcurrentChange = "inventory_supplier_return_exit_concurrent_change";
    public const string QuantityProfileUnavailable = "inventory_supplier_return_exit_quantity_profile_unavailable";
    public const string Unavailable = "inventory_supplier_return_exit_unavailable";

    public static IReadOnlySet<string> Status400Codes { get; } = Set(InvalidRequest, InvalidQuantity, InvalidCost);
    public static IReadOnlySet<string> Status404Codes { get; } = Set(InvalidReference);
    public static IReadOnlySet<string> Status409Codes { get; } = Set(
        ReceiptNotRecorded, ExitExceedsReceipt, StockNotAvailable, StockAllocated, CostDifferenceUnposted, CostAllocationRequired,
        IdempotencyConflict, ConcurrentChange, LineageMismatch, QuantityCostUnreadable);
    public static IReadOnlySet<string> Status503Codes { get; } = Set(
        ReceiptTraceInvalid, QuantityProfileUnavailable, Unavailable);

    public static bool IsDefined(int status, string code) => status switch
    {
        400 => Status400Codes.Contains(code),
        404 => Status404Codes.Contains(code),
        409 => Status409Codes.Contains(code),
        503 => Status503Codes.Contains(code),
        _ => false,
    };

    private static IReadOnlySet<string> Set(params string[] codes) =>
        codes.ToHashSet(StringComparer.Ordinal);
}

public sealed class InventorySupplierReturnExitException : Exception
{
    private InventorySupplierReturnExitException(int status, string code, Exception? innerException = null) : base(code, innerException)
    {
        Status = status;
        Code = code;
    }

    public int Status { get; }
    public string Code { get; }

    public static InventorySupplierReturnExitException Refused(int status, string code, Exception? innerException = null) =>
        InventorySupplierReturnExitCodesV1.IsDefined(status, code)
            ? new(status, code, innerException)
            : throw new InvalidOperationException("Inventory supplier-return exit refusal is outside its closed contract.");
}

/// <summary>The validated quantity and the optional original unit cost of one supplier-return exit.</summary>
public sealed record InventorySupplierReturnExitInput(Kilograms Quantity, decimal? OriginalUnitCostTryPerKg,
    PieceCount? Pieces = null);

public static class InventorySupplierReturnExitContractV1
{
    public const string SourceKind = "purchasing.supplier-return";
    public const string CommandName = "inventory.supplier-return-exit.stage.v1";
    public const string LineageCommandName = "inventory.supplier-return-lineage-exit.stage.v1";
    public const int MaximumLineageParts = 64;
    private const int IdentityMaximumLength = 128;
    private const int NumberMaximumLength = 40;

    public static InventorySupplierReturnExitInput Validate(
        InventorySupplierReturnExitRequestV1 request,
        InventoryCommandMetadata metadata,
        Guid enlistedCompanyId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        if (enlistedCompanyId == Guid.Empty || request.CompanyId == Guid.Empty
            || request.SupplierReturnId == Guid.Empty || request.SupplierReturnLineId == Guid.Empty
            || request.GoodsAcceptanceId == Guid.Empty || request.InventoryMovementId == Guid.Empty
            || request.SupplierReturnId == request.InventoryMovementId
            || request.SupplierReturnLineId == request.InventoryMovementId
            || request.SourceBlock is not (BlockState.Free or BlockState.Blocked)
            || request.OccurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw Refused(400, InventorySupplierReturnExitCodesV1.InvalidRequest);
        }

        if (request.CompanyId != enlistedCompanyId)
        {
            throw Refused(404, InventorySupplierReturnExitCodesV1.InvalidReference);
        }

        RequireMetadata(metadata);
        if (!TryCanonical(request.QuantityKg, "0.###", out var parsed))
        {
            throw Refused(400, InventorySupplierReturnExitCodesV1.InvalidQuantity);
        }

        Kilograms quantity;
        try
        {
            quantity = Kilograms.Positive(parsed);
        }
        catch (InventoryRuleException)
        {
            throw Refused(400, InventorySupplierReturnExitCodesV1.InvalidQuantity);
        }

        decimal? cost = null;
        if (request.OriginalUnitCostTryPerKg is not null)
        {
            // A zero cost is "cost unknown" (Sinan 2026-11-11, SAT-D304 (2)): an unknown cost is sent as null.
            if (!TryCanonical(request.OriginalUnitCostTryPerKg, "0.####", out var parsedCost)
                || parsedCost <= 0m || !AverageCostPrecision.IsAtInternalScale(parsedCost))
            {
                throw Refused(400, InventorySupplierReturnExitCodesV1.InvalidCost);
            }

            cost = parsedCost;
        }

        if (request.SupplierReturnAmountTry is not null
            && (!TryCanonical(request.SupplierReturnAmountTry, "0.##", out var amount) || amount <= 0m))
        {
            throw Refused(400, InventorySupplierReturnExitCodesV1.InvalidCost);
        }

        if (request.ApprovedStockExitAmountTry is not null
            && (!TryCanonical(request.ApprovedStockExitAmountTry, "0.##", out var approvedAmount) || approvedAmount <= 0m
                || request.SupplierReturnAmountTry is null || request.ApprovedCostException != true))
        {
            throw Refused(400, InventorySupplierReturnExitCodesV1.InvalidCost);
        }

        if (request.PieceCount is < 0)
        {
            throw Refused(400, InventorySupplierReturnExitCodesV1.InvalidQuantity);
        }

        return new InventorySupplierReturnExitInput(quantity, cost,
            request.PieceCount is { } pieces ? PieceCount.Of(pieces) : null);
    }

    private static bool TryCanonical(string? value, string format, out decimal parsed)
    {
        parsed = 0m;
        return !string.IsNullOrEmpty(value) && value.Length <= NumberMaximumLength
            && value.All(character => char.IsAsciiDigit(character) || character == '.')
            && decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out parsed)
            && string.Equals(parsed.ToString(format, CultureInfo.InvariantCulture), value, StringComparison.Ordinal);
    }

    private static void RequireMetadata(InventoryCommandMetadata metadata)
    {
        if (!ValidIdentity(metadata.ActorId) || !ValidIdentity(metadata.PermissionCode)
            || !ValidIdentity(metadata.CorrelationId) || !ValidIdentity(metadata.IdempotencyKey)
            || !ValidIdentity(metadata.SourceChannel)
            || metadata.CausationId is not null && !ValidIdentity(metadata.CausationId)
            || metadata.RoleCodes is null || metadata.RoleCodes.Any(role => !ValidIdentity(role)))
        {
            throw Refused(400, InventorySupplierReturnExitCodesV1.InvalidRequest);
        }
    }

    private static bool ValidIdentity(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= IdentityMaximumLength
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && !value.Any(char.IsControl);

    private static InventorySupplierReturnExitException Refused(int status, string code) =>
        InventorySupplierReturnExitException.Refused(status, code);
}

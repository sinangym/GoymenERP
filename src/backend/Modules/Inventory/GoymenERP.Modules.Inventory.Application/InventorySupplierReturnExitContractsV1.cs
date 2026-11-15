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
}

/// <summary>
/// The return names the original goods acceptance (SAT-D306 (a)); Inventory resolves the label, product, owner and
/// custodian from that receipt movement and the label's current balance, so a caller cannot pick another label. The
/// caller chooses only whether the free or the blocked kilograms leave (B-19). <see cref="OriginalUnitCostTryPerKg"/>
/// is the base-currency unit cost per stock kilogram booked at the original receipt (SAT-D306 (b)); null means that
/// cost is unavailable and is carried as unavailable, never as zero.
/// </summary>
public sealed record InventorySupplierReturnExitRequestV1(
    Guid CompanyId,
    Guid SupplierReturnId,
    Guid SupplierReturnLineId,
    Guid GoodsAcceptanceId,
    Guid InventoryMovementId,
    BlockState SourceBlock,
    string QuantityKg,
    string? OriginalUnitCostTryPerKg,
    DateTimeOffset OccurredAtUtc);

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
    string OriginalCorrelationId);

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
    public const string IdempotencyConflict = "inventory_supplier_return_exit_idempotency_conflict";
    public const string ConcurrentChange = "inventory_supplier_return_exit_concurrent_change";
    public const string QuantityProfileUnavailable = "inventory_supplier_return_exit_quantity_profile_unavailable";
    public const string Unavailable = "inventory_supplier_return_exit_unavailable";

    public static IReadOnlySet<string> Status400Codes { get; } = Set(InvalidRequest, InvalidQuantity, InvalidCost);
    public static IReadOnlySet<string> Status404Codes { get; } = Set(InvalidReference);
    public static IReadOnlySet<string> Status409Codes { get; } = Set(
        ReceiptNotRecorded, ExitExceedsReceipt, StockNotAvailable, IdempotencyConflict, ConcurrentChange);
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
    private InventorySupplierReturnExitException(int status, string code) : base(code)
    {
        Status = status;
        Code = code;
    }

    public int Status { get; }
    public string Code { get; }

    public static InventorySupplierReturnExitException Refused(int status, string code) =>
        InventorySupplierReturnExitCodesV1.IsDefined(status, code)
            ? new(status, code)
            : throw new InvalidOperationException("Inventory supplier-return exit refusal is outside its closed contract.");
}

/// <summary>The validated quantity and the optional original unit cost of one supplier-return exit.</summary>
public sealed record InventorySupplierReturnExitInput(Kilograms Quantity, decimal? OriginalUnitCostTryPerKg);

public static class InventorySupplierReturnExitContractV1
{
    public const string SourceKind = "purchasing.supplier-return";
    public const string CommandName = "inventory.supplier-return-exit.stage.v1";
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

        return new InventorySupplierReturnExitInput(quantity, cost);
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

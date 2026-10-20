using System.Globalization;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// A single-use Inventory participant enlisted in the host's goods-receipt transaction. The host
/// calls Purchasing's acceptance participant and this participant with the same acceptance facts,
/// then owns commit/rollback. Inventory stages only its lot, label, receipt movement, balance,
/// command and audit rows; it never commits the borrowed transaction.
/// </summary>
public interface IInventoryGoodsReceiptParticipantV1 : IAsyncDisposable
{
    /// <summary>
    /// Reads the immutable Inventory side of a committed same-key receipt before mutable owner
    /// masters are consulted again. The participant remains single-use: the caller compares its
    /// current physical fields with <see cref="InventoryGoodsReceiptSourceV1.OriginalRequest"/>
    /// and then calls <see cref="StageAsync"/>, whose payload hash is still the final replay fence.
    /// </summary>
    Task<InventoryGoodsReceiptSourceV1?> ReadReplayAsync(
        Guid acceptanceId,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<InventoryGoodsReceiptResultV1> StageAsync(
        InventoryGoodsReceiptRequestV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Inventory-owned, company-scoped source read for reopening one Purchasing acceptance. This port
/// publishes no HTTP route and grants no authority. Its consumer must apply its existing Purchasing
/// read permission and fresh company/actor fences around the awaited call. Missing and foreign-company
/// sources are the same null result; an internally inconsistent committed source fails closed.
/// </summary>
public interface IInventoryGoodsReceiptSourceV1
{
    Task<InventoryGoodsReceiptSourceV1?> ReadAsync(
        Guid acceptanceId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The original physical request reconstructed from Inventory's immutable command, receipt movement,
/// line, label and lot rows, together with the original recorded result. No idempotency key, actor or
/// payload hash is exposed by the ordinary reopen read.
/// </summary>
public sealed record InventoryGoodsReceiptSourceV1(
    InventoryGoodsReceiptRequestV1 OriginalRequest,
    InventoryGoodsReceiptResultV1 OriginalResult);

/// <summary>
/// Explicit physical facts for one accepted Purchasing line. Purchase quantity remains Purchasing's
/// fact; <paramref name="QuantityKg"/> is the separately measured Inventory quantity. No conversion,
/// tolerance, quality or initial block policy is inferred here. Owner, custodian, location and block
/// must be supplied from already-authorized server-side facts. <paramref name="PurchaseOrderVersionAfter"/>
/// is Purchasing's returned post-stage version, never the host request's pre-stage expected version.
/// </summary>
public sealed record InventoryGoodsReceiptRequestV1(
    Guid CompanyId,
    Guid AcceptanceId,
    Guid PurchaseOrderId,
    Guid PurchaseOrderLineId,
    string PurchaseOrderVersionAfter,
    Guid ProductId,
    string ExpectedProductVersion,
    Guid PurchaseUnitId,
    Guid LotId,
    Guid LabelId,
    string LabelCode,
    string? HeatNumber,
    string? CoilNumber,
    IReadOnlyList<string> RequiredDocuments,
    StockPartyKind OwnerKind,
    Guid OwnerId,
    StockPartyKind CustodianKind,
    Guid CustodianId,
    Guid LocationId,
    BlockState InitialBlock,
    Guid MovementId,
    string QuantityKg,
    DateTimeOffset OccurredAtUtc);

public sealed record InventoryGoodsReceiptResultV1(
    Guid CompanyId,
    Guid AcceptanceId,
    Guid PurchaseOrderId,
    Guid PurchaseOrderLineId,
    string PurchaseOrderVersion,
    Guid ProductId,
    string ProductVersion,
    Guid PurchaseUnitId,
    Guid LotId,
    Guid LabelId,
    string LabelVersion,
    Guid MovementId,
    string QuantityKg,
    InventoryGoodsReceiptQuantityV2? StockItemQuantity,
    bool IsReplay,
    string OriginalCorrelationId);

public static class InventoryGoodsReceiptCodesV1
{
    public const string InvalidRequest = "inventory_goods_receipt_invalid_request";
    public const string InvalidVersion = "inventory_goods_receipt_version_invalid";
    public const string InvalidQuantity = "inventory_goods_receipt_quantity_invalid";
    public const string InvalidReference = "inventory_goods_receipt_invalid_reference";
    public const string LabelAlreadyRegistered = "inventory_goods_receipt_label_already_registered";
    public const string LotOrLabelConflict = "inventory_goods_receipt_lot_or_label_conflict";
    public const string IdempotencyConflict = "inventory_goods_receipt_idempotency_conflict";
    public const string ConcurrentChange = "inventory_goods_receipt_concurrent_change";
    public const string QuantityProfileUnavailable = "inventory_goods_receipt_quantity_profile_unavailable";
    public const string Unavailable = "inventory_goods_receipt_unavailable";

    public static bool IsDefined(int status, string code) => status switch
    {
        400 => code is InvalidRequest or InvalidVersion or InvalidQuantity,
        404 => code == InvalidReference,
        409 => code is LabelAlreadyRegistered or LotOrLabelConflict or IdempotencyConflict or ConcurrentChange,
        503 => code is QuantityProfileUnavailable or Unavailable,
        _ => false,
    };
}

public sealed class InventoryGoodsReceiptException : Exception
{
    private InventoryGoodsReceiptException(int status, string code) : base(code)
    {
        Status = status;
        Code = code;
    }

    public int Status { get; }
    public string Code { get; }

    public static InventoryGoodsReceiptException Refused(int status, string code) =>
        InventoryGoodsReceiptCodesV1.IsDefined(status, code)
            ? new(status, code)
            : throw new InvalidOperationException("Inventory goods-receipt refusal is outside its closed contract.");
}

public static class InventoryGoodsReceiptContractV1
{
    private const int IdentityMaximumLength = 128;
    public const string SourceKind = "purchasing.goods-acceptance";
    public const string CommandName = "inventory.goods-receipt.stage.v1";

    public static Kilograms Validate(InventoryGoodsReceiptRequestV1 request, InventoryCommandMetadata metadata,
        Guid enlistedCompanyId)
    {
        var quantity = ValidateRequest(request, enlistedCompanyId);
        ArgumentNullException.ThrowIfNull(metadata);
        RequireMetadata(metadata);
        return quantity;
    }

    /// <summary>Validates the physical request independently of the current command envelope.</summary>
    public static Kilograms ValidateRequest(InventoryGoodsReceiptRequestV1 request, Guid enlistedCompanyId)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (enlistedCompanyId == Guid.Empty || request.CompanyId == Guid.Empty
            || request.AcceptanceId == Guid.Empty || request.PurchaseOrderId == Guid.Empty
            || request.PurchaseOrderLineId == Guid.Empty || request.ProductId == Guid.Empty
            || request.PurchaseUnitId == Guid.Empty || request.LotId == Guid.Empty || request.LabelId == Guid.Empty
            || request.OwnerId == Guid.Empty || request.CustodianId == Guid.Empty || request.LocationId == Guid.Empty
            || request.MovementId == Guid.Empty || request.RequiredDocuments is null
            || !Enum.IsDefined(request.OwnerKind) || !Enum.IsDefined(request.CustodianKind)
            || !Enum.IsDefined(request.InitialBlock) || request.OccurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw Refused(400, InventoryGoodsReceiptCodesV1.InvalidRequest);
        }

        if (request.CompanyId != enlistedCompanyId)
        {
            throw Refused(404, InventoryGoodsReceiptCodesV1.InvalidReference);
        }

        RequireVersion(request.PurchaseOrderVersionAfter);
        RequireToken(request.ExpectedProductVersion, 64);
        RequireToken(request.LabelCode, 64);
        RequireOptional(request.HeatNumber, 64);
        RequireOptional(request.CoilNumber, 64);
        foreach (var document in request.RequiredDocuments)
        {
            RequireToken(document, 64);
        }

        if (string.IsNullOrEmpty(request.QuantityKg) || request.QuantityKg.Length > 40
            || request.QuantityKg.Any(character => !char.IsAsciiDigit(character) && character != '.')
            || !decimal.TryParse(request.QuantityKg, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var quantity)
            || !string.Equals(quantity.ToString("0.###", CultureInfo.InvariantCulture), request.QuantityKg,
                StringComparison.Ordinal))
        {
            throw Refused(400, InventoryGoodsReceiptCodesV1.InvalidQuantity);
        }

        try
        {
            return Kilograms.Positive(quantity);
        }
        catch (InventoryRuleException)
        {
            throw Refused(400, InventoryGoodsReceiptCodesV1.InvalidQuantity);
        }
    }

    /// <summary>Validates the identity needed to look up a same-key replay without its payload.</summary>
    public static void ValidateReplayLookup(Guid acceptanceId, InventoryCommandMetadata metadata,
        Guid enlistedCompanyId)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (acceptanceId == Guid.Empty || enlistedCompanyId == Guid.Empty)
        {
            throw Refused(400, InventoryGoodsReceiptCodesV1.InvalidRequest);
        }

        RequireMetadata(metadata);
    }

    private static void RequireVersion(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 19 || value[0] is < '1' or > '9'
            || value.Any(character => character is < '0' or > '9')
            || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            throw Refused(400, InventoryGoodsReceiptCodesV1.InvalidVersion);
        }
    }

    private static void RequireToken(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal) || value.Any(char.IsControl))
        {
            throw Refused(400, InventoryGoodsReceiptCodesV1.InvalidRequest);
        }
    }

    private static void RequireOptional(string? value, int maximumLength)
    {
        if (value is not null)
        {
            RequireToken(value, maximumLength);
        }
    }

    private static void RequireMetadata(InventoryCommandMetadata metadata)
    {
        RequireIdentity(metadata.ActorId);
        RequireIdentity(metadata.PermissionCode);
        RequireIdentity(metadata.CorrelationId);
        RequireIdentity(metadata.IdempotencyKey);
        RequireIdentity(metadata.SourceChannel);
        if (metadata.CausationId is not null) RequireIdentity(metadata.CausationId);
        if (metadata.RoleCodes is null || metadata.RoleCodes.Any(role => !ValidIdentity(role)))
        {
            throw Refused(400, InventoryGoodsReceiptCodesV1.InvalidRequest);
        }
    }

    private static void RequireIdentity(string? value)
    {
        if (!ValidIdentity(value)) throw Refused(400, InventoryGoodsReceiptCodesV1.InvalidRequest);
    }

    private static bool ValidIdentity(string? value) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= IdentityMaximumLength
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && !value.Any(char.IsControl);

    private static InventoryGoodsReceiptException Refused(int status, string code) =>
        InventoryGoodsReceiptException.Refused(status, code);
}

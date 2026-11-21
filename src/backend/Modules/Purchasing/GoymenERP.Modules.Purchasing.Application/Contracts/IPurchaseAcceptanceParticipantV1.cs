using System.Globalization;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// Purchasing-owned participant used by a host transaction to durably record one goods-acceptance share.
/// The host owns commit/rollback and the open connection; the participant only stages Purchasing rows and audit.
/// </summary>
public interface IPurchaseAcceptanceParticipantV1 : IAsyncDisposable
{
    /// <summary>
    /// Read-only receipt lock target on the borrowed connection, before any stage. No order-state or expected-version
    /// decision is made here: immutable receipt replays remain valid after a later order revision or cancellation.
    /// </summary>
    Task<PurchaseAcceptanceReceiptTargetV1?> ResolveReceiptTargetAsync(Guid purchaseOrderId, Guid purchaseOrderLineId,
        CancellationToken cancellationToken = default);

    Task<PurchaseAcceptanceStageResultV1> StageAsync(
        PurchaseAcceptanceStageRequestV1 request,
        PurchasingWriteEnvelope envelope,
        CancellationToken cancellationToken = default);
}

/// <summary>Purchasing-owned identities and current version; a technical receipt names its owning general line.</summary>
public sealed record PurchaseAcceptanceReceiptTargetV1(Guid CompanyId, Guid PurchaseOrderId,
    Guid PurchaseOrderLineId, Guid PurchaseOrderGeneralLineId, string ObservedOrderVersion);

public sealed record PurchaseAcceptanceStageRequestV1(
    Guid CompanyId,
    Guid AcceptanceId,
    Guid PurchaseOrderId,
    Guid PurchaseOrderLineId,
    string ExpectedPurchaseOrderVersion,
    Guid ProductId,
    string ExpectedProductVersion,
    Guid UnitId,
    string AcceptedQuantity,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// The staged acceptance share. F-017 F17-1 (ALIS_F017 v1 §3.4/§5): <see cref="PurchaseOrderGeneralLineId"/> is the purchase GENERAL
/// line whose serialization lock the participant took — the accepted line itself, or its general line when a technical slice was
/// accepted — so the host's Inventory side converts the future allocations of that general line in the same transaction. The field
/// is additive and trailing (a result recorded before F17-1 deserializes it as null and the participant resolves it from the line).
/// </summary>
public sealed record PurchaseAcceptanceStageResultV1(
    Guid CompanyId,
    Guid AcceptanceId,
    Guid PurchaseOrderId,
    Guid PurchaseOrderLineId,
    string PurchaseOrderVersion,
    string AcceptedQuantity,
    string RemainingQuantity,
    bool IsReplay,
    string OriginalCorrelationId,
    Guid? PurchaseOrderGeneralLineId = null);

public sealed record ValidatedPurchaseAcceptanceV1(
    long ExpectedPurchaseOrderVersion,
    decimal AcceptedQuantity);

public static class PurchaseAcceptanceParticipantContractV1
{
    public const int ProductVersionMaximumLength = 64;

    public static ValidatedPurchaseAcceptanceV1 Validate(PurchaseAcceptanceStageRequestV1 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CompanyId == Guid.Empty || request.AcceptanceId == Guid.Empty || request.PurchaseOrderId == Guid.Empty
            || request.PurchaseOrderLineId == Guid.Empty || request.ProductId == Guid.Empty || request.UnitId == Guid.Empty)
        {
            throw new PurchaseAcceptanceParticipantException(
                PurchaseAcceptanceParticipantCodes.InvalidRequest,
                400,
                "Kabul isteğinin şirket, kabul, sipariş, satır, ürün ve birim kimlikleri dolu olmalıdır.");
        }

        if (!long.TryParse(request.ExpectedPurchaseOrderVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedVersion)
            || expectedVersion < 1
            || !string.Equals(expectedVersion.ToString(CultureInfo.InvariantCulture), request.ExpectedPurchaseOrderVersion, StringComparison.Ordinal))
        {
            throw new PurchaseAcceptanceParticipantException(
                PurchaseAcceptanceParticipantCodes.InvalidVersion,
                400,
                "Sipariş sürümü kanonik pozitif tam sayı olmalıdır.");
        }

        if (string.IsNullOrWhiteSpace(request.ExpectedProductVersion)
            || !string.Equals(request.ExpectedProductVersion, request.ExpectedProductVersion.Trim(), StringComparison.Ordinal)
            || request.ExpectedProductVersion.Length > ProductVersionMaximumLength)
        {
            throw new PurchaseAcceptanceParticipantException(
                PurchaseAcceptanceParticipantCodes.InvalidRequest,
                400,
                "Ürün sürümü kanonik ve en fazla 64 karakter olmalıdır.");
        }

        if (!decimal.TryParse(request.AcceptedQuantity, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var accepted)
            || accepted <= 0m
            || !string.Equals(FormatQuantity(accepted), request.AcceptedQuantity, StringComparison.Ordinal))
        {
            throw new PurchaseAcceptanceParticipantException(
                PurchaseAcceptanceParticipantCodes.InvalidQuantity,
                400,
                "Kabul miktarı kanonik ve pozitif ondalık olmalıdır.");
        }

        if (request.OccurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new PurchaseAcceptanceParticipantException(
                PurchaseAcceptanceParticipantCodes.InvalidRequest,
                400,
                "Kabul zamanı UTC olmalıdır.");
        }

        return new ValidatedPurchaseAcceptanceV1(expectedVersion, accepted);
    }

    public static string FormatQuantity(decimal value) =>
        value.ToString("0.#############################", CultureInfo.InvariantCulture);
}

public static class PurchaseAcceptanceParticipantCodes
{
    public const string InvalidRequest = "purchasing_acceptance_invalid_request";
    public const string InvalidVersion = "purchasing_acceptance_invalid_version";
    public const string InvalidQuantity = "purchasing_acceptance_invalid_quantity";
    public const string InvalidReference = "purchasing_acceptance_invalid_reference";
    public const string OrderVersionConflict = "purchasing_acceptance_order_version_conflict";
    public const string ProductVersionConflict = "purchasing_acceptance_product_version_conflict";
    public const string UnitConflict = "purchasing_acceptance_unit_conflict";
    public const string OrderStateConflict = "purchasing_acceptance_order_state_conflict";
    public const string QuantityExceedsRemaining = "purchasing_acceptance_quantity_exceeds_remaining";
    public const string IdempotencyConflict = "purchasing_acceptance_idempotency_conflict";
    public const string ConcurrentChange = "purchasing_acceptance_concurrent_change";
    public const string Unavailable = "purchasing_acceptance_unavailable";

    /// <summary>
    /// F-017 §3.4: the purchase general line's serialization lock was not granted in time; nothing was staged (409). The same
    /// code as <c>PurchaseLineSerializationLockContractV1.BusyCode</c>.
    /// </summary>
    public const string LineBusy = "purchasing_line_busy";
}

public sealed class PurchaseAcceptanceParticipantException(
    string code,
    int statusCode,
    string message,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;

    public int StatusCode { get; } = statusCode;
}

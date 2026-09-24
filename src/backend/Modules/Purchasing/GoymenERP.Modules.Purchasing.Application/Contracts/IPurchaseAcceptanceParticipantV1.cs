using System.Globalization;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// Purchasing-owned participant used by a host transaction to durably record one goods-acceptance share.
/// The host owns commit/rollback and the open connection; the participant only stages Purchasing rows and audit.
/// </summary>
public interface IPurchaseAcceptanceParticipantV1 : IAsyncDisposable
{
    Task<PurchaseAcceptanceStageResultV1> StageAsync(
        PurchaseAcceptanceStageRequestV1 request,
        PurchasingWriteEnvelope envelope,
        CancellationToken cancellationToken = default);
}

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

public sealed record PurchaseAcceptanceStageResultV1(
    Guid CompanyId,
    Guid AcceptanceId,
    Guid PurchaseOrderId,
    Guid PurchaseOrderLineId,
    string PurchaseOrderVersion,
    string AcceptedQuantity,
    string RemainingQuantity,
    bool IsReplay,
    string OriginalCorrelationId);

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

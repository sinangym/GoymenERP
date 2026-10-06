using System.Globalization;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Quantities;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// A single-use Inventory participant borrowed by the central host's already-open customer-return
/// approval transaction. The host owns commit, rollback, authority and the connection lifetime;
/// Inventory stages only its compensating movement, command, audit and outbox facts.
/// </summary>
public interface IInventoryCustomerReturnParticipantV1 : IAsyncDisposable
{
    Task<InventoryCustomerReturnResultV1> StageAsync(
        InventoryCustomerReturnRequestV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Inventory resolves every physical fact from the immutable definite-shipment movement. A caller
/// cannot choose a label, product, owner, custodian, location, block state or disposition.
/// </summary>
public sealed record InventoryCustomerReturnRequestV1(
    Guid CompanyId,
    Guid CustomerReturnId,
    Guid SourceShipmentId,
    Guid LoadingInstructionLineId,
    Guid InventoryMovementId,
    string QuantityKg,
    DateTimeOffset OccurredAtUtc);

/// <summary>The exact Inventory identities staged by one approved customer return.</summary>
public sealed record InventoryCustomerReturnResultV1(
    Guid CompanyId,
    Guid CustomerReturnId,
    Guid SourceShipmentId,
    Guid InventoryMovementId,
    Guid OriginalShipmentMovementId,
    Guid OriginalLabelId,
    string OriginalLabelVersion,
    string QuantityKg,
    Guid OutboxMessageId,
    bool IsReplay,
    string OriginalCorrelationId);

public static class InventoryCustomerReturnCodesV1
{
    public const string InvalidRequest = "inventory_customer_return_invalid_request";
    public const string InvalidQuantity = "inventory_customer_return_quantity_invalid";
    public const string InvalidReference = "inventory_customer_return_invalid_reference";
    public const string ShipmentNotRecorded = "inventory_customer_return_shipment_not_recorded";
    public const string ShipmentTraceInvalid = "inventory_customer_return_shipment_trace_invalid";
    public const string ReturnExceedsShipment = "inventory_customer_return_exceeds_shipment";
    public const string IdempotencyConflict = "inventory_customer_return_idempotency_conflict";
    public const string ConcurrentChange = "inventory_customer_return_concurrent_change";
    public const string QuantityProfileUnavailable = "inventory_customer_return_quantity_profile_unavailable";
    public const string Unavailable = "inventory_customer_return_unavailable";

    public static IReadOnlySet<string> Status400Codes { get; } = Set(InvalidRequest, InvalidQuantity);
    public static IReadOnlySet<string> Status404Codes { get; } = Set(InvalidReference);
    public static IReadOnlySet<string> Status409Codes { get; } = Set(
        ShipmentNotRecorded, ReturnExceedsShipment, IdempotencyConflict, ConcurrentChange);
    public static IReadOnlySet<string> Status503Codes { get; } = Set(
        ShipmentTraceInvalid, QuantityProfileUnavailable, Unavailable);

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

public sealed class InventoryCustomerReturnException : Exception
{
    private InventoryCustomerReturnException(int status, string code) : base(code)
    {
        Status = status;
        Code = code;
    }

    public int Status { get; }
    public string Code { get; }

    public static InventoryCustomerReturnException Refused(int status, string code) =>
        InventoryCustomerReturnCodesV1.IsDefined(status, code)
            ? new(status, code)
            : throw new InvalidOperationException("Inventory customer-return refusal is outside its closed contract.");
}

public static class InventoryCustomerReturnContractV1
{
    public const string SourceKind = "logistics.customer-return";
    public const string CommandName = "inventory.customer-return.stage.v1";
    private const int IdentityMaximumLength = 128;

    public static Kilograms Validate(
        InventoryCustomerReturnRequestV1 request,
        InventoryCommandMetadata metadata,
        Guid enlistedCompanyId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        if (enlistedCompanyId == Guid.Empty || request.CompanyId == Guid.Empty
            || request.CustomerReturnId == Guid.Empty || request.SourceShipmentId == Guid.Empty
            || request.LoadingInstructionLineId == Guid.Empty || request.InventoryMovementId == Guid.Empty
            || request.CustomerReturnId == request.InventoryMovementId
            || request.OccurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw Refused(400, InventoryCustomerReturnCodesV1.InvalidRequest);
        }

        if (request.CompanyId != enlistedCompanyId)
        {
            throw Refused(404, InventoryCustomerReturnCodesV1.InvalidReference);
        }

        RequireMetadata(metadata);
        if (string.IsNullOrEmpty(request.QuantityKg) || request.QuantityKg.Length > 40
            || request.QuantityKg.Any(character => !char.IsAsciiDigit(character) && character != '.')
            || !decimal.TryParse(request.QuantityKg, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var parsed)
            || !string.Equals(parsed.ToString("0.###", CultureInfo.InvariantCulture), request.QuantityKg,
                StringComparison.Ordinal))
        {
            throw Refused(400, InventoryCustomerReturnCodesV1.InvalidQuantity);
        }

        try
        {
            return Kilograms.Positive(parsed);
        }
        catch (InventoryRuleException)
        {
            throw Refused(400, InventoryCustomerReturnCodesV1.InvalidQuantity);
        }
    }

    private static void RequireMetadata(InventoryCommandMetadata metadata)
    {
        if (!ValidIdentity(metadata.ActorId) || !ValidIdentity(metadata.PermissionCode)
            || !ValidIdentity(metadata.CorrelationId) || !ValidIdentity(metadata.IdempotencyKey)
            || !ValidIdentity(metadata.SourceChannel)
            || metadata.CausationId is not null && !ValidIdentity(metadata.CausationId)
            || metadata.RoleCodes is null || metadata.RoleCodes.Any(role => !ValidIdentity(role)))
        {
            throw Refused(400, InventoryCustomerReturnCodesV1.InvalidRequest);
        }
    }

    private static bool ValidIdentity(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= IdentityMaximumLength
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && !value.Any(char.IsControl);

    private static InventoryCustomerReturnException Refused(int status, string code) =>
        InventoryCustomerReturnException.Refused(status, code);
}

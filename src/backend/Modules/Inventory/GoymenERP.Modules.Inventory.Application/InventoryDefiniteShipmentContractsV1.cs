using System.Globalization;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Quantities;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// A single-use Inventory participant borrowed by the central host's already-open definite-shipment
/// transaction. The eventual Inventory runtime implementation owns enlistment; this application
/// contract exposes no database handle and cannot create an independent transaction. It stages the
/// allocation consumption, one stock exit, audit and outbox, and never commits the host attempt.
/// </summary>
public interface IInventoryDefiniteShipmentParticipantV1 : IAsyncDisposable
{
    Task<InventoryDefiniteShipmentResultV1> StageAsync(
        InventoryDefiniteShipmentRequestV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The exact Inventory facts carried by a definite shipment. The allocation is the authority for
/// the physical label and position; owner, custodian and location are deliberately not accepted
/// from Logistics. Quantity is lossless kg text and is consumed exactly as supplied.
/// </summary>
public sealed record InventoryDefiniteShipmentRequestV1(
    Guid CompanyId,
    Guid ShipmentId,
    Guid LoadingInstructionId,
    Guid LoadingInstructionLineId,
    Guid AllocationId,
    string ExpectedAllocationVersion,
    Guid LabelId,
    string ExpectedLabelVersion,
    Guid MovementId,
    string QuantityKg,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Inventory's staged answer. It is uncommitted until the host atomically persists the Logistics
/// shipped share and commits its transaction. A replay returns the original identities and
/// versions without a second consumption, movement, audit or outbox message.
/// </summary>
public sealed record InventoryDefiniteShipmentResultV1(
    Guid CompanyId,
    Guid ShipmentId,
    Guid AllocationId,
    string AllocationVersion,
    string RemainingAllocatedKg,
    Guid LabelId,
    string LabelVersion,
    Guid MovementId,
    Guid OutboxMessageId,
    bool IsReplay,
    string OriginalCorrelationId);

/// <summary>Closed refusal channel of the Inventory participant. No code discloses another company's facts.</summary>
public static class InventoryDefiniteShipmentCodesV1
{
    public const string InvalidRequest = "inventory_definite_shipment_invalid_request";
    public const string InvalidVersion = "inventory_definite_shipment_version_invalid";
    public const string InvalidQuantity = "inventory_definite_shipment_quantity_invalid";
    public const string InvalidReference = "inventory_definite_shipment_invalid_reference";
    public const string AllocationVersionConflict = "inventory_allocation_version_conflict";
    public const string LabelVersionConflict = "inventory_label_version_conflict";
    public const string AllocationState = "inventory_allocation_state_conflict";
    public const string AllocationLabelMismatch = "inventory_allocation_label_mismatch";
    public const string AllocationInsufficient = "inventory_allocation_insufficient";
    public const string LabelNotReleasable = "inventory_label_not_releasable";
    public const string PhysicalStockInsufficient = "inventory_physical_stock_insufficient";
    public const string IdempotencyConflict = "inventory_definite_shipment_idempotency_conflict";
    public const string ConcurrentChange = "inventory_definite_shipment_concurrent_change";
    public const string Unavailable = "inventory_definite_shipment_unavailable";

    public static IReadOnlySet<string> Status400Codes { get; } = Set(InvalidRequest, InvalidVersion, InvalidQuantity);
    public static IReadOnlySet<string> Status404Codes { get; } = Set(InvalidReference);
    public static IReadOnlySet<string> Status409Codes { get; } = Set(AllocationVersionConflict, LabelVersionConflict,
        AllocationState, AllocationLabelMismatch, AllocationInsufficient, LabelNotReleasable,
        PhysicalStockInsufficient, IdempotencyConflict, ConcurrentChange);
    public static IReadOnlySet<string> Status503Codes { get; } = Set(Unavailable);

    public static bool IsDefined(int status, string code) => status switch
    {
        400 => Status400Codes.Contains(code),
        404 => Status404Codes.Contains(code),
        409 => Status409Codes.Contains(code),
        503 => Status503Codes.Contains(code),
        _ => false,
    };

    private static IReadOnlySet<string> Set(params string[] codes) => codes.ToHashSet(StringComparer.Ordinal);
}

public sealed class InventoryDefiniteShipmentException : Exception
{
    private InventoryDefiniteShipmentException(int status, string code) : base(code)
    {
        Status = status;
        Code = code;
    }

    public int Status { get; }
    public string Code { get; }

    public static InventoryDefiniteShipmentException Refused(int status, string code) =>
        InventoryDefiniteShipmentCodesV1.IsDefined(status, code)
            ? new(status, code)
            : throw new InvalidOperationException("Inventory definite-shipment refusal is outside its closed contract.");
}

/// <summary>
/// Shape validation shared by the owner implementation and contract tests. It chooses no allocation,
/// shipment, unit or partial-shipment policy. Stored allocation, label, balance and authority facts
/// are intentionally left to the enlisted implementation and are fail-closed when unavailable.
/// </summary>
public static class InventoryDefiniteShipmentContractV1
{
    private const int IdentityMaximumLength = 128;

    public static Kilograms Validate(
        InventoryDefiniteShipmentRequestV1 request,
        InventoryCommandMetadata metadata,
        Guid enlistedCompanyId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);

        if (enlistedCompanyId == Guid.Empty || request.CompanyId == Guid.Empty
            || request.ShipmentId == Guid.Empty || request.LoadingInstructionId == Guid.Empty
            || request.LoadingInstructionLineId == Guid.Empty || request.AllocationId == Guid.Empty
            || request.LabelId == Guid.Empty || request.MovementId == Guid.Empty)
        {
            throw Refused(400, InventoryDefiniteShipmentCodesV1.InvalidRequest);
        }

        if (request.CompanyId != enlistedCompanyId)
        {
            throw Refused(404, InventoryDefiniteShipmentCodesV1.InvalidReference);
        }

        RequireVersion(request.ExpectedAllocationVersion);
        RequireVersion(request.ExpectedLabelVersion);

        if (request.OccurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw Refused(400, InventoryDefiniteShipmentCodesV1.InvalidRequest);
        }

        RequireMetadata(metadata);
        try
        {
            if (string.IsNullOrEmpty(request.QuantityKg) || request.QuantityKg.Length > 40
                || !request.QuantityKg.All(character => char.IsAsciiDigit(character) || character == '.')
                || !decimal.TryParse(request.QuantityKg, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var value)
                || value <= 0m)
            {
                throw Refused(400, InventoryDefiniteShipmentCodesV1.InvalidQuantity);
            }

            return Kilograms.Positive(value);
        }
        catch (InventoryRuleException exception) when (exception.Code is
            InventoryRuleCodes.KilogramPrecisionExceeded or InventoryRuleCodes.QuantityNotPositive)
        {
            throw Refused(400, InventoryDefiniteShipmentCodesV1.InvalidQuantity);
        }
    }

    private static void RequireVersion(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 19 || value[0] is < '1' or > '9'
            || value.Any(character => character is < '0' or > '9')
            || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            throw Refused(400, InventoryDefiniteShipmentCodesV1.InvalidVersion);
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
            throw Refused(400, InventoryDefiniteShipmentCodesV1.InvalidRequest);
        }
    }

    private static bool ValidIdentity(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && value.Length <= IdentityMaximumLength
        && !value.Any(char.IsControl);

    private static InventoryDefiniteShipmentException Refused(int status, string code) =>
        InventoryDefiniteShipmentException.Refused(status, code);
}

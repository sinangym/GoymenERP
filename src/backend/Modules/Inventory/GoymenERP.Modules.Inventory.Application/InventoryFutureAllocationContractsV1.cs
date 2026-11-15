using System.Data.Common;
using System.Globalization;
using System.Text.Json.Serialization;
using GoymenERP.Modules.Inventory.Domain.FutureSupply;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// F-017 STOK v1 §3/§4 (F017-b): a future supply allocation reserves part of an approved purchase GENERAL line's not-yet-received
/// quantity for one approved Sales order line. The amount is kept in the PURCHASE line's own unit (SAT-D298); the request's unit is
/// converted once, server-side, before the ledger check. No HTTP route, OpenAPI entry or permission is declared here (F017-b2): the
/// host calls <see cref="IInventoryFutureAllocationWriterV1"/> only after its own permission check and passes a fence that
/// re-checks the same authority right before the write.
/// </summary>
public static class InventoryFutureAllocationKindsV1
{
    /// <summary>The supply side: an approved purchase general line.</summary>
    public const string PurchaseOrderLine = "purchasing.order-line";

    /// <summary>The demand side: an approved Sales order line (same value as the physical allocation demand kind).</summary>
    public const string SalesOrderLine = InventoryAllocationDemandKindsV1.SalesOrderLine;
}

/// <summary>Which module asked for a release (F-017 STOK §3.3); stored on the row and carried by the released event.</summary>
public static class InventoryFutureAllocationOriginsV1
{
    public const string Inventory = "inventory";
    public const string Purchasing = "purchasing";
    public const string Sales = "sales";

    public static bool IsKnown(string? origin) =>
        string.Equals(origin, Inventory, StringComparison.Ordinal)
        || string.Equals(origin, Purchasing, StringComparison.Ordinal)
        || string.Equals(origin, Sales, StringComparison.Ordinal);
}

/// <summary>
/// F-017 STOK §3.3/§3.4 release reason codes named by the specs. The store keeps the code as given and validates only its format
/// (<see cref="IsWellFormed"/>): the list is not closed here, so a later slice's code needs no Inventory change.
/// ASSUMPTION-STK-REASON-1 (reversible): format-only validation, no fixed catalog; a catalog, if needed, comes from Settings in a
/// separate slice. A malformed code is 400 and, being a request-shape refusal, is not a recorded terminal refusal.
/// </summary>
public static class InventoryFutureAllocationReleaseReasonsV1
{
    public const int MaximumLength = 64;

    public const string Manual = "manual-release";
    public const string PurchasingLinkDropped = "purchasing-link-dropped";
    public const string SourceCancelled = "source-cancelled";
    public const string PurchaseLineClosed = "purchase-line-closed";
    public const string AcceptanceLinkFailed = "acceptance-link-failed";
    public const string AcceptanceReversedNoSupply = "acceptance-reversed-no-supply";
    public const string SalesLineCancelled = "sales-line-cancelled";
    public const string SalesLineReduced = "sales-line-reduced";

    /// <summary>Lowercase ASCII words joined by single hyphens (kebab-case), at most <see cref="MaximumLength"/> characters.</summary>
    public static bool IsWellFormed(string? code)
    {
        if (string.IsNullOrEmpty(code) || code.Length > MaximumLength || code[0] == '-' || code[^1] == '-')
        {
            return false;
        }

        var previousHyphen = false;
        foreach (var character in code)
        {
            if (character == '-')
            {
                if (previousHyphen)
                {
                    return false;
                }

                previousHyphen = true;
                continue;
            }

            if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character))
            {
                return false;
            }

            previousHyphen = false;
        }

        return true;
    }
}

/// <summary>F-017 STOK §7 problem codes; the shared ones are <see cref="InventoryAllocationCodesV1"/>'s.</summary>
public static class InventoryFutureAllocationCodesV1
{
    public const string ValidationFailed = InventoryAllocationCodesV1.ValidationFailed;
    public const string IdempotencyConflict = InventoryAllocationCodesV1.IdempotencyConflict;
    public const string ConcurrentChange = InventoryAllocationCodesV1.ConcurrentChange;
    public const string VersionConflict = InventoryAllocationCodesV1.AllocationVersionConflict;
    public const string StateConflict = InventoryAllocationCodesV1.AllocationStateConflict;

    /// <summary>404 (unknown) / 422 (not allocatable, not approved, product mismatch).</summary>
    public const string InvalidReference = "inventory_future_allocation_invalid_reference";

    /// <summary>422: no central conversion between the request unit and the purchase line unit (or the demand unit).</summary>
    public const string UnitConversionUnavailable = "inventory_future_allocation_unit_conversion_unavailable";

    /// <summary>409: active allocations plus this one would exceed the purchase line's allocatable quantity.</summary>
    public const string InsufficientFutureSupply = "inventory_future_allocation_insufficient_future_supply";

    /// <summary>409: physical plus future allocations of the Sales line would exceed its open quantity.</summary>
    public const string ExceedsDemandOpenQuantity = "inventory_future_allocation_exceeds_demand_open_quantity";

    /// <summary>422: the release quantity is not positive or exceeds the allocation's remaining quantity.</summary>
    public const string ReleaseSelectionInvalid = "inventory_future_allocation_release_selection_invalid";

    /// <summary>503: a source module could not be read; nothing is written (fail-closed).</summary>
    public const string ServiceUnavailable = "service_unavailable";
}

public static class InventoryFutureAllocationCommandsV1
{
    public const string Create = "inventory.future-allocation.create.v1";
    public const string Release = "inventory.future-allocation.release.v1";
}

/// <summary>The outbox contract of a created future allocation (Sequence 1 of the allocation aggregate).</summary>
public static class InventoryFutureAllocationCreatedOutboxV1
{
    public const string EventType = "inventory.future-allocation.created.v1";
    public const int SchemaVersion = 1;
    public const string SourceOperation = InventoryFutureAllocationCommandsV1.Create;
    public const string AggregateType = "Inventory.FutureSupplyAllocation";
}

/// <summary>The outbox contract of a release (Sequence = the allocation version after the release).</summary>
public static class InventoryFutureAllocationReleasedOutboxV1
{
    public const string EventType = "inventory.future-allocation.released.v1";
    public const int SchemaVersion = 1;
    public const string SourceOperation = InventoryFutureAllocationCommandsV1.Release;
    public const string AggregateType = InventoryFutureAllocationCreatedOutboxV1.AggregateType;
}

public sealed record InventoryFutureAllocationCreatedV1(
    Guid CompanyId,
    Guid AllocationId,
    string AllocationVersion,
    Guid SalesOrderId,
    Guid SalesOrderLineId,
    Guid PurchaseOrderId,
    Guid PurchaseOrderLineId,
    Guid ProductId,
    string ProductVersion,
    Guid UnitId,
    string AllocatedQuantity,
    string CorrelationId,
    string? CausationId);

public sealed record InventoryFutureAllocationReleasedV1(
    Guid CompanyId,
    Guid AllocationId,
    string AllocationVersion,
    Guid SalesOrderId,
    Guid SalesOrderLineId,
    Guid PurchaseOrderId,
    Guid PurchaseOrderLineId,
    Guid UnitId,
    string ReleasedQuantity,
    string RemainingQuantity,
    string Status,
    string ReasonCode,
    string ReasonText,
    string Origin,
    string CorrelationId,
    string? CausationId);

/// <summary>
/// Create request. <see cref="QuantityText"/> is in <see cref="UnitId"/>; the store converts it to the purchase line unit. Product,
/// product version and allocatable quantity are never taken from the caller: they come from the source modules' ports.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateFutureSupplyAllocationV1(
    [property: JsonRequired] Guid AllocationId,
    [property: JsonRequired] Guid SalesOrderId,
    [property: JsonRequired] Guid SalesOrderLineId,
    [property: JsonRequired] Guid PurchaseOrderId,
    [property: JsonRequired] Guid PurchaseOrderLineId,
    [property: JsonRequired] string QuantityText,
    [property: JsonRequired] Guid UnitId,
    [property: JsonRequired] string Reason);

/// <summary>Release request; a null <see cref="QuantityText"/> releases the whole remaining quantity (purchase line unit).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReleaseFutureSupplyAllocationV1(
    [property: JsonRequired] string ExpectedAllocationVersion,
    string? QuantityText,
    [property: JsonRequired] string ReasonCode,
    [property: JsonRequired] string ReasonText);

public sealed record InventoryFutureAllocationViewV1(
    Guid AllocationId,
    Guid CompanyId,
    string Version,
    string Status,
    Guid SalesOrderId,
    Guid SalesOrderLineId,
    Guid PurchaseOrderId,
    Guid PurchaseOrderLineId,
    Guid ProductId,
    string ProductVersion,
    Guid UnitId,
    string AllocatedQuantity,
    string RemainingQuantity,
    string ReleasedQuantity,
    string ConsumedQuantity,
    int? ManualSequence,
    string Reason,
    string? ReleaseReasonCode,
    string? ReleaseOrigin,
    string? ReleaseReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ReleasedAtUtc);

public sealed record InventoryFutureAllocationResultV1(
    InventoryFutureAllocationViewV1 Allocation,
    bool IsReplay,
    string OriginalCorrelationId);

/// <summary>
/// The host's re-check of the caller's authority (same company, same actor, permission still granted), called by the writer after
/// the locks and again right before the save. It throws the host's own refusal; the writer records nothing when it does.
/// </summary>
public interface IInventoryFutureAllocationAuthorityFenceV1
{
    Task RequireStillAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The module-owned create/release service (F017-b, no route). Every call is one database transaction: replay first, then the
/// Sales line lock, then the purchase line lock, then the reads, the checks and one save (allocation + ledger + command + audit +
/// outbox). Refusals throw <see cref="InventoryAllocationException"/> with an <see cref="InventoryFutureAllocationCodesV1"/> code.
/// </summary>
public interface IInventoryFutureAllocationWriterV1
{
    Task<InventoryFutureAllocationResultV1> CreateAsync(
        CreateFutureSupplyAllocationV1 request,
        InventoryCommandMetadata metadata,
        IInventoryFutureAllocationAuthorityFenceV1 fence,
        CancellationToken cancellationToken = default);

    Task<InventoryFutureAllocationResultV1> ReleaseAsync(
        Guid allocationId,
        ReleaseFutureSupplyAllocationV1 request,
        InventoryCommandMetadata metadata,
        IInventoryFutureAllocationAuthorityFenceV1 fence,
        CancellationToken cancellationToken = default);
}

public enum InventoryFutureAllocationReadOutcomeV1
{
    /// <summary>At least one allocation counts; the figure is exact.</summary>
    Present = 1,

    /// <summary>Nothing counts on the line (no ledger row, or zero).</summary>
    None = 2,

    /// <summary>Inventory could not answer reliably (ledger disagrees with its rows, or a read failure). Fail-closed.</summary>
    Unreadable = 3,
}

/// <summary>The active allocated quantity of one purchase line, in the purchase line's unit (<see cref="UnitId"/>).</summary>
public sealed record InventoryFutureAllocationSumV1(
    InventoryFutureAllocationReadOutcomeV1 Outcome,
    string? QuantityText,
    Guid? UnitId);

/// <summary>Whether any allocation on the purchase line has consumed quantity (F017-c writes consumption).</summary>
public sealed record InventoryFutureAllocationPresenceV1(InventoryFutureAllocationReadOutcomeV1 Outcome);

/// <summary>
/// F-017 STOK §4: Inventory's read port for Purchasing and Sales. No authority, the server-resolved company only, nothing is written.
/// </summary>
public interface IInventoryFutureAllocationReadPortV1
{
    Task<InventoryFutureAllocationSumV1> GetAllocatedOnPurchaseLineAsync(Guid purchaseOrderLineId,
        CancellationToken cancellationToken = default);

    Task<InventoryFutureAllocationPresenceV1> GetConsumedPresenceOnPurchaseLineAsync(Guid purchaseOrderLineId,
        CancellationToken cancellationToken = default);
}

/// <summary>A release asked by another module inside its own transaction (no idempotency row: the caller's command owns it).</summary>
public sealed record InventoryFutureAllocationReleaseRequestV1(
    Guid AllocationId,
    long ExpectedAllocationVersion,
    string? QuantityText,
    string ReasonCode,
    string ReasonText,
    string Origin);

/// <summary>
/// F-017 STOK §4: Inventory's release port for Purchasing and Sales. <see cref="EnlistAsync"/> joins the caller's open connection
/// and transaction for the server-resolved company; the caller commits or rolls back. Each release re-takes the allocation's
/// purchase line lock (re-entrant in the same transaction), checks the expected version and writes allocation + ledger + audit +
/// outbox in one save.
/// </summary>
public interface IInventoryFutureAllocationReleasePortV1
{
    Task<IInventoryFutureAllocationReleaseSessionV1> EnlistAsync(DbConnection connection, DbTransaction transaction,
        CancellationToken cancellationToken = default);
}

public interface IInventoryFutureAllocationReleaseSessionV1 : IAsyncDisposable
{
    Task<IReadOnlyList<InventoryFutureAllocationViewV1>> ListActiveByPurchaseLineAsync(Guid purchaseOrderLineId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryFutureAllocationViewV1>> ListActiveByDemandLineAsync(Guid salesOrderLineId,
        CancellationToken cancellationToken = default);

    Task<InventoryFutureAllocationViewV1> ReleaseAsync(InventoryFutureAllocationReleaseRequestV1 request,
        InventoryCommandMetadata metadata, CancellationToken cancellationToken = default);
}

public enum InventoryFutureSupplySourceOutcomeV1
{
    Allocatable = 1,
    NotAllocatable = 2,
    NotFound = 3,
    Unavailable = 4,
}

/// <summary>The purchase general line as Inventory needs it; quantities are in <see cref="UnitId"/>.</summary>
public sealed record InventoryFutureSupplyLineV1(
    Guid OrderId,
    Guid LineId,
    Guid ProductId,
    string ProductVersion,
    Guid UnitId,
    string AllocatableText,
    long ObservedOrderVersion);

public sealed record InventoryFutureSupplySourceAnswerV1(
    InventoryFutureSupplySourceOutcomeV1 Outcome,
    InventoryFutureSupplyLineV1? Line);

/// <summary>
/// Inventory-declared source of the purchase line (the host adapts Purchasing's read port; Inventory never references Purchasing).
/// Read under the purchase line lock, in the caller's transaction. No authority, server-resolved company only.
/// </summary>
public interface IInventoryFutureSupplySourceV1
{
    Task<InventoryFutureSupplySourceAnswerV1> GetAllocatableLineAsync(Guid purchaseOrderId, Guid purchaseOrderLineId,
        CancellationToken cancellationToken = default);
}

public enum InventoryFutureDemandSourceOutcomeV1
{
    Open = 1,
    NotApproved = 2,
    NotFound = 3,
    Unavailable = 4,
}

/// <summary>
/// The approved Sales line and its open quantity, in <see cref="UnitId"/> (the order unit). <see cref="DueDate"/> is the line's
/// requested delivery date when the Sales port answers it (F017-c tie rule); null sorts after every dated line.
/// </summary>
public sealed record InventoryFutureDemandLineV1(
    Guid OrderId,
    Guid LineId,
    Guid ProductId,
    string ProductVersion,
    Guid UnitId,
    string OpenQuantityText,
    DateOnly? DueDate = null);

public sealed record InventoryFutureDemandSourceAnswerV1(
    InventoryFutureDemandSourceOutcomeV1 Outcome,
    InventoryFutureDemandLineV1? Line);

/// <summary>Inventory-declared source of the Sales demand line (approved line + open quantity). No authority.</summary>
public interface IInventoryFutureDemandSourceV1
{
    Task<InventoryFutureDemandSourceAnswerV1> GetDemandLineAsync(Guid salesOrderId, Guid salesOrderLineId,
        CancellationToken cancellationToken = default);
}

public enum InventoryFutureUnitConversionOutcomeV1
{
    Converted = 1,

    /// <summary>No central conversion source for this product and unit pair; the caller refuses 422, never guesses.</summary>
    NoSource = 2,
}

public sealed record InventoryFutureUnitConversionV1(InventoryFutureUnitConversionOutcomeV1 Outcome, decimal? Quantity);

/// <summary>
/// The conversion seam (SAT-D298: the consumer converts its input to the line unit; KLN-D66: when the Sales and purchase units
/// differ, both sides are compared in kilograms). The default answers only the identity (same unit) and
/// <see cref="InventoryFutureUnitConversionOutcomeV1.NoSource"/> otherwise; a central conversion service replaces it without an
/// Inventory change. A missing source is never a guess: the caller refuses 422 (KLN-D66 (3), fail-closed).
/// </summary>
public interface IInventoryFutureUnitConverterV1
{
    Task<InventoryFutureUnitConversionV1> ConvertAsync(Guid productId, string productVersion, decimal quantity, Guid fromUnitId,
        Guid toUnitId, CancellationToken cancellationToken = default);

    /// <summary>
    /// KLN-D65 (4) / KLN-D66 (1): the kilogram basis of the demand cap. Physical allocations hold kilograms without a unit id
    /// (INV-AL-1); the Sales open quantity and the future allocations are brought to kilograms here. Only a central source knows
    /// whether a unit is the kilogram or how the product weighs; without one the answer is NoSource.
    /// </summary>
    Task<InventoryFutureUnitConversionV1> ConvertToKilogramsAsync(Guid productId, string productVersion, decimal quantity,
        Guid fromUnitId, CancellationToken cancellationToken = default);
}

/// <summary>Identity-only default of <see cref="IInventoryFutureUnitConverterV1"/>.</summary>
public sealed class InventoryIdentityFutureUnitConverterV1 : IInventoryFutureUnitConverterV1
{
    public Task<InventoryFutureUnitConversionV1> ConvertAsync(Guid productId, string productVersion, decimal quantity,
        Guid fromUnitId, Guid toUnitId, CancellationToken cancellationToken = default) =>
        Task.FromResult(fromUnitId != Guid.Empty && fromUnitId == toUnitId
            ? new InventoryFutureUnitConversionV1(InventoryFutureUnitConversionOutcomeV1.Converted, quantity)
            : new InventoryFutureUnitConversionV1(InventoryFutureUnitConversionOutcomeV1.NoSource, null));

    /// <summary>The default does not know which unit is the kilogram: NoSource (the caller refuses 422 when kilograms are needed).</summary>
    public Task<InventoryFutureUnitConversionV1> ConvertToKilogramsAsync(Guid productId, string productVersion,
        decimal quantity, Guid fromUnitId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new InventoryFutureUnitConversionV1(InventoryFutureUnitConversionOutcomeV1.NoSource, null));
}

/// <summary>Fail-closed default of <see cref="IInventoryFutureSupplySourceV1"/> until the host wires the Purchasing adapter.</summary>
public sealed class InventoryUnavailableFutureSupplySourceV1 : IInventoryFutureSupplySourceV1
{
    public Task<InventoryFutureSupplySourceAnswerV1> GetAllocatableLineAsync(Guid purchaseOrderId, Guid purchaseOrderLineId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new InventoryFutureSupplySourceAnswerV1(InventoryFutureSupplySourceOutcomeV1.Unavailable, null));
}

/// <summary>Canonical text rules shared by the writer, the release port and the tests.</summary>
public static class InventoryFutureAllocationTextV1
{
    private const int MaximumQuantityTextLength = 40;

    /// <summary>A positive canonical decimal (ASCII digits, at most one dot, at most three decimals).</summary>
    public static bool TryPositiveQuantity(string? value, out FutureSupplyQuantity quantity)
    {
        quantity = FutureSupplyQuantity.Zero;
        if (!TryDecimal(value, out var parsed) || parsed <= 0m)
        {
            return false;
        }

        quantity = FutureSupplyQuantity.Positive(parsed);
        return true;
    }

    /// <summary>A non-negative decimal as a source module answers it ("0" included); more than three decimals is refused.</summary>
    public static bool TryNonNegativeQuantity(string? value, out FutureSupplyQuantity quantity)
    {
        quantity = FutureSupplyQuantity.Zero;
        if (!TryDecimal(value, out var parsed))
        {
            return false;
        }

        quantity = FutureSupplyQuantity.Of(parsed);
        return true;
    }

    /// <summary>A canonical positive int64 version (ASCII digits only).</summary>
    public static bool TryVersion(string? value, out long version)
    {
        version = 0;
        return !string.IsNullOrEmpty(value) && value.All(char.IsAsciiDigit)
            && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out version) && version > 0;
    }

    private static bool TryDecimal(string? value, out decimal parsed)
    {
        parsed = 0m;
        return !string.IsNullOrEmpty(value) && value.Length <= MaximumQuantityTextLength
            && value.Count(character => character == '.') <= 1
            && value.All(character => char.IsAsciiDigit(character) || character == '.')
            && decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out parsed)
            && ((decimal.GetBits(parsed)[3] >> 16) & 0xFF) <= FutureSupplyQuantity.MaximumDecimals;
    }
}

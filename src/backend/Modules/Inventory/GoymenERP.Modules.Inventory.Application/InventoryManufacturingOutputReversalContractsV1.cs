using GoymenERP.Modules.Inventory.Domain.Quantities;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// F-009 D1 (URT-D28, URT-D31): a single-use Inventory participant, enlisted in Manufacturing's already-open
/// transaction, that stages the reversal of one output declaration's receipt. Manufacturing stages its own output
/// reversal fact first and supplies the post-stage order version; Inventory writes exactly one
/// <c>ManufacturingOutputReversal</c> movement out of the output label's original Free position and never owns
/// commit, rollback, connection lifetime or Manufacturing state.
/// <para>
/// Only kilograms still free and unallocated at the output's original position can be reversed. Shipped, issued to a
/// manufacturing order, transferred, allocated or quality-blocked kilograms are refused with a code naming the reason;
/// releasing them first is the owning module's own command. The sum of every reversal of one output never exceeds the
/// kilograms that output received. The reversal has no average-cost impact: a manufacturing receipt is recorded
/// without cost (<c>CostResult.NotCalculated</c>) and enters no average while its order is open (§4.8).
/// </para>
/// </summary>
public interface IInventoryManufacturingOutputReversalParticipantV1 : IAsyncDisposable
{
    Task<InventoryManufacturingOutputReversalResultV1> StageOutputReversalAsync(
        InventoryManufacturingOutputReversalRequestV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The reversal of <see cref="QuantityKg"/> from output <see cref="OutputId"/> of order
/// <see cref="ManufacturingOrderId"/>, on that output's label. <see cref="ReversalId"/> is Manufacturing's reversal
/// fact id and becomes the movement's source line; <see cref="ExpectedLabelVersion"/> is the label version the caller
/// read (for example from <see cref="IInventoryLabelDispositionReadPortV1"/>) and is advanced by the movement.
/// </summary>
public sealed record InventoryManufacturingOutputReversalRequestV1(
    Guid CompanyId,
    Guid ManufacturingOrderId,
    Guid OutputId,
    Guid ReversalId,
    string ManufacturingOrderVersionAfter,
    Guid LabelId,
    string ExpectedLabelVersion,
    Guid MovementId,
    string QuantityKg,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// <see cref="ReversedReceiptMovementId"/> is the output receipt the reversal is bound to (the old <c>Receipt</c>
/// movement stays unchanged; net output = receipt − reversals).
/// </summary>
public sealed record InventoryManufacturingOutputReversalResultV1(
    Guid CompanyId,
    Guid ManufacturingOrderId,
    Guid OutputId,
    Guid ReversalId,
    string ManufacturingOrderVersion,
    Guid LabelId,
    string LabelVersion,
    Guid MovementId,
    Guid ReversedReceiptMovementId,
    string QuantityKg,
    bool IsReplay,
    string OriginalCorrelationId);

/// <summary>
/// The closed refusal set of the output reversal: the V1 manufacturing-realization codes plus the reversal-only
/// codes below. Every reversal-only code is a 409; Manufacturing maps each one to its own problem code.
/// </summary>
public static class InventoryManufacturingOutputReversalCodesV1
{
    /// <summary>The reversal would take more than the output received, counting every earlier reversal of it.</summary>
    public const string ExceedsProduced = "inventory_manufacturing_output_reversal_exceeds_produced";

    /// <summary>The kilograms are no longer at the output position because a definite shipment took them.</summary>
    public const string Shipped = "inventory_manufacturing_output_reversal_shipped";

    /// <summary>The kilograms were issued as input to a manufacturing order and not returned.</summary>
    public const string IssuedToManufacturing = "inventory_manufacturing_output_reversal_issued_to_manufacturing";

    /// <summary>The kilograms were dispatched by a transfer and are no longer at the output position.</summary>
    public const string Transferred = "inventory_manufacturing_output_reversal_transferred";

    /// <summary>The kilograms are held by an active allocation (a sales line or a manufacturing plan input).</summary>
    public const string Allocated = "inventory_manufacturing_output_reversal_allocated";

    /// <summary>The kilograms are quality-blocked; removing the block is Inventory's own command.</summary>
    public const string Blocked = "inventory_manufacturing_output_reversal_blocked";

    /// <summary>The kilograms are not free at the output position for another recorded reason (scrap, count).</summary>
    public const string NotFree = "inventory_manufacturing_output_reversal_not_free";

    public static bool IsDefined(int status, string code) =>
        status == 409 && code is ExceedsProduced or Shipped or IssuedToManufacturing or Transferred or Allocated
            or Blocked or NotFree
        || InventoryManufacturingRealizationCodesV1.IsDefined(status, code);
}

public static class InventoryManufacturingOutputReversalContractV1
{
    /// <summary>The source kind of the reversal movement; its source id is the order, its line the reversal fact.</summary>
    public const string OutputReversalSourceKind = "manufacturing.output-reversal";

    public const string CommandName = "inventory.manufacturing-output-reversal.stage.v1";

    public static Kilograms Validate(InventoryManufacturingOutputReversalRequestV1 request,
        InventoryCommandMetadata metadata, Guid enlistedCompanyId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        if (enlistedCompanyId == Guid.Empty || request.CompanyId == Guid.Empty
            || request.ManufacturingOrderId == Guid.Empty || request.OutputId == Guid.Empty
            || request.ReversalId == Guid.Empty || request.LabelId == Guid.Empty || request.MovementId == Guid.Empty
            || request.OccurredAtUtc.Offset != TimeSpan.Zero
            || request.ReversalId == request.MovementId || request.ReversalId == request.OutputId)
        {
            throw Refused(400, InventoryManufacturingRealizationCodesV1.InvalidRequest);
        }

        if (request.CompanyId != enlistedCompanyId)
        {
            throw Refused(404, InventoryManufacturingRealizationCodesV1.InvalidReference);
        }

        InventoryManufacturingRealizationContractV1.RequireVersion(request.ManufacturingOrderVersionAfter);
        InventoryManufacturingRealizationContractV1.RequireVersion(request.ExpectedLabelVersion);
        InventoryManufacturingRealizationContractV1.RequireMetadata(metadata);
        return InventoryManufacturingRealizationContractV1.Quantity(request.QuantityKg);
    }

    private static InventoryManufacturingRealizationException Refused(int status, string code) =>
        InventoryManufacturingRealizationException.RefusedOutputReversal(status, code);
}

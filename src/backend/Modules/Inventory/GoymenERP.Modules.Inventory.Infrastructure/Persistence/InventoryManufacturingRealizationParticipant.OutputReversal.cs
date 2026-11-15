using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// F-009 D1 (URT-D28, URT-D31): the output reversal, staged in Manufacturing's transaction. It re-reads the label's
/// disposition inside the transaction with the same computation the read port shows
/// (<see cref="InventoryLabelDispositionReader"/>), so a figure seen before the command is never the decision source.
/// The old output <c>Receipt</c> stays unchanged; the reversal is one <c>ManufacturingOutputReversal</c> Out line at
/// the receipt's original position, Free block, without cost (§4.8: no average-cost impact).
/// </summary>
public sealed partial class InventoryManufacturingRealizationParticipant : IInventoryManufacturingOutputReversalParticipantV1
{
    public async Task<InventoryManufacturingOutputReversalResultV1> StageOutputReversalAsync(
        InventoryManufacturingOutputReversalRequestV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        Begin();
        var quantity = InventoryManufacturingOutputReversalContractV1.Validate(request, metadata, companyId);
        var keyHash = Hash(metadata.IdempotencyKey);
        var payloadHash = RequestHash(request);
        if (await FindCommandAsync(InventoryManufacturingOutputReversalContractV1.CommandName, keyHash,
                cancellationToken) is { } existing)
        {
            EnsureReplay(existing, metadata, payloadHash);
            var replay = JsonSerializer.Deserialize<InventoryManufacturingOutputReversalResultV1>(existing.ResultJson,
                Json) ?? throw Refused(503, InventoryManufacturingRealizationCodesV1.Unavailable);
            return replay with { IsReplay = true, OriginalCorrelationId = existing.CorrelationId };
        }

        // The label version fence serializes this read with every other writer of the label (each bumps it).
        var parent = await LoadParentAsync(request.LabelId, request.ExpectedLabelVersion, requireReleasable: false,
            cancellationToken);
        var disposition = await InventoryLabelDispositionReader.ReadAsync(context, companyId, parent.Label.Id,
            cancellationToken);
        if (disposition.Status != InventoryLabelDispositionStatusV1.Value)
        {
            throw Refused(503, InventoryManufacturingRealizationCodesV1.Unavailable);
        }

        if (disposition.Output is not { } output || output.ManufacturingOrderId != request.ManufacturingOrderId
            || output.OutputId != request.OutputId)
        {
            throw Refused(404, InventoryManufacturingRealizationCodesV1.InvalidReference);
        }

        if (output.ReversedKg + quantity.Value > output.ProducedKg)
        {
            throw RefusedReversal(InventoryManufacturingOutputReversalCodesV1.ExceedsProduced);
        }

        if (quantity.Value > output.FreeUnallocatedAtPositionKg)
        {
            throw RefusedReversal(LockedReason(disposition, output));
        }

        StockPosition position;
        try
        {
            position = new StockPosition(companyId,
                new StockOwner((StockPartyKind)output.Position.OwnerKind, output.Position.OwnerId),
                new StockCustodian((StockPartyKind)output.Position.CustodianKind, output.Position.CustodianId),
                output.Position.LocationId, parent.Label, BlockState.Free);
        }
        catch (Exception exception) when (exception is InventoryRuleException or ArgumentException)
        {
            throw Refused(503, InventoryManufacturingRealizationCodesV1.Unavailable);
        }

        var movement = new StockMovement(request.MovementId, companyId, StockMovementKind.ManufacturingOutputReversal,
            request.OccurredAtUtc,
            new StockSourceReference(InventoryManufacturingOutputReversalContractV1.OutputReversalSourceKind,
                request.ManufacturingOrderId, request.ReversalId),
            [new StockMovementLine(position, StockDirection.Out, quantity)], CostResult.NotCalculated);
        // The figures were checked under the label fence above; a shortfall here is a concurrent change.
        await RecordMovementAsync(movement, metadata, InventoryManufacturingRealizationCodesV1.ConcurrentChange,
            cancellationToken);

        var result = new InventoryManufacturingOutputReversalResultV1(companyId, request.ManufacturingOrderId,
            request.OutputId, request.ReversalId, request.ManufacturingOrderVersionAfter, parent.Label.Id,
            parent.Row.Version.ToString(CultureInfo.InvariantCulture), movement.Id, output.ReceiptMovementId,
            quantity.ToString(), IsReplay: false, OriginalCorrelationId: metadata.CorrelationId);
        await SaveCommandAsync(InventoryManufacturingOutputReversalContractV1.CommandName, keyHash, payloadHash,
            request.ReversalId, result, metadata, cancellationToken);
        return result;
    }

    /// <summary>
    /// The first reason, in this fixed order, why the requested kilograms are not free and unallocated at the output
    /// position: net shipped, net issued to manufacturing, transferred away, allocated there, blocked there; any other
    /// recorded cause (scrap, count, an earlier reversal) is <c>NotFree</c>. The order is a reversible presentation
    /// choice; every listed cause blocks the reversal alike.
    /// </summary>
    internal static string LockedReason(InventoryLabelDispositionSnapshot disposition,
        InventoryLabelOutputSnapshot output)
    {
        if (disposition.ShippedKg - disposition.CustomerReturnedKg > 0m)
        {
            return InventoryManufacturingOutputReversalCodesV1.Shipped;
        }

        if (disposition.ManufacturingIssuedKg - disposition.ManufacturingReturnedKg > 0m)
        {
            return InventoryManufacturingOutputReversalCodesV1.IssuedToManufacturing;
        }

        if (disposition.TransferDispatchedKg > 0m)
        {
            return InventoryManufacturingOutputReversalCodesV1.Transferred;
        }

        if (output.AllocatedAtPositionKg > 0m)
        {
            return InventoryManufacturingOutputReversalCodesV1.Allocated;
        }

        return output.BlockedAtPositionKg > 0m
            ? InventoryManufacturingOutputReversalCodesV1.Blocked
            : InventoryManufacturingOutputReversalCodesV1.NotFree;
    }

    private static InventoryManufacturingRealizationException RefusedReversal(string code) =>
        InventoryManufacturingRealizationException.RefusedOutputReversal(409, code);
}

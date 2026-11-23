using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

public sealed partial class InventorySupplierReturnExitParticipant
{
    private async Task<InventorySupplierReturnAllocationProofV1?> ResolveCostAllocationAsync(
        InventorySupplierReturnExitRequestV1 request, long labelVersion, CancellationToken token)
    {
        var command = request.CostAllocation;
        InventorySupplierReturnCostSourceReader.Snapshot? legacySource = null;
        if (command is null)
        {
            // A legacy wholly pending return retains its old replay and physical semantics. Once sources exist,
            // an absent selection cannot mean an automatically chosen mixture.
            if (!await context.PurchaseInvoiceReceiptCostEffects.AnyAsync(x => x.AcceptanceId == request.GoodsAcceptanceId, token)) return null;
            if (request.PurchaseSource is null || request.UndoQuantity is null)
                throw Refused(409, InventorySupplierReturnExitCodesV1.CostAllocationRequired);
            var legacyScope = new CompanyContext(); legacyScope.Set(companyId);
            legacySource = await new InventorySupplierReturnCostSourceReader(context, legacyScope).ReadAsync(companyId,
                request.GoodsAcceptanceId, request.PurchaseSource, token);
            if (legacySource.Preview.PendingQuantityKg.Value().Sign != 0 || legacySource.Preview.InvoicedSources.Count != 1)
                throw Refused(409, InventorySupplierReturnExitCodesV1.CostAllocationRequired);
            // The existing command names the sole fully invoiced source unambiguously. Preserve that command;
            // do not infer a mixture or manufacture purchase units from its kilograms.
            command = new(legacySource.Preview.SourceVersion, new(false, "0", "0"),
                [new(legacySource.Preview.InvoicedSources[0].SourceEventId, false, request.QuantityKg, request.UndoQuantity)]);
        }
        if (request.PurchaseSource is null || request.UndoQuantity is null || command.Pending is null
            || command.InvoicedSources is null || command.InvoicedSources.Count > 10_000
            || command.InvoicedSources.Any(x => x is null || x.SourceEventId == Guid.Empty)
            || command.InvoicedSources.Select(x => x.SourceEventId).Distinct().Count() != command.InvoicedSources.Count)
            throw Refused(400, InventorySupplierReturnExitCodesV1.InvalidRequest);
        var scope = new CompanyContext(); scope.Set(companyId);
        var actual = legacySource ?? await new InventorySupplierReturnCostSourceReader(context, scope).ReadAsync(companyId,
            request.GoodsAcceptanceId, request.PurchaseSource, token);
        if (!string.Equals(actual.Preview.SourceVersion, command.ExpectedSourceVersion, StringComparison.Ordinal)
            || actual.LabelVersion != labelVersion)
            throw Refused(409, InventorySupplierReturnExitCodesV1.ConcurrentChange);
        var resolved = new List<InventorySupplierReturnResolvedSourceV1>
        {
            Resolve(null, command.Pending.TakeAll, command.Pending.QuantityKg, command.Pending.UndoQuantity,
                actual.Preview.PendingQuantityKg.Value(), actual.Preview.PendingUndoQuantity.Value(),
                actual.Preview.PendingWholePairAvailable, true)
        };
        foreach (var item in command.InvoicedSources)
        {
            var available = actual.Preview.InvoicedSources.SingleOrDefault(x => x.SourceEventId == item.SourceEventId)
                ?? throw Refused(409, InventorySupplierReturnExitCodesV1.ConcurrentChange);
            resolved.Add(Resolve(item.SourceEventId, item.TakeAll, item.QuantityKg, item.UndoQuantity,
                available.AvailableQuantityKg.Value(), available.AvailableUndoQuantity.Value(), available.WholePairAvailable, false));
        }
        if (resolved.Aggregate(ExactCostRatio.Of(0m), (sum, x) => sum.Add(x.QuantityKg.Value())).CompareTo(Parse(request.QuantityKg, 3)) != 0
            || resolved.Aggregate(ExactCostRatio.Of(0m), (sum, x) => sum.Add(x.UndoQuantity.Value())).CompareTo(Parse(request.UndoQuantity, 6)) != 0)
            throw Refused(400, InventorySupplierReturnExitCodesV1.InvalidRequest);
        foreach (var item in resolved)
        {
            var root = item.SourceEventId is { } id ? actual.Roots[id]
                : new InventorySupplierReturnCostSourceReader.SourceRemainder(actual.PendingRootKg, actual.PendingQuantity);
            var remainingKg = root.RootKg.Subtract(item.QuantityKg.Value());
            var remainingQuantity = root.Quantity.Subtract(item.UndoQuantity.Value());
            if (remainingKg.Sign < 0 || remainingQuantity.Sign < 0 || (remainingKg.Sign == 0) != (remainingQuantity.Sign == 0))
                throw Refused(400, InventorySupplierReturnExitCodesV1.InvalidRequest);
        }
        return new(actual.Preview.SourceVersion, request.GoodsAcceptanceId, labelVersion, checked(labelVersion + 1),
            request.PurchaseSource, actual.Preview, resolved);
    }

    private static InventorySupplierReturnResolvedSourceV1 Resolve(Guid? id, bool all, string? kg, string? quantity,
        ExactCostRatio availableKg, ExactCostRatio availableQuantity, bool wholePairAvailable, bool allowZero)
    {
        if (all && (kg is not null || quantity is null && !wholePairAvailable)) throw Refused(400, InventorySupplierReturnExitCodesV1.InvalidRequest);
        var selectedKg = all ? availableKg : Parse(kg, 3);
        var selectedQuantity = all && quantity is null ? availableQuantity : Parse(quantity, 6);
        if (selectedKg.Sign < 0 || selectedQuantity.Sign < 0 || (!allowZero && selectedKg.Sign == 0)
            || (selectedKg.Sign == 0) != (selectedQuantity.Sign == 0)
            || selectedKg.CompareTo(availableKg) > 0 || selectedQuantity.CompareTo(availableQuantity) > 0)
            throw Refused(400, InventorySupplierReturnExitCodesV1.InvalidRequest);
        return new(id, InventoryExactCostRatioV1.From(selectedKg), InventoryExactCostRatioV1.From(selectedQuantity));
    }

    private static ExactCostRatio Parse(string? value, int scale)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 40
            || !decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            || parsed < 0m || parsed.ToString(scale == 3 ? "0.###" : "0.######", CultureInfo.InvariantCulture) != value)
            throw Refused(400, InventorySupplierReturnExitCodesV1.InvalidRequest);
        return ExactCostRatio.Of(parsed);
    }
}

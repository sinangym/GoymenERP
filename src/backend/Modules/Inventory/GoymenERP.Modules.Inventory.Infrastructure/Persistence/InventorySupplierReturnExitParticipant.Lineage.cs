using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// AK-8 (SAT-D433, SAT-D441, SAT-D444, SAT-D445), behind <see cref="SupplierReturnLineageGoldenV1"/>: the exit leaves the named
/// source label instead of the receipt's own label. The receipt still bounds the kilograms (SAT-D306 (a)) and is the movement's
/// source, so the exit is valued from that receipt's cost stamp exactly as before.
/// <list type="bullet">
/// <item><b>Lineage</b>: the source label's lineage must be the receipt's root alone (a split of that receipt). A multi-receipt
/// lineage needs one exit per receipt in one atomic return and stays 503 here.</item>
/// <item><b>Quantity</b>: the source label's lineage is not that receipt alone. Stock must leave at the location/product moving
/// average (SAT-D444): Inventory admits it only when the average is readable and equals both the supplier price the caller
/// names and the receipt's own known TL cost per kg; a readable difference is the SAT-D371 409, anything unreadable is 503.</item>
/// </list>
/// A receipt that already carries invoice cost effects (or a request with an explicit cost allocation) needs per-invoice source
/// attribution of a foreign label, which the cost-source reader does not have: it stays 503, never a guessed share.
/// </summary>
public sealed partial class InventorySupplierReturnExitParticipant
{
    private async Task<ReceiptTrace> LineageExitTraceAsync(ReceiptTrace receipt, InventorySupplierReturnExitRequestV1 request,
        InventorySupplierReturnExitInput input, CancellationToken token)
    {
        var sourceLabelId = request.SourceLabelId!.Value;
        if (request.CostAllocation is not null
            || await context.PurchaseInvoiceReceiptCostEffects.AnyAsync(row => row.AcceptanceId == request.GoodsAcceptanceId, token))
        {
            throw Refused(503, InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid);
        }

        var labelRow = await context.Labels.SingleOrDefaultAsync(row => row.Id == sourceLabelId, token);
        if (labelRow is null || labelRow.ProductId != receipt.ProductId)
        {
            throw Refused(409, InventorySupplierReturnExitCodesV1.LineageMismatch);
        }

        var scope = new CompanyContext();
        scope.Set(companyId);
        var lineage = await new InventorySupplierReturnLineageReader(context, scope).ReadAsync(companyId, sourceLabelId, token);
        if (lineage.ProductId != receipt.ProductId) throw Refused(409, InventorySupplierReturnExitCodesV1.LineageMismatch);
        if (request.LineageMode == InventorySupplierReturnLineageModeV1.Lineage)
        {
            InventorySupplierReturnLineageGateV1.RequireLineageMode(lineage, receipt.LabelRow.Id);
        }
        else
        {
            // SAT-D445: a lineage that is the named receipt alone is a lineage return, never a quantity return.
            if (lineage.Roots.Count == 1 && lineage.Roots[0].RootLabelId == receipt.LabelRow.Id)
                throw Refused(409, InventorySupplierReturnExitCodesV1.LineageMismatch);
        }

        MaterialLabel label;
        try
        {
            label = await RestoreLabelAsync(labelRow, token);
        }
        catch (Exception exception) when (exception is InventoryRuleException or ArgumentException or JsonException)
        {
            throw Refused(503, InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid);
        }

        var exit = receipt with { Label = label, LabelRow = labelRow };
        if (request.LineageMode == InventorySupplierReturnLineageModeV1.Quantity)
        {
            await RequireQuantityReturnAtAverageAsync(exit, input, request.SourceBlock, token);
        }

        return exit;
    }

    /// <summary>
    /// SAT-D444/TI8: the quantity return leaves at the moving average and bills the chosen order's unit price; the exit itself is
    /// valued from the receipt's stamp. Only when all three are the same known value is nothing left as a difference.
    /// </summary>
    private async Task RequireQuantityReturnAtAverageAsync(ReceiptTrace exit, InventorySupplierReturnExitInput input,
        GoymenERP.Modules.Inventory.Domain.Stock.BlockState block, CancellationToken token)
    {
        if (input.OriginalUnitCostTryPerKg is not { } price
            || exit.OwnerKind != GoymenERP.Modules.Inventory.Domain.Stock.StockPartyKind.Company || exit.OwnerId != companyId)
        {
            throw Refused(503, InventorySupplierReturnExitCodesV1.Unavailable);
        }

        var source = await SourcePositionAsync(exit, block, token);
        var locationId = source.Position.LocationId;
        var productId = exit.ProductId;
        var row = await context.AverageCostLedger.AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.LocationId == locationId && item.ProductId == productId)
            .SingleOrDefaultAsync(token) ?? throw Refused(503, InventorySupplierReturnExitCodesV1.Unavailable);
        AverageCostPosition position;
        try
        {
            position = InventoryAverageCostLedgerRows.PositionOf(row);
        }
        catch (InvalidOperationException)
        {
            throw Refused(503, InventorySupplierReturnExitCodesV1.Unavailable);
        }

        var stockKg = await InventoryAverageCostLedgerRows.CostedStock(context, companyId, productId)
            .Where(balance => balance.LocationId == locationId)
            .SumAsync(balance => (decimal?)balance.QuantityKg, token) ?? 0m;
        if (position.Status != AverageCostStatus.Known || position.AverageCostPerKg is not { } average
            || position.QuantityKg != stockKg)
        {
            throw Refused(503, InventorySupplierReturnExitCodesV1.Unavailable);
        }

        var result = SupplierReturnLineageCalculator.QuantityReturn(input.Quantity.Value, decimal.MaxValue, average, price);
        if (result.Outcome == SupplierReturnLineageOutcome.CostDifference)
        {
            throw Refused(409, InventorySupplierReturnExitCodesV1.CostDifferenceUnposted);
        }

        var stamp = await context.MovementCostStamps.AsNoTracking().SingleOrDefaultAsync(item => item.MovementId == exit.MovementId
            && item.LineNumber == 1 && item.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira, token);
        if (stamp is not { Status: (int)InventoryMovementCostStampStatusV1.Known } || stamp.UnitCostPerKg != average)
        {
            throw Refused(503, InventorySupplierReturnExitCodesV1.Unavailable);
        }
    }
}

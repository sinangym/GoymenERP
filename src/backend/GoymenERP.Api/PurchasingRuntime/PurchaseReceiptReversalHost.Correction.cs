using System.Data.Common;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.PurchasingRuntime;

internal sealed partial class PurchaseReceiptReversalHost
{
    private async Task<PurchaseReceiptCorrectionStockV1?> CorrectionAsync(DbConnection connection, DbTransaction transaction,
        IPurchaseReceiptAdjustmentParticipantV1 purchase,
        Guid companyId, PurchaseReceiptReversalWriteV1 write, PurchaseReceiptAdjustmentResultV1 adjusted,
        InventoryGoodsReceiptReversalResultV1 reversed, PurchasingWriteEnvelope envelope,
        Func<CancellationToken, Task> freshAuthority, CancellationToken token)
    {
        if (adjusted.IsReplay != reversed.IsReplay || adjusted.OriginalCorrelationId != reversed.OriginalCorrelationId) throw Unavailable();
        var next = write.Request.ReplacementReceipt;
        if (next is null) return null;
        var metadata = Metadata(envelope, write.CommandName);
        await using var receipt = await InventoryGoodsReceiptParticipant.EnlistAsync(connection, transaction, companyId,
            services.GetRequiredService<ISettingsPrerequisiteProvider>(), token, clock);
        InventoryGoodsReceiptRequestV1 request;
        if (adjusted.IsReplay)
        {
            var original = await receipt.ReadReplayAsync(next.AcceptanceId, metadata, token) ?? throw Unavailable();
            await freshAuthority(token);
            request = original.OriginalRequest;
            if (request.CompanyId != companyId || request.AcceptanceId != next.AcceptanceId
                || request.PurchaseOrderId != write.OrderId || request.PurchaseOrderLineId != adjusted.LineId
                || request.PurchaseOrderVersionAfter != adjusted.OrderVersion || request.ExpectedProductVersion != next.ExpectedProductVersion
                || request.LabelId != next.LabelId || request.LotId != next.LotId || request.LabelCode != next.LabelCode
                || request.MovementId != next.MovementId || request.QuantityKg != next.MeasuredQuantityKg
                || request.LocationId != next.LocationId || request.HeatNumber != next.HeatNumber || request.CoilNumber != next.CoilNumber
                || request.OccurredAtUtc != next.OccurredAtUtc) throw Unavailable();
        }
        else
        {
            // This is the existing NEW-receipt location/ownership/block/document policy, applied after the genuine
            // Purchasing correction acceptance has been flushed. No second RecordAcceptance call is made.
            {
                var owner = RequireTarget(await purchase.ReadTargetAsync(write.OrderId, write.AcceptanceId, token), companyId, write);
                await freshAuthority(token);
                var location = await locations.ReadAsync(companyId, next.LocationId, token);
                await freshAuthority(token);
                if (location is null || !location.IsActive || location.IsExternal)
                    throw new PurchasingServiceException(404, InventoryGoodsReceiptCodesV1.InvalidReference);
                request = new(companyId, next.AcceptanceId, write.OrderId, adjusted.LineId, adjusted.OrderVersion,
                    owner.ProductId, next.ExpectedProductVersion, owner.UnitId, next.LotId, next.LabelId, next.LabelCode,
                    next.HeatNumber, next.CoilNumber, PurchaseGoodsReceiptDocumentPolicyV1.RequiredForNewReceipt,
                    StockPartyKind.Company, companyId, StockPartyKind.Company, companyId, next.LocationId,
                    Enum.Parse<BlockState>(PurchaseGoodsReceiptApiV1.InitialBlock), next.MovementId, next.MeasuredQuantityKg, next.OccurredAtUtc);
            }
        }
        var stocked = await receipt.StageAsync(request, metadata, token);
        await freshAuthority(token);
        if (stocked.CompanyId != companyId || stocked.AcceptanceId != next.AcceptanceId || stocked.IsReplay != adjusted.IsReplay
            || stocked.PurchaseOrderId != write.OrderId || stocked.PurchaseOrderLineId != adjusted.LineId
            || stocked.PurchaseOrderVersion != adjusted.OrderVersion || stocked.OriginalCorrelationId != adjusted.OriginalCorrelationId
            || stocked.QuantityKg != next.MeasuredQuantityKg || stocked.LabelId != next.LabelId || stocked.MovementId != next.MovementId)
            throw Unavailable();
        var reopened = reversed.Conversions.Where(item => item.ReopenedQuantity != "0").Select(item => item.FutureAllocationId).Distinct().ToArray();
        await using var conversion = await services.GetRequiredService<IInventoryGoodsReceiptFutureSupplyPortV1>()
            .EnlistAsync(connection, transaction, token);
        var converted = await conversion.ConvertAsync(new(companyId, next.AcceptanceId, write.OrderId, adjusted.GeneralLineId,
            request.PurchaseUnitId, request.ProductId, request.ExpectedProductVersion, request.LabelId,
            adjusted.NewAcceptedQuantity, request.QuantityKg, stocked.IsReplay, reopened), metadata, token);
        await freshAuthority(token);
        var warnings = PurchaseGoodsReceiptHost.Participants.LinkFailedReleases(converted, next.AcceptanceId);
        if (warnings.Count > 0 && !converted.IsReplay)
        {
            await using var releases = await services.GetRequiredService<IInventoryFutureAllocationReleasePortV1>()
                .EnlistAsync(connection, transaction, token);
            warnings = [.. warnings, .. await PurchaseGoodsReceiptHost.Participants.ReleaseRemainingActiveAsync(releases,
                adjusted.GeneralLineId, warnings[0].FailureCode, metadata, token)];
            await freshAuthority(token);
        }
        return new(next.AcceptanceId, stocked.LabelId, stocked.LabelVersion, stocked.MovementId, adjusted.NewAcceptedQuantity,
            stocked.QuantityKg, converted.Conversions.Where(item => item.Outcome == nameof(InventoryFutureSupplyConversionOutcomeV1.Converted)
                && reopened.Contains(item.FutureAllocationId)).Select(item => item.FutureAllocationId).ToArray(), warnings);
    }
}

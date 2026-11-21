using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed record PurchaseSupplierReturnReceiptAdjustmentV1(Guid AdjustmentId, decimal UndoQuantity, bool IsSupplierReturn);
public sealed record PurchaseSupplierReturnPurchaseSourceV1(decimal AcceptedQuantity, string SourceVersion,
    IReadOnlyList<PurchaseSupplierReturnReceiptAdjustmentV1> ReceiptAdjustments);

/// <summary>Purchasing's immutable purchase-unit evidence. No conversion from Inventory kilograms is inferred.</summary>
public sealed class PurchaseSupplierReturnPurchaseSourceReader(PurchasingDbContext context, ICompanyContext companyContext,
    IPurchaseAcceptanceSourceV1 acceptances)
{
    public async Task<PurchaseSupplierReturnPurchaseSourceV1> ReadAsync(Guid companyId, Guid orderId, Guid acceptanceId,
        Guid? excludedAdjustmentId = null, CancellationToken cancellationToken = default)
    {
        RequireCompany(companyId);
        PurchaseAcceptanceSourceV1? original;
        try { original = await acceptances.ReadAsync(orderId, acceptanceId, cancellationToken); }
        catch (PurchaseAcceptanceParticipantException exception) { throw Unavailable(exception); }
        if (original is null) throw new PurchasingServiceException(404, "not_found");
        if (original.CompanyId != companyId || original.PurchaseOrderId != orderId || original.AcceptanceId != acceptanceId)
            throw Unavailable();
        var rows = await context.ReceiptAdjustments.AsNoTracking().Where(row => row.CompanyId == companyId
            && row.AcceptanceId == acceptanceId && (!excludedAdjustmentId.HasValue || row.AdjustmentId != excludedAdjustmentId.Value))
            .OrderBy(row => row.AdjustmentId).ToArrayAsync(cancellationToken);
        RequireCompany(companyId);
        var accepted = decimal.Parse(original.AcceptedQuantity, CultureInfo.InvariantCulture);
        if (rows.Any(row => row.OrderId != orderId || row.LineId != original.PurchaseOrderLineId || row.AdjustmentId == Guid.Empty
            || row.UndoQuantity <= 0) || rows.Sum(row => row.UndoQuantity) > accepted) throw Unavailable();
        var adjustments = rows.Select(row => new PurchaseSupplierReturnReceiptAdjustmentV1(row.AdjustmentId,
            row.UndoQuantity, row.IsSupplierReturn)).ToArray();
        var payload = JsonSerializer.Serialize(new { original, adjustments }, PurchasingJson.Options);
        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        return new PurchaseSupplierReturnPurchaseSourceV1(accepted, version, adjustments);
    }

    private void RequireCompany(Guid companyId)
    {
        if (companyId == Guid.Empty || !companyContext.IsResolved || companyContext.CompanyId != companyId)
            throw new PurchasingServiceException(403, "forbidden");
    }
    private static PurchasingServiceException Unavailable(Exception? exception = null) =>
        new PurchasingServiceException(503, PurchaseSupplierReturnCodesV1.Unavailable, innerException: exception);
}

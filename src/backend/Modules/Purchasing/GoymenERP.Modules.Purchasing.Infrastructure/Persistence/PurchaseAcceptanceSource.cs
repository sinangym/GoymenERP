using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>Read-only projection of the existing acceptance row/result. No new ledger, migration or current-total inference.</summary>
public sealed class PurchaseAcceptanceSource(PurchasingDbContext context, ICompanyContext companyContext) : IPurchaseAcceptanceSourceV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PurchaseAcceptanceSourceV1?> ReadAsync(Guid purchaseOrderId, Guid acceptanceId,
        CancellationToken cancellationToken = default)
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
            throw new PurchasingServiceException(403, "forbidden");
        if (purchaseOrderId == Guid.Empty || acceptanceId == Guid.Empty) return null;
        var companyId = companyContext.CompanyId;
        var row = await context.Acceptances.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId
            && candidate.OrderId == purchaseOrderId && candidate.AcceptanceId == acceptanceId, cancellationToken);
        if (!companyContext.IsResolved || companyContext.CompanyId != companyId)
            throw new PurchasingServiceException(403, "forbidden");
        if (row is null) return null;

        PurchaseAcceptanceStageResultV1? original;
        try { original = JsonSerializer.Deserialize<PurchaseAcceptanceStageResultV1>(row.ResultJson, Json); }
        catch (JsonException) { throw Unavailable(); }
        if (original is null || original.CompanyId != companyId || original.AcceptanceId != row.AcceptanceId
            || original.PurchaseOrderId != row.OrderId || original.PurchaseOrderLineId != row.LineId || row.LineId == Guid.Empty
            || row.OrderVersionAfter < 1 || original.PurchaseOrderVersion != row.OrderVersionAfter.ToString(CultureInfo.InvariantCulture)
            || original.AcceptedQuantity != PurchaseAcceptanceParticipantContractV1.FormatQuantity(row.Quantity)
            || row.Quantity <= 0 || original.IsReplay || !CanonicalNonnegative(original.RemainingQuantity))
        {
            throw Unavailable();
        }

        return new(companyId, row.AcceptanceId, row.OrderId, row.LineId, original.PurchaseOrderVersion,
            original.AcceptedQuantity, original.RemainingQuantity, row.RecordedAtUtc);
    }

    private static bool CanonicalNonnegative(string text) => decimal.TryParse(text, NumberStyles.AllowDecimalPoint,
        CultureInfo.InvariantCulture, out var value) && value >= 0
        && PurchaseAcceptanceParticipantContractV1.FormatQuantity(value) == text;

    private static PurchaseAcceptanceParticipantException Unavailable() => new(PurchaseAcceptanceParticipantCodes.Unavailable,
        503, "The immutable acceptance source is unavailable.");
}

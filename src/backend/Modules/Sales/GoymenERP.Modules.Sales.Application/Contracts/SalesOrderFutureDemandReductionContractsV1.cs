using System.Data;

namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>KLN-D105: the effective accepted revision's remaining demand, computed by Sales from exact order-unit quantity
/// minus Logistics' shipped total. A removed line has zero remaining demand. No browser quantity crosses this boundary.</summary>
public sealed record SalesOrderFutureDemandReductionLineV1(Guid OrderLineId, Guid ProductId, string ProductVersion,
    Guid DemandUnitId, string NewOpenQuantityText);

/// <summary>Only lines reduced from the last effective approved/customer-accepted revision; ordered by line id.
/// The host locks ALL these demand lines ascending, then ALL distinct purchase lines ascending, before reducing any.
/// Each owner mutation shares this Sales command's transaction and envelope; the caller owns commit/rollback.</summary>
public sealed record SalesOrderFutureDemandReductionRequestV1(Guid OrderId, int Revision,
    IReadOnlyList<SalesOrderFutureDemandReductionLineV1> Lines, string ReasonText, SalesDraftWriteEnvelope Envelope);

public interface ISalesOrderFutureDemandReductionParticipantV1
{
    Task ReduceForAcceptedRevisionAsync(IDbTransaction transaction, SalesOrderFutureDemandReductionRequestV1 request,
        CancellationToken cancellationToken = default);
}

public static class SalesOrderFutureDemandReductionTextsV1
{
    public const string AcceptedRevision =
        "İç onayı ve gerekli müşteri kabulü tamamlanan satış revizyonunda azalan talebin gelecek ayırması bırakıldı (KLN-D105).";
}

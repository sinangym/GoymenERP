using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.RiskExceptions;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// API-07: the Sales-owned implementation of <see cref="ISalesShipmentRiskExceptionReadPortV1"/> over the company-filtered risk
/// exception store. It reads under the caller's server context — the consumer (the Logistics shipment gate, not yet connected)
/// measures its own authority before asking — and creates or changes nothing. Another company's exceptions are invisible under the
/// tenant filter and answer <see cref="SalesShipmentRiskExceptionStatusV1.None"/>, never 403. Without a resolved company, or
/// without the Europe/Istanbul time zone, it throws: the gate fails closed and the shipment stays held (SAT-D160 is never measured
/// in UTC).
/// </summary>
public sealed class SalesShipmentRiskExceptionReadPort(ISalesRiskExceptionStore store, ICompanyContext companyContext,
    TimeProvider timeProvider) : ISalesShipmentRiskExceptionReadPortV1
{
    public async Task<SalesShipmentRiskExceptionReadV1> GetAsync(Guid orderId, Guid loadingInstructionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            throw new InvalidOperationException("Risk istisnası şirket bağlamı çözülmeden okunmaz.");
        }

        if (orderId == Guid.Empty || loadingInstructionId == Guid.Empty)
        {
            return new SalesShipmentRiskExceptionReadV1(orderId, loadingInstructionId, SalesShipmentRiskExceptionStatusV1.None, null, null);
        }

        var today = SalesBusinessDay.DateOf(timeProvider.GetUtcNow());
        var items = await store.ListShipmentRiskExceptionsAsync(orderId, loadingInstructionId, cancellationToken);
        return Decide(orderId, loadingInstructionId, items, today);
    }

    /// <summary>A today-valid exception wins, then a pending one, then the newest (lapsed or rejected); none answers None.</summary>
    internal static SalesShipmentRiskExceptionReadV1 Decide(Guid orderId, Guid loadingInstructionId,
        IReadOnlyList<SalesShipmentRiskException> items, DateOnly today)
    {
        var chosen = items.FirstOrDefault(item => item.IsValidOn(today))
            ?? items.FirstOrDefault(item => item.State == SalesShipmentRiskExceptionState.Requested)
            ?? items.OrderByDescending(item => item.ExceptionNo).FirstOrDefault();
        if (chosen is null)
        {
            return new SalesShipmentRiskExceptionReadV1(orderId, loadingInstructionId, SalesShipmentRiskExceptionStatusV1.None, null, null);
        }

        var status = chosen.State switch
        {
            SalesShipmentRiskExceptionState.Requested => SalesShipmentRiskExceptionStatusV1.Pending,
            SalesShipmentRiskExceptionState.Approved when chosen.IsValidOn(today) => SalesShipmentRiskExceptionStatusV1.Valid,
            SalesShipmentRiskExceptionState.Approved => SalesShipmentRiskExceptionStatusV1.Lapsed,
            _ => SalesShipmentRiskExceptionStatusV1.Rejected,
        };
        return new SalesShipmentRiskExceptionReadV1(orderId, loadingInstructionId, status, chosen.ExceptionNo, chosen.ValidOn);
    }
}

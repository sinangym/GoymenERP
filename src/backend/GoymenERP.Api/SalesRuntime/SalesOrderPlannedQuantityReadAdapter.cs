using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.SalesRuntime;

/// <summary>
/// SAT-D406 / SAT-D367: binds the Sales planned-quantity port to the Logistics owner's read (Logistics references Sales, so the
/// host maps one to the other). A pure translation: the outcome, company, lines, answered orders and the paging flag pass as is.
/// </summary>
internal sealed class SalesOrderPlannedQuantityReadAdapter(ILogisticsSalesOrderPlannedQuantityReadPortV1 logistics)
    : ISalesOrderPlannedQuantityReadPortV1
{
    public async Task<SalesOrderPlannedQuantityReadV1> ReadOpenInstructionQuantitiesAsync(Guid companyId,
        IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default)
    {
        var answer = await logistics.ReadOpenInstructionQuantitiesAsync(companyId, orderIds, cancellationToken);
        var outcome = answer.Outcome switch
        {
            LogisticsSalesOrderPlannedQuantityOutcomeV1.Found => SalesOrderPlannedQuantityOutcomeV1.Found,
            LogisticsSalesOrderPlannedQuantityOutcomeV1.Denied => SalesOrderPlannedQuantityOutcomeV1.Denied,
            _ => SalesOrderPlannedQuantityOutcomeV1.Unavailable,
        };

        return outcome != SalesOrderPlannedQuantityOutcomeV1.Found
            ? new SalesOrderPlannedQuantityReadV1(outcome, answer.CompanyId, [], [], false)
            : new SalesOrderPlannedQuantityReadV1(outcome, answer.CompanyId,
                [.. answer.Lines.Select(line => new SalesOrderPlannedQuantityLineV1(line.OrderId, line.OrderLineId, line.UnitId,
                    line.OpenInstructionQuantity))],
                [.. answer.AnsweredOrderIds], answer.HasMore);
    }
}

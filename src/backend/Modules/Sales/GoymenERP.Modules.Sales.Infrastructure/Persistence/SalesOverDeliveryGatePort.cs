using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// SAT-D475: the Sales-owned implementation of <see cref="ISalesOverDeliveryGatePortV1"/> the Logistics shipment gate calls. The
/// judgement and the request are the draft service's (the one realization calculator, the Settings tolerance, the order's own
/// over-delivery approvals); the answer is checked to be about the asked line before it leaves Sales.
/// </summary>
internal sealed class SalesOverDeliveryGatePort(SalesDraftService service) : ISalesOverDeliveryGatePortV1
{
    public async Task<SalesOverDeliveryGateReadV1> EvaluateAsync(SalesOverDeliveryGateRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var answer = await service.EvaluateOverDeliveryGateAsync(request, cancellationToken);
        return answer.OrderId == request.OrderId && answer.LineId == request.LineId
            && string.Equals(answer.ContractVersion, SalesOverDeliveryGateContractV1.Version, StringComparison.Ordinal)
            && (answer.Result == SalesOverDeliveryGateResultV1.Held) == answer.RequestNo is >= 1
            ? answer
            : new SalesOverDeliveryGateReadV1(request.OrderId, request.LineId, SalesOverDeliveryGateResultV1.Unavailable, null,
                SalesOverDeliveryGateContractV1.Version);
    }
}

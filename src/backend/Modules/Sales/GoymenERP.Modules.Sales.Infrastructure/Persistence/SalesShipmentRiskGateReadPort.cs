using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// F-018 / plan S2: the Sales-owned implementation of <see cref="ISalesShipmentRiskGateReadPortV1"/>. The risk half comes from the
/// draft service's system-read evaluation (<see cref="ISalesShipmentRiskGateSource"/>, the one risk calculator); the shipment's
/// single-shipment risk exception comes from <see cref="ISalesShipmentRiskExceptionReadPortV1"/>, read once and embedded so the gate
/// decides on one answer. It creates or changes nothing and answers no amount (SAT-D162). Without a resolved company it throws, and
/// any error of either half propagates: the gate fails closed and the shipment stays held.
/// </summary>
public sealed class SalesShipmentRiskGateReadPort(ISalesShipmentRiskGateSource source,
    ISalesShipmentRiskExceptionReadPortV1 exceptions, ICompanyContext companyContext) : ISalesShipmentRiskGateReadPortV1
{
    public async Task<SalesShipmentRiskGateReadV1> GetAsync(Guid orderId, Guid loadingInstructionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            throw new InvalidOperationException("Sevk risk kapısı şirket bağlamı çözülmeden okunmaz.");
        }

        var decision = await source.EvaluateShipmentRiskGateAsync(orderId, cancellationToken);
        var exception = await exceptions.GetAsync(orderId, loadingInstructionId, cancellationToken);
        return Compose(orderId, loadingInstructionId, decision, exception);
    }

    /// <summary>
    /// The answer of one read. A decision that contradicts itself (a hold reason on "within", or none on a hold) is never passed
    /// on as it stands: it becomes <see cref="SalesShipmentRiskHoldReasonV1.SourceUnavailable"/> (fail-closed). An exception answer
    /// about another order or shipment is an error, never embedded.
    /// </summary>
    internal static SalesShipmentRiskGateReadV1 Compose(Guid orderId, Guid loadingInstructionId, SalesShipmentRiskGateDecisionV1 decision,
        SalesShipmentRiskExceptionReadV1 exception)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(exception);
        if (exception.OrderId != orderId || exception.LoadingInstructionId != loadingInstructionId)
        {
            throw new InvalidOperationException("Risk istisnası yanıtı istenen sevke ait değil.");
        }

        var consistent = decision.Result switch
        {
            SalesShipmentRiskResultV1.Within => decision.HoldReason is null,
            SalesShipmentRiskResultV1.Over => decision.HoldReason == SalesShipmentRiskHoldReasonV1.OverLimit,
            SalesShipmentRiskResultV1.Unavailable => decision.HoldReason
                is SalesShipmentRiskHoldReasonV1.CreditLimitNotSet or SalesShipmentRiskHoldReasonV1.SourceUnavailable,
            _ => false,
        };
        var (result, holdReason) = consistent
            ? (decision.Result, decision.HoldReason)
            : (SalesShipmentRiskResultV1.Unavailable, (SalesShipmentRiskHoldReasonV1?)SalesShipmentRiskHoldReasonV1.SourceUnavailable);
        return new SalesShipmentRiskGateReadV1(orderId, loadingInstructionId, result, holdReason, exception,
            SalesShipmentRiskGateContractV1.Version);
    }
}

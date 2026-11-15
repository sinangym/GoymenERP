using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// F-018 / plan S2 (FNS-D30, SAT-D310): the risk half of the Logistics shipment gate read. The order is evaluated by the same
/// <see cref="EvaluateRiskAsync"/> path and the same <see cref="SalesCustomerRiskCalculator"/> as the read-only risk check and the
/// S1 limit warning, and classified by the S1 mapping (<see cref="SalesOrderRiskWarnings.Classify"/>), so the gate, the warning and
/// the screen never disagree.
/// <list type="bullet">
/// <item>A company-scoped system read: the consumer (the Logistics gate) measured its own authority; no Sales permission is measured
/// here and no actor or role is used. Every owner read is still fenced — on the server-resolved company staying the same.</item>
/// <item>Read-only: nothing is created, recorded or audited (the S1 warning is the order command's, not the gate's).</item>
/// <item>No resolved company throws (fail-closed). Another company's or an unknown order, and any failure of the evaluation other
/// than a cancellation or a lost authority, is <see cref="SalesShipmentRiskHoldReasonV1.SourceUnavailable"/>, never "within".</item>
/// </list>
/// </summary>
public sealed partial class SalesDraftService : ISalesShipmentRiskGateSource
{
    public async Task<SalesShipmentRiskGateDecisionV1> EvaluateShipmentRiskGateAsync(Guid orderId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            throw new InvalidOperationException("Sevk risk kapısı şirket bağlamı çözülmeden okunmaz.");
        }

        if (orderId == Guid.Empty)
        {
            return SalesShipmentRiskGateDecisions.SourceUnavailable;
        }

        var scope = new Scope(companyContext.CompanyId, string.Empty, [], IsSalesSupport: false, IsSystemRead: true);
        try
        {
            // The store reads under the tenant filter: another company's order is null here, never a 403.
            var order = await GuardedAsync(scope, null, () => store.FindDraftAsync(orderId, cancellationToken), cancellationToken);
            if (order is null || order.CompanyId != scope.CompanyId)
            {
                return SalesShipmentRiskGateDecisions.SourceUnavailable;
            }

            var (party, _, _, _, _, _, _, evaluation) = await EvaluateRiskAsync(scope, null, order, cancellationToken);
            return SalesShipmentRiskGateDecisions.From(
                SalesOrderRiskWarnings.Classify(evaluation, party.CreditLimit, ServerBusinessDay()));
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException { Status: 401 or 403 }))
        {
            return SalesShipmentRiskGateDecisions.SourceUnavailable;
        }
    }
}

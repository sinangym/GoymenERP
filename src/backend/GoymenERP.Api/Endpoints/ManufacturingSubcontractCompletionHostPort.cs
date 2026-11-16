using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Manufacturing.Application;

namespace GoymenERP.Api.Endpoints;

/// <summary>URT-S18: translates the Manufacturing owner's read; Logistics measures authority and creates no sale.</summary>
public sealed class ManufacturingLogisticsSubcontractOrderReadPort(
    IManufacturingSubcontractCompletionReadPortV1 manufacturing, ICompanyContext companyContext)
    : ILogisticsSubcontractOrderReadPortV1
{
    public async Task<LogisticsSubcontractOrderReadV1> GetAsync(Guid manufacturingOrderId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var companyId = companyContext.IsResolved ? companyContext.CompanyId : Guid.Empty;
        if (companyId == Guid.Empty || manufacturingOrderId == Guid.Empty) return Unavailable();
        try
        {
            var answer = await manufacturing.GetAsync(companyId, manufacturingOrderId, cancellationToken);
            if (!companyContext.IsResolved || companyContext.CompanyId != companyId || answer is null
                || answer.CompanyId != companyId || answer.ManufacturingOrderId != manufacturingOrderId)
                return Unavailable();
            if (answer.Outcome == ManufacturingSubcontractCompletionOutcomeV1.NotFound)
                return new(LogisticsSubcontractOrderReadOutcomeV1.NotFound, companyId, manufacturingOrderId,
                    Guid.Empty, [], false);
            if (answer.Outcome != ManufacturingSubcontractCompletionOutcomeV1.Available || answer.CustomerPartyId == Guid.Empty
                || answer.OutputLabelIds is null || answer.OutputLabelIds.Contains(Guid.Empty)
                || answer.OutputLabelIds.Distinct().Count() != answer.OutputLabelIds.Count) return Unavailable();
            return new(LogisticsSubcontractOrderReadOutcomeV1.Available, companyId, manufacturingOrderId,
                answer.CustomerPartyId, answer.OutputLabelIds, answer.IsCompleted);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Unavailable(); }

        LogisticsSubcontractOrderReadV1 Unavailable() => new(LogisticsSubcontractOrderReadOutcomeV1.Unavailable,
            companyId, manufacturingOrderId, Guid.Empty, [], false);
    }
}

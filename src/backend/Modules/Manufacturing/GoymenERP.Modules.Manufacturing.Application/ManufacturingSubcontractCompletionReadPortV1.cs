using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Modules.Manufacturing.Application;

public enum ManufacturingSubcontractCompletionOutcomeV1 { Available = 1, NotFound = 2, Unavailable = 3 }

/// <summary>URT-S18: owner-only completion and output membership, without a sale, turnover or stock effect.</summary>
public sealed record ManufacturingSubcontractCompletionReadV1(
    ManufacturingSubcontractCompletionOutcomeV1 Outcome, Guid CompanyId, Guid ManufacturingOrderId,
    Guid CustomerPartyId, IReadOnlyList<Guid> OutputLabelIds, bool IsCompleted);

public interface IManufacturingSubcontractCompletionReadPortV1
{
    Task<ManufacturingSubcontractCompletionReadV1> GetAsync(Guid companyId, Guid manufacturingOrderId,
        CancellationToken cancellationToken = default);
}

public sealed class ManufacturingSubcontractCompletionReadPort(
    IManufacturingOrderStore orders, IManufacturingPlanStore plans, IManufacturingLabelLineageReadPortV1 lineage,
    ICompanyContext companyContext) : IManufacturingSubcontractCompletionReadPortV1
{
    public async Task<ManufacturingSubcontractCompletionReadV1> GetAsync(Guid companyId, Guid manufacturingOrderId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Scoped() || manufacturingOrderId == Guid.Empty) return Empty(ManufacturingSubcontractCompletionOutcomeV1.Unavailable);
        try
        {
            var order = await orders.FindOrderAsync(manufacturingOrderId, cancellationToken);
            if (!Scoped()) return Empty(ManufacturingSubcontractCompletionOutcomeV1.Unavailable);
            if (order is null) return Empty(ManufacturingSubcontractCompletionOutcomeV1.NotFound);
            if (order.CompanyId != companyId || order.Id != manufacturingOrderId)
                return Empty(ManufacturingSubcontractCompletionOutcomeV1.Unavailable);
            var plan = await plans.FindPlanAsync(order.PlanId, cancellationToken);
            if (!Scoped()) return Empty(ManufacturingSubcontractCompletionOutcomeV1.Unavailable);
            if (plan?.SalesOrderLink is not { } link || plan.CompanyId != companyId
                || order.PlannedInputs.Count == 0 || order.PlannedInputs.Any(input =>
                    input.CommercialContext is not { } context || context.PartyId != link.CustomerPartyId
                    || context.CompanyPartyAccountId != link.CustomerAccountId))
                return Empty(ManufacturingSubcontractCompletionOutcomeV1.NotFound);
            var trace = await lineage.ReadOrderAsync(companyId, manufacturingOrderId, cancellationToken);
            if (!Scoped() || trace is null || trace.CompanyId != companyId || trace.OrderId != manufacturingOrderId
                || trace.Truncated || trace.Outputs is null || trace.Outputs.Any(output => output.CompanyId != companyId
                    || output.OrderId != manufacturingOrderId || output.OutputLabelId == Guid.Empty))
                return Empty(ManufacturingSubcontractCompletionOutcomeV1.Unavailable);
            var remaining = order.Outputs.Where(output => output.Mass.Value > order.OutputReversals
                .Where(reversal => reversal.OutputId == output.OutputId).Sum(reversal => reversal.Mass.Value))
                .Select(output => output.OutputId).ToHashSet();
            var labels = trace.Outputs.Where(output => remaining.Contains(output.OutputId))
                .Select(output => output.OutputLabelId).Distinct().Order().ToArray();
            return new(ManufacturingSubcontractCompletionOutcomeV1.Available, companyId, manufacturingOrderId,
                link.CustomerPartyId, labels, order.Status == OrderStatus.Closed);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Empty(ManufacturingSubcontractCompletionOutcomeV1.Unavailable); }

        bool Scoped() => companyId != Guid.Empty && companyContext.IsResolved && companyContext.CompanyId == companyId;
        ManufacturingSubcontractCompletionReadV1 Empty(ManufacturingSubcontractCompletionOutcomeV1 outcome) =>
            new(outcome, companyId, manufacturingOrderId, Guid.Empty, [], false);
    }
}

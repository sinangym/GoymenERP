using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Modules.Manufacturing.Application;

public sealed record ManufacturingSubcontractOutputProofV2(Guid OutputFactId, Guid LabelId, string RemainingKg);
/// <summary>Price-free owner facts. Sales must separately prove its current order/line and Inventory its actual ownership.
/// SalesLinkRevision is separate from plan Version: changing a link does not change plan content.</summary>
public sealed record ManufacturingSubcontractCompletionProofV2(long OrderVersion, Guid PlanId, long PlanVersion,
    long SalesLinkRevision, Guid SalesOrderId, Guid SalesOrderLineId, int SalesLineRevision,
    Guid CustomerPartyId, Guid CustomerAccountId, Guid ProductId, bool IsCompleted,
    IReadOnlyList<ManufacturingSubcontractOutputProofV2> Outputs, string SourceVersion);
public sealed record ManufacturingSubcontractCompletionReadV2(ManufacturingSubcontractCompletionOutcomeV1 Outcome,
    Guid CompanyId, Guid ManufacturingOrderId, ManufacturingSubcontractCompletionProofV2? Value);
public sealed record ManufacturingSubcontractCompletionPageV2(ManufacturingSubcontractCompletionOutcomeV1 Outcome,
    Guid CompanyId, Guid SalesOrderId, Guid SalesOrderLineId, int PageIndex, bool HasNext,
    IReadOnlyList<ManufacturingSubcontractCompletionReadV2> Items);

public interface IManufacturingSubcontractCompletionReadPortV2
{
    Task<ManufacturingSubcontractCompletionReadV2> GetAsync(Guid companyId, Guid manufacturingOrderId, CancellationToken token = default);
    Task<ManufacturingSubcontractCompletionPageV2> ReadBySalesLineAsync(Guid companyId, Guid salesOrderId,
        Guid salesOrderLineId, int pageIndex, CancellationToken token = default);
}

/// <summary>Rebuilds existing domain facts; the infrastructure owner supplies a consistent transaction.</summary>
public sealed class ManufacturingSubcontractCompletionReaderV2(IManufacturingOrderStore orders, IManufacturingPlanStore plans,
    IManufacturingLabelLineageReadPortV1 lineage, ICompanyContext company)
{
    public async Task<ManufacturingSubcontractCompletionReadV2> GetAsync(Guid companyId, Guid orderId, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!Scoped() || orderId == Guid.Empty) return Closed(ManufacturingSubcontractCompletionOutcomeV1.Unavailable);
        try
        {
            var order = await orders.FindOrderAsync(orderId, token);
            if (!Scoped()) return Closed(ManufacturingSubcontractCompletionOutcomeV1.Unavailable);
            if (order is null) return Closed(ManufacturingSubcontractCompletionOutcomeV1.NotFound);
            if (order.CompanyId != companyId || order.Id != orderId || order.Version < 1)
                return Closed(ManufacturingSubcontractCompletionOutcomeV1.Unavailable);
            var plan = await plans.FindPlanAsync(order.PlanId, token);
            if (!Scoped()) return Closed(ManufacturingSubcontractCompletionOutcomeV1.Unavailable);
            if (plan?.SalesOrderLink is not { } link) return Closed(ManufacturingSubcontractCompletionOutcomeV1.NotFound);
            if (plan.Id != order.PlanId || plan.CompanyId != companyId || plan.Version < 1 || plan.SalesOrderLinkRevision < 1
                || !link.IsWellFormed() || order.PlannedInputs.Count == 0 || order.PlannedInputs.Any(input =>
                    input.CommercialContext is not { } context || context.PartyId != link.CustomerPartyId
                    || context.CompanyPartyAccountId != link.CustomerAccountId))
                return Closed(ManufacturingSubcontractCompletionOutcomeV1.Unavailable);
            var trace = await lineage.ReadOrderAsync(companyId, orderId, token);
            if (!Scoped() || trace is null || trace.CompanyId != companyId || trace.OrderId != orderId || trace.Truncated
                || trace.Outputs is null || trace.Outputs.Any(output => output.CompanyId != companyId || output.OrderId != orderId
                    || output.OutputId == Guid.Empty || output.OutputLabelId == Guid.Empty))
                return Closed(ManufacturingSubcontractCompletionOutcomeV1.Unavailable);
            var outputProofs = new List<ManufacturingSubcontractOutputProofV2>();
            foreach (var output in order.Outputs.OrderBy(output => output.OutputId))
            {
                var remaining = output.Mass.Value - order.OutputReversals.Where(reversal => reversal.OutputId == output.OutputId)
                    .Sum(reversal => reversal.Mass.Value);
                if (remaining < 0m || decimal.Round(remaining, 3) != remaining)
                    return Closed(ManufacturingSubcontractCompletionOutcomeV1.Unavailable);
                if (remaining == 0m) continue;
                // URT-D43 / URETIM_HAZIRLIK_PLANI §7.3: ProcessLoss is an explicit non-stock fact.
                // It has no Inventory label and cannot become a delivery candidate. Every stockable
                // declaration still needs its own actual lineage; no legacy label identity is inferred.
                if (output.Class == OutputClass.ProcessLoss) continue;
                var labels = trace.Outputs.Where(item => item.OutputId == output.OutputId).ToArray();
                if (labels.Length != 1) return Closed(ManufacturingSubcontractCompletionOutcomeV1.Unavailable);
                outputProofs.Add(new(output.OutputId, labels[0].OutputLabelId, remaining.ToString("0.###", CultureInfo.InvariantCulture)));
            }
            if (outputProofs.Select(output => output.LabelId).Distinct().Count() != outputProofs.Count)
                return Closed(ManufacturingSubcontractCompletionOutcomeV1.Unavailable);
            var sourceVersion = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                companyId, orderId, orderVersion = order.Version, planId = plan.Id, planVersion = plan.Version,
                plan.SalesOrderLinkRevision, link, outputs = outputProofs,
            })));
            return new(ManufacturingSubcontractCompletionOutcomeV1.Available, companyId, orderId,
                new(order.Version, plan.Id, plan.Version, plan.SalesOrderLinkRevision, link.SalesOrderId,
                    link.SalesOrderLineId, link.LineRevision, link.CustomerPartyId, link.CustomerAccountId, link.ProductId,
                    order.Status == OrderStatus.Closed, outputProofs.AsReadOnly(), sourceVersion));
        }
        catch (OperationCanceledException) { throw; }
        catch { return Closed(ManufacturingSubcontractCompletionOutcomeV1.Unavailable); }
        bool Scoped() => companyId != Guid.Empty && company.IsResolved && company.CompanyId == companyId;
        ManufacturingSubcontractCompletionReadV2 Closed(ManufacturingSubcontractCompletionOutcomeV1 outcome) => new(outcome, companyId, orderId, null);
    }
}

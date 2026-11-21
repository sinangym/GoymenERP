namespace GoymenERP.Modules.Logistics.Application.Contracts;

public enum LogisticsSubcontractOrderReadOutcomeV1 { Available = 1, NotFound = 2, Unavailable = 3 }

/// <summary>URT-S18 / LOJ-D3: a customer-goods order's completion and output labels. No sale or stock effect.</summary>
public sealed record LogisticsSubcontractOrderReadV1(
    LogisticsSubcontractOrderReadOutcomeV1 Outcome, Guid CompanyId, Guid ManufacturingOrderId,
    Guid CustomerPartyId, IReadOnlyList<Guid> OutputLabelIds, bool IsCompleted);

/// <summary>The caller fences its own authority and validates the selected Inventory label's product and ownership.</summary>
public interface ILogisticsSubcontractOrderReadPortV1
{
    Task<LogisticsSubcontractOrderReadV1> GetAsync(Guid manufacturingOrderId, CancellationToken cancellationToken = default);
}

using System.Text.Json.Serialization;
using GoymenERP.Modules.Purchasing.Domain.Services;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>Private priced owner facts, not a public VM or durable approval. Status/version remain the actual Draft's.
/// The future parent participant must consume these inside its owned transaction and persist its native outcomes atomically.</summary>
public sealed record PurchaseServiceOrderApprovalPreparationV1(Guid CompanyId, string ActorId,
    [property: JsonIgnore] PurchaseServiceOrderState Prepared);

public interface IPurchaseServiceOrderApprovalPreparationV1
{
    Task<PurchaseServiceSupplierRevisionPreparationV1> ReadSupplierRevisionPreparationAsync(Guid serviceId, string expectedVersion,
        Guid supplierPartyId, Guid companyAccountId, string reason, Func<CancellationToken, Task> freshOwnerFence,
        CancellationToken cancellationToken = default) => Task.FromException<PurchaseServiceSupplierRevisionPreparationV1>(
            new PurchasingServiceException(503, "service_unavailable"));
    Task<PurchaseServiceOrderApprovalPreparationV1> ReadApprovalPreparationAsync(Guid serviceId, string expectedVersion,
        Func<CancellationToken, Task> freshOwnerFence, CancellationToken cancellationToken = default) =>
        Task.FromException<PurchaseServiceOrderApprovalPreparationV1>(new PurchasingServiceException(503, "service_unavailable"));
}

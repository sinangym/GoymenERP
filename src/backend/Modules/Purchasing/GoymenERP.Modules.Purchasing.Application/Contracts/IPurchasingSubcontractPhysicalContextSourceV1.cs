using GoymenERP.Modules.Purchasing.Domain.Subcontracts;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

public enum PurchasingSubcontractPhysicalContextOutcomeV1 { Unavailable = 0, Denied = 1, NotFound = 2, Found = 3 }
public sealed record PurchasingSubcontractPhysicalContextAnswerV1(
    PurchasingSubcontractPhysicalContextOutcomeV1 Outcome, SubcontractPhysicalContextV1? Context);
public interface IPurchasingSubcontractPhysicalContextSourceV1
{
    Task<PurchasingSubcontractPhysicalContextAnswerV1> GetAuthorizedAsync(Guid companyId,
        Guid sourcePhysicalRequestId, string expectedVersion, CancellationToken cancellationToken = default);
}

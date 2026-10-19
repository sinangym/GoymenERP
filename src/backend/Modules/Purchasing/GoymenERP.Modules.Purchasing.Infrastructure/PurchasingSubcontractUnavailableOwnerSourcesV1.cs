using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Modules.Purchasing.Infrastructure;

internal sealed class PurchasingSubcontractUnavailableSubjectSourceV1 : IPurchasingSubcontractSubjectSourceV1
{
    public Task<PurchasingSubcontractSubjectAnswerV1> GetAuthorizedAsync(Guid companyId,
        PurchasingCommercialLineSubjectReferenceV1 subject, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PurchasingSubcontractSubjectAnswerV1(PurchasingSubcontractSubjectOutcomeV1.Unavailable, null));
}

internal sealed class PurchasingSubcontractUnavailablePhysicalContextSourceV1 : IPurchasingSubcontractPhysicalContextSourceV1
{
    public Task<PurchasingSubcontractPhysicalContextAnswerV1> GetAuthorizedAsync(Guid companyId,
        Guid sourcePhysicalRequestId, string expectedVersion, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PurchasingSubcontractPhysicalContextAnswerV1(PurchasingSubcontractPhysicalContextOutcomeV1.Unavailable, null));
}

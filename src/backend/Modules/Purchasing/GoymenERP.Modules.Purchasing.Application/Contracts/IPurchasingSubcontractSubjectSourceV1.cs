namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// Authorized owner read used immediately before a subcontract order is admitted.
/// The provider resolves the current commercial subject in the requested company;
/// Purchasing does not infer a Product permission or retain an independent master.
/// </summary>
public interface IPurchasingSubcontractSubjectSourceV1
{
    Task<PurchasingSubcontractSubjectAnswerV1> GetAuthorizedAsync(
        Guid companyId,
        PurchasingCommercialLineSubjectReferenceV1 subject,
        CancellationToken cancellationToken = default);
}

public enum PurchasingSubcontractSubjectOutcomeV1
{
    Unavailable = 0,
    Denied = 1,
    NotFound = 2,
    Found = 3,
}

public sealed record PurchasingSubcontractSubjectAnswerV1(
    PurchasingSubcontractSubjectOutcomeV1 Outcome,
    PurchasingCommercialLineSubjectSnapshotV1? Subject);

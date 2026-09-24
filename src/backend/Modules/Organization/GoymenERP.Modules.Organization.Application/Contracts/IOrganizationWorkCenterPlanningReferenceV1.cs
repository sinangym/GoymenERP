namespace GoymenERP.Modules.Organization.Application.Contracts;

/// <summary>
/// The Organization-owned identity and canonical kind of a work center selected by another
/// module's planning command. Active-state policy is deliberately absent: no approved source says
/// that a work center's active flag decides whether Manufacturing may save a draft plan.
/// </summary>
public sealed record OrganizationWorkCenterPlanningReferenceV1(Guid WorkCenterId, string Kind);

/// <summary>
/// Company-scoped module read for a planning reference. It applies no Organization user permission:
/// the calling module authorizes its own command. An unknown or foreign work center is null; an
/// unresolved or different company context throws <see cref="OrganizationCompanyContextMismatchException"/>.
/// </summary>
public interface IOrganizationWorkCenterPlanningReferenceV1
{
    Task<OrganizationWorkCenterPlanningReferenceV1?> ReadAsync(
        Guid companyId,
        Guid workCenterId,
        CancellationToken cancellationToken = default);
}

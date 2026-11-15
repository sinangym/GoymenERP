namespace GoymenERP.Modules.Organization.Application.Contracts;

/// <summary>One weekly working window of a work centre, in <see cref="OrganizationWorkCenterPlanningReferenceV2.TimeZoneId"/>.</summary>
public sealed record OrganizationWorkCenterShiftV2(DayOfWeek Day, TimeOnly Start, TimeOnly End);

/// <summary>
/// Version 2 of the work-centre planning reference (B-15): V1's identity and canonical kind plus the work centre's
/// calendar. <paramref name="Version"/> is the calendar revision the read saw (0 while the work centre is on the URT-D11
/// default, <paramref name="Source"/> <c>Default</c>; the saved revision otherwise, <c>WorkCenter</c>), so a consumer can
/// record which calendar it used. <paramref name="Holidays"/> are the closed business dates of that revision, ascending.
/// <para>
/// URT-D29: capacity and calendar computation are outside v1; this reference is published so a consumer can show or
/// store the calendar, not so it derives a termin. <see cref="IOrganizationWorkCenterPlanningReferenceV1"/> stays as it is.
/// </para>
/// </summary>
public sealed record OrganizationWorkCenterPlanningReferenceV2(
    Guid WorkCenterId,
    string Kind,
    long Version,
    string Source,
    string TimeZoneId,
    IReadOnlyList<OrganizationWorkCenterShiftV2> Shifts,
    IReadOnlyList<DateOnly> Holidays);

/// <summary>
/// Company-scoped module read, with the same fences as V1: no Organization user permission is applied (the calling
/// module authorizes its own command); an unknown or foreign work centre is null; an unresolved or different company
/// context throws <see cref="OrganizationCompanyContextMismatchException"/>.
/// </summary>
public interface IOrganizationWorkCenterPlanningReferenceV2
{
    Task<OrganizationWorkCenterPlanningReferenceV2?> ReadAsync(
        Guid companyId,
        Guid workCenterId,
        CancellationToken cancellationToken = default);
}

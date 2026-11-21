namespace GoymenERP.Modules.Organization.Application.Contracts;

/// <summary>
/// One weekly working window of a work centre, in <see cref="OrganizationWorkCenterPlanningReferenceV2.TimeZoneId"/>,
/// starting on <paramref name="Day"/>. KIM-D33: when <paramref name="End"/> is earlier than <paramref name="Start"/> the
/// window spills into the next day (22:00–06:00 ends at 06:00 on the following day; 16:00–00:00 ends at midnight), and
/// a consumer that computes capacity splits it across both days. <paramref name="Start"/> never equals
/// <paramref name="End"/>.
/// </summary>
public sealed record OrganizationWorkCenterShiftV2(DayOfWeek Day, TimeOnly Start, TimeOnly End);

/// <summary>
/// Version 2 of the work-centre planning reference (B-15): V1's identity and canonical kind plus the work centre's
/// calendar. <paramref name="Version"/> is the calendar revision the read saw (0 while the work centre is on the URT-D11
/// default, <paramref name="Source"/> <c>Default</c>; the saved revision otherwise, <c>WorkCenter</c>), so a consumer can
/// record which calendar it used. <paramref name="Holidays"/> are the closed business dates of that revision, ascending.
/// <para>
/// URT-D29 kept capacity and calendar computation out of v1; NS-10 (Issue #66, SINAN-CEVAP 14: "calendar + suggestion")
/// now lets a consumer derive a due-date SUGGESTION from this calendar, which a person accepts and records through the
/// consumer's own command; nothing is written automatically and Organization computes no termin itself.
/// <see cref="IOrganizationWorkCenterPlanningReferenceV1"/> stays as it is.
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

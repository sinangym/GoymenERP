using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// URT-D89: the central adapter that answers the Settings work center capacity administration whether a work center exists
/// in the server company, from the Organization owner's planning reference read. Settings reads no Organization table;
/// Organization re-fences the company. The company asked about must be the server company (a copy of the context, never an
/// input). An unknown or foreign work center is <see cref="SettingsWorkCenterReferenceOutcomeV1.NotFound"/>; a context
/// mismatch or a failing owner is <see cref="SettingsWorkCenterReferenceOutcomeV1.Unavailable"/> (the command fails closed).
/// The work center's active flag is deliberately not consulted: no approved source makes it decide a capacity record.
/// </summary>
public sealed class SettingsWorkCenterReferenceHost(
    IOrganizationWorkCenterPlanningReferenceV1 reader,
    ICompanyContext companyContext) : ISettingsWorkCenterReferenceV1
{
    public async Task<SettingsWorkCenterReferenceOutcomeV1> FindAsync(Guid companyId, Guid workCenterId,
        CancellationToken cancellationToken = default)
    {
        if (!companyContext.IsResolved || companyContext.CompanyId != companyId)
        {
            return SettingsWorkCenterReferenceOutcomeV1.Unavailable;
        }

        OrganizationWorkCenterPlanningReferenceV1? reference;
        try
        {
            reference = await reader.ReadAsync(companyId, workCenterId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return SettingsWorkCenterReferenceOutcomeV1.Unavailable;
        }

        return reference is not null && reference.WorkCenterId == workCenterId
            ? SettingsWorkCenterReferenceOutcomeV1.Found
            : SettingsWorkCenterReferenceOutcomeV1.NotFound;
    }
}

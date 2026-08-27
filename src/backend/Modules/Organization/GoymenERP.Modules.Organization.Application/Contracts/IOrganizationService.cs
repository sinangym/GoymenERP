namespace GoymenERP.Modules.Organization.Application.Contracts;

public interface IOrganizationService
{
    Task<IReadOnlyList<CompanySummary>> GetAuthorizedCompaniesAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SiteSummary>> GetSitesAsync(CancellationToken cancellationToken);

    Task<PageResult<LocationSummary>> GetLocationsAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<LocationSummary?> GetLocationAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkCenterSummary>> GetWorkCentersAsync(CancellationToken cancellationToken);

    Task<CommandExecutionResult> SetLocationActiveStateAsync(
        Guid id,
        SetLocationActiveState command,
        CommandMetadata metadata,
        CancellationToken cancellationToken);
}

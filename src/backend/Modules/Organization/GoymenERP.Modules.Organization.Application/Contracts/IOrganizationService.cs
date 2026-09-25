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

    /// <summary>
    /// ORG-04: one work center of the resolved company inside the caller's <c>organization.work-centers.read</c>
    /// scope, or null — another company's, an out-of-scope and an unknown id are the same null (a code-less 404),
    /// never a disclosure that the row exists.
    /// </summary>
    /// <remarks>
    /// The default body exists only so the shell registry's in-memory test doubles of this interface
    /// (<c>ShellCapabilityRegistryTests</c>, a central file) keep compiling without an edit outside this module.
    /// It fails loudly rather than answering "not found": a registered implementation without the read is a
    /// wiring defect, not an empty result. <c>OrganizationService</c>, the one registered implementation, answers it.
    /// </remarks>
    Task<WorkCenterDetail?> GetWorkCenterAsync(Guid id, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Bu Organization servisi iş merkezi kartını okumuyor.");

    Task<CommandExecutionResult> SetLocationActiveStateAsync(
        Guid id,
        SetLocationActiveState command,
        CommandMetadata metadata,
        CancellationToken cancellationToken);
}

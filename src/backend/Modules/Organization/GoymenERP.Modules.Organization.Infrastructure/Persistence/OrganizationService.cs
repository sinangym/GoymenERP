using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

internal sealed class OrganizationService(
    OrganizationDbContext dbContext,
    ICurrentUser currentUser,
    ICompanyContext companyContext,
    ILogger<OrganizationService> logger)
    : IOrganizationService
{
    private const int MaximumPageSize = 100;

    public async Task<IReadOnlyList<CompanySummary>> GetAuthorizedCompaniesAsync(
        CancellationToken cancellationToken)
    {
        var allowedCompanyIds = currentUser.AllowedCompanyIds.ToArray();
        if (allowedCompanyIds.Length == 0)
        {
            return [];
        }

        return await dbContext.Companies
            .AsNoTracking()
            .Where(x => x.IsActive && allowedCompanyIds.Contains(x.Id))
            .OrderBy(x => x.Code)
            .Select(x => new CompanySummary(x.Id, x.Code, x.LegalName, x.IsPlaceholder))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SiteSummary>> GetSitesAsync(
        CancellationToken cancellationToken)
    {
        return await dbContext.Sites
            .AsNoTracking()
            .OrderBy(x => x.Code)
            .Select(x => new SiteSummary(
                x.Id,
                x.Code,
                x.Name,
                x.Type.ToString(),
                x.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<PageResult<LocationSummary>> GetLocationsAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, MaximumPageSize);
        var query = dbContext.Locations.AsNoTracking().OrderBy(x => x.Code);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(x => new LocationSummary(
                x.Id,
                x.SiteId,
                x.Code,
                x.Name,
                x.IsExternal,
                x.IsActive,
                x.Version))
            .ToListAsync(cancellationToken);

        return new PageResult<LocationSummary>(
            items,
            normalizedPage,
            normalizedPageSize,
            totalCount);
    }

    public Task<LocationSummary?> GetLocationAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return dbContext.Locations
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new LocationSummary(
                x.Id,
                x.SiteId,
                x.Code,
                x.Name,
                x.IsExternal,
                x.IsActive,
                x.Version))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkCenterSummary>> GetWorkCentersAsync(
        CancellationToken cancellationToken)
    {
        return await dbContext.WorkCenters
            .AsNoTracking()
            .OrderBy(x => x.Code)
            .Select(x => new WorkCenterSummary(
                x.Id,
                x.LocationId,
                x.Code,
                x.Name,
                x.Kind.ToString(),
                x.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<CommandResult> SetLocationActiveStateAsync(
        Guid id,
        SetLocationActiveState command,
        CommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        ValidateCommandMetadata(metadata);

        var location = await dbContext.Locations
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (location is null)
        {
            return CommandResult.NotFound;
        }

        if (location.Version != command.ExpectedVersion)
        {
            return CommandResult.VersionConflict;
        }

        location.SetActive(command.IsActive);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Organization location active state updated. CompanyId={CompanyId} LocationId={LocationId} ActorId={ActorId} CorrelationId={CorrelationId} IdempotencyKeySupplied={IdempotencyKeySupplied}",
                metadata.CompanyId,
                id,
                metadata.ActorId,
                metadata.CorrelationId,
                true);
            return CommandResult.Succeeded;
        }
        catch (DbUpdateConcurrencyException)
        {
            return CommandResult.VersionConflict;
        }
    }

    private void ValidateCommandMetadata(CommandMetadata metadata)
    {
        if (metadata.CompanyId != companyContext.CompanyId
            || !string.Equals(metadata.ActorId, currentUser.UserId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(metadata.CorrelationId)
            || string.IsNullOrWhiteSpace(metadata.IdempotencyKey))
        {
            throw new InvalidOperationException("Command yürütme bağlamı geçersiz.");
        }
    }
}

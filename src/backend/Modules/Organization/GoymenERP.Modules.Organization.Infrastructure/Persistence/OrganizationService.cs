using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

internal sealed class OrganizationService(
    OrganizationDbContext dbContext,
    ICurrentUser currentUser,
    ICompanyContext companyContext,
    IErpAuthorizationService authorizationService,
    IAuditEventCollector auditEventCollector,
    ILogger<OrganizationService> logger)
    : IOrganizationService
{
    private const int MaximumPageSize = 100;
    private const int CommandExecutionSchemaVersion = 1;
    private const string SetLocationActiveStateCommandName =
        "organization.location.set-active-state.v1";

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
        var scope = await authorizationService.GetPermissionScopeAsync(
            ErpPermissions.OrganizationSitesRead,
            companyContext.CompanyId,
            cancellationToken);
        var siteIds = scope.SiteIds.ToArray();
        var locationIds = scope.LocationIds.ToArray();
        var workCenterIds = scope.WorkCenterIds.ToArray();
        var query = dbContext.Sites.AsNoTracking();
        if (!scope.IsCompanyWide)
        {
            query = query.Where(site =>
                siteIds.Contains(site.Id)
                || dbContext.Locations.Any(location =>
                    location.SiteId == site.Id
                    && (locationIds.Contains(location.Id)
                        || dbContext.WorkCenters.Any(workCenter =>
                            workCenter.LocationId == location.Id
                            && workCenterIds.Contains(workCenter.Id)))));
        }

        return await query
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
        var scope = await authorizationService.GetPermissionScopeAsync(
            ErpPermissions.OrganizationLocationsRead,
            companyContext.CompanyId,
            cancellationToken);
        var query = ApplyLocationScope(dbContext.Locations.AsNoTracking(), scope)
            .OrderBy(x => x.Code);
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

    public async Task<LocationSummary?> GetLocationAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var scope = await authorizationService.GetPermissionScopeAsync(
            ErpPermissions.OrganizationLocationsRead,
            companyContext.CompanyId,
            cancellationToken);
        return await ApplyLocationScope(dbContext.Locations.AsNoTracking(), scope)
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
        var scope = await authorizationService.GetPermissionScopeAsync(
            ErpPermissions.OrganizationWorkCentersRead,
            companyContext.CompanyId,
            cancellationToken);
        var siteIds = scope.SiteIds.ToArray();
        var locationIds = scope.LocationIds.ToArray();
        var workCenterIds = scope.WorkCenterIds.ToArray();
        var query = dbContext.WorkCenters.AsNoTracking();
        if (!scope.IsCompanyWide)
        {
            query = query.Where(workCenter =>
                workCenterIds.Contains(workCenter.Id)
                || locationIds.Contains(workCenter.LocationId)
                || dbContext.Locations.Any(location =>
                    location.Id == workCenter.LocationId
                    && siteIds.Contains(location.SiteId)));
        }

        return await query
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

    public async Task<CommandExecutionResult> SetLocationActiveStateAsync(
        Guid id,
        SetLocationActiveState command,
        CommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        ValidateCommandMetadata(metadata);
        ValidateReason(command.Reason);

        var scope = await authorizationService.GetPermissionScopeAsync(
            ErpPermissions.OrganizationLocationsStatusUpdate,
            companyContext.CompanyId,
            cancellationToken);
        var idempotencyKeyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(
            new SetLocationActiveStateFingerprint(
                id,
                command.IsActive,
                command.ExpectedVersion,
                command.Reason.Trim()));
        var existingExecution = await FindCommandExecutionAsync(
            idempotencyKeyHash,
            cancellationToken);
        if (existingExecution is not null)
        {
            return ResolveExistingExecution(existingExecution, requestHash, metadata);
        }

        var location = await ApplyLocationScope(dbContext.Locations, scope)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (location is null)
        {
            return await CompleteCommandAsync(
                CommandResult.NotFound,
                idempotencyKeyHash,
                requestHash,
                metadata,
                hasBusinessMutation: false,
                cancellationToken);
        }

        if (location.Version != command.ExpectedVersion)
        {
            return await CompleteCommandAsync(
                CommandResult.VersionConflict,
                idempotencyKeyHash,
                requestHash,
                metadata,
                hasBusinessMutation: false,
                cancellationToken);
        }

        var versionBefore = location.Version;
        var stateChanged = location.IsActive != command.IsActive;
        var beforeJson = SerializeLocationAuditSnapshot(
            location,
            location.IsActive,
            versionBefore);
        location.SetActive(command.IsActive);
        var versionAfter = stateChanged ? versionBefore + 1 : versionBefore;
        var afterJson = SerializeLocationAuditSnapshot(
            location,
            command.IsActive,
            versionAfter);
        auditEventCollector.Enqueue(new AuditEventDraft(
            metadata.CompanyId,
            "organization.location.active-state.changed.v1",
            "Organization.Location",
            id.ToString("D"),
            stateChanged
                ? command.IsActive ? "Activate" : "Deactivate"
                : "NoChange",
            versionBefore,
            versionAfter,
            AuditActorTypes.User,
            metadata.ActorId,
            ErpPermissions.OrganizationLocationsStatusUpdate,
            scope.RoleCodes,
            beforeJson,
            afterJson,
            command.Reason,
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            metadata.SourceChannel,
            metadata.ClientIpAddress,
            metadata.UserAgent,
            metadata.CorrelationId,
            metadata.CausationId,
            metadata.IdempotencyKey));

        var result = await CompleteCommandAsync(
            CommandResult.Succeeded,
            idempotencyKeyHash,
            requestHash,
            metadata,
            hasBusinessMutation: true,
            cancellationToken);
        logger.LogInformation(
            "Organization location active state command completed. CompanyId={CompanyId} LocationId={LocationId} ActorId={ActorId} CorrelationId={CorrelationId} Result={Result} IsReplay={IsReplay}",
            metadata.CompanyId,
            id,
            metadata.ActorId,
            metadata.CorrelationId,
            result.Result,
            result.IsReplay);
        return result;
    }

    private async Task<CommandExecutionResult> CompleteCommandAsync(
        CommandResult result,
        string idempotencyKeyHash,
        string requestHash,
        CommandMetadata metadata,
        bool hasBusinessMutation,
        CancellationToken cancellationToken)
    {
        var response = CommandResponses.For(result);
        dbContext.CommandExecutions.Add(CommandExecution.Complete(new CommandExecutionData(
            Guid.NewGuid(),
            metadata.CompanyId,
            SetLocationActiveStateCommandName,
            idempotencyKeyHash,
            requestHash,
            metadata.ActorId,
            result.ToString(),
            response.StatusCode,
            JsonSerializer.Serialize(response),
            metadata.CorrelationId,
            DateTimeOffset.UtcNow,
            CommandExecutionSchemaVersion)));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new CommandExecutionResult(response, false, metadata.CorrelationId);
        }
        catch (DbUpdateConcurrencyException) when (hasBusinessMutation)
        {
            dbContext.ChangeTracker.Clear();
            var winner = await FindCommandExecutionAsync(
                idempotencyKeyHash,
                cancellationToken);
            if (winner is not null)
            {
                return ResolveExistingExecution(winner, requestHash, metadata);
            }

            return await CompleteCommandAsync(
                CommandResult.VersionConflict,
                idempotencyKeyHash,
                requestHash,
                metadata,
                hasBusinessMutation: false,
                cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            var winner = await FindCommandExecutionAsync(
                idempotencyKeyHash,
                cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return ResolveExistingExecution(winner, requestHash, metadata);
        }
    }

    private Task<CommandExecution?> FindCommandExecutionAsync(
        string idempotencyKeyHash,
        CancellationToken cancellationToken)
    {
        return dbContext.CommandExecutions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                execution =>
                    execution.CommandName == SetLocationActiveStateCommandName
                    && execution.IdempotencyKeyHash == idempotencyKeyHash,
                cancellationToken);
    }

    private static CommandExecutionResult ResolveExistingExecution(
        CommandExecution execution,
        string requestHash,
        CommandMetadata metadata)
    {
        if (!string.Equals(execution.ActorId, metadata.ActorId, StringComparison.Ordinal)
            || !string.Equals(execution.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new CommandExecutionResult(
                CommandResponses.For(CommandResult.IdempotencyConflict),
                false,
                metadata.CorrelationId);
        }

        var response = JsonSerializer.Deserialize<CommandResponseSnapshot>(
            execution.ResponseJson);
        if (response is null
            || !string.Equals(
                response.Result.ToString(),
                execution.Outcome,
                StringComparison.Ordinal)
            || response.StatusCode != execution.HttpStatusCode
            || response.Result == CommandResult.IdempotencyConflict)
        {
            throw new InvalidOperationException(
                "Kalıcı idempotency sonucu tanımlı command sözleşmesiyle uyumsuz.");
        }

        return new CommandExecutionResult(response, true, execution.OriginalCorrelationId);
    }

    private IQueryable<GoymenERP.Modules.Organization.Domain.Locations.Location>
        ApplyLocationScope(
            IQueryable<GoymenERP.Modules.Organization.Domain.Locations.Location> query,
            PermissionScope scope)
    {
        if (scope.IsCompanyWide)
        {
            return query;
        }

        var siteIds = scope.SiteIds.ToArray();
        var locationIds = scope.LocationIds.ToArray();
        var workCenterIds = scope.WorkCenterIds.ToArray();
        return query.Where(location =>
            locationIds.Contains(location.Id)
            || siteIds.Contains(location.SiteId)
            || dbContext.WorkCenters.Any(workCenter =>
                workCenter.LocationId == location.Id
                && workCenterIds.Contains(workCenter.Id)));
    }

    private void ValidateCommandMetadata(CommandMetadata metadata)
    {
        if (metadata.CompanyId != companyContext.CompanyId
            || !string.Equals(metadata.ActorId, currentUser.UserId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(metadata.CorrelationId)
            || string.IsNullOrWhiteSpace(metadata.SourceChannel)
            || string.IsNullOrWhiteSpace(metadata.IdempotencyKey))
        {
            throw new InvalidOperationException("Command yürütme bağlamı geçersiz.");
        }
    }

    private static void ValidateReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1000)
        {
            throw new ArgumentException(
                "Kritik değişiklik için 1-1000 karakter arasında bir gerekçe zorunludur.",
                nameof(reason));
        }
    }

    private static string SerializeLocationAuditSnapshot(
        GoymenERP.Modules.Organization.Domain.Locations.Location location,
        bool isActive,
        long version)
    {
        return JsonSerializer.Serialize(new
        {
            location.Id,
            location.CompanyId,
            location.SiteId,
            location.Code,
            location.Name,
            location.IsExternal,
            IsActive = isActive,
            Version = version,
        });
    }

    private sealed record SetLocationActiveStateFingerprint(
        Guid LocationId,
        bool IsActive,
        long ExpectedVersion,
        string Reason);

}

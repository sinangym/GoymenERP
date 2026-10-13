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
            .ThenBy(x => x.Id)
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
                x.IsActive,
                x.Version))
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
                x.Version,
                x.OperatorPartyId))
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
                x.Version,
                x.OperatorPartyId))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkCenterSummary>> GetWorkCentersAsync(
        CancellationToken cancellationToken)
    {
        var scope = await authorizationService.GetPermissionScopeAsync(
            ErpPermissions.OrganizationWorkCentersRead,
            companyContext.CompanyId,
            cancellationToken);
        var query = ApplyWorkCenterScope(dbContext.WorkCenters.AsNoTracking(), scope);

        // ORG-03 row eligibility (#66 5700319744): a row offers its location card only when
        // the caller may read THAT location. It is answered by the very predicate the card
        // read uses (`GetLocationAsync` → `ApplyLocationScope` on `locations.read`), so
        // "the row links" and "the card answers 200" cannot disagree. Without the
        // permission there is nothing to evaluate and no location query is issued.
        var locationScope = await authorizationService.GetPermissionScopeAsync(
            ErpPermissions.OrganizationLocationsRead,
            companyContext.CompanyId,
            cancellationToken);
        if (!locationScope.IsGranted)
        {
            return await query
                .OrderBy(x => x.Code)
                .Select(x => new WorkCenterSummary(
                    x.Id,
                    x.LocationId,
                    x.Code,
                    x.Name,
                    x.Kind.ToString(),
                    x.IsActive,
                    false))
                .ToListAsync(cancellationToken);
        }

        var openableLocationIds = ApplyLocationScope(
                dbContext.Locations.AsNoTracking(),
                locationScope)
            .Select(location => location.Id);
        return await query
            .OrderBy(x => x.Code)
            .Select(x => new WorkCenterSummary(
                x.Id,
                x.LocationId,
                x.Code,
                x.Name,
                x.Kind.ToString(),
                x.IsActive,
                openableLocationIds.Contains(x.LocationId)))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// ORG-04: one work center, answered by the list's own scope predicate (<see cref="ApplyWorkCenterScope"/>) on
    /// <c>organization.work-centers.read</c> under the tenant filter, so "the row is listed" and "the card answers 200"
    /// cannot disagree. Another company's, an out-of-scope and an unknown id are the same null. The location flag is
    /// the list's too: the location card's own read predicate on <c>organization.locations.read</c>, and without that
    /// permission no location query is issued. A read: nothing is written, audited or recorded.
    /// </summary>
    public async Task<WorkCenterDetail?> GetWorkCenterAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var scope = await authorizationService.GetPermissionScopeAsync(
            ErpPermissions.OrganizationWorkCentersRead,
            companyContext.CompanyId,
            cancellationToken);
        var query = ApplyWorkCenterScope(dbContext.WorkCenters.AsNoTracking(), scope)
            .Where(x => x.Id == id);

        var locationScope = await authorizationService.GetPermissionScopeAsync(
            ErpPermissions.OrganizationLocationsRead,
            companyContext.CompanyId,
            cancellationToken);
        if (!locationScope.IsGranted)
        {
            return await query
                .Select(x => new WorkCenterDetail(
                    x.Id,
                    x.LocationId,
                    x.Code,
                    x.Name,
                    x.Kind.ToString(),
                    x.IsActive,
                    x.Version,
                    false))
                .SingleOrDefaultAsync(cancellationToken);
        }

        var openableLocationIds = ApplyLocationScope(
                dbContext.Locations.AsNoTracking(),
                locationScope)
            .Select(location => location.Id);
        return await query
            .Select(x => new WorkCenterDetail(
                x.Id,
                x.LocationId,
                x.Code,
                x.Name,
                x.Kind.ToString(),
                x.IsActive,
                x.Version,
                openableLocationIds.Contains(x.LocationId)))
            .SingleOrDefaultAsync(cancellationToken);
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
        var location = await ApplyLocationScope(dbContext.Locations, scope)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        var existingExecution = await FindCommandExecutionAsync(
            idempotencyKeyHash,
            cancellationToken);
        if (existingExecution is not null)
        {
            if (!scope.IsCompanyWide && location is null)
            {
                return new CommandExecutionResult(
                    CommandResponses.For(CommandResult.NotFound),
                    false,
                    metadata.CorrelationId);
            }

            return ResolveExistingExecution(existingExecution, requestHash, metadata);
        }

        if (location is null)
        {
            return await CompleteCommandAsync(
                CommandResponses.For(CommandResult.NotFound),
                idempotencyKeyHash,
                requestHash,
                metadata,
                hasBusinessMutation: false,
                concurrencyContext: null,
                cancellationToken);
        }

        if (location.Version != command.ExpectedVersion)
        {
            return await CompleteCommandAsync(
                CommandResponses.VersionConflict(
                    command.ExpectedVersion,
                    location.Version),
                idempotencyKeyHash,
                requestHash,
                metadata,
                hasBusinessMutation: false,
                concurrencyContext: null,
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
            CommandResponses.For(CommandResult.Succeeded),
            idempotencyKeyHash,
            requestHash,
            metadata,
            hasBusinessMutation: true,
            new CommandConcurrencyContext(id, command.ExpectedVersion, scope),
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
        CommandResponseSnapshot response,
        string idempotencyKeyHash,
        string requestHash,
        CommandMetadata metadata,
        bool hasBusinessMutation,
        CommandConcurrencyContext? concurrencyContext,
        CancellationToken cancellationToken)
    {
        dbContext.CommandExecutions.Add(CommandExecution.Complete(new CommandExecutionData(
            Guid.NewGuid(),
            metadata.CompanyId,
            SetLocationActiveStateCommandName,
            idempotencyKeyHash,
            requestHash,
            metadata.ActorId,
            response.Result.ToString(),
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

            if (concurrencyContext is null)
            {
                throw;
            }

            var currentVersion = await ApplyLocationScope(
                    dbContext.Locations.AsNoTracking(),
                    concurrencyContext.Scope)
                .Where(x => x.Id == concurrencyContext.LocationId)
                .Select(x => (long?)x.Version)
                .SingleOrDefaultAsync(cancellationToken);
            var terminalResponse = currentVersion.HasValue
                ? CommandResponses.VersionConflict(
                    concurrencyContext.ExpectedVersion,
                    currentVersion.Value)
                : CommandResponses.For(CommandResult.NotFound);
            return await CompleteCommandAsync(
                terminalResponse,
                idempotencyKeyHash,
                requestHash,
                metadata,
                hasBusinessMutation: false,
                concurrencyContext: null,
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

    /// <summary>
    /// The work-centre read scope (ORG-03, unchanged): company-wide, or the work centre itself, its location, or its
    /// location's site. The list and the ORG-04 card both use this one predicate.
    /// </summary>
    private IQueryable<GoymenERP.Modules.Organization.Domain.WorkCenters.WorkCenter>
        ApplyWorkCenterScope(
            IQueryable<GoymenERP.Modules.Organization.Domain.WorkCenters.WorkCenter> query,
            PermissionScope scope) =>
        OrganizationScopes.ApplyWorkCenterScope(dbContext, query, scope);

    private IQueryable<GoymenERP.Modules.Organization.Domain.Locations.Location>
        ApplyLocationScope(
            IQueryable<GoymenERP.Modules.Organization.Domain.Locations.Location> query,
            PermissionScope scope) =>
        OrganizationScopes.ApplyLocationScope(dbContext, query, scope);

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

    private sealed record CommandConcurrencyContext(
        Guid LocationId,
        long ExpectedVersion,
        PermissionScope Scope);

}

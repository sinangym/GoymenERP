using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.Common;
using GoymenERP.Modules.Organization.Domain.Locations;
using GoymenERP.Modules.Organization.Domain.Sites;
using GoymenERP.Modules.Organization.Domain.WorkCenters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

/// <summary>
/// The site / location / work-centre master edit. Each command follows the location active-state command's protocol:
/// the key is looked up first and a repeated key with the same payload and actor replays the stored terminal answer
/// without applying anything; every terminal answer (created, updated, not found, stale version, code in use) is
/// stored with the key; an applied change writes exactly one audit event in the same transaction. The resolved company
/// context fences every read and write (tenant query filter plus the DbContext write guard), and each record kind is
/// scope-checked on its own write permission with the module's shared predicates.
/// </summary>
internal sealed class OrganizationMasterDataService(
    OrganizationDbContext dbContext,
    ICurrentUser currentUser,
    ICompanyContext companyContext,
    IErpAuthorizationService authorizationService,
    IAuditEventCollector auditEventCollector,
    ILogger<OrganizationMasterDataService> logger)
    : IOrganizationMasterDataService
{
    private const int CommandExecutionSchemaVersion = 1;
    internal const string CreateSiteCommandName = "organization.site.create.v1";
    internal const string UpdateSiteCommandName = "organization.site.update.v1";
    internal const string CreateLocationCommandName = "organization.location.create.v1";
    internal const string UpdateLocationCommandName = "organization.location.update.v1";
    internal const string CreateWorkCenterCommandName = "organization.work-center.create.v1";
    internal const string UpdateWorkCenterCommandName = "organization.work-center.update.v1";

    private const string SiteEntityType = "Organization.Site";
    private const string LocationEntityType = "Organization.Location";
    private const string WorkCenterEntityType = "Organization.WorkCenter";

    public async Task<CommandExecutionResult> CreateSiteAsync(
        CreateSite command,
        CommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandMetadata(metadata);
        var text = RequireText(command.Code, command.Name, command.Reason);
        if (!OrganizationMasterDataRules.IsSiteType(command.Type))
        {
            throw new ArgumentException("Tesis türü tanımlı değil.", nameof(command));
        }

        var type = Enum.Parse<SiteType>(command.Type, ignoreCase: false);
        const string permission = OrganizationPermissionManifestV1.SitesWrite;
        var scope = await ReadScopeAsync(permission, cancellationToken);
        var request = CreateRequest(
            CreateSiteCommandName,
            metadata,
            new CreateSiteFingerprint(text.Code, text.Name, command.Type, text.Reason));

        // A site has no parent inside the company: only a company-wide grant reaches the site master.
        var existing = await FindCommandExecutionAsync(request, cancellationToken);
        if (existing is not null)
        {
            return scope.IsCompanyWide
                ? ResolveExistingExecution(existing, request, metadata)
                : NotRecordedNotFound(metadata);
        }

        if (!scope.IsCompanyWide)
        {
            return await CompleteAsync(request, metadata, CommandResponses.For(CommandResult.NotFound), cancellationToken);
        }

        if (await IsCodeInUseAsync(dbContext.Sites, text.Code, exceptId: null, cancellationToken))
        {
            return await CompleteAsync(request, metadata, CommandResponses.For(CommandResult.CodeInUse), cancellationToken);
        }

        var site = new Site(Guid.NewGuid(), metadata.CompanyId, text.Code, text.Name, type);
        dbContext.Sites.Add(site);
        EnqueueAudit(
            metadata,
            scope,
            permission,
            "organization.site.created.v1",
            SiteEntityType,
            site.Id,
            "Create",
            versionBefore: null,
            site.Version,
            beforeJson: "{}",
            SerializeSite(site, site.Version),
            text.Reason);

        return await CompleteMutationAsync(
            request,
            metadata,
            CommandResponses.Created(site.Id, site.Version),
            (_, token) => RecoverCreateAsync(dbContext.Sites, text.Code, token),
            site.Id,
            cancellationToken);
    }

    public async Task<CommandExecutionResult> UpdateSiteAsync(
        Guid id,
        UpdateSite command,
        CommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandMetadata(metadata);
        var text = RequireText(command.Code, command.Name, command.Reason);
        RequireExpectedVersion(command.ExpectedVersion);
        const string permission = OrganizationPermissionManifestV1.SitesWrite;
        var scope = await ReadScopeAsync(permission, cancellationToken);
        var request = CreateRequest(
            UpdateSiteCommandName,
            metadata,
            new UpdateFingerprint(id, text.Code, text.Name, command.ExpectedVersion, text.Reason));

        IQueryable<Site> ScopedSites(IQueryable<Site> query) =>
            scope.IsCompanyWide ? query : query.Where(_ => false);

        var site = await ScopedSites(dbContext.Sites).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return await UpdateAsync(
            request,
            metadata,
            scope,
            permission,
            site,
            command.ExpectedVersion,
            text,
            dbContext.Sites,
            ScopedSites(dbContext.Sites.AsNoTracking()).Where(x => x.Id == id),
            "organization.site.updated.v1",
            SiteEntityType,
            SerializeSite,
            cancellationToken);
    }

    public async Task<CommandExecutionResult> CreateLocationAsync(
        CreateLocation command,
        CommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandMetadata(metadata);
        var text = RequireText(command.Code, command.Name, command.Reason);
        const string permission = OrganizationPermissionManifestV1.LocationsWrite;
        var scope = await ReadScopeAsync(permission, cancellationToken);
        var request = CreateRequest(
            CreateLocationCommandName,
            metadata,
            new CreateLocationFingerprint(command.SiteId, text.Code, text.Name, text.Reason));

        // The parent site must be inside the grant's scope; another company's, an out-of-scope and an unknown site
        // are the same code-less 404, never a disclosure that the site exists.
        var site = await OrganizationScopes.ApplyLocationParentScope(dbContext.Sites.AsNoTracking(), scope)
            .Where(x => x.Id == command.SiteId)
            .Select(x => new { x.Id, x.Type })
            .SingleOrDefaultAsync(cancellationToken);
        var existing = await FindCommandExecutionAsync(request, cancellationToken);
        if (existing is not null)
        {
            return !scope.IsCompanyWide && site is null
                ? NotRecordedNotFound(metadata)
                : ResolveExistingExecution(existing, request, metadata);
        }

        if (site is null)
        {
            return await CompleteAsync(request, metadata, CommandResponses.For(CommandResult.NotFound), cancellationToken);
        }

        if (await IsCodeInUseAsync(dbContext.Locations, text.Code, exceptId: null, cancellationToken))
        {
            return await CompleteAsync(request, metadata, CommandResponses.For(CommandResult.CodeInUse), cancellationToken);
        }

        // The external flag follows the parent site's type, as the runtime seed derives it (DEC-ORG-004, OPEN-002).
        var location = new Location(
            Guid.NewGuid(),
            metadata.CompanyId,
            site.Id,
            text.Code,
            text.Name,
            site.Type == SiteType.ExternalWarehouse);
        dbContext.Locations.Add(location);
        EnqueueAudit(
            metadata,
            scope,
            permission,
            "organization.location.created.v1",
            LocationEntityType,
            location.Id,
            "Create",
            versionBefore: null,
            location.Version,
            beforeJson: "{}",
            SerializeLocation(location, location.Version),
            text.Reason);

        return await CompleteMutationAsync(
            request,
            metadata,
            CommandResponses.Created(location.Id, location.Version),
            (_, token) => RecoverCreateAsync(dbContext.Locations, text.Code, token),
            location.Id,
            cancellationToken);
    }

    public async Task<CommandExecutionResult> UpdateLocationAsync(
        Guid id,
        UpdateLocation command,
        CommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandMetadata(metadata);
        var text = RequireText(command.Code, command.Name, command.Reason);
        RequireExpectedVersion(command.ExpectedVersion);
        const string permission = OrganizationPermissionManifestV1.LocationsWrite;
        var scope = await ReadScopeAsync(permission, cancellationToken);
        var request = CreateRequest(
            UpdateLocationCommandName,
            metadata,
            new UpdateFingerprint(id, text.Code, text.Name, command.ExpectedVersion, text.Reason));

        var location = await OrganizationScopes.ApplyLocationScope(dbContext, dbContext.Locations, scope)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return await UpdateAsync(
            request,
            metadata,
            scope,
            permission,
            location,
            command.ExpectedVersion,
            text,
            dbContext.Locations,
            OrganizationScopes.ApplyLocationScope(dbContext, dbContext.Locations.AsNoTracking(), scope)
                .Where(x => x.Id == id),
            "organization.location.updated.v1",
            LocationEntityType,
            SerializeLocation,
            cancellationToken);
    }

    public async Task<CommandExecutionResult> CreateWorkCenterAsync(
        CreateWorkCenter command,
        CommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandMetadata(metadata);
        var text = RequireText(command.Code, command.Name, command.Reason);
        if (!OrganizationMasterDataRules.IsWorkCenterKind(command.Kind))
        {
            throw new ArgumentException("İş merkezi türü tanımlı değil.", nameof(command));
        }

        var kind = Enum.Parse<WorkCenterKind>(command.Kind, ignoreCase: false);
        const string permission = OrganizationPermissionManifestV1.WorkCentersWrite;
        var scope = await ReadScopeAsync(permission, cancellationToken);
        var request = CreateRequest(
            CreateWorkCenterCommandName,
            metadata,
            new CreateWorkCenterFingerprint(command.LocationId, text.Code, text.Name, command.Kind, text.Reason));

        var locationId = await OrganizationScopes.ApplyWorkCenterParentScope(dbContext.Locations.AsNoTracking(), scope)
            .Where(x => x.Id == command.LocationId)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        var existing = await FindCommandExecutionAsync(request, cancellationToken);
        if (existing is not null)
        {
            return !scope.IsCompanyWide && locationId is null
                ? NotRecordedNotFound(metadata)
                : ResolveExistingExecution(existing, request, metadata);
        }

        if (locationId is null)
        {
            return await CompleteAsync(request, metadata, CommandResponses.For(CommandResult.NotFound), cancellationToken);
        }

        if (await IsCodeInUseAsync(dbContext.WorkCenters, text.Code, exceptId: null, cancellationToken))
        {
            return await CompleteAsync(request, metadata, CommandResponses.For(CommandResult.CodeInUse), cancellationToken);
        }

        var workCenter = new WorkCenter(
            Guid.NewGuid(),
            metadata.CompanyId,
            locationId.Value,
            text.Code,
            text.Name,
            kind);
        dbContext.WorkCenters.Add(workCenter);
        EnqueueAudit(
            metadata,
            scope,
            permission,
            "organization.work-center.created.v1",
            WorkCenterEntityType,
            workCenter.Id,
            "Create",
            versionBefore: null,
            workCenter.Version,
            beforeJson: "{}",
            SerializeWorkCenter(workCenter, workCenter.Version),
            text.Reason);

        return await CompleteMutationAsync(
            request,
            metadata,
            CommandResponses.Created(workCenter.Id, workCenter.Version),
            (_, token) => RecoverCreateAsync(dbContext.WorkCenters, text.Code, token),
            workCenter.Id,
            cancellationToken);
    }

    public async Task<CommandExecutionResult> UpdateWorkCenterAsync(
        Guid id,
        UpdateWorkCenter command,
        CommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandMetadata(metadata);
        var text = RequireText(command.Code, command.Name, command.Reason);
        RequireExpectedVersion(command.ExpectedVersion);
        const string permission = OrganizationPermissionManifestV1.WorkCentersWrite;
        var scope = await ReadScopeAsync(permission, cancellationToken);
        var request = CreateRequest(
            UpdateWorkCenterCommandName,
            metadata,
            new UpdateFingerprint(id, text.Code, text.Name, command.ExpectedVersion, text.Reason));

        var workCenter = await OrganizationScopes.ApplyWorkCenterScope(dbContext, dbContext.WorkCenters, scope)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return await UpdateAsync(
            request,
            metadata,
            scope,
            permission,
            workCenter,
            command.ExpectedVersion,
            text,
            dbContext.WorkCenters,
            OrganizationScopes.ApplyWorkCenterScope(dbContext, dbContext.WorkCenters.AsNoTracking(), scope)
                .Where(x => x.Id == id),
            "organization.work-center.updated.v1",
            WorkCenterEntityType,
            SerializeWorkCenter,
            cancellationToken);
    }

    /// <summary>
    /// The shared edit path: replay first, then 404 / stale version / code in use as stored terminal answers, then the
    /// rename with one audit event. An edit that changes nothing is still an audited, stored command ("NoChange") and
    /// does not move the version, like the active-state command's NoChange.
    /// </summary>
    private async Task<CommandExecutionResult> UpdateAsync<TEntity>(
        CommandRequest request,
        CommandMetadata metadata,
        PermissionScope scope,
        string permission,
        TEntity? entity,
        long expectedVersion,
        MasterText text,
        IQueryable<TEntity> codeOwners,
        IQueryable<TEntity> scopedCurrent,
        string eventType,
        string entityType,
        Func<TEntity, long, string> serialize,
        CancellationToken cancellationToken)
        where TEntity : TenantEntity
    {
        var existing = await FindCommandExecutionAsync(request, cancellationToken);
        if (existing is not null)
        {
            return !scope.IsCompanyWide && entity is null
                ? NotRecordedNotFound(metadata)
                : ResolveExistingExecution(existing, request, metadata);
        }

        if (entity is null)
        {
            return await CompleteAsync(request, metadata, CommandResponses.For(CommandResult.NotFound), cancellationToken);
        }

        if (entity.Version != expectedVersion)
        {
            return await CompleteAsync(
                request,
                metadata,
                CommandResponses.VersionConflict(expectedVersion, entity.Version),
                cancellationToken);
        }

        if (await IsCodeInUseAsync(codeOwners, text.Code, entity.Id, cancellationToken))
        {
            return await CompleteAsync(request, metadata, CommandResponses.For(CommandResult.CodeInUse), cancellationToken);
        }

        var versionBefore = entity.Version;
        var changed = !string.Equals(entity.Code, text.Code, StringComparison.Ordinal)
            || !string.Equals(entity.Name, text.Name, StringComparison.Ordinal);
        var beforeJson = serialize(entity, versionBefore);
        entity.Rename(text.Code, text.Name);
        var versionAfter = changed ? versionBefore + 1 : versionBefore;
        EnqueueAudit(
            metadata,
            scope,
            permission,
            eventType,
            entityType,
            entity.Id,
            changed ? "Update" : "NoChange",
            versionBefore,
            versionAfter,
            beforeJson,
            serialize(entity, versionAfter),
            text.Reason);

        return await CompleteMutationAsync(
            request,
            metadata,
            CommandResponses.For(CommandResult.Succeeded),
            async (exception, token) =>
            {
                var currentVersion = await scopedCurrent
                    .Select(x => (long?)x.Version)
                    .SingleOrDefaultAsync(token);
                if (currentVersion is null)
                {
                    return CommandResponses.For(CommandResult.NotFound);
                }

                if (exception is DbUpdateConcurrencyException || currentVersion.Value != expectedVersion)
                {
                    return CommandResponses.VersionConflict(expectedVersion, currentVersion.Value);
                }

                return await IsCodeInUseAsync(codeOwners, text.Code, entity.Id, token)
                    ? CommandResponses.For(CommandResult.CodeInUse)
                    : null;
            },
            entity.Id,
            cancellationToken);
    }

    private async Task<CommandResponseSnapshot?> RecoverCreateAsync<TEntity>(
        IQueryable<TEntity> codeOwners,
        string code,
        CancellationToken cancellationToken)
        where TEntity : TenantEntity
    {
        // A create has no version to lose; the only business race it can lose is the (CompanyId, Code) unique index.
        return await IsCodeInUseAsync(codeOwners, code, exceptId: null, cancellationToken)
            ? CommandResponses.For(CommandResult.CodeInUse)
            : null;
    }

    private static Task<bool> IsCodeInUseAsync<TEntity>(
        IQueryable<TEntity> query,
        string code,
        Guid? exceptId,
        CancellationToken cancellationToken)
        where TEntity : TenantEntity
    {
        // Under the tenant filter and deliberately NOT under the caller's row scope: the unique index is company-wide,
        // so an out-of-scope record holding the code still makes the code unusable. The answer names no record.
        return query
            .AsNoTracking()
            .AnyAsync(
                x => x.Code == code && (exceptId == null || x.Id != exceptId),
                cancellationToken);
    }

    private async Task<CommandExecutionResult> CompleteMutationAsync(
        CommandRequest request,
        CommandMetadata metadata,
        CommandResponseSnapshot response,
        Func<DbUpdateException, CancellationToken, Task<CommandResponseSnapshot?>> recover,
        Guid entityId,
        CancellationToken cancellationToken)
    {
        AddCommandExecution(request, metadata, response);
        CommandExecutionResult result;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            result = new CommandExecutionResult(response, false, metadata.CorrelationId);
        }
        catch (DbUpdateException exception)
        {
            dbContext.ChangeTracker.Clear();
            var winner = await FindCommandExecutionAsync(request, cancellationToken);
            if (winner is not null)
            {
                return ResolveExistingExecution(winner, request, metadata);
            }

            var terminal = await recover(exception, cancellationToken);
            if (terminal is null)
            {
                throw;
            }

            result = await CompleteAsync(request, metadata, terminal, cancellationToken);
        }

        logger.LogInformation(
            "Organization master data command completed. CompanyId={CompanyId} Command={Command} EntityId={EntityId} ActorId={ActorId} CorrelationId={CorrelationId} Result={Result} IsReplay={IsReplay}",
            metadata.CompanyId,
            request.CommandName,
            entityId,
            metadata.ActorId,
            metadata.CorrelationId,
            result.Result,
            result.IsReplay);
        return result;
    }

    /// <summary>A terminal answer with no business mutation: only the command record is written.</summary>
    private async Task<CommandExecutionResult> CompleteAsync(
        CommandRequest request,
        CommandMetadata metadata,
        CommandResponseSnapshot response,
        CancellationToken cancellationToken)
    {
        AddCommandExecution(request, metadata, response);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new CommandExecutionResult(response, false, metadata.CorrelationId);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            var winner = await FindCommandExecutionAsync(request, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return ResolveExistingExecution(winner, request, metadata);
        }
    }

    private void AddCommandExecution(
        CommandRequest request,
        CommandMetadata metadata,
        CommandResponseSnapshot response)
    {
        dbContext.CommandExecutions.Add(CommandExecution.Complete(new CommandExecutionData(
            Guid.NewGuid(),
            metadata.CompanyId,
            request.CommandName,
            request.IdempotencyKeyHash,
            request.RequestHash,
            metadata.ActorId,
            response.Result.ToString(),
            response.StatusCode,
            JsonSerializer.Serialize(response),
            metadata.CorrelationId,
            DateTimeOffset.UtcNow,
            CommandExecutionSchemaVersion)));
    }

    private Task<CommandExecution?> FindCommandExecutionAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        return dbContext.CommandExecutions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                execution =>
                    execution.CommandName == request.CommandName
                    && execution.IdempotencyKeyHash == request.IdempotencyKeyHash,
                cancellationToken);
    }

    private static CommandExecutionResult ResolveExistingExecution(
        CommandExecution execution,
        CommandRequest request,
        CommandMetadata metadata)
    {
        if (!string.Equals(execution.ActorId, metadata.ActorId, StringComparison.Ordinal)
            || !string.Equals(execution.RequestHash, request.RequestHash, StringComparison.Ordinal))
        {
            return new CommandExecutionResult(
                CommandResponses.For(CommandResult.IdempotencyConflict),
                false,
                metadata.CorrelationId);
        }

        var response = JsonSerializer.Deserialize<CommandResponseSnapshot>(execution.ResponseJson);
        if (response is null
            || !string.Equals(response.Result.ToString(), execution.Outcome, StringComparison.Ordinal)
            || response.StatusCode != execution.HttpStatusCode
            || response.Result == CommandResult.IdempotencyConflict
            || (response.Result == CommandResult.Created) != (response.Created is not null))
        {
            throw new InvalidOperationException(
                "Kalıcı idempotency sonucu tanımlı command sözleşmesiyle uyumsuz.");
        }

        return new CommandExecutionResult(response, true, execution.OriginalCorrelationId);
    }

    /// <summary>
    /// A replayed key whose record is outside the caller's CURRENT scope: the same not-found a first attempt would get,
    /// not recorded again, so a narrowed grant cannot read back a stored answer about a record it no longer reaches.
    /// </summary>
    private static CommandExecutionResult NotRecordedNotFound(CommandMetadata metadata) =>
        new CommandExecutionResult(CommandResponses.For(CommandResult.NotFound), false, metadata.CorrelationId);

    private void EnqueueAudit(
        CommandMetadata metadata,
        PermissionScope scope,
        string permission,
        string eventType,
        string entityType,
        Guid entityId,
        string operation,
        long? versionBefore,
        long versionAfter,
        string beforeJson,
        string afterJson,
        string reason)
    {
        auditEventCollector.Enqueue(new AuditEventDraft(
            metadata.CompanyId,
            eventType,
            entityType,
            entityId.ToString("D"),
            operation,
            versionBefore,
            versionAfter,
            AuditActorTypes.User,
            metadata.ActorId,
            permission,
            scope.RoleCodes,
            beforeJson,
            afterJson,
            reason,
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            metadata.SourceChannel,
            metadata.ClientIpAddress,
            metadata.UserAgent,
            metadata.CorrelationId,
            metadata.CausationId,
            metadata.IdempotencyKey));
    }

    private Task<PermissionScope> ReadScopeAsync(string permission, CancellationToken cancellationToken) =>
        authorizationService.GetPermissionScopeAsync(permission, companyContext.CompanyId, cancellationToken);

    private static CommandRequest CreateRequest<TFingerprint>(
        string commandName,
        CommandMetadata metadata,
        TFingerprint fingerprint)
    {
        return new CommandRequest(
            commandName,
            IdempotencyHash.ComputeKey(metadata.IdempotencyKey),
            IdempotencyHash.ComputePayload(fingerprint));
    }

    private void ValidateCommandMetadata(CommandMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!companyContext.IsResolved
            || metadata.CompanyId != companyContext.CompanyId
            || !string.Equals(metadata.ActorId, currentUser.UserId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(metadata.CorrelationId)
            || string.IsNullOrWhiteSpace(metadata.SourceChannel)
            || string.IsNullOrWhiteSpace(metadata.IdempotencyKey))
        {
            throw new InvalidOperationException("Command yürütme bağlamı geçersiz.");
        }
    }

    private static MasterText RequireText(string code, string name, string reason)
    {
        if (!OrganizationMasterDataRules.IsValidCode(code)
            || !OrganizationMasterDataRules.IsValidName(name)
            || !OrganizationMasterDataRules.IsValidReason(reason))
        {
            throw new ArgumentException("Kod, ad ve gerekçe tanımlı sınırlar içinde olmalıdır.");
        }

        return new MasterText(code.Trim(), name.Trim(), reason.Trim());
    }

    private static void RequireExpectedVersion(long expectedVersion)
    {
        if (expectedVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedVersion),
                "Beklenen sürüm sıfırdan büyük olmalıdır.");
        }
    }

    private static string SerializeSite(Site site, long version) =>
        JsonSerializer.Serialize(new
        {
            site.Id,
            site.CompanyId,
            site.Code,
            site.Name,
            Type = site.Type.ToString(),
            site.IsActive,
            Version = version,
        });

    private static string SerializeLocation(Location location, long version) =>
        JsonSerializer.Serialize(new
        {
            location.Id,
            location.CompanyId,
            location.SiteId,
            location.Code,
            location.Name,
            location.IsExternal,
            location.IsActive,
            Version = version,
        });

    private static string SerializeWorkCenter(WorkCenter workCenter, long version) =>
        JsonSerializer.Serialize(new
        {
            workCenter.Id,
            workCenter.CompanyId,
            workCenter.LocationId,
            workCenter.Code,
            workCenter.Name,
            Kind = workCenter.Kind.ToString(),
            workCenter.IsActive,
            Version = version,
        });

    private sealed record MasterText(string Code, string Name, string Reason);

    private sealed record CommandRequest(string CommandName, string IdempotencyKeyHash, string RequestHash);

    private sealed record CreateSiteFingerprint(string Code, string Name, string Type, string Reason);

    private sealed record CreateLocationFingerprint(Guid SiteId, string Code, string Name, string Reason);

    private sealed record CreateWorkCenterFingerprint(
        Guid LocationId,
        string Code,
        string Name,
        string Kind,
        string Reason);

    private sealed record UpdateFingerprint(
        Guid Id,
        string Code,
        string Name,
        long ExpectedVersion,
        string Reason);
}

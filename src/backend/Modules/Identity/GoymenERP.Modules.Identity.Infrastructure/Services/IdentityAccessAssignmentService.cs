using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Approvals;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Infrastructure.Bootstrap;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GoymenERP.Modules.Identity.Infrastructure.Services;

/// <summary>
/// ID-3 role assignment, revocation and the Critical role request (spec section ID-3; KIM-D1, KIM-D5, KIM-D15,
/// KIM-D17, KIM-D19; BS-6). The service decides nothing about who holds the permissions it checks: grants, roles and
/// Critical assignments to administrators stay a human gate. A Critical role (any of its permissions is
/// <see cref="ErpPermissions.IsCritical"/>) never becomes an assignment in one step except the active bootstrap
/// founder's own direct assignment (KIM-D5 path a), which still closes an <see cref="AccessGrantRequest"/>.
/// </summary>
internal sealed partial class IdentityAccessAssignmentService(
    IdentityDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorizationService authorization,
    IErpAuthorityRevalidationService revalidation,
    IAuditEventCollector auditEvents,
    IRefusedAttemptRecorderV1 refusedAttempts,
    ISecurityEventWriterV1 securityEvents,
    TimeProvider timeProvider,
    IOptions<IdentityBootstrapOptions> bootstrapOptions,
    ILogger<IdentityAccessAssignmentService> logger)
    : IIdentityAccessAssignmentService
{
    public async Task<IdentityAccessCommandResultV1> AssignAsync(
        AssignIdentityRoleRequestV1 request,
        IdentityAccessCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var prepared = await BeginAsync(
            new Intent(
                IdentityAccessCommandsV1.Assign,
                Operations.Assign,
                RefusedAttemptV1.NoTarget,
                RefusedAttemptV1.NoTarget,
                IdentityPermissionManifestV1.RolesManage,
                request,
                ExpectedVersionText: null,
                VersionRequired: false,
                GrantFieldErrors(request.UserId, request.RoleId, request.ValidFromUtc, request.ValidUntilUtc, request.Reason)),
            metadata,
            cancellationToken);
        if (prepared.Command is not { } command)
        {
            return prepared.Early!;
        }

        return await RunLockedAsync(command, now => AssignStepAsync(command, request, now, cancellationToken), cancellationToken);
    }

    public async Task<IdentityAccessCommandResultV1> CreateGrantRequestAsync(
        CreateAccessGrantRequestRequestV1 request,
        IdentityAccessCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var prepared = await BeginAsync(
            new Intent(
                IdentityAccessCommandsV1.CreateGrantRequest,
                Operations.Create,
                RefusedAttemptV1.NoTarget,
                RefusedAttemptV1.NoTarget,
                IdentityPermissionManifestV1.RolesManage,
                request,
                ExpectedVersionText: null,
                VersionRequired: false,
                GrantFieldErrors(request.UserId, request.RoleId, request.ValidFromUtc, request.ValidUntilUtc, request.Reason)),
            metadata,
            cancellationToken);
        if (prepared.Command is not { } command)
        {
            return prepared.Early!;
        }

        return await RunLockedAsync(
            command,
            now => CreateRequestStepAsync(command, request, now, cancellationToken),
            cancellationToken);
    }

    public async Task<IdentityAccessCommandResultV1> RevokeAsync(
        Guid assignmentId,
        RevokeIdentityRoleAssignmentRequestV1 request,
        IdentityAccessCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var prepared = await BeginAsync(
            new Intent(
                IdentityAccessCommandsV1.Revoke,
                Operations.Revoke,
                RoleAssignmentEntityType,
                TargetText(assignmentId),
                IdentityPermissionManifestV1.RolesManage,
                request,
                request.ExpectedVersion,
                VersionRequired: true,
                ReasonFieldErrors(request.Reason)),
            metadata,
            cancellationToken);
        if (prepared.Command is not { } command)
        {
            return prepared.Early!;
        }

        if (string.IsNullOrWhiteSpace(bootstrapOptions.Value.AdminRoleCode))
        {
            // The last Admin rule (KIM-D17) cannot be checked without the configured Admin role: fail closed, store nothing.
            return Done(command, Unstored(Answers.Unavailable(), command.Metadata));
        }

        return await RunLockedAsync(
            command,
            now => RevokeStepAsync(command, assignmentId, request, now, cancellationToken),
            cancellationToken);
    }

    /// <summary>Direct assignment: non-Critical roles, or the active founder's own Critical assignment (KIM-D5 path a).</summary>
    private async Task<Step> AssignStepAsync(
        Command command,
        AssignIdentityRoleRequestV1 request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var target = await LoadGrantTargetAsync(request.UserId, request.RoleId, cancellationToken);
        if (target.Refusal is { } refused)
        {
            return Step.Refuse(refused);
        }

        if (await HasActiveAssignmentAsync(command.CompanyId, request.UserId, request.RoleId, now, cancellationToken))
        {
            return Step.Refuse(Answers.DuplicateActiveAssignment());
        }

        var (validFrom, validUntil) = EffectiveWindow(request.ValidFromUtc, request.ValidUntilUtc, now);
        if (validUntil is { } until && until <= validFrom)
        {
            return Step.Refuse(Answers.ValidityElapsed());
        }

        var reason = AccessGrantRequest.NormalizeReason(request.Reason);
        if (!target.IsCritical)
        {
            var assignment = NewAssignment(command, request.UserId, request.RoleId, validFrom, validUntil,
                approvedByUserId: null);
            database.RoleAssignments.Add(assignment);
            AddPermissionChanges(command, request.UserId, assignment.Id, assignment.Version, target.Permissions,
                IdentityPermissionChangedV1.Granted, requestId: null, now);
            return Step.Changed(new Staged(
                Answers.Created(assignment.Id, assignment.Version, assignment.Status.ToString(), assignment.Id),
                [
                    Audit(command, AssignmentGrantedEvent, RoleAssignmentEntityType, assignment.Id, Operations.Assign,
                        versionBefore: null, assignment.Version, before: null, AssignmentImage(assignment, target.RoleCode, reason),
                        "Kritik olmayan rol tek adımda atandı.", approvalContextJson: null),
                ],
                PermissionChangedEvent(command, now),
                Answers.DuplicateActiveAssignment()));
        }

        if (!await IsActiveFounderAsync(command.ActorUserId, cancellationToken))
        {
            return Step.Refuse(Answers.SecondApprovalRequired());
        }

        // KIM-D5 path (a): the founder's request is opened and closed in the same save as FounderDirect.
        var founderRequest = new AccessGrantRequest(
            Guid.NewGuid(),
            command.ActorUserId,
            request.UserId,
            request.RoleId,
            command.CompanyId,
            requiresSecondApproval: true,
            now,
            validFrom,
            validUntil,
            reason);
        var created = RequestImage(founderRequest);
        var founderAssignment = NewAssignment(command, request.UserId, request.RoleId, validFrom, validUntil,
            approvedByUserId: command.ActorUserId);
        founderRequest.ApproveAsFounder(command.ActorUserId, now, founderAssignment.Id);
        database.AccessGrantRequests.Add(founderRequest);
        database.RoleAssignments.Add(founderAssignment);
        AddPermissionChanges(command, request.UserId, founderAssignment.Id, founderAssignment.Version, target.Permissions,
            IdentityPermissionChangedV1.Granted, founderRequest.Id, now);
        var context = ApprovalContext(AccessGrantApprovalMode.FounderDirect, founderRequest.Id, command.ActorUserId);
        return Step.Changed(new Staged(
            Answers.Created(founderAssignment.Id, founderAssignment.Version, founderAssignment.Status.ToString(),
                founderAssignment.Id),
            [
                Audit(command, RequestApprovedEvent, GrantRequestEntityType, founderRequest.Id, Operations.Approve,
                    versionBefore: null, founderRequest.Version, created, RequestImage(founderRequest),
                    "Kurucu doğrudan kritik rol ataması (tek kişi, FounderDirect).", context),
                Audit(command, AssignmentGrantedEvent, RoleAssignmentEntityType, founderAssignment.Id, Operations.Assign,
                    versionBefore: null, founderAssignment.Version, before: null,
                    AssignmentImage(founderAssignment, target.RoleCode, reason),
                    "Kurucu doğrudan kritik rol ataması (tek kişi, FounderDirect).", context),
            ],
            PermissionChangedEvent(command, now),
            Answers.DuplicateActiveAssignment()));
    }

    /// <summary>A pending request for a Critical role; nothing is assigned and nothing is granted here.</summary>
    private async Task<Step> CreateRequestStepAsync(
        Command command,
        CreateAccessGrantRequestRequestV1 request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var target = await LoadGrantTargetAsync(request.UserId, request.RoleId, cancellationToken);
        if (target.Refusal is { } refused)
        {
            return Step.Refuse(refused);
        }

        if (!target.IsCritical)
        {
            return Step.Refuse(Answers.RoleNotCritical());
        }

        if (await HasActiveAssignmentAsync(command.CompanyId, request.UserId, request.RoleId, now, cancellationToken))
        {
            return Step.Refuse(Answers.DuplicateActiveAssignment());
        }

        if (request.ValidUntilUtc is { } until && until.ToUniversalTime() <= now)
        {
            return Step.Refuse(Answers.ValidityElapsed());
        }

        if (await database.AccessGrantRequests.AnyAsync(
                x => x.CompanyId == command.CompanyId && x.TargetUserId == request.UserId && x.RoleId == request.RoleId
                    && x.Status == AccessGrantRequestStatus.Pending,
                cancellationToken))
        {
            return Step.Refuse(Answers.DuplicatePendingRequest());
        }

        var grantRequest = new AccessGrantRequest(
            Guid.NewGuid(),
            command.ActorUserId,
            request.UserId,
            request.RoleId,
            command.CompanyId,
            requiresSecondApproval: true,
            now,
            request.ValidFromUtc?.ToUniversalTime(),
            request.ValidUntilUtc?.ToUniversalTime(),
            request.Reason);
        database.AccessGrantRequests.Add(grantRequest);
        return Step.Changed(new Staged(
            Answers.Created(grantRequest.Id, grantRequest.Version, grantRequest.Status.ToString(), assignmentId: null),
            [
                Audit(command, RequestCreatedEvent, GrantRequestEntityType, grantRequest.Id, Operations.Create,
                    versionBefore: null, grantRequest.Version, before: null, RequestImage(grantRequest),
                    "Kritik rol için ikinci onay talebi açıldı.", approvalContextJson: null),
            ],
            SecurityEvent: null,
            Answers.DuplicatePendingRequest()));
    }

    /// <summary>One-step revocation (KIM-D15), refused for the company's last effective Admin (KIM-D17).</summary>
    private async Task<Step> RevokeStepAsync(
        Command command,
        Guid assignmentId,
        RevokeIdentityRoleAssignmentRequestV1 request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var assignment = await database.RoleAssignments
            .Include(x => x.Scopes)
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == assignmentId, cancellationToken);
        if (assignment is null)
        {
            return Step.Refuse(Answers.NotFound());
        }

        if (assignment.Version != command.ExpectedVersion)
        {
            return Step.Refuse(Answers.VersionConflict());
        }

        if (assignment.Status != RoleAssignmentStatus.Active)
        {
            return Step.Refuse(Answers.AssignmentNotActive());
        }

        var role = await database.Roles.AsNoTracking()
            .Where(x => x.Id == assignment.RoleId)
            .Select(x => new { x.Code })
            .SingleAsync(cancellationToken);
        if (string.Equals(role.Code, bootstrapOptions.Value.AdminRoleCode, StringComparison.Ordinal)
            && await IdentityAdminLock.CountOtherEffectiveHoldersAsync(
                database, command.CompanyId, assignment.RoleId, assignment.Id, now, cancellationToken) == 0)
        {
            return Step.Refuse(Answers.LastAdminRequired());
        }

        var permissions = await RolePermissionsAsync(assignment.RoleId, cancellationToken);
        var reason = AccessGrantRequest.NormalizeReason(request.Reason);
        var before = AssignmentImage(assignment, role.Code, reason: null);
        var versionBefore = assignment.Version;
        assignment.Revoke(command.ActorUserId, now);
        AddPermissionChanges(command, assignment.UserId, assignment.Id, assignment.Version, permissions,
            IdentityPermissionChangedV1.Revoked, requestId: null, now);
        return Step.Changed(new Staged(
            Answers.Ok(assignment.Id, assignment.Version, assignment.Status.ToString(), assignment.Id),
            [
                Audit(command, AssignmentRevokedEvent, RoleAssignmentEntityType, assignment.Id, Operations.Revoke,
                    versionBefore, assignment.Version, before, AssignmentImage(assignment, role.Code, reason),
                    "Rol ataması tek adımda geri alındı.", approvalContextJson: null),
            ],
            PermissionChangedEvent(command, now),
            Answers.VersionConflict()));
    }
}

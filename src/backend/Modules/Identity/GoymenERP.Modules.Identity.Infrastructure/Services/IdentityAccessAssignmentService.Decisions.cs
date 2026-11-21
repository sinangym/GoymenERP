using System.Globalization;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Approvals;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Identity.Infrastructure.Services;

/// <summary>
/// The decisions on a pending Critical role request (KIM-D1, ADR-004 section 6): the second person's approve or reject,
/// the requester's withdrawal, and the approval queue. The requester and the target never decide their own request;
/// the domain (<see cref="AccessGrantRequest"/>) and <c>CK_AccessGrantRequests_SecondApproval</c> hold the same rule.
/// </summary>
internal sealed partial class IdentityAccessAssignmentService
{
    public async Task<IdentityAccessCommandResultV1> ApproveAsync(
        Guid requestId,
        ApproveAccessGrantRequestV1 request,
        IdentityAccessCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var prepared = await BeginAsync(
            new Intent(
                IdentityAccessCommandsV1.Approve,
                Operations.Approve,
                GrantRequestEntityType,
                TargetText(requestId),
                IdentityPermissionManifestV1.AccessApprove,
                request,
                request.ExpectedVersion,
                VersionRequired: true,
                []),
            metadata,
            cancellationToken);
        if (prepared.Command is not { } command)
        {
            return prepared.Early!;
        }

        return await RunLockedAsync(command, now => ApproveStepAsync(command, requestId, now, cancellationToken), cancellationToken);
    }

    public async Task<IdentityAccessCommandResultV1> RejectAsync(
        Guid requestId,
        RejectAccessGrantRequestV1 request,
        IdentityAccessCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        IReadOnlyList<RefusedAttemptFieldErrorV1> errors = AccessGrantRequest.IsCanonicalReasonCode(request.ReasonCode)
            ? []
            : [new RefusedAttemptFieldErrorV1("reasonCode", FieldCodes.Invalid)];
        var prepared = await BeginAsync(
            new Intent(
                IdentityAccessCommandsV1.Reject,
                Operations.Reject,
                GrantRequestEntityType,
                TargetText(requestId),
                IdentityPermissionManifestV1.AccessApprove,
                request,
                request.ExpectedVersion,
                VersionRequired: true,
                errors),
            metadata,
            cancellationToken);
        if (prepared.Command is not { } command)
        {
            return prepared.Early!;
        }

        return await RunLockedAsync(
            command,
            now => RejectStepAsync(command, requestId, request.ReasonCode, now, cancellationToken),
            cancellationToken);
    }

    public async Task<IdentityAccessCommandResultV1> WithdrawAsync(
        Guid requestId,
        WithdrawAccessGrantRequestV1 request,
        IdentityAccessCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var prepared = await BeginAsync(
            new Intent(
                IdentityAccessCommandsV1.Withdraw,
                Operations.Withdraw,
                GrantRequestEntityType,
                TargetText(requestId),
                IdentityPermissionManifestV1.RolesManage,
                request,
                request.ExpectedVersion,
                VersionRequired: true,
                []),
            metadata,
            cancellationToken);
        if (prepared.Command is not { } command)
        {
            return prepared.Early!;
        }

        return await RunLockedAsync(command, now => WithdrawStepAsync(command, requestId, now, cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<IdentityAccessGrantRequestView>?> GetPendingGrantRequestsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || !companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            return null;
        }

        var companyId = companyContext.CompanyId;
        if (!await HoldsCompanyWideAsync(IdentityPermissionManifestV1.AccessApprove, companyId, cancellationToken)
            && !await HoldsCompanyWideAsync(IdentityPermissionManifestV1.AssignmentsRead, companyId, cancellationToken))
        {
            return null;
        }

        var rows = await (
                from grantRequest in database.AccessGrantRequests.AsNoTracking()
                where grantRequest.CompanyId == companyId && grantRequest.Status == AccessGrantRequestStatus.Pending
                join role in database.Roles.AsNoTracking() on grantRequest.RoleId equals role.Id
                join requester in database.Users.AsNoTracking() on grantRequest.RequestedByUserId equals requester.Id
                join target in database.Users.AsNoTracking() on grantRequest.TargetUserId equals target.Id
                orderby grantRequest.RequestedAtUtc, grantRequest.Id
                select new
                {
                    grantRequest.Id,
                    grantRequest.RequestedByUserId,
                    RequesterName = requester.DisplayName,
                    grantRequest.TargetUserId,
                    TargetName = target.DisplayName,
                    grantRequest.RoleId,
                    RoleCode = role.Code,
                    RoleName = role.Name,
                    grantRequest.RequestedAtUtc,
                    grantRequest.ValidFromUtc,
                    grantRequest.ValidUntilUtc,
                    grantRequest.Reason,
                    grantRequest.Version,
                })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new IdentityAccessGrantRequestView(
                row.Id,
                new IdentityAccessPerson(row.RequestedByUserId, row.RequesterName),
                new IdentityAccessPerson(row.TargetUserId, row.TargetName),
                row.RoleId,
                row.RoleCode,
                row.RoleName,
                AccessGrantRequestStatus.Pending.ToString(),
                row.RequestedAtUtc,
                row.ValidFromUtc,
                row.ValidUntilUtc,
                row.Reason,
                row.Version.ToString(CultureInfo.InvariantCulture)))
            .ToArray();
    }

    /// <summary>The second person's approval: the assignment is created with the approver recorded (KIM-D1).</summary>
    private async Task<Step> ApproveStepAsync(
        Command command,
        Guid requestId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var grantRequest = await database.AccessGrantRequests
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == requestId, cancellationToken);
        var state = PendingState(command, grantRequest);
        if (state is not null)
        {
            return Step.Refuse(state);
        }

        var pending = grantRequest!;
        if (pending.RequiresSecondApproval && pending.IsRequesterOrTarget(command.ActorUserId))
        {
            return Step.Refuse(Answers.SelfApprovalRefused());
        }

        var target = await LoadGrantTargetAsync(pending.TargetUserId, pending.RoleId, cancellationToken);
        if (target.Refusal is { } refused)
        {
            return Step.Refuse(refused);
        }

        if (await HasActiveAssignmentAsync(command.CompanyId, pending.TargetUserId, pending.RoleId, now, cancellationToken))
        {
            return Step.Refuse(Answers.DuplicateActiveAssignment());
        }

        var (validFrom, validUntil) = EffectiveWindow(pending.ValidFromUtc, pending.ValidUntilUtc, now);
        if (validUntil is { } until && until <= validFrom)
        {
            return Step.Refuse(Answers.ValidityElapsed());
        }

        var before = RequestImage(pending);
        var versionBefore = pending.Version;
        var assignment = NewAssignment(command, pending.TargetUserId, pending.RoleId, validFrom, validUntil,
            approvedByUserId: command.ActorUserId);
        pending.Approve(command.ActorUserId, now, assignment.Id);
        database.RoleAssignments.Add(assignment);
        AddPermissionChanges(command, pending.TargetUserId, assignment.Id, assignment.Version, target.Permissions,
            IdentityPermissionChangedV1.Granted, pending.Id, now);
        var context = ApprovalContext(AccessGrantApprovalMode.SecondApproval, pending.Id, command.ActorUserId);
        return Step.Changed(new Staged(
            Answers.Ok(pending.Id, pending.Version, pending.Status.ToString(), assignment.Id),
            [
                Audit(command, RequestApprovedEvent, GrantRequestEntityType, pending.Id, Operations.Approve,
                    versionBefore, pending.Version, before, RequestImage(pending),
                    "Kritik rol talebi ikinci kişi tarafından onaylandı.", context),
                Audit(command, AssignmentGrantedEvent, RoleAssignmentEntityType, assignment.Id, Operations.Assign,
                    versionBefore: null, assignment.Version, before: null,
                    AssignmentImage(assignment, target.RoleCode, pending.Reason),
                    "Kritik rol ikinci onayla atandı.", context),
            ],
            PermissionChangedEvent(command, now),
            Answers.VersionConflict()));
    }

    /// <summary>The second person's reject with a canonical reason code; nothing is assigned.</summary>
    private async Task<Step> RejectStepAsync(
        Command command,
        Guid requestId,
        string reasonCode,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var grantRequest = await database.AccessGrantRequests
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == requestId, cancellationToken);
        var state = PendingState(command, grantRequest);
        if (state is not null)
        {
            return Step.Refuse(state);
        }

        var pending = grantRequest!;
        if (pending.RequiresSecondApproval && pending.IsRequesterOrTarget(command.ActorUserId))
        {
            return Step.Refuse(Answers.SelfApprovalRefused());
        }

        var before = RequestImage(pending);
        var versionBefore = pending.Version;
        pending.Reject(command.ActorUserId, reasonCode, now);
        return Step.Changed(new Staged(
            Answers.Ok(pending.Id, pending.Version, pending.Status.ToString(), assignmentId: null),
            [
                Audit(command, RequestRejectedEvent, GrantRequestEntityType, pending.Id, Operations.Reject,
                    versionBefore, pending.Version, before, RequestImage(pending),
                    "Kritik rol talebi ikinci kişi tarafından reddedildi.", approvalContextJson: null),
            ],
            SecurityEvent: null,
            Answers.VersionConflict()));
    }

    /// <summary>The requester's own withdrawal of a pending request; nothing is assigned.</summary>
    private async Task<Step> WithdrawStepAsync(
        Command command,
        Guid requestId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var grantRequest = await database.AccessGrantRequests
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == requestId, cancellationToken);
        var state = PendingState(command, grantRequest);
        if (state is not null)
        {
            return Step.Refuse(state);
        }

        var pending = grantRequest!;
        if (pending.RequestedByUserId != command.ActorUserId)
        {
            return Step.Refuse(Answers.NotRequester());
        }

        var before = RequestImage(pending);
        var versionBefore = pending.Version;
        pending.Withdraw(command.ActorUserId, now);
        return Step.Changed(new Staged(
            Answers.Ok(pending.Id, pending.Version, pending.Status.ToString(), assignmentId: null),
            [
                Audit(command, RequestWithdrawnEvent, GrantRequestEntityType, pending.Id, Operations.Withdraw,
                    versionBefore, pending.Version, before, RequestImage(pending),
                    "Kritik rol talebi talep eden tarafından geri çekildi.", approvalContextJson: null),
            ],
            SecurityEvent: null,
            Answers.VersionConflict()));
    }

    /// <summary>Not found (code-less 404), stale version, or no longer pending, in this order.</summary>
    private static IdentityAccessCommandAnswerV1? PendingState(Command command, AccessGrantRequest? grantRequest)
    {
        if (grantRequest is null)
        {
            return Answers.NotFound();
        }

        if (grantRequest.Version != command.ExpectedVersion)
        {
            return Answers.VersionConflict();
        }

        return grantRequest.Status == AccessGrantRequestStatus.Pending ? null : Answers.RequestNotPending();
    }

    private async Task<bool> HoldsCompanyWideAsync(string permission, Guid companyId, CancellationToken cancellationToken)
    {
        var scope = await authorization.GetPermissionScopeAsync(permission, companyId, cancellationToken);
        return scope is { IsGranted: true, IsCompanyWide: true };
    }
}

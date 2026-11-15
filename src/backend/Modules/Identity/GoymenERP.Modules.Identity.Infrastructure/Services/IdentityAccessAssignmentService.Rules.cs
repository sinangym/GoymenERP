using System.Globalization;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Approvals;
using GoymenERP.Modules.Identity.Domain.Assignments;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Identity.Infrastructure.Services;

/// <summary>
/// The shared business reads of the ID-3 commands: who and what can be assigned, whether an effective assignment
/// already exists, the effective validity window, and the audit images. Every read of a company-owned row filters
/// on the server-resolved company explicitly (the identity context has no company query filter).
/// </summary>
internal sealed partial class IdentityAccessAssignmentService
{
    /// <summary>The target user and role of a grant, the role's permissions (sorted) and whether any of them is Critical.</summary>
    private sealed record GrantTarget(
        IdentityAccessCommandAnswerV1? Refusal,
        string RoleCode,
        IReadOnlyList<string> Permissions,
        bool IsCritical)
    {
        public static GrantTarget Refused(IdentityAccessCommandAnswerV1 refusal) => new(refusal, string.Empty, [], false);
    }

    private static string TargetText(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);

    /// <summary>Request-shape checks of a grant: identities present, a coherent window, a storable reason.</summary>
    private static IReadOnlyList<RefusedAttemptFieldErrorV1> GrantFieldErrors(
        Guid userId,
        Guid roleId,
        DateTimeOffset? validFromUtc,
        DateTimeOffset? validUntilUtc,
        string? reason)
    {
        var errors = new List<RefusedAttemptFieldErrorV1>();
        if (userId == Guid.Empty)
        {
            errors.Add(new RefusedAttemptFieldErrorV1("userId", FieldCodes.Required));
        }

        if (roleId == Guid.Empty)
        {
            errors.Add(new RefusedAttemptFieldErrorV1("roleId", FieldCodes.Required));
        }

        if (!AccessGrantRequest.IsValidWindow(validFromUtc, validUntilUtc))
        {
            errors.Add(new RefusedAttemptFieldErrorV1("validUntilUtc", FieldCodes.Invalid));
        }

        errors.AddRange(ReasonFieldErrors(reason));
        return errors;
    }

    private static IReadOnlyList<RefusedAttemptFieldErrorV1> ReasonFieldErrors(string? reason)
    {
        try
        {
            AccessGrantRequest.NormalizeReason(reason);
            return [];
        }
        catch (ArgumentException)
        {
            return [new RefusedAttemptFieldErrorV1("reason", FieldCodes.Invalid)];
        }
    }

    /// <summary>The user must exist and be active, the role must exist and be active (group-level rows, no company).</summary>
    private async Task<GrantTarget> LoadGrantTargetAsync(Guid userId, Guid roleId, CancellationToken cancellationToken)
    {
        var userActive = await database.Users.AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => (bool?)x.IsActive)
            .SingleOrDefaultAsync(cancellationToken);
        if (userActive is not true)
        {
            return GrantTarget.Refused(Answers.UserNotAssignable());
        }

        var role = await database.Roles.AsNoTracking()
            .Where(x => x.Id == roleId)
            .Select(x => new { x.Code, x.IsActive })
            .SingleOrDefaultAsync(cancellationToken);
        if (role is not { IsActive: true })
        {
            return GrantTarget.Refused(Answers.RoleNotAssignable());
        }

        var permissions = await RolePermissionsAsync(roleId, cancellationToken);
        return new GrantTarget(null, role.Code, permissions, permissions.Any(ErpPermissions.IsCritical));
    }

    private async Task<IReadOnlyList<string>> RolePermissionsAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var codes = await database.RolePermissions.AsNoTracking()
            .Where(x => x.RoleId == roleId)
            .Select(x => x.PermissionCode)
            .ToListAsync(cancellationToken);
        return codes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>An active assignment of the same user and role in this company that has not ended (a future start counts).</summary>
    private Task<bool> HasActiveAssignmentAsync(
        Guid companyId,
        Guid userId,
        Guid roleId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        database.RoleAssignments.AsNoTracking()
            .AnyAsync(
                x => x.CompanyId == companyId && x.UserId == userId && x.RoleId == roleId
                    && x.Status == RoleAssignmentStatus.Active
                    && (x.ValidUntilUtc == null || x.ValidUntilUtc > now),
                cancellationToken);

    /// <summary>A missing or past start is "now" (an assignment never starts in the past); the end is kept as asked.</summary>
    private static (DateTimeOffset ValidFrom, DateTimeOffset? ValidUntil) EffectiveWindow(
        DateTimeOffset? validFromUtc,
        DateTimeOffset? validUntilUtc,
        DateTimeOffset now)
    {
        var requested = validFromUtc?.ToUniversalTime() ?? now;
        return (requested > now ? requested : now, validUntilUtc?.ToUniversalTime());
    }

    /// <summary>
    /// A company-scoped assignment. <paramref name="approvedByUserId"/> is set only when a Critical request closed it
    /// (second approver or founder-direct); a one-step non-Critical assignment records no approver.
    /// </summary>
    private static RoleAssignment NewAssignment(
        Command command,
        Guid userId,
        Guid roleId,
        DateTimeOffset validFrom,
        DateTimeOffset? validUntil,
        Guid? approvedByUserId)
    {
        var assignment = new RoleAssignment(
            Guid.NewGuid(),
            userId,
            roleId,
            command.CompanyId,
            validFrom,
            validUntil,
            approvedByUserId);
        assignment.AddScope(AccessScopeType.Company, command.CompanyId);
        return assignment;
    }

    /// <summary>KIM-D5 path (a): the actor is the recorded bootstrap founder and the founder account is still active.</summary>
    private async Task<bool> IsActiveFounderAsync(Guid actorUserId, CancellationToken cancellationToken)
    {
        var founderUserId = await database.BootstrapStates.AsNoTracking()
            .Select(x => (Guid?)x.FounderUserId)
            .SingleOrDefaultAsync(cancellationToken);
        if (founderUserId != actorUserId)
        {
            return false;
        }

        return await database.Users.AsNoTracking().AnyAsync(x => x.Id == actorUserId && x.IsActive, cancellationToken);
    }

    private static object RequestImage(AccessGrantRequest request) => new
    {
        id = request.Id,
        companyId = request.CompanyId,
        requestedByUserId = request.RequestedByUserId,
        targetUserId = request.TargetUserId,
        roleId = request.RoleId,
        requiresSecondApproval = request.RequiresSecondApproval,
        status = request.Status.ToString(),
        requestedAtUtc = request.RequestedAtUtc,
        validFromUtc = request.ValidFromUtc,
        validUntilUtc = request.ValidUntilUtc,
        reason = request.Reason,
        decidedByUserId = request.DecidedByUserId,
        decidedAtUtc = request.DecidedAtUtc,
        approvalMode = request.ApprovalMode?.ToString(),
        decisionReasonCode = request.DecisionReasonCode,
        resultingRoleAssignmentId = request.ResultingRoleAssignmentId,
        version = Text(request.Version),
    };

    private static object AssignmentImage(RoleAssignment assignment, string roleCode, string? reason) => new
    {
        id = assignment.Id,
        companyId = assignment.CompanyId,
        userId = assignment.UserId,
        roleId = assignment.RoleId,
        roleCode,
        status = assignment.Status.ToString(),
        validFromUtc = assignment.ValidFromUtc,
        validUntilUtc = assignment.ValidUntilUtc,
        approvedByUserId = assignment.ApprovedByUserId,
        revokedByUserId = assignment.RevokedByUserId,
        revokedAtUtc = assignment.RevokedAtUtc,
        reason,
        version = Text(assignment.Version),
    };
}

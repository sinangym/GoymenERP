using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Approvals;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Identity.Infrastructure.Services;

internal sealed partial class IdentityAccessAssignmentService : IIdentityAccessGrantDecisionV1
{
    public async Task<IdentityAccessGrantDecisionDetailV1?> ReadDecisionAsync(Guid requestId,
        IdentityAccessCommandMetadataV1 context, Func<CancellationToken, Task> consumerFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consumerFence);
        await DecisionReadFenceAsync(context, consumerFence, cancellationToken);
        var row = await database.AccessGrantRequests.AsNoTracking().SingleOrDefaultAsync(
            x => x.CompanyId == context.CompanyId && x.Id == requestId, cancellationToken);
        await DecisionReadFenceAsync(context, consumerFence, cancellationToken);
        if (row is null) return null;
        if (row.RequiresSecondApproval && row.IsRequesterOrTarget(Guid.Parse(context.ActorId))) throw new UnauthorizedAccessException();
        var role = await database.Roles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == row.RoleId, cancellationToken);
        await DecisionReadFenceAsync(context, consumerFence, cancellationToken);
        var people = await database.Users.AsNoTracking().Where(x => x.Id == row.TargetUserId || x.Id == row.RequestedByUserId)
            .Select(x => new { x.Id, x.DisplayName, x.IsActive }).ToListAsync(cancellationToken);
        await DecisionReadFenceAsync(context, consumerFence, cancellationToken);
        var permissions = await RolePermissionsAsync(row.RoleId, cancellationToken);
        await DecisionReadFenceAsync(context, consumerFence, cancellationToken);
        var requester = people.SingleOrDefault(x => x.Id == row.RequestedByUserId);
        var target = people.SingleOrDefault(x => x.Id == row.TargetUserId);
        if (role is null || requester is null || target is null) return null;
        var view = new IdentityAccessGrantRequestView(row.Id, new(row.RequestedByUserId, requester.DisplayName),
            new(row.TargetUserId, target.DisplayName), role.Id, role.Code, role.Name, row.Status.ToString(),
            row.RequestedAtUtc, row.ValidFromUtc, row.ValidUntilUtc, row.Reason, Text(row.Version));
        var impact = AccessDecisionImpact(row, role.Code, permissions);
        var detail = new IdentityAccessGrantDecisionDetailV1(view, row.RequiresSecondApproval, target.IsActive, role.IsActive,
            permissions, permissions.Any(ErpPermissions.IsCritical), AccessDecisionSource(row), impact, string.Empty);
        detail = detail with { PreviewDigest = IdempotencyHash.ComputePayload(detail) };
        await DecisionReadFenceAsync(context, consumerFence, cancellationToken);
        return detail;
    }

    private async Task DecisionReadFenceAsync(IdentityAccessCommandMetadataV1 context,
        Func<CancellationToken, Task> consumer, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!SameAccessApprovalContext(context) || !Guid.TryParse(context.ActorId, out var actor) || actor == Guid.Empty)
            throw new UnauthorizedAccessException();
        await consumer(token);
        token.ThrowIfCancellationRequested();
        if (!SameAccessApprovalContext(context)) throw new UnauthorizedAccessException();
        await RequireStillAsync(IdentityPermissionManifestV1.AccessApprove, context.CompanyId, token);
        token.ThrowIfCancellationRequested();
        if (!SameAccessApprovalContext(context)) throw new UnauthorizedAccessException();
    }

    private static string AccessDecisionSource(AccessGrantRequest row) => IdempotencyHash.ComputePayload(new
    { row.Id, row.CompanyId, row.RequestedByUserId, row.TargetUserId, row.RoleId, row.RequiresSecondApproval,
      row.RequestedAtUtc, row.ValidFromUtc, row.ValidUntilUtc, row.Reason });

    private static string AccessDecisionImpact(AccessGrantRequest row, string roleCode, IReadOnlyList<string> permissions) =>
        IdempotencyHash.ComputePayload(new { Source = AccessDecisionSource(row), RoleCode = roleCode, Permissions = permissions });
}

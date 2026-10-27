using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Audit.Infrastructure.Services;

/// <summary>
/// The company audit read. Its rows carry the most sensitive content the platform stores: business
/// before/after values, approval and AI context, the client IP address and user agent. So the
/// company-wide audit authority is measured fresh before the read and again after it, immediately
/// before anything is released (BLG-03): a grant revoked while the query ran releases no row and
/// no cursor.
/// </summary>
internal sealed class AuditQueryService(
    AuditDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority)
    : IAuditQueryService
{
    public async Task<AuditEventQueryResult> GetEventsAsync(
        int pageSize,
        string? cursor,
        string? correlationId,
        string? entityType,
        string? entityId,
        CancellationToken cancellationToken = default)
    {
        if (!companyContext.IsResolved
            || !currentUser.IsAuthenticated
            || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            return AuditEventQueryResult.Denied;
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId!;
        if (!await HasFreshCompanyWideAccessAsync(companyId, actorId, cancellationToken))
        {
            return AuditEventQueryResult.Denied;
        }

        var normalizedPageSize = NormalizePageSize(pageSize);
        var query = dbContext.AuditEvents.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            if (!AuditCursor.TryDecode(cursor, out var decodedCursor))
            {
                return AuditEventQueryResult.InvalidCursor;
            }

            query = query.Where(x =>
                x.OccurredAtUtc < decodedCursor.OccurredAtUtc
                || (x.OccurredAtUtc == decodedCursor.OccurredAtUtc
                    && x.Id.CompareTo(decodedCursor.Id) < 0));
        }

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            var normalizedCorrelationId = correlationId.Trim();
            query = query.Where(x => x.CorrelationId == normalizedCorrelationId);
        }

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            var normalizedEntityType = entityType.Trim();
            query = query.Where(x => x.EntityType == normalizedEntityType);
        }

        if (!string.IsNullOrWhiteSpace(entityId))
        {
            var normalizedEntityId = entityId.Trim();
            query = query.Where(x => x.EntityId == normalizedEntityId);
        }

        var events = await query
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(normalizedPageSize + 1)
            .ToListAsync(cancellationToken);

        // The fence: nothing read above leaves this method unless the authority still stands now.
        if (!await HasFreshCompanyWideAccessAsync(companyId, actorId, cancellationToken))
        {
            return AuditEventQueryResult.Denied;
        }

        var hasMore = events.Count > normalizedPageSize;
        var pageEvents = events.Take(normalizedPageSize).ToList();
        var items = pageEvents.Select(x => new AuditEventItem(
            x.Id,
            x.RequireCompanyId(),
            x.OccurredAtUtc,
            x.EventType,
            x.EntityType,
            x.EntityId,
            x.Operation,
            x.EntityVersionBefore,
            x.EntityVersionAfter,
            x.ActorType,
            x.ActorId,
            x.PermissionCode,
            DeserializeRoleCodes(x.RoleCodesJson),
            x.BeforeJson,
            x.AfterJson,
            x.Reason,
            x.ApprovalContextJson,
            x.AiInvolvement,
            x.AiContextJson,
            x.SourceChannel,
            x.ClientIpAddress,
            x.UserAgent,
            x.CorrelationId,
            x.CausationId,
            x.Outcome,
            x.SchemaVersion,
            x.IntegrityHash)).ToList();

        var nextCursor = hasMore
            ? AuditCursor.Encode(pageEvents[^1].OccurredAtUtc, pageEvents[^1].Id)
            : null;
        return AuditEventQueryResult.Ok(new AuditEventPage(items, normalizedPageSize, nextCursor));
    }

    // The actor and company checks around the fresh read come from Codex's parallel candidate
    // (codex/sy07-audit-read-fence-20261024, 8e390339): the same person must still be acting for
    // the same company on both sides of the authority round-trip.
    private async Task<bool> HasFreshCompanyWideAccessAsync(
        Guid expectedCompanyId,
        string expectedActorId,
        CancellationToken cancellationToken)
    {
        if (!RemainsCurrent(expectedCompanyId, expectedActorId))
        {
            return false;
        }

        var scope = await authority.RevalidatePermissionScopeAsync(
            ErpPermissions.IdentityAuditRead,
            expectedCompanyId,
            cancellationToken);
        return RemainsCurrent(expectedCompanyId, expectedActorId)
            && scope.IsGranted
            && scope.IsCompanyWide;
    }

    private bool RemainsCurrent(Guid expectedCompanyId, string expectedActorId) =>
        currentUser.IsAuthenticated
        && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && companyContext.IsResolved
        && companyContext.CompanyId == expectedCompanyId
        && currentUser.CanAccessCompany(expectedCompanyId)
        && string.Equals(currentUser.UserId, expectedActorId, StringComparison.Ordinal);

    private static int NormalizePageSize(int pageSize)
    {
        return Math.Clamp(pageSize, 1, 100);
    }

    private static IReadOnlyList<string> DeserializeRoleCodes(string json)
    {
        return JsonSerializer.Deserialize<List<string>>(json) ?? [];
    }
}

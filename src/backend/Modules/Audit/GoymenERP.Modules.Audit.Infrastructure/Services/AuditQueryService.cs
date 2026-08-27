using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Audit.Infrastructure.Services;

internal sealed class AuditQueryService(
    AuditDbContext dbContext,
    ICompanyContext companyContext,
    IErpAuthorizationService authorizationService)
    : IAuditQueryService
{
    public async Task<AuditEventPage?> GetEventsAsync(
        int pageSize,
        string? cursor,
        string? correlationId,
        string? entityType,
        string? entityId,
        CancellationToken cancellationToken = default)
    {
        var scope = await authorizationService.GetPermissionScopeAsync(
            ErpPermissions.IdentityAuditRead,
            companyContext.CompanyId,
            cancellationToken);
        if (!scope.IsGranted)
        {
            return new AuditEventPage([], NormalizePageSize(pageSize), null);
        }

        var normalizedPageSize = NormalizePageSize(pageSize);
        var query = dbContext.AuditEvents.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            if (!AuditCursor.TryDecode(cursor, out var decodedCursor))
            {
                return null;
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
        var hasMore = events.Count > normalizedPageSize;
        var pageEvents = events.Take(normalizedPageSize).ToList();
        var items = pageEvents.Select(x => new AuditEventItem(
            x.Id,
            x.CompanyId,
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
        return new AuditEventPage(items, normalizedPageSize, nextCursor);
    }

    private static int NormalizePageSize(int pageSize)
    {
        return Math.Clamp(pageSize, 1, 100);
    }

    private static IReadOnlyList<string> DeserializeRoleCodes(string json)
    {
        return JsonSerializer.Deserialize<List<string>>(json) ?? [];
    }
}

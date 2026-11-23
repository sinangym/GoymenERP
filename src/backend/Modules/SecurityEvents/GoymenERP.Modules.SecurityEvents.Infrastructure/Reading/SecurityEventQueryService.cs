using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.SecurityEvents.Infrastructure.Reading;

/// <summary>
/// The company security event read (ADR-SEC-001 section 5). Order of work:
/// <list type="number">
/// <item>the company-wide <see cref="ErpPermissions.PlatformSecurityEventsRead"/> authority is measured fresh;</item>
/// <item>the read is recorded as <c>security.events.read</c> in the same channel, BEFORE any row is read; the
/// write is fail-closed, so an unrecorded read returns Unavailable;</item>
/// <item>the rows are read through the module DbContext, whose query filter keeps only company-scoped rows of the
/// resolved company (platform rows are never visible here);</item>
/// <item>the authority is measured again immediately before anything is released (the BLG-03 fence of the
/// audit read): a grant revoked while the query ran releases no row and no cursor.</item>
/// </list>
/// </summary>
internal sealed class SecurityEventQueryService(
    SecurityEventsDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    ISecurityEventWriterV1 securityEventWriter,
    ICorrelationContext correlationContext,
    TimeProvider timeProvider)
    : ISecurityEventQueryServiceV1
{
    internal const int MinimumPageSize = 1;
    internal const int MaximumPageSize = 100;

    public async Task<SecurityEventQueryResultV1> GetEventsAsync(
        int pageSize,
        string? cursor,
        string? eventType,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        if (!companyContext.IsResolved
            || !currentUser.IsAuthenticated
            || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            return SecurityEventQueryResultV1.Denied;
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId!;
        if (!await HasFreshCompanyWideAccessAsync(companyId, actorId, cancellationToken))
        {
            return SecurityEventQueryResultV1.Denied;
        }

        SecurityEventCursor? decodedCursor = null;
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            if (!SecurityEventCursor.TryDecode(cursor, out var parsedCursor))
            {
                return SecurityEventQueryResultV1.InvalidCursor;
            }

            decodedCursor = parsedCursor;
        }

        string? normalizedEventType = null;
        if (!string.IsNullOrWhiteSpace(eventType))
        {
            normalizedEventType = eventType.Trim();
            if (!SecurityEventTypesV1.IsDefined(normalizedEventType))
            {
                return SecurityEventQueryResultV1.InvalidFilter;
            }
        }

        string? normalizedCorrelationId = null;
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            normalizedCorrelationId = correlationId.Trim();
            if (normalizedCorrelationId.Length > SecurityEventV1.CorrelationIdMaxLength)
            {
                return SecurityEventQueryResultV1.InvalidFilter;
            }
        }

        if (!await RecordReadAsync(companyId, actorId, cancellationToken))
        {
            return SecurityEventQueryResultV1.Unavailable;
        }

        var normalizedPageSize = Math.Clamp(pageSize, MinimumPageSize, MaximumPageSize);
        var query = dbContext.SecurityEvents.AsNoTracking();
        if (decodedCursor is not null)
        {
            var cursorOccurredAtUtc = decodedCursor.OccurredAtUtc;
            var cursorId = decodedCursor.Id;
            query = query.Where(x =>
                x.OccurredAtUtc < cursorOccurredAtUtc
                || (x.OccurredAtUtc == cursorOccurredAtUtc && x.Id.CompareTo(cursorId) < 0));
        }

        if (normalizedEventType is not null)
        {
            query = query.Where(x => x.EventType == normalizedEventType);
        }

        if (normalizedCorrelationId is not null)
        {
            query = query.Where(x => x.CorrelationId == normalizedCorrelationId);
        }

        var events = await query
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(normalizedPageSize + 1)
            .ToListAsync(cancellationToken);

        // The fence: nothing read above leaves this method unless the authority still stands now.
        if (!await HasFreshCompanyWideAccessAsync(companyId, actorId, cancellationToken))
        {
            return SecurityEventQueryResultV1.Denied;
        }

        var hasMore = events.Count > normalizedPageSize;
        var pageEvents = events.Take(normalizedPageSize).ToList();
        var items = pageEvents.Select(x => new SecurityEventItemV1(
            x.Id,
            x.SchemaVersion,
            x.OccurredAtUtc,
            x.CompanyId ?? throw new InvalidOperationException("A company-scoped security event has no company."),
            x.RequestedCompanyId,
            x.ActorType,
            x.ActorId,
            x.EventType,
            x.Phase,
            x.Outcome,
            x.ReasonCode,
            x.Severity,
            x.PermissionCode,
            x.PolicyId,
            x.PolicyVersion,
            x.CorrelationId,
            x.CausationId,
            x.SourceChannel,
            x.AiToolId,
            x.AiToolVersion,
            x.AiRiskClass,
            x.AiDataClass,
            x.RetainUntilUtc,
            x.OccurrenceCount,
            x.AggregationWindowStartUtc,
            x.AggregationWindowEndUtc,
            x.IntegrityHash)).ToList();

        var nextCursor = hasMore
            ? SecurityEventCursor.Encode(pageEvents[^1].OccurredAtUtc, pageEvents[^1].Id)
            : null;
        return SecurityEventQueryResultV1.Ok(new SecurityEventPageV1(items, normalizedPageSize, nextCursor));
    }

    private async Task<bool> RecordReadAsync(Guid companyId, string actorId, CancellationToken cancellationToken)
    {
        if (!correlationContext.IsResolved)
        {
            return false;
        }

        var result = await securityEventWriter.WriteAsync(
            new SecurityEventV1
            {
                EventId = Guid.NewGuid(),
                OccurredAtUtc = timeProvider.GetUtcNow().ToUniversalTime(),
                ScopeKind = SecurityEventScopeKindsV1.Company,
                CompanyId = companyId,
                ActorType = SecurityEventActorTypesV1.User,
                ActorId = actorId,
                EventType = SecurityEventTypesV1.SecurityEventsRead,
                Phase = SecurityEventPhasesV1.Single,
                Outcome = SecurityEventOutcomesV1.Allowed,
                Severity = SecurityEventSeveritiesV1.Info,
                PermissionCode = ErpPermissions.PlatformSecurityEventsRead,
                CorrelationId = correlationContext.CorrelationId,
                CausationId = correlationContext.CausationId,
                SourceChannel = correlationContext.SourceChannel,
            },
            cancellationToken);

        // security.events.read is fail-closed (ADR-SEC-001 section 4): CallerMayContinue is true only when the
        // row is durably stored.
        return result.Recorded && result.CallerMayContinue;
    }

    // Same actor and company on both sides of every authority round-trip (the audit read fence).
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
            ErpPermissions.PlatformSecurityEventsRead,
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
}

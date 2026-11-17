using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Audit.Infrastructure.Services;

// Owner read, not a new HTTP endpoint. Origin equality never grants access to global history.
public sealed class GroupSharedProductAuditReader(IServiceScopeFactory scopes,
    IGroupSharedMasterAuthorizationService authority)
{
    public async Task<IReadOnlyList<GroupSharedAuditEventData>> ReadAsync(Guid productId, int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var before = await AuthorizeAsync(cancellationToken);
        if (productId == Guid.Empty || limit is < 1 or > 100) throw new ArgumentException("Invalid Product history query.");
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        database.BindGroupSharedRead(before, productId);
        // Version order is the semantic tie-breaker. Event ids are random and cannot decide
        // which adjacent Product version is newer when two commits share a clock tick. Keeping
        // the version before the final stable id tie-breaker also preserves the correct cutoff set.
        var rows = await database.AuditEvents.AsNoTracking().OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.EntityVersionAfter).ThenByDescending(x => x.Id)
            .Take(limit).ToListAsync(cancellationToken);
        if (rows.Count > 0)
        {
            // A COGS revision leaves the Product row version unchanged. Random event ids cannot order its
            // same-clock revisions, or decide the boundary set before the requested page cut.
            // Use the existing owner's maximum read bound for the boundary tick; never return an arbitrary
            // partial tie as complete history. Other timestamps are already strictly newer than this set.
            var boundary = rows[^1].OccurredAtUtc;
            var tied = await database.AuditEvents.AsNoTracking().Where(x => x.OccurredAtUtc == boundary)
                .Take(101).ToListAsync(cancellationToken);
            if (tied.Count > 100) throw new InvalidOperationException("Product audit timestamp tie exceeds the bounded owner read.");
            rows = rows.Where(x => x.OccurredAtUtc > boundary).Concat(tied).ToList();
        }
        var after = await AuthorizeAsync(cancellationToken);
        if (after.LocalActorId != before.LocalActorId || after.OriginCompanyId != before.OriginCompanyId
            || after.AuthorityRevision != before.AuthorityRevision)
            throw new UnauthorizedAccessException("Product history authority changed during lookup.");
        return rows.Select(row => row.ToGroupSharedIntegrityData()).OrderByDescending(x => x.Envelope.OccurredAtUtc)
            .ThenByDescending(x => x.Envelope.EntityVersionAfter).ThenByDescending(x => ClassificationRevision(x.Envelope))
            .ThenByDescending(x => x.Envelope.EntityVersionBefore).ThenByDescending(x => x.Envelope.Id).Take(limit).ToArray();
    }

    private static long ClassificationRevision(GroupSharedAuditEventEnvelope envelope)
    {
        if (envelope.Operation != "SetCogsClassification") return 0;
        using var json = JsonDocument.Parse(envelope.AfterJson);
        if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("classificationVersion",out var field)
            || field.ValueKind != JsonValueKind.String || !long.TryParse(field.GetString(),NumberStyles.None,CultureInfo.InvariantCulture,out var version)
            || version < 1 || version.ToString(CultureInfo.InvariantCulture) != field.GetString())
            throw new InvalidOperationException("A Product classification audit revision is invalid.");
        return version;
    }

    private async Task<GroupSharedMasterReadContext> AuthorizeAsync(CancellationToken cancellationToken) =>
        GroupSharedMasterReadContext.FromServerDecision(await authority.GetSnapshotAsync(
            GroupSharedMasterKind.Product, GroupSharedMasterOperation.Read, cancellationToken));
}

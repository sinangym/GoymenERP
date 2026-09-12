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
        var rows = await database.AuditEvents.AsNoTracking().OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.Id).Take(limit).ToListAsync(cancellationToken);
        var after = await AuthorizeAsync(cancellationToken);
        if (after.LocalActorId != before.LocalActorId || after.OriginCompanyId != before.OriginCompanyId
            || after.AuthorityRevision != before.AuthorityRevision)
            throw new UnauthorizedAccessException("Product history authority changed during lookup.");
        return rows.Select(row => row.ToGroupSharedIntegrityData()).ToArray();
    }

    private async Task<GroupSharedMasterReadContext> AuthorizeAsync(CancellationToken cancellationToken) =>
        GroupSharedMasterReadContext.FromServerDecision(await authority.GetSnapshotAsync(
            GroupSharedMasterKind.Product, GroupSharedMasterOperation.Read, cancellationToken));
}

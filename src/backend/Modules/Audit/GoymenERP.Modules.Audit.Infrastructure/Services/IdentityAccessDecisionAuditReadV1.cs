using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Audit.Infrastructure.Services;

internal sealed class IdentityAccessDecisionAuditReadV1(ICompanyContext companyContext, ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority, IServiceScopeFactory scopes) : IIdentityAccessDecisionAuditReadV1
{
    public async Task<IdentityAccessDecisionAuditArtifactV1?> ReadAsync(IdentityAccessDecisionAuditSelectorV1 selector,
        Func<CancellationToken, Task> consumerFence, DbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(consumerFence);
        await Fence();
        if (selector.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(selector.OriginalActorId)
            || string.IsNullOrWhiteSpace(selector.OriginalCorrelationId) || !Hash(selector.IdempotencyKeyHash)) return null;
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(selector.CompanyId);
        var database = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        if (transaction is not null)
        {
            if (transaction.Connection is not { State: ConnectionState.Open } connection) return null;
            database.Database.SetDbConnection(connection, contextOwnsConnection: false);
            await database.Database.UseTransactionAsync(transaction, cancellationToken);
            await Fence();
        }
        var id = selector.RequestId.ToString("D");
        var rows = await database.AuditEvents.AsNoTracking().Where(x => x.CompanyId == selector.CompanyId
            && x.EventType == "identity.access-grant-request.approved.v1" && x.EntityType == "Identity.AccessGrantRequest"
            && x.EntityId == id && x.ActorId == selector.OriginalActorId
            && x.CorrelationId == selector.OriginalCorrelationId && x.IdempotencyKeyHash == selector.IdempotencyKeyHash)
            .Take(2).ToListAsync(cancellationToken);
        await Fence();
        if (rows.Count != 1) return null;
        var row = rows[0];
        if (row.PermissionCode != ErpPermissions.IdentityAccessApprove || row.ActorType != "User" || row.Operation != "Approve" || row.Outcome != "Succeeded"
            || !string.Equals(row.IntegrityHash, AuditEventIntegrity.Compute(row.ToIntegrityEnvelope()), StringComparison.Ordinal)) return null;
        await Fence();
        return new(row.Id, row.AfterJson, row.EntityVersionBefore, row.EntityVersionAfter, row.IntegrityHash);

        bool Same() => companyContext.IsResolved && companyContext.CompanyId == selector.CompanyId && selector.CompanyId != Guid.Empty
            && currentUser.IsAuthenticated && currentUser.UserId == selector.CurrentActorId && currentUser.CanAccessCompany(selector.CompanyId);
        async Task Fence()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Same()) throw new UnauthorizedAccessException();
            await consumerFence(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!Same()) throw new UnauthorizedAccessException();
            var permission = await authority.RevalidatePermissionScopeAsync(ErpPermissions.IdentityAccessApprove, selector.CompanyId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (permission is not { IsGranted: true, IsCompanyWide: true } || !Same()) throw new UnauthorizedAccessException();
        }
    }

    private static bool Hash(string value) => value is { Length: 64 } && value.All(x => x is >= '0' and <= '9' or >= 'A' and <= 'F');
}

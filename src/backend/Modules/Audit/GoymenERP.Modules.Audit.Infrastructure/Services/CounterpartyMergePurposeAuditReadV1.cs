using System.Data;
using System.Data.Common;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Audit.Infrastructure.Services;

internal sealed class CounterpartyMergePurposeAuditReadV1(ICompanyContext companyContext, ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority, IServiceScopeFactory scopes) : ICounterpartyMergePurposeAuditReadV1
{
    public async Task<CounterpartyMergePurposeAuditArtifactV1?> ReadAsync(CounterpartyMergePurposeAuditSelectorV1 selector,
        Func<CancellationToken, Task> consumerFence, DbTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selector); ArgumentNullException.ThrowIfNull(consumerFence);
        Guid[] companies = [selector.CompanyId];
        await Fence();
        if (selector.MergedPartyId == Guid.Empty || string.IsNullOrWhiteSpace(selector.OriginalActorId)
            || string.IsNullOrWhiteSpace(selector.OriginalCorrelationId) || selector.IdempotencyKeyHash is not { Length: 64 }
            || selector.IdempotencyKeyHash.Any(x => x is not (>= 'A' and <= 'F' or >= '0' and <= '9'))
            || transaction.Connection is not { State: ConnectionState.Open } connection) return null;
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(selector.CompanyId);
        var database = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        database.Database.SetDbConnection(connection, contextOwnsConnection: false);
        await database.Database.UseTransactionAsync(transaction, cancellationToken);
        await Fence();
        var id = selector.MergedPartyId.ToString("D");
        var rows = await database.AuditEvents.AsNoTracking().Where(x => x.CompanyId == selector.CompanyId
            && x.EventType == "counterparty.party.merged.v1" && x.EntityType == "Counterparty.Party" && x.EntityId == id
            && x.Operation == "ApproveMerge" && x.ActorId == selector.OriginalActorId
            && x.CorrelationId == selector.OriginalCorrelationId && x.IdempotencyKeyHash == selector.IdempotencyKeyHash)
            .Take(2).ToListAsync(cancellationToken);
        await Fence();
        if (rows.Count != 1) return null;
        var row = rows[0];
        if (row.ActorType != AuditActorTypes.User || row.PermissionCode != ErpPermissions.CounterpartyPartiesMergeApprove
            || row.Outcome != AuditOutcomes.Succeeded || row.IntegrityHash != AuditEventIntegrity.Compute(row.ToIntegrityEnvelope())) return null;
        try
        {
            using var document = JsonDocument.Parse(row.AfterJson);
            if (!document.RootElement.TryGetProperty("SeenImpact", out var impact)
                || !impact.TryGetProperty("CompanyId", out var active) || !active.TryGetGuid(out var activeId) || activeId != selector.CompanyId
                || !impact.TryGetProperty("HoldingAccounts", out var accounts) || accounts.ValueKind != JsonValueKind.Array) return null;
            var actualCompanies = new HashSet<Guid>();
            foreach (var account in accounts.EnumerateArray())
            {
                if (account.ValueKind != JsonValueKind.Object || !account.TryGetProperty("CompanyId", out var company)
                    || !company.TryGetGuid(out var companyId) || companyId == Guid.Empty) return null;
                actualCompanies.Add(companyId);
            }
            if (!actualCompanies.Contains(selector.CompanyId)) return null;
            companies = actualCompanies.Order().ToArray();
        }
        catch (JsonException) { return null; }
        await Fence();
        return new(row.Id, row.AfterJson, row.EntityVersionBefore, row.EntityVersionAfter, row.IntegrityHash);

        bool Same() => selector.CompanyId != Guid.Empty && companyContext.IsResolved && companyContext.CompanyId == selector.CompanyId
            && currentUser.IsAuthenticated && currentUser.UserId == selector.CurrentActorId && currentUser.CanAccessCompany(selector.CompanyId);
        async Task Fence()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Same()) throw new UnauthorizedAccessException();
            await consumerFence(cancellationToken); cancellationToken.ThrowIfCancellationRequested();
            if (!Same()) throw new UnauthorizedAccessException();
            foreach (var company in companies)
            {
                if (!currentUser.CanAccessCompany(company)) throw new UnauthorizedAccessException();
                foreach (var permission in new[] { ErpPermissions.CounterpartyPartiesMergeApprove, ErpPermissions.CounterpartyPartiesRead })
                {
                    var grant = await authority.RevalidatePermissionScopeAsync(permission, company, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!Same() || !currentUser.CanAccessCompany(company) || grant is not { IsGranted: true, IsCompanyWide: true })
                        throw new UnauthorizedAccessException();
                }
            }
        }
    }
}

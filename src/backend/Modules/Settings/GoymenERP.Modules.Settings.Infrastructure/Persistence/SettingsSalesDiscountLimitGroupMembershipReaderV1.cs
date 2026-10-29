using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// SAT-D63/D66: the limit groups the current actor is bound to in the server-resolved company, read only from Settings'
/// own tables under the company filter (the binding's current version). SAT-D117 (Sinan, 2026-10-29): a passive group
/// keeps counting for the people already bound to it — passivity only closes it to new bindings. An actor id
/// that is not an ERP user id has no binding (empty: every discount goes to approval). Access first; a store that cannot be
/// read is <c>settings_provider_unavailable</c>, never an empty answer.
/// </summary>
internal sealed class SettingsSalesDiscountLimitGroupMembershipReaderV1(
    SettingsDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser) : ISettingsSalesDiscountLimitGroupMembershipReaderV1
{
    public async Task<IReadOnlyList<Guid>> GetCurrentActorLimitGroupIdsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId)
            || !companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw new SettingsPrerequisiteException("settings_access_denied");
        }

        if (!Guid.TryParse(currentUser.UserId, out var userId) || userId == Guid.Empty)
        {
            return [];
        }

        try
        {
            var version = await database.SalesDiscountLimitMemberships.AsNoTracking()
                .Where(membership => membership.UserId == userId)
                .Select(membership => (long?)membership.Version)
                .SingleOrDefaultAsync(cancellationToken);
            if (version is not { } current)
            {
                return [];
            }

            return await database.SalesDiscountLimitMembershipGroups.AsNoTracking()
                .Where(bound => bound.UserId == userId && bound.MembershipVersion == current)
                .Join(database.SalesDiscountLimitGroups.AsNoTracking(),
                    bound => new { bound.CompanyId, Id = bound.LimitGroupId },
                    group => new { group.CompanyId, group.Id },
                    (_, group) => group.Id)
                .OrderBy(id => id)
                .ToListAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException("settings_provider_unavailable");
        }
    }
}

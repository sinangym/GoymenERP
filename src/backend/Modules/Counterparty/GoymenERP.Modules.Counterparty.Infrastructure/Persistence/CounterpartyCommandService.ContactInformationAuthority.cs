using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

internal sealed record CounterpartyContactInformationAuthorityFence(
    Guid CompanyId,
    string ActorId)
{
    public static CounterpartyContactInformationAuthorityFence Capture(
        ICurrentUser currentUser,
        ICompanyContext companyContext)
    {
        var actorId = currentUser.UserId;
        var companyId = companyContext.CompanyId;
        if (!currentUser.IsAuthenticated
            || string.IsNullOrWhiteSpace(actorId)
            || !companyContext.IsResolved
            || companyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyId))
        {
            throw new UnauthorizedAccessException("Cari iletişim bilgisi bağlamı geçersiz.");
        }

        return new CounterpartyContactInformationAuthorityFence(
            companyId,
            actorId);
    }

    public async Task RequireStillAsync(
        ICurrentUser currentUser,
        ICompanyContext companyContext,
        IErpAuthorityRevalidationService authorityRevalidationService,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        cancellationToken.ThrowIfCancellationRequested();
        RequireContextStill(currentUser, companyContext);

        foreach (var permission in permissions)
        {
            var scope = await authorityRevalidationService.RevalidatePermissionScopeAsync(
                permission,
                CompanyId,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            RequireContextStill(currentUser, companyContext);
            if (!scope.IsGranted || !scope.IsCompanyWide)
            {
                throw new UnauthorizedAccessException(
                    $"'{permission}' izni artık şirket genelinde geçerli değil.");
            }
        }
    }

    private void RequireContextStill(
        ICurrentUser currentUser,
        ICompanyContext companyContext)
    {
        if (!currentUser.IsAuthenticated
            || !string.Equals(currentUser.UserId, ActorId, StringComparison.Ordinal)
            || !companyContext.IsResolved
            || companyContext.CompanyId != CompanyId
            || !currentUser.CanAccessCompany(CompanyId))
        {
            throw new UnauthorizedAccessException("Cari iletişim bilgisi bağlamı değişti.");
        }
    }
}

using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Modules.Accounting.Application;

public sealed class FinanceJournalSourceServiceV1(IFinanceJournalSourceStoreV1 store,
    IErpAuthorityRevalidationService authority, ICompanyContext company, ICurrentUser user)
{
    public async Task<AccountingApiResult<FinanceJournalSourceListV1>> ListAsync(CancellationToken cancellationToken)
    {
        var companyId = company.IsResolved ? company.CompanyId : Guid.Empty;
        var actor = user.UserId;
        bool Current() => companyId != Guid.Empty && company.IsResolved && company.CompanyId == companyId
            && user.IsAuthenticated && actor is not null && user.UserId == actor && user.CanAccessCompany(companyId);
        async Task<bool> Allowed()
        {
            if (!Current()) return false;
            var permission = await authority.RevalidatePermissionScopeAsync(ErpPermissions.AccountingJournalsRead, companyId, cancellationToken);
            return Current() && permission.IsGranted && permission.IsCompanyWide;
        }
        if (!await Allowed()) return new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied);
        var rows = await store.ListAsync(cancellationToken);
        if (!await Allowed() || rows.Any(x => x.CompanyId != companyId))
            return new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied);
        return new(AccountingApiStatus.Ok, new(companyId, rows), false, null);
    }
}

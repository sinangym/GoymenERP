using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Modules.Accounting.Application;

public sealed partial class AccountingPostingAccountMappingServiceV1
{
    public async Task<AccountingApiResult<AccountingPostingAccountMappingWorkspaceV1>> WorkspaceAsync(
        CancellationToken cancellationToken = default)
    {
        const string permission = AccountingPostingAccountMappingPermissionNamesV1.Read;
        if (!ErpPermissions.IsKnown(permission) || mappings is not IAccountingPostingAccountMappingWorkspaceStoreV1 reader)
            return new(AccountingApiStatus.Unavailable, null, false, null);
        if (!TryContext(out var company, out var actor)
            || !await AuthorizeCapturedAsync(permission, company, actor, cancellationToken))
            return new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied);
        AccountingPostingAccountMappingWorkspaceV1 result;
        try { result = await reader.WorkspaceAsync(cancellationToken); }
        catch (AccountingStoreException error) when (error.Code == AccountingStoreCodes.CompanyContextMismatch)
        { return new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied); }
        catch (AccountingStoreException)
        { return new(AccountingApiStatus.Unavailable, null, false, null); }
        if (result.CompanyId != company || !await AuthorizeCapturedAsync(permission, company, actor, cancellationToken))
            return new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied);
        return new(AccountingApiStatus.Ok, result, false, null);
    }
}

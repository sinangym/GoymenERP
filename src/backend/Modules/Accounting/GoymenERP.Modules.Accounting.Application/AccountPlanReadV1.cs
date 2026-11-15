using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>
/// FIN-S16 (M-05 Hesap Planı): the Accounting owner's narrow read name for the active approved account plan.
/// Central registration grants it to no role; which persona reads the plan is a human grant decision.
/// </summary>
public static class AccountPlanPermissionNamesV1
{
    public const string Read = "accounting.account-plan.read";

    public static IReadOnlyList<string> All { get; } = [Read];
}

/// <summary>
/// Where the active account plan came from: the active approved source catalog package and the exact account plan
/// source version and SHA-256 it carries. The activator is not released here.
/// </summary>
public sealed record AccountPlanSourceViewV1(
    Guid PackageId,
    long? CatalogVersion,
    string AccountPlanSourceVersion,
    string AccountsSourceSha256,
    DateTimeOffset? ActivatedAtUtc,
    int AccountCount);

/// <summary>One account exactly as the approved mirror names it: no classification, hierarchy or fallback.</summary>
public sealed record AccountPlanAccountViewV1(Guid AccountId, string Code, string Name);

/// <summary>
/// M-05: the company's ACTIVE approved account plan. <see cref="Source"/> is null and <see cref="Accounts"/> empty
/// when no catalog is active yet; the screen shows that as "no active plan", not as an error. Accounts are ordered
/// by code (ordinal), the same order the approved file defines no other way. Read-only; it creates, renames or
/// classifies no account.
/// </summary>
public sealed record AccountPlanViewV1(AccountPlanSourceViewV1? Source, IReadOnlyList<AccountPlanAccountViewV1> Accounts);

/// <summary>The company's single ACTIVE approved source catalog package, or null when none is active.</summary>
public interface IActiveAccountPlanReaderV1
{
    Task<ApprovedAccountingSourceCatalogRecordV1?> ReadActiveAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The guarded M-05 read: narrow company-wide permission measured fresh before and after the store read, the
/// captured company and actor unchanged across it, and a record of another company refused. Nothing is written.
/// </summary>
public sealed class AccountPlanReadServiceV1(
    IActiveAccountPlanReaderV1 reader,
    IErpAuthorityRevalidationService authority,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    ICorrelationContext correlation)
{
    public async Task<AccountingApiResult<AccountPlanViewV1>> ReadActiveAsync(CancellationToken cancellationToken = default)
    {
        const string permission = AccountPlanPermissionNamesV1.Read;
        if (!ErpPermissions.IsKnown(permission))
        {
            return new AccountingApiResult<AccountPlanViewV1>(AccountingApiStatus.Unavailable, null, false, null);
        }

        if (!TryContext(out var company, out var actor)
            || !await AuthorizeCapturedAsync(permission, company, actor, cancellationToken))
        {
            return Denied();
        }

        ApprovedAccountingSourceCatalogRecordV1? record;
        try
        {
            record = await reader.ReadActiveAsync(cancellationToken);
        }
        catch (AccountingStoreException exception) when (exception.Code == AccountingStoreCodes.CompanyContextMismatch)
        {
            return Denied();
        }

        if (!await AuthorizeCapturedAsync(permission, company, actor, cancellationToken))
        {
            return Denied();
        }

        if (record is null)
        {
            return new AccountingApiResult<AccountPlanViewV1>(
                AccountingApiStatus.Ok, new AccountPlanViewV1(null, []), false, null);
        }

        if (record.CompanyId != company
            || record.Status != ApprovedAccountingSourceCatalogStatusV1.Active
            || record.Package.Mirror.CompanyId != company)
        {
            return Denied();
        }

        return new AccountingApiResult<AccountPlanViewV1>(AccountingApiStatus.Ok, View(record), false, null);
    }

    public static AccountPlanViewV1 View(ApprovedAccountingSourceCatalogRecordV1 record)
    {
        var accounts = record.Package.Mirror.Rows
            .OrderBy(row => row.Code, StringComparer.Ordinal)
            .Select(row => new AccountPlanAccountViewV1(row.Account.AccountId, row.Code, row.Name))
            .ToArray();
        return new AccountPlanViewV1(
            new AccountPlanSourceViewV1(
                record.PackageId,
                record.CatalogVersion,
                record.AccountPlanSourceVersion,
                record.AccountsSourceSha256,
                record.ActivatedAtUtc,
                accounts.Length),
            accounts);
    }

    private async Task<bool> AuthorizeCapturedAsync(
        string permission,
        Guid company,
        Guid actor,
        CancellationToken cancellationToken)
    {
        if (!ContextMatches(company, actor))
        {
            return false;
        }

        var scope = await authority.RevalidatePermissionScopeAsync(permission, company, cancellationToken);
        return scope.IsGranted && scope.IsCompanyWide && ContextMatches(company, actor);
    }

    private bool TryContext(out Guid company, out Guid actor)
    {
        company = companyContext.IsResolved ? companyContext.CompanyId : Guid.Empty;
        actor = Guid.Empty;
        return companyContext.IsResolved && company != Guid.Empty && currentUser.IsAuthenticated
            && Guid.TryParse(currentUser.UserId, out actor) && actor != Guid.Empty
            && currentUser.CanAccessCompany(company) && correlation.IsResolved;
    }

    private bool ContextMatches(Guid company, Guid actor) =>
        TryContext(out var currentCompany, out var currentActor)
        && currentCompany == company && currentActor == actor;

    private static AccountingApiResult<AccountPlanViewV1> Denied() =>
        new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied);
}
